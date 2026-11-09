## Why

The AI ChatAssistant agent needs to process media files — download videos, transcode them with ffmpeg, query JSON, and upload results to Discord. Running these operations inside the bot process is a security risk (yt-dlp cookies, Discord token, DB credentials all in the same process) and provides no isolation between concurrent user sessions. A sandboxed execution environment with per-session Docker containers solves this, keeping secrets in the bot process while allowing the LLM to run arbitrary commands in an isolated, ephemeral container.

## What Changes

- Add a sandbox Docker image with a Python HTTP server, ffmpeg, curl, and jq
- Wire the per-session sandbox lifecycle (lazy create on first use → poll → dispose-driven destroy) into the agent invocation
- Populate `TerminalTools` stub to execute commands against the sandbox via HTTP
- Populate `DiscordTools` with real `DownloadMedia` and `UploadFile` methods using the shared filesystem
- Extend agent context with SessionId, ChannelId, GuildId
- Add `SandboxOptions` settings class configurable under `Ai:Sandbox`
- Add `SandboxReaper` hosted service to clean up orphaned containers
- Add `docker-socket-protector` sidecar to the production compose file
- Add `/ai` slash command to Discord to invoke the agent

## Capabilities

### New Capabilities
- `sandbox-execution`: Run commands (ffmpeg, curl, jq) in an isolated per-session Docker container via a Python HTTP API. Supports short (5s), default (20s), and long (300s) timeouts.
- `sandbox-lifecycle`: Automatic per-session container creation, health polling, and destruction. Configurable resource limits, orphan cleanup via SandboxReaper.
- `media-download`: Download media from URLs using the existing yt-dlp/Cobalt infrastructure, writing output to the sandbox's shared volume for further processing.
- `media-upload`: Accept files from the sandbox's shared volume for upload; the invoking handler sends them to Discord as attachments, with S3 fallback for oversized files.
- `sandbox-security`: Container hardening (read-only rootfs, --cap-drop ALL, no-new-privileges, tmpfs), docker-socket-protector sidecar, path sanitization in tools.

### Modified Capabilities
- *(None — no existing specs are affected)*

## Impact

- **Dotto.Ai**: New files in `sandbox/` directory (Dockerfile, server.py), new `Internal/SandboxContainerService.cs`, `Internal/SandboxReaper.cs`, `Settings/SandboxOptions.cs`, new methods in `Tools/DiscordTools.cs` and `Tools/TerminalTools.cs`, extended context classes, new DI registrations
- **Dotto.Ai.csproj**: Add `Docker.DotNet.Enhanced` NuGet, project references to `Dotto.Downloader.Contracts`, `Dotto.Application`
- **Dotto.Discord**: New `Commands/Ai/` directory with slash command
- **docker-compose.yml**: Add `socket-protector` service, configure `DOCKER_HOST` env var
- **appsettings.json**: New `Ai:Sandbox` config section