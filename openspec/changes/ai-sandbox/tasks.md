## 1. Foundation — Context & Tool Plumbing

- [x] 1.1 Extend `AgentContext` with `string SessionId` (required)
- [x] 1.2 Extend `ChatAssistantContext` with `ulong ChannelId` and `ulong GuildId` (required, 0 for DMs)
- [x] 1.3 Generate `SessionId` in `ChatAssistant.Invoke()` (`{callerId}_{yyyyMMdd_HHmmss}`) and pass `ChannelId`/`GuildId` to context
- [x] 1.4 Add `SandboxOptions` settings class in `Settings/SandboxOptions.cs`
- [x] 1.5 Register `SandboxOptions` with `AddOptions<SandboxOptions>().BindConfiguration("Ai:Sandbox").ValidateDataAnnotations().ValidateOnStart()`, including the `MemoryMb > WorkTmpfsMb` check
- [x] 1.6 Add project references to `Dotto.Downloader.Contracts`, `Dotto.Application` in `Dotto.Ai.csproj`
- [x] 1.7 Implement `DownloadMedia` in `DiscordTools` using `IDownloaderServiceFactory`, streaming each result into `/work/downloads/` via `PUT /file`
- [x] 1.8 Implement `UploadFile` in `DiscordTools` validating `/work/` or `/storage/` paths, streaming via `GET /file`, and enqueuing an `AgentAttachment`
- [x] 1.9 Register `TerminalTools` in tool discovery array in `DependencyInjection.cs`
- [x] 1.10 Update `ChatAssistant.liquid` prompt with tool descriptions and `/work` vs `/storage` lifecycle

## 2. Sandbox Image & HTTP Server

- [x] 2.1 Create `Dotto.Ai/sandbox-image/server.py` with Python HTTP server (`GET /ping`, `POST /execute`, `GET /file`, `PUT /file`)
- [x] 2.2 Implement timeout tiers in `server.py`: `"short"` (5s), default (20s), `"long"` (300s)
- [x] 2.3 Implement `cwd` resolution in `server.py` — defaults to `/work`, accepts relative and absolute paths
- [x] 2.4 Create `Dotto.Ai/sandbox-image/Dockerfile` — `python:3.13-slim` with static ffmpeg/ffprobe, apt curl/jq, non-root user
- [x] 2.5 Build and test the sandbox image: `docker build -f Dotto.Ai/sandbox-image/Dockerfile -t dotto-sandbox:latest .`
- [x] 2.6 Require `X-Sandbox-Token` on every endpoint (401 on mismatch, 503 when unconfigured), compared with `hmac.compare_digest`
- [x] 2.7 Confine `/file` paths to `/work` and `/storage` with `realpath` (parent resolution for writes); support `dedupe=1` numeric suffixes and explicit `Content-Length`
- [x] 2.8 Cap combined stdout/stderr at 10,000 characters, keeping the tail with a truncation marker
- [x] 2.9 Enforce the `/storage` quota before writes (`507`) and with an execution watchdog that kills the process group on breach
- [x] 2.10 Document build/run/endpoints in `Dotto.Ai/sandbox-image/README.md`

## 3. Sandbox Lifecycle Management

- [x] 3.1 Add `Docker.DotNet.Enhanced` NuGet package to `Dotto.Ai.csproj`
- [x] 3.2 Create `ISandbox` in `Sandbox/` — single scoped sandbox entity (`IAsyncDisposable`) exposing `Metadata` and a nullable `Container` segment
- [x] 3.3 Implement `Sandbox` in `Sandbox/` — owns the Docker client; eager storage provisioning via `InitializeAsync`, lazy container create/start with health polling, dispose-driven stop/remove
- [x] 3.4 Create `SandboxMetadata` (`SessionId`, `CallerId`, `HostStorageDir`) and `ISandboxContainer`/`SandboxContainer` (`ApiUrl`, `ContainerId`, `ExecuteAsync`, `PingAsync`, `GetFileAsync`, `PutFileAsync`) in `Sandbox/`
- [x] 3.5 Keep sandbox connection details off `ChatAssistantContext`; expose them through `ISandbox`/`SandboxContainer` instead
- [x] 3.6 Wire `ChatAssistant.Invoke()`: generate session ID, provision per-caller storage via `sandbox.InitializeAsync(sessionId, callerId)`, then resolve the keyed agent
- [x] 3.7 Wire teardown in the invoking handler: `AiCommandHandler` disposes the sandbox in a `finally` that wraps both the agent run and attachment materialization (`ChatAssistant` never disposes it)
- [x] 3.8 Register `ISandbox` (scoped) in DI
- [x] 3.9 Register `IDockerClient` as a singleton shared by `Sandbox` and `SandboxReaper`
- [x] 3.10 Create `{StorageBasePath}/{callerId}` during initialization and widen Unix permissions so the non-root sandbox user can write to it
- [x] 3.11 Name containers `sandbox-{sessionId}-{randomSuffix}` to avoid collisions between same-second sessions
- [x] 3.12 Resolve the API URL by container name when `NetworkName` is set; otherwise publish `8080` to a random loopback port
- [x] 3.13 Mount `/storage` as the per-caller bind and `/work` + `/tmp` as sized tmpfs mounts
- [x] 3.14 Inject `SANDBOX_TOKEN` and `SANDBOX_STORAGE_QUOTA_MB` as the only environment entries
- [x] 3.15 Set `PidsLimit = 256` and disable swap (`MemorySwap = Memory`)

