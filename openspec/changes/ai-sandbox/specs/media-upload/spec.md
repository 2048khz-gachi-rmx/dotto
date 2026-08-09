## Purpose

Accepts a file path relative to the sandbox's shared volume and enqueues the file for upload to Discord. The actual Discord attachment (or S3 fallback for oversized files) is performed after the agent run by the invoking handler, which owns the channel/upload context.

## ADDED Requirements

### Requirement: Enqueue File for Upload
The `UploadFile` tool SHALL accept a sandbox-relative `/work/...` path, validate it resolves within the session directory, and enqueue the file onto the session's attachment list so it is uploaded after the agent finishes.

#### Scenario: Enqueue successful upload
- **WHEN** the LLM calls `UploadFile("/work/outputs/result.mp4")`
- **THEN** the file is enqueued for upload and the tool returns a confirmation such as `"Enqueued 'result.mp4' for upload."`

### Requirement: Path Validation
The tool SHALL validate that the provided path is a sandbox-relative `/work/...` path resolving within the session's host directory. Paths containing `..` or absolute paths outside the session directory SHALL be rejected.

#### Scenario: Path traversal rejected
- **WHEN** the LLM calls `UploadFile("/work/../../etc/passwd")`
- **THEN** the tool returns an error and does not enqueue

### Requirement: Upload Happens After Agent Run
The invoking handler SHALL, after the agent run completes, upload each enqueued file as a Discord attachment when it is within the channel's size limit, and fall back to S3 (posting a link) when it exceeds the limit. When no S3 upload service is configured, oversized files SHALL be silently dropped (the stream is disposed and the file is not uploaded).

#### Scenario: File within Discord limit uploaded as attachment
- **WHEN** the handler processes an enqueued file within the channel's Discord limit
- **THEN** it sends the file as a Discord attachment to the invoking channel

#### Scenario: Oversized file falls back to S3 link
- **WHEN** the handler processes an enqueued file larger than the channel's Discord limit and an S3 upload service is configured
- **THEN** it uploads the file to S3 and the resulting link is included in the response

#### Scenario: Oversized file dropped when S3 unavailable
- **WHEN** the handler processes an enqueued file larger than the channel's Discord limit and no S3 upload service is configured
- **THEN** the file stream is disposed and the file is silently dropped (not uploaded)
