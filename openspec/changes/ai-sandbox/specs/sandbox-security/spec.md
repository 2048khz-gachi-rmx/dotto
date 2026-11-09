## Purpose

Hardens sandbox containers against breakout and privilege escalation, prevents credential leakage between bot and sandbox, and adds a docker-socket-protector sidecar to limit the bot's Docker API access.

## ADDED Requirements

### Requirement: Container Hardening
Every sandbox container SHALL be created with the following security settings:
- Read-only root filesystem
- All Linux capabilities dropped
- `no-new-privileges` security option enabled
- tmpfs mount at `/tmp` with 64MB size limit
- Empty environment (zero environment variables)
- `dotto-sandbox=true` label applied
- `dotto-session-id={sessionId}` label applied (used by the reaper to locate the host session directory)
- `--rm` for auto-removal on stop

#### Scenario: Hardened container created
- **WHEN** a sandbox container is created
- **THEN** it has `ReadonlyRootfs: true`, `CapDrop: ALL`, `SecurityOpt: ["no-new-privileges:true"]`, and `Env: []`

### Requirement: Credential Isolation
The sandbox container SHALL NOT have access to:
- The Discord bot token
- The yt-dlp cookies file
- Database credentials
- S3 access keys
- LLM API keys
- The Docker socket

#### Scenario: No environment variables in sandbox
- **WHEN** a sandbox container is created
- **THEN** its environment variable list is empty

#### Scenario: No Docker socket mounted
- **WHEN** a sandbox container is created
- **THEN** the Docker socket is NOT mounted in the container

### Requirement: Docker Socket Protection
The bot SHALL communicate with the Docker daemon through `docker-socket-protector`, which SHALL whitelist only the following Docker API endpoints:
- `POST /containers/create`
- `POST /containers/{id}/start`
- `GET /containers/{id}/json`
- `GET /containers/json`
- `DELETE /containers/{id}`
- `POST /containers/{id}/stop`

Docker exec endpoints are NOT whitelisted — the bot communicates with sandboxes via HTTP (the Python server's `POST /execute`), not Docker exec.

#### Scenario: Unauthorized endpoint rejected
- **WHEN** the bot attempts to call `POST /images/create` (pull image)
- **THEN** the socket protector rejects the request

### Requirement: Path Sanitization in Host-Facing Tools
Tools that read files from the host filesystem (e.g. `UploadFile`) SHALL validate and sanitize
paths from the LLM. Paths containing `..` or resolving outside the session directory SHALL be rejected.
Tools that execute commands inside the sandbox (e.g. `ExecuteCommand`) do not need explicit
path sanitization — the container's read-only rootfs and bind mount already provide isolation.

#### Scenario: Upload tool rejects path traversal
- **WHEN** the LLM calls `UploadFile` with a path containing `..`
- **THEN** the tool returns an error without uploading