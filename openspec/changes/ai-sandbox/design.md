## Context

The AI agent system in `Dotto.Ai` already exists with:
- `ChatAssistant` wrapper that creates a DI scope, populates `AgentContext`, resolves a keyed `AIAgent` (OpenAI chat client), and runs it with tools
- `DiscordTools` with stub methods (`RespondText`, `DoSuperSecretThing`)
- `TerminalTools` as an empty class
- `AgentContext` with `CallerId` and `CallerName` (needs `SessionId`, `ChannelId`, `GuildId`)
- `ChatAssistantContext` with `Attachments` and `Messages`
- Prompt system using Fluid/Liquid templates

The sandbox began life as two services (`SandboxContainerService` for raw Docker CRUD and a scoped
`SandboxSessionService` for lazy state) but is being consolidated into a single scoped entity.

The key security constraint: the sandbox must never see the bot's secrets (Discord token, yt-dlp cookies, DB credentials, S3 keys, LLM API key). The bot runs downloaders locally and only shares the output files via a bind-mounted host directory.

See `proposal.md` — Why for the full motivation.

## Goals / Non-Goals

**Goals:**
- Per-session ephemeral Docker containers for command execution
- Python HTTP server inside the sandbox (POST /execute, GET /ping)
- Shared filesystem via host bind mount for file exchange
- Bot-side download tool using existing yt-dlp/Cobalt infrastructure
- Bot-side upload tool that enqueues files; the invoking handler uploads them to Discord (S3 fallback)
- Orphan container reaper
- Docker socket protection via socket-protector sidecar
- Configurable timeouts, resource limits, and security settings

**Non-Goals:**
- gVisor integration (postponed to a separate change: `sandbox-gvisor-benchmark`)
- MicroVM isolation (Firecracker/SmolVM — Docker containers are sufficient for the trusted-binaries workload)
- Multiple LLM provider support beyond the existing OpenAI/OpenRouter setup
- Streaming command output back to the LLM (request-response is sufficient)
- Persistent storage across sessions (containers are ephemeral by design)

## Decisions

### Decision 1: Python over Deno for sandbox HTTP server

**Chosen:** Python 3 stdlib (`http.server` + `subprocess`)
**Alternatives considered:** Deno, Go static binary, Rust

**Rationale:** The sandbox container is already isolated by Docker — Deno's security sandbox (`--allow-read`, `--allow-net`) is redundant. Python is familiar to the team, requires no compile step, no additional toolchain, and no pip dependencies (stdlib only). A single `server.py` file is ~30 lines.

### Decision 2: Bind mount over multi-part HTTP for file exchange

**Chosen:** Host directory bind-mounted as `/work` in the sandbox container
**Alternatives considered:** Multi-part upload/download (stateless), S3 as intermediary

**Rationale:** For multi-step workflows (download → ffmpeg → upload), a 100MB file would cross the network 3 times with multi-part. The bind mount is zero-copy: the bot writes to a host directory, the sandbox sees it at `/work/downloads/`, and the bot reads results from `/work/outputs/`. This is the same pattern used by VS Code Dev Containers.

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

**Rationale:** The protector whitelists only the 9 endpoints the bot needs (container CRUD + exec). If the bot container is compromised, the attacker cannot pull malicious images, access other containers' data, or modify Docker configuration. The protector itself is locked down (read-only, no capabilities, 32MB memory limit).

### Decision 6: Three-tier timeout model

**Chosen:** `"short"` (5s), default (20s), `"long"` (300s) as a string field
**Alternatives considered:** Fixed timeout, LLM-specified milliseconds

**Rationale:** A fixed timeout is too rigid (5s for jq, 120s for ffmpeg). Milliseconds requires the LLM to guess the right value. Three tiers are simple for the LLM to reason about: quick probes → short, most commands → default, media processing → long. The `string? timeout` parameter on the tool method maps to the three tiers.

### Decision 7: The sandbox is a single scoped, self-contained entity

