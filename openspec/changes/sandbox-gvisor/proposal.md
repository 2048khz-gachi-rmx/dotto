# Proposal

## Why

The sandbox executes model-generated commands, but it still runs on `runc`, whose only isolation boundary is the shared host kernel. `SandboxOptions.UseGVisorRuntime` exists but was never measured, so gVisor stayed off by default. The benchmark in `decision.md` closes that question: gVisor costs 8.9–10.7% on an isolated single-thread transcode, is 6.2% *faster* under the bot's real one-CPU quota, and uses roughly 470MB less peak memory there.

Enabling it also exposes the last structural gap: in production the bot reaches the sandbox API over TCP on the `sandbox` bridge. That forces the bot onto the bridge, which in turn rules out `enable_icc=false`, leaving sibling sandboxes mutually reachable (known gap 10.5 in `ai-sandbox`). gVisor's `--host-uds=create` makes a Unix-domain-socket control channel viable, so the runtime and the control channel can change together instead of shipping gVisor on top of a weaker network topology.

## What Changes

- **Default runtime:** `UseGVisorRuntime` defaults to `true` (`SandboxOptions`, `appsettings.json`). Startup logs an error if the daemon has no `runsc` runtime; sandbox-backed tools then fail with the existing start-failure result. **BREAKING**: hosts must install and register `runsc` (with `--host-uds=create`) before upgrading; there is no silent `runc` fallback — an operator who wants the old behavior must set `UseGVisorRuntime=false` explicitly.
- **UDS control channel:** in production the sandbox binds `<ipc-base>/<session>/sandbox.sock` and the bot connects through a bind-mounted directory. The token header and HTTP contract are unchanged. Dev/Windows keeps the loopback TCP fallback.
- **Network tightening:** the bot leaves the `sandbox` network; that network gets `enable_icc=false`. The existing `br-sandbox` egress filter stays the internet/LAN boundary.
- **File transfer:** the sandbox server uses a 1MiB chunk for `GET`/`PUT /file` (exec pipe pumps keep 64KiB), recovering most of gVisor's bulk-transfer penalty.
- **Docs and gaps:** deployment prerequisite in `.docker/README.md`, sandbox image README updated, `ai-sandbox` tasks 10.5/10.6 closed.

## Capabilities

### New Capabilities

- `sandbox-runtime`: which container runtime the sandbox uses, the failure mode when it is unavailable, and the hardening invariants that must hold under either runtime.
- `sandbox-isolation`: how the host reaches the sandbox (transport, authentication, socket lifecycle) and what the sandbox cannot reach (peers, host, LAN) while internet egress stays available.

### Modified Capabilities

None. `openspec/specs/` is currently empty because `ai-sandbox` has not been archived; the requirements below extend that design rather than modifying an existing archived capability.

## Impact

- **Code:** `Dotto.Ai/Sandbox/Sandbox.cs`, `SandboxContainer.cs`, `ISandboxContainer.cs`, `Dotto.Ai/Settings/SandboxOptions.cs`, `Dotto.Ai/sandbox-image/server.py`, `Dotto.Bot/appsettings.json`.
- **Deployment:** `docker-compose.yml` (bot network membership, IPC volume, `enable_icc`, env), `.docker/README.md`, sandbox image README; host prerequisite is a registered `runsc` runtime with `--host-uds=create`.
- **Tests:** sandbox container parameters, start/cleanup, and new UDS transport coverage; no database or API-contract changes for Discord.