## 4. Terminal Tool Integration

- [x] 4.1 Implement `TerminalTools.ExecuteCommand` — calls `sandbox.EnsureStartedAsync()` then `SandboxContainer.ExecuteAsync`
- [x] 4.2 `SandboxContainer` owns its own `HttpClient`s (310s for execute/ping, 900s for file transfer); buffered responses capped at 128 KiB (worst-case escaping of the 10,000-character output cap); no shared DI registration
- [x] 4.3 Inject `ISandbox` into `TerminalTools` (no separate session service)

## 5. Security & Hardening

- [x] 5.1 Implement `SandboxReaper` hosted service in `Sandbox/` — periodic orphan container sweep
- [x] 5.2 Register `SandboxReaper` as hosted service in `DependencyInjection.cs`
- [x] 5.3 Apply container hardening flags in `Sandbox`: read-only rootfs, cap-drop ALL, no-new-privileges, tmpfs, labels, stop-timeout, `AutoRemove`
- [x] 5.4 Path confinement lives in the sandbox server (`realpath` against `/work` and `/storage`); execution does not need a separate host-side check
- [x] 5.5 Add path validation in `DiscordTools.UploadFile` — require `/work/` or `/storage/` and reject `..`
- [x] 5.6 Add `socket-protector` service to `docker-compose.yml` with the 6-endpoint profile
- [x] 5.7 Configure `DOCKER_HOST` env var and `depends_on` for socket-protector in the bot service
- [x] 5.8 Add the `sandbox-image` pre-pull service (image pulls are not whitelisted by the socket protector)
- [x] 5.9 Add the dedicated `sandbox` network and attach sandbox containers to it in production
- [x] 5.10 Keep bot secrets out of the sandbox; only the per-container token and quota are passed
- [x] 5.11 Buffer Discord-bound attachments into bot-named temporary files (`dotto-ai-{guid}{ext}`) so sandbox input never chooses a host path

## 6. Integration & Polish

- [x] 6.1 Create `/ai` slash, text, and message commands in `Dotto.Discord/Commands/Ai/` with a handler delegating to `ChatAssistant.Invoke()`
- [x] 6.2 Add structured logging in `ChatAssistant.Invoke` (session start/end, duration)
- [x] 6.3 Add logging for sandbox lifecycle (create, ready, stop) and reaper actions
- [x] 6.4 Add validation attributes to `SandboxOptions` properties
- [x] 6.5 Add `Ai:Sandbox` and `Ai:ChatAssistant:MaxAttachments` config sections to `appsettings.json` with defaults
- [x] 6.6 Write unit tests for `TerminalTools` (if applicable) and sandbox HTTP behavior
- [x] 6.7 Write unit tests for `DiscordTools` with mocked downloader and sandbox container
- [x] 6.8 Write integration test for sandbox container lifecycle (requires Docker daemon)
- [x] 6.9 Write tests for reaper logic with mock Docker client
- [x] 6.10 Document the sandbox image and compose setup

## 7. Post-Design-Revision Follow-ups

- [x] 7.1 Update `DownloadMedia` to report sandbox-relative `/work/downloads/...` paths so the agent can feed them to `UploadFile`/`ExecuteCommand`
- [x] 7.2 Refactor the separate container/session services into a single scoped sandbox entity (`IAsyncDisposable`) exposing an always-present `Metadata` segment and a nullable `Container` segment
- [x] 7.3 Remove unused `SandboxPortStart` and `IdleTimeout` from `SandboxOptions`
- [x] 7.4 Update `UploadFile` to accept sandbox-relative `/work/...` paths
- [x] 7.5 Support multiple file downloads in `DownloadMedia` (e.g. playlists), returning every `/work/downloads/...` path

## 8. Post-Implementation Hardening

- [x] 8.1 Replace the shared host session directory with per-caller persistent storage at `{StorageBasePath}/{callerId}` mounted as `/storage`
- [x] 8.2 Make `/work` an ephemeral sized tmpfs; keep `/tmp` at 64MB
- [x] 8.3 Add per-container token authentication to the sandbox API and client
- [x] 8.4 Add `GET /file` / `PUT /file` streaming endpoints and `SandboxContainer.GetFileAsync` / `PutFileAsync`
- [x] 8.5 Enforce the persistent-storage quota before writes and during execution
- [x] 8.6 Cap command output at 10,000 characters with byte-bounded pipe drains; size the client buffer for the cap's worst-case JSON encoding instead of adding a second response cap
- [x] 8.7 Limit sandbox process count to 256
- [x] 8.8 Enforce `ChatAssistant.MaxAttachments` and cap attachment delivery at `max(discord limit, S3 max)`
- [x] 8.9 Key S3 objects by a random id and preserve the original filename via `Content-Disposition`
- [x] 8.10 Add the `sandbox-image` pre-pull service, dedicated `sandbox` network, and persistent storage mount to compose
- [x] 8.11 Add `NetworkName` and resolve the sandbox API by container name in production
- [x] 8.12 Update tests for streaming file transfer and token auth (handler attachment limits and quota behavior moved to 11.12)

