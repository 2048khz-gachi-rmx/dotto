## Purpose

Downloads media from URLs using the existing yt-dlp/Cobalt infrastructure and streams the output files into the sandbox's ephemeral `/work/downloads/` directory for further processing by the LLM.

## ADDED Requirements

### Requirement: Download into the Sandbox
The `DownloadMedia` tool SHALL use the existing downloader infrastructure: absolute http(s) URLs SHALL go through `IMediaDownloader.DownloadMediaFromUrl` (which applies URL corrections and reports structured errors instead of throwing), and yt-dlp search queries (e.g. `ytsearch:...`, `scsearch:...`) SHALL go straight to `IDownloaderServiceFactory` because they are not URIs. It SHALL start the sandbox container if needed and stream each downloaded file into the sandbox over `PUT /file` at `/work/downloads/{filename}`. The bot SHALL NOT write the file to a host directory of its own choosing beyond the downloader's own staging temp file.

#### Scenario: Successful download
- **WHEN** the LLM calls `DownloadMedia("https://example.com/video.mp4")`
- **THEN** the file is downloaded via yt-dlp or Cobalt and streamed to `/work/downloads/video.mp4` in the sandbox

#### Scenario: Search query download
- **WHEN** the LLM calls `DownloadMedia("ytsearch:funny cats")`
- **THEN** yt-dlp resolves the query and the result is streamed to `/work/downloads/`

#### Scenario: Audio-only download
- **WHEN** the LLM calls `DownloadMedia("https://example.com/video.mp4", audioOnly: true)`
- **THEN** the file is downloaded as audio-only and streamed to `/work/downloads/`

### Requirement: Downloader Priority
The tool SHALL respect the existing downloader priority from the downloader factory: Instagram URLs use Cobalt first, all other URLs use yt-dlp first. If a downloader returns no files or throws, the next downloader SHALL be attempted.

#### Scenario: Instagram URL uses Cobalt
- **WHEN** the LLM calls `DownloadMedia("https://www.instagram.com/p/abc123/")`
- **THEN** the Cobalt downloader is attempted first, with yt-dlp as fallback

#### Scenario: YouTube URL uses yt-dlp
- **WHEN** the LLM calls `DownloadMedia("https://youtube.com/watch?v=abc123")`
- **THEN** the yt-dlp downloader is attempted first, with Cobalt as fallback

### Requirement: Download Limits
A single download SHALL be limited to 100 MiB. When there are no usable results from any downloader, the tool SHALL return a failure message and SHALL NOT start the sandbox container.

Fatal downloader errors (`MediaErrorCode.DownloaderError` — the ones the model can act on, e.g. an unsupported URL) SHALL be appended to the failure message. Non-fatal errors (`MediaErrorCode.ServiceUnavailable` — a backend being down while another was tried) SHALL be suppressed to keep the tool result focused.

A failure to start the container, or a rejection while streaming the bytes in (e.g. `/work` full), SHALL be returned as a tool result describing the cause, not thrown, so the agent run survives.

#### Scenario: Oversized download refused
- **WHEN** the media exceeds the 100 MiB limit
- **THEN** the downloader does not produce a file and the tool reports failure

#### Scenario: No downloader succeeded
- **WHEN** every downloader throws or returns no files
- **THEN** the tool returns `"Failed to download media from the provided URL."` and no container is started

#### Scenario: Actionable downloader errors reach the model
- **WHEN** the only attempts failed with a downloader error
- **THEN** the tool result contains the failure sentence plus the downloader's message, and none of the unavailable-backend noise

#### Scenario: Sandbox write failure is a tool result
- **WHEN** the sandbox rejects the upload (`500`, e.g. `No space left on device`)
- **THEN** the tool returns the server's message and the agent run continues

### Requirement: Filename Handling
The tool SHALL let the sandbox server resolve and deduplicate filenames. When a target name already exists in `/work/downloads/`, the server SHALL append a numeric suffix and return the effective path.

#### Scenario: Duplicate filename gets a suffix
- **WHEN** a file is streamed to a path that already exists
- **THEN** the sandbox stores it under a suffixed name and the tool reports that effective path

### Requirement: Download Result Reporting
The tool SHALL return a string describing what was downloaded, including the sandbox-relative `/work/downloads/...` path(s) so the agent can feed them to `ExecuteCommand` or `UploadFile`. Every downloaded file SHALL be disposed after its bytes are streamed.

#### Scenario: Single file download
- **WHEN** a single file is downloaded
- **THEN** the tool returns a confirmation message containing the file's `/work/downloads/...` path

#### Scenario: Multiple files downloaded
- **WHEN** multiple files are downloaded (e.g., playlist)
- **THEN** the tool returns a confirmation message listing each file's `/work/downloads/...` path
