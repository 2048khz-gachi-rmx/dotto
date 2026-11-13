# Spec Delta

## Purpose

Defines which container runtime the execute sandbox uses by default, how an unavailable runtime surfaces, and the container-hardening invariants that hold regardless of runtime.

## ADDED Requirements

### Requirement: gVisor is the default sandbox runtime

Sandbox containers SHALL be created with the gVisor runtime (`runsc`) by default. The only way to run on the default Docker runtime (`runc`) SHALL be an explicit operator setting (`Ai:Sandbox:UseGVisorRuntime=false`).

#### Scenario: Default configuration uses gVisor

- **WHEN** a sandbox container is started with the shipped configuration
- **THEN** the container's host configuration names `runsc` as its runtime

#### Scenario: Explicit opt-out uses runc

- **WHEN** the operator sets `Ai:Sandbox:UseGVisorRuntime=false`
- **THEN** the container is created without a custom runtime and runs on the Docker default (`runc`)

### Requirement: Missing runtime fails closed

If gVisor is enabled but the Docker daemon has no `runsc` runtime registered, the sandbox SHALL fail to start and SHALL NOT fall back to `runc` silently. The bot SHALL log the condition at startup, and the invoking tool SHALL return the standard start-failure result rather than an exception.

#### Scenario: runsc is not registered

- **WHEN** `UseGVisorRuntime` is true and the daemon rejects the container because `runsc` is unknown
- **THEN** the tool returns `"Execution sandbox failed to start; consider this tool disabled."` and the failure is logged with the missing runtime named

#### Scenario: Startup detects a missing runtime

- **WHEN** the bot starts with gVisor enabled and the daemon reports no `runsc` in its runtime list
- **THEN** startup logs an error identifying the missing runtime (the bot still starts, so a later fix does not require a restart to change configuration)

### Requirement: Hardening invariants hold under either runtime

Every sandbox container SHALL be created with a read-only root filesystem, all Linux capabilities dropped, `no-new-privileges`, a PID limit, and a memory limit equal to the memory-swap limit (no swap), independent of the selected runtime.

#### Scenario: Hardened host configuration

- **WHEN** a sandbox container is created
- **THEN** its host configuration has a read-only rootfs, `CapDrop=["ALL"]`, `no-new-privileges:true`, a bounded PID limit, and `Memory == MemorySwap`

#### Scenario: Invariants survive runtime selection

- **WHEN** the same configuration runs with `runsc` and then with `runc`
- **THEN** both containers report the same hardening fields
