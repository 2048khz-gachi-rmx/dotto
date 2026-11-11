## Purpose

Streams a file out of the sandbox's `/work` or `/storage` directories and enqueues it for delivery. The invoking handler performs the actual Discord attachment (or S3 fallback for oversized files) after the agent run, because it owns the channel and upload context.

## ADDED Requirements

### Requirement: Enqueue File for Upload
The `UploadFile` tool SHALL accept a sandbox-relative path starting with `/work/` or `/storage/`, stream the file out of the sandbox over `GET /file`, and enqueue it as an attachment containing the stream, filename, and announced length. It SHALL return a confirmation such as `"Enqueued 'result.mp4' for upload."`

#### Scenario: Enqueue successful upload
- **WHEN** the LLM calls `UploadFile("/work/outputs/result.mp4")`
- **THEN** the file is streamed out of the sandbox, enqueued for upload, and the tool returns a confirmation

#### Scenario: File does not exist
- **WHEN** the requested sandbox path does not exist
- **THEN** the tool returns a file-not-found message and enqueues nothing

### Requirement: Path Validation
The tool SHALL reject any path that is empty, does not start with `/work/` or `/storage/`, or contains `..`. The sandbox server SHALL independently confine the resolved path to `/work` and `/storage`.

#### Scenario: Path traversal rejected
- **WHEN** the LLM calls `UploadFile("/work/../../etc/passwd")`
- **THEN** the tool returns an error and does not enqueue

#### Scenario: Path outside the sandbox roots rejected
- **WHEN** the LLM calls `UploadFile("/etc/passwd")`
- **THEN** the tool returns an error and does not enqueue

### Requirement: Attachment Budget
The handler SHALL enforce `Ai:ChatAssistant:MaxAttachments` (default 10). Attachments beyond the limit SHALL be discarded and their streams disposed; the response SHALL note how many were omitted.

#### Scenario: Too many attachments
- **WHEN** the agent enqueues more files than `MaxAttachments`
- **THEN** only the first `MaxAttachments` are processed and the response notes the omitted count

### Requirement: Upload Happens After Agent Run
The invoking handler SHALL, after the agent run completes, materialize each enqueued file:
- Files within the channel's Discord limit SHALL be copied into a bot-owned temporary file and sent as a Discord attachment.
- Files larger than the Discord limit but within the configured S3 maximum SHALL be uploaded to S3 inline, and the resulting link SHALL be included in the response.
- Files above `max(discordUploadLimit, S3 MaxFileSizeBytes)` SHALL be dropped and counted as oversized.
- If no S3 upload service is configured, files above the Discord limit SHALL be dropped.

A file above the Discord limit and above the S3 maximum SHALL NOT be silently treated as a Discord attachment.

#### Scenario: File within Discord limit uploaded as attachment
- **WHEN** the handler processes an enqueued file within the channel's Discord limit
- **THEN** it sends the file as a Discord attachment to the invoking channel

#### Scenario: Oversized file falls back to S3 link
- **WHEN** the handler processes a file larger than the Discord limit but within the S3 maximum
- **THEN** it uploads the file to S3 and includes the resulting link in the response

#### Scenario: Oversized file dropped when S3 unavailable
- **WHEN** the handler processes a file larger than the Discord limit and no S3 upload service is configured
- **THEN** the file stream is disposed and the file is dropped

### Requirement: S3 Object Naming
S3 uploads SHALL key objects by a randomly generated id rather than the original filename, and SHALL preserve the original filename as the download filename via `Content-Disposition`.

#### Scenario: Repeated filenames do not overwrite
- **WHEN** two different files with the same original filename are uploaded
- **THEN** they are stored under different S3 keys and both remain retrievable

### Requirement: Upload Logging
Every file the handler actually delivers (Discord attachment or S3 link) SHALL be recorded in the database the same way the download command records media: one row per delivered file with the invoking user, channel, message, delivery URL, and source. The source SHALL be the sandbox path the bytes came from when known (falling back to the file name). Dropped/oversized/omitted attachments SHALL NOT be recorded.

#### Scenario: Delivered files are recorded
- **WHEN** the handler sends an AI response with one Discord attachment and one S3 link
- **THEN** two records exist for that channel/message, one per delivered URL, attributed to the invoking user

#### Scenario: Dropped files are not recorded
- **WHEN** an attachment is dropped for exceeding the upload limit or the attachment budget
- **THEN** no record is written for it

### Requirement: Container Lifetime During Delivery
Enqueued attachment streams SHALL remain readable until the handler materializes them. The agent invocation SHALL NOT stop the container while attachments are pending; the handler SHALL dispose the sandbox after every attachment has been buffered or uploaded.

#### Scenario: Container stays alive for attachment delivery
- **WHEN** the agent returns attachments
- **THEN** the container remains running until the handler has materialized all of them
