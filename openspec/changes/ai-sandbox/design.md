## Context

The AI agent system in `Dotto.Ai` already exists with:
- `ChatAssistant` wrapper that creates a DI scope, populates `AgentContext`, resolves a keyed `AIAgent` (OpenAI chat client), and runs it with tools
- `DiscordTools` with `DownloadMedia` and `UploadFile` tools
- `TerminalTools` with `ExecuteCommand`
- `AgentContext` with `CallerId` and `CallerName`; `ChatAssistantContext` adds `ChannelId`, `GuildId`, `SessionId`, and `Attachments`
- Prompt system using Fluid/Liquid templates

The sandbox is a single scoped entity (`Dotto.Ai/Sandbox/`) that owns the Docker client and encapsulates one session's container lifecycle. The bot never reads or writes sandbox files on the host filesystem: every byte crosses the boundary through the sandbox HTTP API.

The key security constraint: the sandbox must never see the bot's secrets (Discord token, yt-dlp cookies, DB credentials, S3 keys, LLM API key) or the Docker socket. The bot runs downloaders locally and streams the output into the sandbox.

See `proposal.md` — Why for the full motivation.

## Goals / Non-Goals

**Goals:**
- Per-session ephemeral Docker containers for command execution
- Python HTTP server inside the sandbox (`GET /ping`, `POST /execute`, `GET /file`, `PUT /file`)
- Token-authenticated sandbox API; no bot secrets in the container
- Ephemeral `/work` tmpfs plus persistent per-caller `/storage` bind mount with a quota
- Bot-side download tool that streams yt-dlp/Cobalt output into the sandbox
- Bot-side upload tool that streams files out and enqueues them; the invoking handler uploads them to Discord (S3 fallback)
- Orphan container reaper
- Docker socket protection via socket-protector sidecar
- Egress containment: sandboxes reach the internet but not the host, the LAN, or other containers' published ports
- A single `Ai:Sandbox:Enabled` kill switch that removes sandbox-backed tools entirely
- Configurable timeouts, resource limits, and security settings

**Non-Goals:**
- gVisor integration (postponed to a separate change: `sandbox-gvisor-benchmark`)
- MicroVM isolation (Firecracker/SmolVM — Docker containers are sufficient for the trusted-binaries workload)
- Multiple LLM provider support beyond the existing OpenAI/OpenRouter setup
- Streaming command output back to the LLM (request-response is sufficient)
- Persistent storage across sessions as a bot-visible filesystem (only the sandbox sees `/storage`)

## Decisions

### Decision 1: Python over Deno for sandbox HTTP server

**Chosen:** Python 3 stdlib (`http.server` + `subprocess`)
**Alternatives considered:** Deno, Go static binary, Rust

**Rationale:** The sandbox container is already isolated by Docker — Deno's security sandbox (`--allow-read`, `--allow-net`) is redundant. Python is familiar to the team, requires no compile step, no additional toolchain, and the server itself needs no pip dependencies (stdlib only; the image's pip packages exist for the assistant's use, not the server's). A single `server.py` file is small and easy to audit.

### Decision 2: HTTP file streaming over a shared bot-visible filesystem

**Chosen:** The bot streams file bytes over `PUT /file` and `GET /file`. `/work` is an ephemeral tmpfs inside the container; `/storage` is a per-caller bind mount the bot never reads or writes.
**Alternatives considered:** A host session directory bind-mounted into the sandbox as `/work`, with the bot reading and writing files directly.

**Rationale:** A shared bind mount forces the bot to resolve sandbox-authored paths in its own namespace. A sandbox could plant a symlink (or a path whose parent is swapped between validation and use) and make the bot read or write arbitrary host files. Streaming removes that trust boundary: the sandbox owns all path resolution, and the bot only handles opaque byte streams. The persistent `/storage` mount is still a bind mount, but it is mounted for the sandbox only — the bot merely creates the directory and never resolves paths inside it. `/work` is kept as tmpfs so scratch files disappear with the container and cannot exhaust host disk.

### Decision 3: Per-session containers over shared container

**Chosen:** One container per `ChatAssistant.Invoke()` call
**Alternatives considered:** Single persistent container with session-scoped subdirectories, Deno subprocess

