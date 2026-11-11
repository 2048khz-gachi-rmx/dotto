## Purpose

Manages the lifecycle of per-session sandbox Docker containers: eager per-caller storage provisioning, lazy container creation on first tool use, health polling, dispose-driven teardown, and orphan cleanup. A single scoped sandbox entity encapsulates the container and its lifecycle.

## ADDED Requirements

### Requirement: Scoped Sandbox Entity
The sandbox SHALL be represented by a single scoped service — one instance per agent invocation — that owns the Docker client and encapsulates the container's lifecycle. It SHALL implement `IAsyncDisposable`; disposing it SHALL stop and remove the container if one was started.

The entity SHALL expose two segments:
- `Metadata`: always present after initialization — the session ID, the caller ID, and the host directory bind-mounted into the container as `/storage`. Initialization SHALL provision that directory eagerly. It SHALL NOT start the container.
- `Container`: nullable, present only after `EnsureStartedAsync()` — the API URL, the container ID, and the operations allowed on the underlying container (execute command, health check, file get/put). Accessing a container operation SHALL ensure the container is started.

#### Scenario: One sandbox entity per invocation
- **WHEN** an agent invocation begins
- **THEN** a single scoped sandbox entity is created for that invocation

#### Scenario: Initialization does not start the container
- **WHEN** `ChatAssistant.Invoke()` initializes the sandbox metadata
- **THEN** no Docker container is created and `Container` remains null

#### Scenario: Container ops start the container lazily
- **WHEN** a container operation (e.g. `ExecuteCommand`, `DownloadMedia`, `UploadFile`) is invoked
- **THEN** the sandbox starts the container (via `EnsureStartedAsync`) before running the operation

#### Scenario: Disposal stops the container
- **WHEN** the sandbox entity is disposed (agent invocation completes)
- **THEN** the underlying container is stopped/removed

### Requirement: Persistent Per-Caller Storage
The sandbox SHALL create a per-caller storage directory at `SandboxOptions.StorageBasePath/{callerId}` during initialization and bind-mount it into the container as `/storage`. The directory SHALL persist across that caller's sessions. The bot SHALL only create the directory; it SHALL NOT read or write files inside it.

#### Scenario: Storage created on invocation
- **WHEN** `ChatAssistant.Invoke()` runs for a caller
- **THEN** `{StorageBasePath}/{callerId}` exists immediately and is available via `Metadata`

#### Scenario: Storage survives session teardown
- **WHEN** an agent invocation completes
- **THEN** the caller's storage directory and its contents remain on disk

#### Scenario: Storage mounted into the container
- **WHEN** the sandbox container is lazily started
- **THEN** the caller's storage directory is bind-mounted at `/storage` in the container

### Requirement: Ephemeral Work Directory
The container SHALL mount `/work` as a tmpfs sized by `SandboxOptions.WorkTmpfsMb`. Contents under `/work` SHALL NOT survive the container. The bot SHALL NOT depend on `/work` existing on the host.

#### Scenario: Scratch files are ephemeral
- **WHEN** a session ends and a new session starts
- **THEN** files written under `/work` in the earlier session are gone

### Requirement: Lazy Per-Session Container Creation
The sandbox SHALL NOT create the container at invocation time. Instead, the container SHALL be created lazily on first use of a container operation (`EnsureStartedAsync`). The container name SHALL be `sandbox-{sessionId}-{randomSuffix}`, where the random suffix avoids collisions between sessions with the same second-resolution ID.

#### Scenario: Container NOT created on invocation
- **WHEN** `ChatAssistant.Invoke()` is called
- **THEN** no Docker container is created yet

#### Scenario: Container created on first tool use
- **WHEN** the LLM calls a container-backed tool
- **THEN** a new Docker container is created with a `sandbox-{sessionId}-{randomSuffix}` name and the image specified in `SandboxOptions.ImageTag`

#### Scenario: Subsequent tool calls reuse same container
- **WHEN** a container-backed tool is called a second time
- **THEN** the existing container is reused (no second container created)

### Requirement: Health Polling
The sandbox SHALL poll `GET /ping` (with the container token) after the container starts. The container SHALL be considered ready when it responds with HTTP 200. The sandbox SHALL timeout after `SandboxOptions.ContainerStartTimeout` and throw if the container is not ready.

#### Scenario: Container becomes ready
- **WHEN** the sandbox container starts
- **THEN** the bot polls `/ping` every 200ms until it responds with HTTP 200

#### Scenario: Container start timeout
- **WHEN** the sandbox container does not respond to `/ping` within `ContainerStartTimeout`
- **THEN** the sandbox throws an error

### Requirement: Start Failure Cleanup
A container that was created but never became ready SHALL NOT be left running. If anything fails after the container was created (start call, API URL resolution, health polling), the sandbox SHALL stop/remove that container before propagating the error, tolerating an already-removed container. This is the last line of defense for the orphan case; the `SandboxReaper` covers the rest.

Sandbox-backed tools SHALL NOT let a start failure abort the agent run. They SHALL catch it, log it, and return the fixed tool result `"Execution sandbox failed to start; consider this tool disabled."` so the model can continue with other work instead of retrying a dead tool.

#### Scenario: Failed start leaves no container
- **WHEN** a container is created but `/ping` never succeeds within `ContainerStartTimeout`
- **THEN** the sandbox stops that container before throwing

#### Scenario: Start failure is a tool result, not an exception
- **WHEN** the Docker daemon is unreachable (or the image is missing, or the socket protector rejects the create) during a tool call
- **THEN** the tool returns `"Execution sandbox failed to start; consider this tool disabled."` and the agent run continues