## 9. Egress Containment, Robustness & Kill Switch

- [x] 9.1 Publish Postgres on `127.0.0.1` only in both compose files
- [x] 9.2 Pin the `sandbox` bridge name (`br-sandbox`) and subnet (`SANDBOX_SUBNET`, default `10.213.0.0/24`) in compose
- [x] 9.3 Add `.docker/sandbox-egress.sh` (same-bridge `RETURN`, private-range `DROP`s in `DOCKER-USER`, `NEW` `DROP` in `INPUT`) with `--serve` mode, and ship it as the `sandbox-firewall` compose sidecar (plus a systemd unit as the non-compose alternative)
- [x] 9.4 Verify internet egress works while LAN, host services, published ports and sibling networks are blocked, and bot↔sandbox calls keep working
- [x] 9.5 Make `Ai:Sandbox:Enabled=false` remove sandbox-backed tools, skip provisioning and the reaper, and throw an actionable `SandboxDisabledException`
- [x] 9.6 Read exec output as bytes with bounded tails and `errors="replace"`; add binary, huge-output and astral-escape tests
- [x] 9.7 Derive the client buffer (128 KiB) from the character cap's worst-case escaping; add a fast unit test that enforces the relationship
- [x] 9.8 Document that the stack owns egress containment, the manual alternatives, and the dev-mode difference
- [x] 9.9 Enforce the response cap for non-output payloads and bound path length (`_limit_body`, `MAX_PATH_CHARS`)
- [x] 9.10 Stop pipe readers after the drain deadline so detached grandchildren cannot leak threads/fds
- [x] 9.11 Pin sandbox DNS resolvers (`Ai:Sandbox:DnsServers`) so containment does not break lookups on LAN-resolver hosts
- [x] 9.12 Fail the sidecar closed: `--check` healthcheck, bot waits for `service_healthy`, pin the sidecar base image digest
- [x] 9.13 Tag tools with `SandboxBacked` so the disabled path filters by capability instead of assuming every tool needs the sandbox

## 10. Known Gaps (not addressed here)

- [ ] 10.1 Hard storage quota (ZFS dataset quota or XFS project quota) instead of the watchdog's ~1s soft limit
- [ ] 10.2 GC for stale `{StorageBasePath}/{callerId}` directories
- [ ] 10.3 Serialize concurrent invocations by the same caller on one storage volume (current gates cover provisioning only)
- [ ] 10.4 Docker-backed CI job so the `[Explicit]` sandbox security tests run automatically
- [x] 10.5 Sibling-sandbox isolation on one bridge — resolved in `sandbox-gvisor`: the bot leaves the `sandbox` network, `enable_icc=false` is set on it, and the control channel moves to a per-session Unix socket.
- [x] 10.6 gVisor benchmark — done in `sandbox-gvisor` (`decision.md`); gVisor (`runsc`) is now the default sandbox runtime.

## 11. Implementation Review Follow-ups

- [x] 11.1 Stop a created-but-never-ready container before rethrowing, so a failed start cannot orphan a container until the reaper
- [x] 11.2 Sandbox-backed tools return `"Execution sandbox failed to start; consider this tool disabled."` instead of letting a start failure abort the agent run
- [x] 11.3 Treat the 10,000-character output cap as one shared budget (stderr keeps ≥ half, stdout takes the remainder) instead of a 50/50 split
- [x] 11.4 Charge each `/storage` file `max(256 KiB, size)` against the quota so many tiny files cannot evade it
- [x] 11.5 Close the connection on token rejections so an unread request body cannot desync a keep-alive socket
- [x] 11.6 Report a missing `cwd` (and the OS message for an unexecutable command) instead of blaming the command
- [x] 11.7 Route http(s) URLs through `IMediaDownloader` (URL correction + structured errors) and forward fatal downloader errors as tool results
- [x] 11.8 Make teardown the invoker's obligation; `ChatAssistant` no longer disposes the scoped sandbox
- [x] 11.9 Mix a process-wide counter into the session ID so same-second invocations do not collide
- [x] 11.10 Reaper deletes orphaned containers explicitly instead of relying solely on `AutoRemove`
- [x] 11.11 Document the trust model (everything assumes the bot itself is not compromised) and the salted-extension temp-file rule
- [x] 11.12 Record delivered AI uploads in the database, like `DownloadCommandHandler.LogDownloadedMedia` (source = sandbox path when known; only actually-delivered files)
- [x] 11.13 Tests for the handler attachment budget (drop/oversize/S3 fallback), the downloader error surfacing, start-failure tool results, the shared output budget, and quota behavior (507, per-file minimum charge, watchdog kill, auth close)