**Chosen:** One scoped class per `ChatAssistant.Invoke()` that IS the sandbox — it owns the Docker client and encapsulates the container's whole lifecycle. It implements `IAsyncDisposable`; disposing it stops/removes the container and deletes the host directory. It exposes two segments:
- `Metadata` — always present: session ID + eagerly-created host session directory. Filesystem-only tools (`DownloadMedia`, `UploadFile`) read from here and never start the container.
- `Container` — nullable: API URL, container ID, and the allowed container operations (execute, health check). Accessing it ensures the container is started (`EnsureStartedAsync`); it stays null if never touched.

**Alternatives considered:** Eager container creation in `ChatAssistant.Invoke()`, tools creating containers independently, a separate stateless container service + scoped session registry

**Rationale:** Not every invocation uses the terminal — the LLM may only call `DownloadMedia` / `UploadFile`, which need only `Metadata`. Eager creation would waste 500ms–1s every invocation. Making the sandbox one scoped entity (rather than two split services) keeps the container's lifetime and the agent's scope in lockstep: disposal of the scope is disposal of the container, so teardown is guaranteed regardless of which tool triggered the lazy start. The `Metadata`/`Container` split makes the spin-up boundary explicit — filesystem ops are side-effect free, container ops are not. The price is that each scope owns its own `DockerClient` (slightly heavier) and raw Docker CRUD is less isolated for unit testing; both are acceptable.

### Decision 8: Single shell command string over command+args split

**Chosen:** One `command` string executed by a shell (`subprocess.run(...)`, `shell=True`), supporting pipes, redirection, `&&`, globs.

**Alternatives considered:** A `{command, args: [], shell: bool}` request shape

**Rationale:** The argv/shell split was tried and retired: the LLM kept shoving command arguments into the `command` string and using shell syntax anyway (pipes, `&&`), so the split added friction without improving safety. A single string is simpler for the model and works with full shell syntax.

### Decision 9: Upload is enqueue-then-send, delegated to the handler

**Chosen:** `UploadFile` only validates the path and enqueues the file onto the session's attachment list, returning a confirmation. After the agent run, the invoking Discord handler sends each enqueued file as an attachment (S3 fallback when oversized).

**Alternatives considered:** `UploadFile` sending to Discord directly during the turn

**Rationale:** Centralizes attachment/S3 behavior in the Discord layer and keeps the send logic decoupled from the AI project — useful if the agent is ever surfaced outside Discord. The agent only needs to know the file was enqueued, not how it's delivered. The tool resolves the `/work` path into the host session dir; the handler owns the channel + size-limit decision.

### Decision 10: Static ffmpeg tarball over apt

**Chosen:** ffmpeg/ffprobe fetched as static binaries from a release tarball; apt installs only curl and jq.

**Alternatives considered:** `apt-get install ffmpeg`

**Rationale:** apt's ffmpeg pulls a large dependency tree (GUI libs, systemd-related packages) which bloats the image. Static binaries keep the sandbox image lean; runtime shares nothing with the host.

## Risks / Trade-offs

- **[Risk] ffmpeg under gVisor may be slow** → Mitigation: gVisor is optional and gated behind `UseGVisorRuntime`. Benchmarking is tracked in the `sandbox-gvisor-benchmark` change.
- **[Risk] Docker socket access is a powerful attack surface** → Mitigation: socket-protector limits to 9 whitelisted endpoints. Even if the bot is compromised, attacker can only create/run/destroy ephemeral containers.
- **[Risk] Orphaned containers on bot crash** → Mitigation: SandboxReaper sweeps every 30 seconds. Additionally, containers have `--rm` and `--stop-timeout` for cleanup.
- **[Risk] Temp directory fills disk** → Mitigation: Each session directory is deleted on teardown. Reaper deletes orphan directories. Configurable `HostBasePath` can be pointed to a monitored volume.
- **[Risk] Shared kernel vulnerability** → Mitigation: containers use `--cap-drop ALL`, `--read-only`, `--no-new-privileges`, seccomp. The sandbox only runs trusted binaries (ffmpeg, curl, jq). If the threat model escalates, gVisor or microVMs can be adopted without changing tool interfaces.