**Rationale:** Per-session containers provide true PID namespace isolation (session A cannot kill session B's ffmpeg), filesystem isolation (no shared `/tmp`), and guaranteed cleanup on crash. The startup latency (~500ms-1s) is negligible compared to LLM response time (typically 5-30s). The single-container approach was rejected because path enforcement is fragile (symlinks, `/proc/<pid>`) and relies on the LLM not interfering with other sessions.

### Decision 4: Docker.DotNet SDK over shelling out to `docker` CLI

**Chosen:** `Docker.DotNet.Enhanced` NuGet package
**Alternatives considered:** `System.Diagnostics.Process` to run `docker create/start/stop/rm`

**Rationale:** The SDK provides typed responses, `CancellationToken` support, structured error handling, and a single persistent HTTP client connection. Shelling out to the CLI requires parsing stdout for container IDs, no cancellation support, and fragile error handling.

### Decision 5: docker-socket-protector over direct socket mount

**Chosen:** `docker-socket-protector` sidecar container
**Alternatives considered:** Direct `/var/run/docker.sock` mount into the bot container

**Rationale:** The protector whitelists only the six endpoints the bot needs (container create/start/inspect/list/delete/stop). If the bot container is compromised, the attacker cannot pull malicious images, access other containers' data, or modify Docker configuration. The protector itself is locked down (read-only, no capabilities, 32MB memory limit). Because image pulls are not whitelisted, a separate one-shot `sandbox-image` compose service pre-pulls the sandbox image before the bot starts.

### Decision 6: Three-tier timeout model plus a bounded output

**Chosen:** `"short"` (5s), default (20s), `"long"` (300s) as a string field; stdout/stderr capped at 10,000 characters of *combined* output, with a byte-bounded tail (40,000 bytes per stream = four bytes per character of the cap, enough for all-UTF-8-maximal output). The character cap is the only output cap: the bot's buffered-response limit covers its worst-case JSON encoding (12 bytes per character, because astral characters are escaped as surrogate pairs).
**Alternatives considered:** Fixed timeout, LLM-specified milliseconds, unbounded output

**Rationale:** A fixed timeout is too rigid (5s for jq, 300s for ffmpeg). Milliseconds requires the LLM to guess the right value. Three tiers are simple for the LLM to reason about: quick probes → short, most commands → default, media processing → long. Tool output is fed straight back to the model, so an uncapped `cat` or `ffmpeg -loglevel debug` could both flood the context and buffer an unbounded JSON response; the server drains stdout/stderr with reader threads, keeps a bounded tail, and decodes with `errors="replace"` so binary output never fails a request. There is deliberately one cap rather than two: a second byte cap on the response would silently truncate astral output earlier than plain ASCII and produce two different limits for the same model context. Instead the client buffer is derived from the character cap's worst case (12 bytes per character), so the character cap always binds. The budget is shared rather than split 50/50: stderr reserves at least half (diagnostics matter more) and stdout takes whatever stderr does not need, so a command whose output fits is never truncated just because the other stream was non-empty. On timeout, the server kills the whole process group (`start_new_session=True` + `killpg`) so child processes cannot outlive the command.

### Decision 7: The sandbox is a single scoped, self-contained entity

**Chosen:** One scoped class per `ChatAssistant.Invoke()` that IS the sandbox — it owns the Docker client and encapsulates the container's whole lifecycle. It implements `IAsyncDisposable` (with a synchronous `Dispose` fallback); disposing it stops/removes the container. It exposes two segments:
- `Metadata` — always present after `InitializeAsync`: session ID, caller ID, and the host directory bind-mounted as `/storage`. It is created eagerly.
- `Container` — nullable: API URL, container ID, and the allowed container operations (execute, health check, file get/put). Accessing it ensures the container is started (`EnsureStartedAsync`); it stays null if never touched.

**Alternatives considered:** Eager container creation in `ChatAssistant.Invoke()`, tools creating containers independently, a separate stateless container service + scoped session registry

**Rationale:** Not every invocation uses the terminal, and some may only need metadata. Eager creation would waste 500ms–1s every invocation. Making the sandbox one scoped entity (rather than two split services) keeps the container's lifetime and the agent's scope in lockstep: disposal of the scope is disposal of the container, so teardown is guaranteed regardless of which tool triggered the lazy start. The `Metadata`/`Container` split makes the spin-up boundary explicit. The price is that each scope owns its own `DockerClient` (slightly heavier) and raw Docker CRUD is less isolated for unit testing; both are acceptable. A shared `IDockerClient` is registered as a singleton because the client is thread-safe and pools connections.

### Decision 8: Single shell command string over command+args split

**Chosen:** One `command` string executed by a shell (`subprocess.Popen(..., shell=True)`), supporting pipes, redirection, `&&`, globs.

**Alternatives considered:** A `{command, args: [], shell: bool}` request shape

**Rationale:** The argv/shell split was tried and retired: the LLM kept shoving command arguments into the `command` string and using shell syntax anyway (pipes, `&&`), so the split added friction without improving safety. A single string is simpler for the model and works with full shell syntax.

### Decision 9: Upload is stream-out-then-enqueue, delivery delegated to the handler

**Chosen:** `UploadFile` validates the path, streams the file out of the sandbox over `GET /file`, and enqueues the resulting stream as an `AgentAttachment`. After the agent run, the invoking handler materializes each attachment: Discord-bound files are buffered into a bot-owned temp file, oversized files go to S3 inline, and files above `max(discord limit, S3 max)` or above `ChatAssistant.MaxAttachments` are dropped.

**Alternatives considered:** `UploadFile` sending to Discord directly during the turn; copying files into a bot-visible host directory

**Rationale:** Centralizes attachment/S3 behavior in the Discord layer and keeps send logic decoupled from the AI project — useful if the agent is ever surfaced outside Discord. The agent only needs to know the file was enqueued, not how it is delivered. The tool does not resolve host paths; the sandbox server owns path resolution. Because the attachment bytes live only while the container is alive, the container's whole lifetime is the invoker's: `ChatAssistant` never disposes the sandbox, and the handler disposes it in a `finally` that wraps both the agent run and attachment materialization. Delivered files are then recorded in the database exactly like the download command's media, so AI uploads are traceable rather than invisible to the bot's own history.

### Decision 10: Static ffmpeg tarball over apt

**Chosen:** ffmpeg/ffprobe fetched as static binaries from a release tarball; apt installs only curl and jq.

**Alternatives considered:** `apt-get install ffmpeg`

**Rationale:** apt's ffmpeg pulls a large dependency tree (GUI libs, systemd-related packages) which bloats the image. Static binaries keep the sandbox image lean; runtime shares nothing with the host.

### Decision 11: Per-container token authentication

**Chosen:** A fresh 32-byte random token is generated per container, injected as `SANDBOX_TOKEN`, and required in `X-Sandbox-Token` on every request.

**Alternatives considered:** No auth on the sandbox API, network isolation alone, a static shared token

**Rationale:** The API is effectively remote code execution. Network isolation alone is insufficient once the sandbox has outbound connectivity and is reachable by name from the bot's network. A per-container token makes the API useless to anything that can merely reach the port, and it is discarded with the container.

### Decision 12: Persistent per-caller storage with an enforced quota

**Chosen:** `StorageBasePath/{callerId}` is created eagerly, bind-mounted at `/storage`, and shared across that caller's sessions. The sandbox server enforces `StorageQuotaMb` before accepting writes (`507`) and via a watchdog that kills a command whose process pushes usage over the quota. Usage charges each file `max(256 KiB, size)`, not its raw size: a million empty files cost inodes and make the (O(files)) usage walk expensive, so they must not be free. The quota is explicitly best-effort — polling, not a filesystem quota — because a real one would require ZFS/XFS on the host, which is too large a deployment requirement for this bot.

**Alternatives considered:** No persistence, per-session storage, trusting the LLM not to fill the disk

**Rationale:** Users need files to survive between sessions (e.g. reusable assets, intermediate results), but the bot must not manage that data. Scoping storage by caller prevents cross-user access. A quota prevents one user from exhausting host disk; because the bot never touches the files, enforcement lives in the sandbox server. `/work` remains ephemeral so scratch usage is bounded by a sized tmpfs instead.

### Decision 13: Network-resolved API URL with a loopback dev fallback

**Chosen:** When `SandboxOptions.NetworkName` is set (production), the sandbox joins that Docker network and the bot reaches it at `http://{container-name}:8080`. When unset (dev, bot on host), the sandbox publishes `8080` to a random loopback port and the bot uses `http://127.0.0.1:{port}`.

**Alternatives considered:** Always publishing a host port, always using a fixed bridge address

**Rationale:** Docker's embedded DNS resolves container names on a shared network, so no host port is needed in production and the API is never exposed on the host. Dev needs a published port because the bot is not in a container; binding it to `127.0.0.1` only keeps it off external interfaces.

> **Updated by `sandbox-gvisor`:** in production the bot no longer resolves the container by name on the sandbox bridge. With `NetworkName` set it connects to the per-session Unix socket at `<IpcBasePath>/<sessionId>/sandbox.sock`; the loopback TCP path described here remains only for development (unset `NetworkName`).

### Decision 14: Host-side egress filter over Docker network options

**Chosen:** The `sandbox` network pins a stable bridge name (`br-sandbox`) and an explicit subnet (`SANDBOX_SUBNET`, default `10.213.0.0/24`). the `sandbox-firewall` compose sidecar (host network + `NET_ADMIN`, image built from `.docker/sandbox-egress/`) runs `.docker/sandbox-egress.sh` in `--serve` mode, which applies interface-scoped iptables rules and removes them on shutdown: `DOCKER-USER` first RETURNs traffic that stays on the bridge, then DROPs RFC1918, CGNAT, loopback, link-local, multicast and reserved destinations; `INPUT` DROPs NEW connections arriving from the bridge. The production compose file publishes Postgres on `127.0.0.1` only.

**Alternatives considered:** `com.docker.network.bridge.enable_icc=false` (rejected because it also blocks the bot's HTTP calls to the sandbox on the same bridge — verified: bot↔sandbox requests time out), `--network none` (rejected because sandboxes need outbound internet for curl/search), per-sandbox networks (does not stop LAN or host reachability, and multiplies networks per session), blocking at the application layer (the sandbox runs arbitrary commands, so there is no application layer to block at).

**Rationale:** Docker bridges have no egress ACL, and a published host port is reachable from every bridge. Filtering on the bridge interface is the only mechanism that distinguishes sandbox traffic from bot and backend traffic, so internet egress keeps working while the LAN, host services, published ports of other containers and the socket-protector are cut off. The same-bridge `RETURN` is required because `br_netfilter` runs container-to-container traffic through `FORWARD`; without it the private-range drops also match the sandbox's own subnet and bot↔sandbox calls time out (verified with a throwaway bridge). The subnet is pinned because the default Docker pools are exhausted on hosts with many networks and an accidental overlap with the LAN would be routed. Making the filter a compose service (rather than a systemd unit) keeps the rules a property of the stack: `docker compose up` is sufficient, a container restart re-applies them, and `docker compose down` removes them. There is deliberately no periodic re-apply: the rules are keyed by the pinned bridge name (so they survive network recreation), a container restart re-applies them anyway, and anything able to flush host netfilter can also remove the sidecar, so polling adds no security. The bridge name and subnet are only applied when Docker first creates the network — after changing either, the network must be removed and the stack brought up again. Rules are IPv4-only; the network pins `enable_ipv6: false` for the same reason, and enabling IPv6 removes the containment. An `internal: true` network was measured and rejected as the sole control: it blocks LAN egress and other containers, but host-open ports (e.g. SSH) remain reachable through the bridge gateway. Known limitation: sibling sandboxes on one bridge stay mutually reachable, since ICC cannot be disabled without severing bot↔sandbox; per-container tokens make the API useless to a peer.

> **Updated by `sandbox-gvisor`:** `enable_icc=false` is now set on the `sandbox` network and the bot is no longer a member, because the control channel moved off the bridge to a per-session Unix socket (gVisor `--host-uds=create`). The interface-scoped egress rules and the `DOCKER-USER` `RETURN` remain unchanged; the same-bridge `RETURN` is now harmless (no legitimate same-bridge traffic remains). Sibling sandboxes can no longer reach each other.

### Decision 15: Disabled means "no sandbox tools", not "tools that throw"

**Chosen:** When `Ai:Sandbox:Enabled` is false, the keyed agent's tool list is empty, `ChatAssistant.Invoke()` skips storage provisioning and renders the prompt without tool instructions, `Sandbox.InitializeAsync`/`EnsureStartedAsync` throw `SandboxDisabledException` (defense in depth), and `SandboxReaper` logs once and does not start.

**Alternatives considered:** Letting tools call `EnsureStartedAsync` and surface the resulting failure to the model; disabling the `/ai` command entirely.

**Rationale:** A tool that can only throw turns a deliberate configuration into opaque provider errors and wastes a model turn. Omitting the tools makes the kill switch honest, while the guard message tells an operator exactly which setting to change. The assistant itself keeps answering plain chat, which is useful when only the container runtime is unavailable. `ToolAttribute.SandboxBacked` keeps the omit-when-disabled rule true by construction: the tool list is filtered by capability, so a future sandbox-free tool stays available without touching the filter.

### Decision 16: A failed container start is a tool result, not an exception

**Chosen:** Every sandbox-backed tool catches a failure from `EnsureStartedAsync`, logs it, and returns the fixed string `"Execution sandbox failed to start; consider this tool disabled."`. The sandbox itself stops any container it managed to create before rethrowing, so a failed start leaves no orphan.
**Alternatives considered:** Letting the Docker/HTTP exception propagate; returning the raw exception text.

**Rationale:** This is the same reasoning as Decision 15: a Docker daemon outage, a missing image, or a socket-protector rejection is an environment condition the model cannot fix, and an escaping exception aborts the entire agent run rather than costing one turn. The fixed wording also stops the model from retrying a tool that cannot succeed. Cleanup on the failure path matters because `AutoRemove` only fires on stop: a created-but-never-ready container would otherwise hold its quota and storage mount until the reaper's next sweep after `WallClockTimeout`.

### Decision 17: Deliver-then-record uploads

**Chosen:** The handler records one database row per delivered file (Discord attachment URL or S3 link), mirroring `DownloadCommandHandler.LogDownloadedMedia`, with the sandbox source path when known.
**Alternatives considered:** Recording at enqueue time; recording in the tool.

**Rationale:** Only the handler knows whether a file was actually delivered or dropped for size/budget reasons, and only it has the message id after sending. Logging at enqueue would create records for files the user never received.

## Risks / Trade-offs

- **[Risk] ffmpeg under gVisor may be slow** → Mitigation: gVisor is optional and gated behind `UseGVisorRuntime`. Benchmarking is tracked in the `sandbox-gvisor-benchmark` change.
- **[Risk] Docker socket access is a powerful attack surface** → Mitigation: socket-protector limits to 6 whitelisted endpoints. Even if the bot is compromised, the attacker can only create/run/destroy ephemeral containers.
- **[Risk] Orphaned containers on bot crash or failed start** → Mitigation: `EnsureStartedAsync` stops any container it created when startup fails, `SandboxReaper` sweeps every 30 seconds and stops *and deletes* containers older than `WallClockTimeout`, and containers use `--rm`.
- **[Risk] Persistent `/storage` fills the host disk** → Mitigation: `StorageQuotaMb` is enforced before writes and by an execution watchdog. Orphaned storage is not automatically reclaimed; operators can point `StorageBasePath` at a monitored volume and reclaim it out of band.
- **[Risk] Ephemeral `/work` tmpfs consumes memory** → Mitigation: `/work` has a hard size limit (`WorkTmpfsMb`), `/tmp` is capped at 64MB, and `MemoryMb` is validated to be larger than `WorkTmpfsMb`.
- **[Risk] Runaway processes exhaust the container** → Mitigation: `PidsLimit = 256`; commands run in their own process group and are killed as a group on timeout or quota breach.
- **[Risk] Shared kernel vulnerability** → Mitigation: containers use `--cap-drop ALL`, `--read-only`, `--no-new-privileges`, seccomp. The sandbox only runs trusted binaries (ffmpeg, curl, jq). If the threat model escalates, gVisor or microVMs can be adopted without changing tool interfaces.
- **[Risk] Large or chatty tool output bloats the LLM context or the container's memory** → Mitigation: the server reads stdout/stderr incrementally with a byte-bounded tail (so a runaway command cannot buffer unbounded memory) and caps the retained output at 10,000 characters; the client's buffer is sized for that cap's worst-case JSON encoding (12 bytes/char).
- **[Risk] A sandbox reaches the host, the LAN or another container's published port** → Mitigation: postgres publishes on loopback only, and `.docker/sandbox-egress.sh` drops non-public destinations plus all NEW host-bound connections from the sandbox bridge. Same-bridge traffic is explicitly excluded from those drops. Sandboxes pin public resolvers (`SandboxOptions.DnsServers`) so the drop list cannot silently break DNS on a host that uses a LAN resolver.
- **[Risk] Egress rules are not installed on a host** → Mitigation: the `sandbox-firewall` compose sidecar owns them (the bot waits for its healthcheck), the compose file pins the bridge name they target, and `.docker/sandbox-egress.service` covers non-compose deployments. Without either, containment degrades to the loopback-only Postgres binding.
- **[Risk] Attachments must be read while the container is alive** → Mitigation: the handler materializes every enqueued attachment before disposing the sandbox; Discord-bound files are copied to bot-owned temp files, S3 uploads happen inline.