#### Scenario: Disabled and broken sandboxes differ
- **WHEN** the sandbox is disabled by configuration
- **THEN** the tools are not registered at all (no tool result is produced)

### Requirement: Invoker-Owned Teardown
Disposal is the invoking consumer's obligation, not the agent wrapper's. The invoker SHALL dispose the scoped sandbox entity after the agent invocation completes, on success and on failure. Disposal SHALL:
1. Stop the container if it was started, with a 5-second grace period before kill; the container is auto-removed via `--rm`
2. Leave the caller's persistent storage directory untouched

Enqueued attachment bytes are only readable while the container is alive, so the invoker SHALL materialize every attachment before disposing. Disposal SHALL be idempotent, so DI scope disposal of the same entity is harmless.

#### Scenario: Cleanup after sandbox use
- **WHEN** the invoking handler finishes with the agent response
- **THEN** it disposes the sandbox, which stops the container if one was started

#### Scenario: Cleanup without sandbox use
- **WHEN** the invocation completes and the container was never started
- **THEN** disposal does nothing (the storage directory remains)

#### Scenario: Cleanup after failure
- **WHEN** `ChatAssistant.Invoke()` throws an exception, or attachment materialization throws
- **THEN** the handler still disposes the sandbox, stopping the container if it was started

#### Scenario: Teardown after attachments
- **WHEN** the agent enqueues attachments
- **THEN** the handler materializes them (Discord temp file or S3) before disposing the sandbox and stopping the container

### Requirement: Orphan Cleanup
A background hosted service (`SandboxReaper`) SHALL periodically (every 30 seconds) list running containers with label `dotto-sandbox=true` and stop any that have exceeded `SandboxOptions.WallClockTimeout` since creation, then delete the container explicitly (idempotently) instead of relying solely on `AutoRemove`, because a container the daemon failed to auto-remove is no longer visible to the running-only sweep. The reaper SHALL NOT delete persistent storage; the `dotto-session-id` label is used only for logging.

#### Scenario: Reaper kills orphaned container
- **WHEN** a sandbox container has been running for longer than `WallClockTimeout`
- **THEN** the reaper stops and removes it

#### Scenario: Reaper does not affect recent containers
- **WHEN** a sandbox container has been running for less than `WallClockTimeout`
- **THEN** the reaper leaves it running

#### Scenario: Reaper preserves storage
- **WHEN** the reaper stops an orphaned container
- **THEN** the associated caller storage directory is left untouched

### Requirement: Configurable Resource Limits
The container SHALL be created with resource limits from `SandboxOptions`:
- CPU: `SandboxOptions.CpuCount`
- Memory: `SandboxOptions.MemoryMb` (swap is disabled by setting it equal to memory)
- Process count: a hard limit of 256 PIDs
- `/work` tmpfs: `SandboxOptions.WorkTmpfsMb`
- `/tmp` tmpfs: 64MB

`MemoryMb` MUST be greater than `WorkTmpfsMb` because tmpfs pages count against the memory cgroup.

#### Scenario: Resource limits applied
- **WHEN** a sandbox container is created
- **THEN** its CPU, memory, pids, and tmpfs limits match the configured values

### Requirement: Sandbox DNS Resolvers
Sandbox containers SHALL use `SandboxOptions.DnsServers` (default `1.1.1.1`, `8.8.8.8`) instead of inheriting the host resolver, because egress containment drops private destinations and a LAN resolver inherited from the host would make every lookup in the sandbox fail.

#### Scenario: Private host resolver does not break the sandbox
- **WHEN** the host's resolver is a private address that the egress filter blocks
- **THEN** the sandbox still resolves public names through its configured resolvers

### Requirement: Sandbox Enable Toggle
`SandboxOptions.Enabled` SHALL behave as a kill switch for the sandbox, not as a runtime failure mode. When it is false, the keyed `AIAgent` SHALL be constructed with no sandbox-backed tools, the prompt SHALL omit tool instructions, `ChatAssistant.Invoke()` SHALL NOT provision storage or create a container, and `SandboxReaper` SHALL NOT start.

#### Scenario: Sandbox disabled
- **WHEN** `SandboxOptions.Enabled` is false
- **THEN** `ChatAssistant.Invoke()` skips sandbox initialization, no container is created, and the model is offered no sandbox-backed tools

### Requirement: Disabled Sandbox Guard
`Sandbox.InitializeAsync` and `Sandbox.EnsureStartedAsync` SHALL throw a `SandboxDisabledException` whose message names `Ai:Sandbox:Enabled=false` when the sandbox is disabled. Disposal SHALL remain safe.

#### Scenario: Disabled guard is actionable
- **WHEN** a code path calls `EnsureStartedAsync` while the sandbox is disabled
- **THEN** it fails with a message naming `Ai:Sandbox:Enabled=false` rather than "Sandbox is not initialized"

### Requirement: API Reachability
When `SandboxOptions.NetworkName` is set, the sandbox container SHALL join that Docker network and the bot SHALL reach the API at `http://{containerName}:8080`. When it is unset, the container SHALL publish port 8080 to a random loopback port and the bot SHALL reach it at `http://127.0.0.1:{port}`.

#### Scenario: Production uses the shared network
- **WHEN** `Ai:Sandbox:NetworkName` is set
- **THEN** no host port is published and the bot resolves the sandbox by container name

#### Scenario: Development uses loopback
- **WHEN** `Ai:Sandbox:NetworkName` is unset
- **THEN** the sandbox is reachable only via `127.0.0.1` on a random published port
