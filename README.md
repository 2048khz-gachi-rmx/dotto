## Dotto
botto on dotnet.
what, you don't know what botto is?

## Features
- Download videos from other services (Youtube, Instagram, etc...) via [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) or [Cobalt](https://github.com/imputnet/cobalt), be it via text commands, slash commands or automatically (details below)
  - Prioritizes embeddable formats (H265 -> VP9 -> ...)
  - Prioritizes filesizes within the upload limits
  - If S3 details are provided, reuploads videos there to bypass the filesize limit
- Recompress video attachments via ffmpeg to AV1/VP9 + opus, then reupload them
- Set custom flags per-channel
  - **Reupload**: should the bot scan messages for certain URLs, download videos from them and re-upload them in chat?
  - **Recompression**: should the bot scan messages for attachments, then recompress and repost them via ffmpeg?

## Getting started
1. Download the deployment compose and the env template (no checkout needed):
```sh
curl -O https://raw.githubusercontent.com/2048khz-gachi-rmx/dotto/master/docker-compose.yml
curl -O https://raw.githubusercontent.com/2048khz-gachi-rmx/dotto/master/.env.example
```

2. Copy the `.env.example` file to a `.env` file:
`cp .env.example .env`

3. Edit the `.env` file: put your Discord token, change the PostgreSQL password, optionally add S3 details to Minio settings.

4. `docker compose up -d`

`docker-compose.yml` is the deployment compose: the bot and Postgres, plus the AI
sandbox plumbing (sandbox image pre-pull, Docker API proxy, sandbox network and
egress firewall).

## AI sandbox

`/ai` runs LLM-generated commands (downloads, ffmpeg, uploads) in short-lived
Docker containers, which the deployment compose above is already wired for,
including the host path used for per-user storage (`/var/lib/dotto-sandbox`).
How the sandbox works and is hardened is documented in
`openspec/changes/ai-sandbox/`.

## Developing
Want to set up the project locally?
You'll find a Docker Compose file with auxilary services (PostgreSQL) ready to go in the `.docker/` folder.  
You'll probably also want to set up user secrets:
```sh
cd Dotto.Bot # need to be in the project to set user secrets

dotnet user-secrets set "Discord:Token" "YOURTOKENHERE"

# Optionally, set up MinIO credentials if you have an S3 server:
dotnet user-secrets set "Minio:BaseUrl" "YOUR_S3_URL"
dotnet user-secrets set "Minio:BucketName" "YOUR_BUCKET_NAME"
dotnet user-secrets set "Minio:Region" "YOUR_S3_REGION"
dotnet user-secrets set "Minio:AccessKey" "YOUR_ACCESS_KEY"
dotnet user-secrets set "Minio:SecretKey" "YOUR_SECRET_KEY"

# Optionally, set up Cobalt credentials to use for certain URLs (currently: only instagram)
dotnet user-secrets set "Downloader:Cobalt:BaseUrl" "YOUR_COBALT_URL"
dotnet user-secrets set "Downloader:Cobalt:ApiKey" "YOUR_COBALT_KEY"

# AI settings — model, API key, base URL
dotnet user-secrets set "Ai:BaseUrl" "https://openrouter.ai/api/v1"
dotnet user-secrets set "Ai:ApiKey" "YOUR_API_KEY"
dotnet user-secrets set "Ai:AssistantModel" "deepseek/deepseek-v4-flash-0731" # You're absolutely right—

# Optional per-assistant overrides (falls back to top-level Ai values)
# dotnet user-secrets set "Ai:ChatAssistant:BaseUrl" "https://api.neuralwatt.com/v1"
# dotnet user-secrets set "Ai:ChatAssistant:ApiKey" "YOUR_OTHER_API_KEY"
# dotnet user-secrets set "Ai:ChatAssistant:Model" "mechaepstein"
# dotnet user-secrets set "Ai:ChatAssistant:HistoryContextSize" "50"

# AI sandbox settings (optional — sandbox containers for LLM tool execution)
# Host directory for per-user persistent storage (/storage in the sandbox). The
# Docker daemon resolves it on the host, so mount the same absolute path into the
# bot container. Defaults: /var/lib/dotto-sandbox (Linux),
# %TEMP%\dotto-sandbox-storage (Windows).
dotnet user-secrets set "Ai:Sandbox:StorageBasePath" "%TEMP%\dotto-sandbox-storage"
```
