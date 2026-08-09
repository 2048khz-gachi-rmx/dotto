## Purpose

Manages the lifecycle of per-session sandbox Docker containers: lazy creation on first tool use, health polling, dispose-driven teardown, and orphan cleanup. A single scoped sandbox entity encapsulates the container and its lifecycle.

## ADDED Requirements

### Requirement: Scoped Sandbox Entity
The sandbox SHALL be represented by a single scoped service — one instance per agent invocation — that owns the Docker client and encapsulates the container's lifecycle. It SHALL implement `IAsyncDisposable`; disposing it SHALL stop and remove the container and delete the host session directory. The entity SHALL expose two segments:
- `Metadata`: always present — the session ID and the eagerly-created host session directory. Operations that only need the filesystem (e.g. `DownloadMedia`, `UploadFile`) SHALL read from here and MUST NOT start the container.
- `Container`: nullable, present only after `EnsureStartedAsync()` — the API URL, the container ID, and the operations allowed on the underlying container (e.g. execute command, health check). Accessing these SHALL ensure the container is started.

#### Scenario: One sandbox entity per invocation
- **WHEN** an agent invocation begins
- **THEN** a single scoped sandbox entity is created for that invocation

#### Scenario: Filesystem ops do not start the container
- **WHEN** `DiscordTools.DownloadMedia()` reads the session directory
- **THEN** no Docker container is started and `Container` remains null

#### Scenario: Container ops start the container lazily
- **WHEN** a container operation (e.g. `ExecuteCommand`) is accessed
- **THEN** the sandbox starts the container (via `EnsureStartedAsync`) before running the operation

#### Scenario: Disposal tears down container
- **WHEN** the sandbox entity is disposed (agent invocation completes)
- **THEN** the underlying container is stopped/removed and the host directory is deleted

### Requirement: Lazy Per-Session Container Creation
The sandbox SHALL NOT create the container at invocation time. Instead, the container SHALL be created lazily on first use of a container operation (`EnsureStartedAsync`). The container SHALL be created with a unique session ID used as the container name suffix.

#### Scenario: Container NOT created on invocation
- **WHEN** `ChatAssistant.Invoke()` is called
- **THEN** no Docker container is created yet

#### Scenario: Container created on first tool use
- **WHEN** the LLM calls `TerminalTools.ExecuteCommand()`
- **THEN** a new Docker container is created with name `sandbox-{sessionId}` and the image specified in `SandboxOptions.ImageTag`

#### Scenario: Subsequent tool calls reuse same container
- **WHEN** `ExecuteCommand()` is called a second time
- **THEN** the existing container is reused (no second container created)

### Requirement: Session Host Directory
The host session directory SHALL be created eagerly on invocation and exposed via the sandbox entity's `Metadata`, so tools that only need the filesystem (`DownloadMedia`, `UploadFile`) can use it without starting the container. The directory SHALL be bind-mounted into the container as `/work` when the container is eventually created.

#### Scenario: Host directory created on invocation
- **WHEN** `ChatAssistant.Invoke()` is called
- **THEN** the host directory `/tmp/dotto-sandbox/{sessionId}` exists immediately and is available via `Metadata`

#### Scenario: Host directory mounted on lazy start
- **WHEN** the sandbox container is lazily started
- **THEN** the host directory is bind-mounted to `/work` in the container

### Requirement: Health Polling
The sandbox SHALL poll `GET /ping` after the container starts. The container SHALL be considered ready when it responds with HTTP 200. The sandbox SHALL timeout after `SandboxOptions.ContainerStartTimeout` and throw if the container is not ready.

#### Scenario: Container becomes ready
- **WHEN** the sandbox container starts
- **THEN** the bot polls `/ping` every 200ms until it responds with HTTP 200

#### Scenario: Container start timeout
- **WHEN** the sandbox container does not respond to `/ping` within `ContainerStartTimeout`
- **THEN** the sandbox throws an error and destroys the container

### Requirement: Dispose-Driven Teardown
After the agent invocation completes (success or failure), the agent scope SHALL dispose the sandbox entity. Disposal SHALL:
1. Stop the container if it was started (5-second grace period before kill; auto-removed via `--rm`)
2. Wait for container removal
3. Delete the host session directory

#### Scenario: Cleanup after sandbox use
- **WHEN** `ChatAssistant.Invoke()` completes and the container was started
- **THEN** disposing the sandbox entity stops the container and deletes the host directory

#### Scenario: Cleanup without sandbox
- **WHEN** `ChatAssistant.Invoke()` completes and the container was never started
- **THEN** disposing the sandbox entity deletes only the host directory (no container to stop)

#### Scenario: Cleanup after failure
- **WHEN** `ChatAssistant.Invoke()` throws an exception
- **THEN** the agent scope still disposes the sandbox entity, stopping the container and deleting the host directory

### Requirement: Orphan Cleanup
A background hosted service (`SandboxReaper`) SHALL periodically (every 30 seconds) list running containers with label `dotto-sandbox=true` and kill any that have exceeded `SandboxOptions.WallClockTimeout` since creation. When a container is reaped, the reaper SHALL also delete the orphaned host session directory, locating it via the `dotto-session-id` container label.

#### Scenario: Reaper kills orphaned container
- **WHEN** a sandbox container has been running for longer than `WallClockTimeout`
- **THEN** the reaper kills and removes it

#### Scenario: Reaper does not affect recent containers
- **WHEN** a sandbox container has been running for less than `WallClockTimeout`
- **THEN** the reaper leaves it running

### Requirement: Configurable Resource Limits
The container SHALL be created with resource limits from `SandboxOptions`:
- CPU: `SandboxOptions.CpuCount`
- Memory: `SandboxOptions.MemoryMb`

#### Scenario: Resource limits applied
- **WHEN** a sandbox container is created
- **THEN** its CPU limit is set to `SandboxOptions.CpuCount` and memory limit to `SandboxOptions.MemoryMb`

### Requirement: Sandbox Enable Toggle
`SandboxOptions.Enabled` SHALL gate sandbox initialization. When `Enabled` is false, `ChatAssistant.Invoke()` SHALL NOT initialize the sandbox entity, create a host session directory, or start a container.

#### Scenario: Sandbox disabled
- **WHEN** `SandboxOptions.Enabled` is false
- **THEN** `ChatAssistant.Invoke()` skips sandbox initialization and disposal entirely
