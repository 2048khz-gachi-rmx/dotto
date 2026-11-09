## Purpose

Downloads media from URLs using the existing yt-dlp/Cobalt infrastructure and writes the output files to the sandbox's shared volume for further processing by the LLM.

## ADDED Requirements

### Requirement: Download to Sandbox Volume
The `DownloadMedia` tool SHALL use the existing `IDownloaderServiceFactory` to download media from a URL or a yt-dlp search query (e.g. `ytsearch:...`, `scsearch:...`) and write the output file(s) to the session's host directory under `downloads/`.

#### Scenario: Successful download
- **WHEN** the LLM calls `DownloadMedia("https://example.com/video.mp4")`
- **THEN** the file is downloaded via yt-dlp or Cobalt and written to `{sessionDir}/downloads/video.mp4`

#### Scenario: Search query download
- **WHEN** the LLM calls `DownloadMedia("ytsearch:funny cats")`
- **THEN** yt-dlp resolves the query and writes the result to `{sessionDir}/downloads/`

#### Scenario: Audio-only download
- **WHEN** the LLM calls `DownloadMedia("https://example.com/video.mp4", audioOnly: true)`
- **THEN** the file is downloaded as audio-only and written to `{sessionDir}/downloads/`

### Requirement: Downloader Priority
The tool SHALL respect the existing downloader priority from `DownloaderServiceFactory`: Instagram URLs use Cobalt first, all other URLs use yt-dlp first.

#### Scenario: Instagram URL uses Cobalt
- **WHEN** the LLM calls `DownloadMedia("https://www.instagram.com/p/abc123/")`
- **THEN** the Cobalt downloader is attempted first, with yt-dlp as fallback

#### Scenario: YouTube URL uses yt-dlp
- **WHEN** the LLM calls `DownloadMedia("https://youtube.com/watch?v=abc123")`
- **THEN** the yt-dlp downloader is attempted first, with Cobalt as fallback

### Requirement: Download Result Reporting
The tool SHALL return a string describing what was downloaded, including the sandbox-relative `/work/downloads/...` path(s) so the agent can feed them to `ExecuteCommand` or `UploadFile`.

#### Scenario: Single file download
- **WHEN** a single file is downloaded
- **THEN** the tool returns a confirmation message containing the file's `/work/downloads/...` path

#### Scenario: Multiple files downloaded
- **WHEN** multiple files are downloaded (e.g., playlist)
- **THEN** the tool returns a confirmation message listing each file's `/work/downloads/...` path