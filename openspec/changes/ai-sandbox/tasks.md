## 1. Foundation — Context & Tool Plumbing

- [x] 1.1 Extend `AgentContext` with `string SessionId` (required)
- [x] 1.2 Extend `ChatAssistantContext` with `ulong ChannelId` and `ulong GuildId` (required, 0 for DMs)
- [x] 1.3 Generate `SessionId` in `ChatAssistant.Invoke()` and pass `ChannelId`/`GuildId` to context
- [x] 1.4 Add `SandboxOptions` settings class in `Settings/SandboxOptions.cs`
- [x] 1.5 Register `SandboxOptions` with `AddOptions<SandboxOptions>().BindConfiguration("Ai:Sandbox").ValidateOnStart()`
- [x] 1.6 Add project references to `Dotto.Downloader.Contracts`, `Dotto.Application` in `Dotto.Ai.csproj`
- [x] 1.7 Implement `DownloadMedia` in `DiscordTools` using `IDownloaderServiceFactory`, writing to session dir
- [x] 1.8 Implement `UploadFile` in `DiscordTools` reading from session dir, sending as Discord attachment with S3 fallback
- [x] 1.9 Register `TerminalTools` in tool discovery array in `DependencyInjection.cs`
- [x] 1.10 Update `ChatAssistant.liquid` prompt with tool descriptions and file lifecycle

## 2. Sandbox Image & HTTP Server

- [x] 2.1 Create `Dotto.Ai/sandbox/server.py` with Python HTTP server (`GET /ping`, `POST /execute`)
- [x] 2.2 Implement timeout tiers in `server.py`: `"short"` (5s), default (20s), `"long"` (300s)
- [x] 2.3 Implement `cwd` resolution in `server.py` — defaults to `/work`, accepts relative and absolute paths
- [x] 2.4 Create `Dotto.Ai/sandbox/Dockerfile` — `python:3.13-slim` with ffmpeg, curl, jq, non-root user
- [x] 2.5 Build and test the sandbox image: `docker build -t dotto-sandbox:latest .`

## 3. Sandbox Lifecycle Management

- [x] 3.1 Add `Docker.DotNet.Enhanced` NuGet package to `Dotto.Ai.csproj`
- [x] 3.2 Create `ISandbox` interface in `Abstractions/` — single scoped sandbox entity (`IAsyncDisposable`) exposing `Metadata` and a nullable `Container` segment
- [x] 3.3 Implement `Sandbox` in `Internal/` — owns the Docker client; eager host-dir creation, lazy container create/start with health polling, dispose-driven stop/remove + host-dir cleanup
- [x] 3.4 Create `SandboxMetadata` (SessionId, HostSessionDir) and `SandboxContainer` (ApiUrl, ContainerId, ExecuteAsync, PingAsync) in `Abstractions/`
- [x] 3.5 Add sandbox properties (`HostSessionDir`, `SandboxApiUrl`, `ContainerId`) to `ChatAssistantContext`
- [x] 3.6 Wire `ChatAssistant.Invoke()`: generate session ID, create host dir eagerly, delegate container lifecycle to session service
- [x] 3.7 Wire teardown in `ChatAssistant.Invoke()` `finally` block: call `sandboxSession.StopIfStartedAsync()`
- [x] 3.8 Register `ISandbox` (scoped) in DI

## 4. Terminal Tool Integration

- [x] 4.1 Implement `TerminalTools.ExecuteCommand` — calls `sandboxSession.EnsureStartedAsync()` then POST to sandbox HTTP API
- [x] 4.2 `SandboxContainer` owns its own `HttpClient` (310s timeout, slightly above the "long" tier); no shared DI registration
- [x] 4.3 Wire `ISandboxSessionService` into `TerminalTools` for target sandbox URL

## 5. Security & Hardening

- [x] 5.1 Implement `SandboxReaper` hosted service in `Internal/` — periodic orphan container sweep
- [x] 5.2 Register `SandboxReaper` as hosted service in `DependencyInjection.cs`
- [x] 5.3 Apply container hardening flags in `SandboxContainerService`: read-only rootfs, cap-drop ALL, no-new-privileges, tmpfs, labels, stop-timeout
- [x] 5.4 Path sanitization not needed — container isolation is sufficient
- [x] 5.5 Add path sanitization in `DiscordTools.UploadFile` — reject `..` and absolute paths
- [x] 5.6 Add `socket-protector` service to `docker-compose.yml`
- [x] 5.7 Configure `DOCKER_HOST` env var and `depends_on` for socket-protector in bot service

## 6. Integration & Polish

- [x] 6.1 Create `/ai` slash command in `Dotto.Discord/Commands/Ai/` with handler delegating to `ChatAssistant.Invoke()`
- [x] 6.2 Add structured logging in `ChatAssistant.Invoke` (session start/end, duration, tool calls)
- [x] 6.3 Add logging for sandbox lifecycle (create, ready, stop, remove) and reaper actions
- [x] 6.4 Add validation attributes to `SandboxOptions` properties
- [x] 6.5 Add `Ai:Sandbox` config section to `appsettings.json` with defaults
- [x] 6.6 Write unit tests for `TerminalTools` with mocked HTTP
- [x] 6.7 Write unit tests for `DiscordTools` with mocked downloader/uploader/session
- [x] 6.8 Write integration test for sandbox container lifecycle (requires Docker daemon)
- [x] 6.9 Write tests for reaper logic with mock Docker client

## 7. Post-Design-Revision Follow-ups

- [x] 8.1 Update `DownloadMedia` to report sandbox-relative `/work/downloads/...` paths (currently returns host paths) so the agent can feed them to `UploadFile`/`ExecuteCommand`
- [x] 8.2 Refactor `SandboxContainerService` + `SandboxSessionService` into a single scoped sandbox entity (`IAsyncDisposable`) exposing an always-present `Metadata` segment (SessionId, HostSessionDir) and a nullable `Container` segment (API URL, container ID, execute/health ops via `EnsureStartedAsync`). Move connection details off `ChatAssistantContext` onto the entity; disposal stops/removes the container and deletes the host dir.
- [x] 8.3 Remove unused `SandboxPortStart` and `IdleTimeout` from `SandboxOptions` (the port is randomly assigned via `HostPort="0"`; the reaper uses only `WallClockTimeout`)
- [x] 8.4 Update `UploadFile` to accept a `/work/...` sandbox-relative path and resolve it into the session host dir (per the agreed path convention)
- [x] 8.5 Support multiple file download in `DownloadMedia` (e.g. playlists) — currently only the first result is kept; return all downloaded files with their `/work/downloads/...` paths