## Why

The AI ChatAssistant agent needs to process media files — download videos, transcode them with ffmpeg, query JSON, and upload results to Discord. Running these operations inside the bot process is a security risk (yt-dlp cookies, Discord token, DB credentials all in the same process) and provides no isolation between concurrent user sessions. A sandboxed execution environment with per-session Docker containers solves this, keeping secrets in the bot process while allowing the LLM to run arbitrary commands in an isolated, ephemeral container.

## What Changes

- Add a sandbox Docker image (`Dotto.Ai/sandbox-image/`) with a Python HTTP server, ffmpeg, curl, and jq; all requests require a per-container token
- Wire the per-session sandbox lifecycle into the agent invocation: eager per-caller storage provisioning, lazy container start, dispose-driven teardown
- Give each sandbox an ephemeral `/work` tmpfs for scratch files and a persistent `/storage` bind mount for per-caller storage, with a configurable quota
- Populate `TerminalTools` to execute shell commands against the sandbox via HTTP
- Populate `DiscordTools.DownloadMedia` to download media and stream the bytes into `/work/downloads/` via `PUT /file`
- Populate `DiscordTools.UploadFile` to stream a file out of the sandbox via `GET /file` and enqueue it for delivery
- Deliver enqueued attachments after the agent run: Discord attachment within the channel limit, S3 fallback for oversized files, capped by `ChatAssistant.MaxAttachments`, and record every delivered file in the database like the download command does
- Extend agent context with SessionId, ChannelId, GuildId
- Add `SandboxOptions` settings class configurable under `Ai:Sandbox`
- Add `SandboxReaper` hosted service to clean up orphaned containers
- Add `docker-socket-protector` sidecar and a `sandbox-image` pre-pull service to the production compose file
- Add host-side egress containment so sandboxes reach the internet but not the host, the LAN, or other containers' published ports (pinned `br-sandbox` bridge + interface-scoped iptables rules applied by a boot-time service)
- Add an `Ai:Sandbox:Enabled` kill switch that removes sandbox-backed tools from the agent instead of letting them fail at call time
- Add `/ai` slash, text, and message commands to Discord to invoke the agent

## Capabilities

### New Capabilities
- `sandbox-execution`: Run commands (ffmpeg, curl, jq) in an isolated per-session Docker container via a token-authenticated Python HTTP API. Supports short (5s), default (20s), and long (300s) timeouts, streams files in and out through `/file`, and keeps output memory- and response-size-bounded.
- `sandbox-lifecycle`: Automatic per-session container creation, health polling, and destruction. Persistent per-caller storage, ephemeral `/work` scratch, configurable resource limits, and orphan cleanup via `SandboxReaper`.
- `media-download`: Download media from URLs using the existing yt-dlp/Cobalt infrastructure and stream the output into the sandbox's ephemeral `/work/downloads/` directory.
- `media-upload`: Stream files out of the sandbox's `/work` or `/storage` directories and enqueue them for delivery; the invoking handler sends them to Discord as attachments, with S3 fallback for oversized files.
- `sandbox-security`: Container hardening (read-only rootfs, `--cap-drop ALL`, no-new-privileges, tmpfs, pids limit), token authentication, path confinement, docker-socket-protector sidecar, egress containment, and no bot secrets in the sandbox.

### Modified Capabilities
- *(None — no existing specs are affected)*

## Impact

- **Dotto.Ai**: New files in `sandbox-image/` (Dockerfile, server.py, README), `Sandbox/` (`ISandbox`, `ISandboxContainer`, `Sandbox`, `SandboxContainer`, `SandboxMetadata`, `SandboxReaper`), `Settings/SandboxOptions.cs`, new methods in `Tools/DiscordTools.cs` and `Tools/TerminalTools.cs`, extended context classes, new DI registrations
- **Dotto.Ai.csproj**: Add `Docker.DotNet.Enhanced` NuGet, project references to `Dotto.Downloader.Contracts`, `Dotto.Application`
- **Dotto.Discord**: New `Commands/Ai/` directory with slash/text/message commands and `CommandHandlers/Ai/AiCommandHandler.cs`; handler owns attachment delivery and final sandbox teardown
- **Dotto.FileUpload**: S3 objects keyed by a random id; original filename preserved via `Content-Disposition`
- **docker-compose.yml**: Add `socket-protector` and `sandbox-image` services, a dedicated `sandbox` network with a pinned bridge name and subnet, `DOCKER_HOST`, the persistent storage mount, and a loopback-only Postgres port
- **.docker/**: New `sandbox-egress.sh`, `sandbox-egress/Dockerfile` and `sandbox-egress.service`; the compose stack runs it as the `sandbox-firewall` sidecar. Dev compose publishes Postgres on loopback only
- **README.md**: AI sandbox section covering network layout, egress containment installation, and the sibling-sandbox limitation
- **appsettings.json**: New `Ai:Sandbox` config section and `Ai:ChatAssistant:MaxAttachments`
