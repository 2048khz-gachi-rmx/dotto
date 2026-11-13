# Design

## Context

See `proposal.md` — Why and `decision.md` for the measurements behind them. The current state that shapes the approach:

- `Sandbox.cs` creates one container per session with `HostConfig.Runtime = "runsc"` when `UseGVisorRuntime` is set (`Dotto.Ai/Sandbox/Sandbox.cs:176-177`); the flag defaults to `false` (`Dotto.Ai/Settings/SandboxOptions.cs:50`, `Dotto.Bot/appsettings.json:72`).
- Production reaches the API over TCP: the sandbox joins the `sandbox` bridge, the bot joins it too, and `ResolveApiUrlAsync` uses `http://<container>:8080` (`Sandbox.cs:338-350`). The egress firewall pins `br-sandbox` and drops private destinations (`.docker/sandbox-egress.sh`), but inter-container traffic stays allowed because that same bridge carries bot↔sandbox (Decision 14 in `openspec/changes/ai-sandbox/design.md`).
- The sandbox server (`Dotto.Ai/sandbox-image/server.py`) is a stdlib `http.server` on TCP 8080, authenticated by `X-Sandbox-Token`, with `CHUNK_SIZE = 64 * 1024` shared by the exec pipe pumps and the file endpoints.
- The bot and the Docker daemon share host paths (`/var/lib/dotto-sandbox`) so a bind source resolves identically inside the bot container and on the host.

## Goals / Non-Goals

**Goals:**
- Ship gVisor as the default without an implicit security downgrade when it is unavailable.
- Make the control channel unreachable from the sandbox network, so ICC can finally be disabled.
- Keep the change deployable on an existing single-host compose stack; no new orchestrator or network plugin.

**Non-Goals:**
- MicroVM isolation (Firecracker/Kata) — separate evaluation.
- Changing the HTTP contract, token scheme, timeout tiers, or storage/quota semantics.
- A hard per-file quota, storage GC, or cross-caller storage isolation (still `ai-sandbox` gaps 10.1–10.4).
- Replacing the egress firewall with nftables or a gateway container; the existing `br-sandbox` rules remain the boundary.

## Decisions

### Decision 1: Default to gVisor and fail closed

`UseGVisorRuntime` becomes `true` in both `SandboxOptions` and `appsettings.json`. A missing `runsc` is not repaired by falling back to `runc`: the bot runs a one-shot startup check against the daemon's runtime list (`docker info` → `Runtimes`) and logs an error naming `runsc` and the install step. Container creation then fails through the existing "failed start is a tool result" path (Decision 16 in `ai-sandbox`). A new `appsettings.Development.json` pins `UseGVisorRuntime=false` so the existing `dotnet run` Development profile keeps working on Windows/Docker Desktop and Podman, where `runsc` is unavailable; production uses the shipped default.

**Alternatives:** automatic `runc` fallback (rejected: turns a misconfigured deployment into a silent isolation downgrade); keeping the default `false` and documenting opt-in (rejected: the measured cost does not justify leaving the weaker default).

### Decision 2: Per-session Unix socket, TCP only for development

In production the sandbox binds `/run/dotto/sandbox.sock`, backed by a host directory `<IpcBasePath>/<sessionId>` bind-mounted into the container. The bot connects to the same host path. `IpcBasePath` defaults next to storage (`/var/lib/dotto-sandbox-ipc` on Linux, a temp dir on Windows) and the compose file mounts it into the bot container. The transport is derived rather than separately configured: a configured `NetworkName` selects the socket, and an unset one selects the `127.0.0.1` TCP path used by Windows/Podman development. This mirrors the existing `ResolveApiUrlAsync` split and avoids a switch that could put production back on a network-reachable API.

The Python server gains a `SANDBOX_SOCKET` mode: a `socketserver.ThreadingUnixStreamServer` with `BaseHTTPRequestHandler`, `protocol_version = "HTTP/1.1"`, unlink-before-bind, `chmod 0666` on the socket, and the same token check. `ThreadingUnixStreamServer` is used rather than subclassing `HTTPServer`, whose `server_bind` assumes an `(host, port)` tuple. The handler must override `address_string` (AF_UNIX `client_address` is an empty string, so the base implementation's `client_address[0]` raises and the connection aborts).

On the .NET side, `SandboxContainer` receives an endpoint (URI or socket path). For a socket, `SocketsHttpHandler.ConnectCallback` opens a `UnixDomainSocketEndPoint`; the request base URI is a synthetic `http://sandbox/` because no network resolution is involved. `AllowAutoRedirect = false` and `UseCookies = false` stay, so the token cannot be redirected. Timeouts and response buffering are unchanged.

**Alternatives:** keep TCP and enable ICC anyway (rejected: it severs the bot's own calls, verified); `docker exec`/stdin for exec plus tar over `put_archive` (rejected: reintroduces container-API surface and loses HTTP streaming/error semantics); host-mounted socket directory read-only (rejected: the sandbox must create the socket).

### Decision 3: Remove the bot from the sandbox bridge and disable ICC

The bot service loses its `sandbox` network membership; the network gains `com.docker.network.bridge.enable_icc=false` in `driver_opts`. The egress firewall stays as-is: it still scopes internet egress and blocks private destinations, and its same-bridge `RETURN` becomes harmless (no legitimate same-bridge traffic remains). `sandbox-image` keeps its network membership only because it is a one-shot pre-pull service.

ICC is a network-creation property: changing `driver_opts` requires deleting and recreating the `sandbox` network, which the migration plan calls out.

**Alternatives:** per-session networks (rejected in `ai-sandbox` Decision 14: multiplies networks and does not fix LAN reachability); Docker bridge port isolation via a network-event hook (works but is runtime state Docker recreates; ICC is declarative and covers the same case).

### Decision 4: 1MiB file chunks, 64KiB exec pumps

`FILE_CHUNK_SIZE = 1024 * 1024` is used only by `_get_file` and `_put_file`. The exec pumps keep `CHUNK_SIZE = 64 * 1024` because their tail buffer is 40KB (`MAX_OUTPUT_BYTES`), so larger reads only increase copy cost. Measured effect: gVisor bulk transfer recovers from 226–376MiB/s to 447–1026MiB/s at 1MiB; 4–8MiB adds nothing.

**Alternatives:** one global larger chunk (rejected: the exec path gains nothing and the 40KB tail would discard more per read); `sendfile`/`splice` (rejected for now: marginal under gVisor's gofer and a larger rewrite of the streaming loop).

### Decision 5: Keep the one-CPU default and document the quota interaction

`CpuCount` stays `1.0`. gVisor derives the guest CPU count from the cgroup quota as `max(2, ceil(quota))`, which is why it is faster under the current configuration; `runc` exposes all host CPUs and oversubscribes. The option's documentation notes the interaction, and the runc fallback guidance is to set an explicit ffmpeg thread count or raise `CpuCount`. No automatic thread injection into model commands is introduced.

### Decision 6: Socket directories follow the container lifecycle

The bot creates `<IpcBasePath>/<sessionId>` before container start (mode `0777`, matching the storage directory rule: bot is root, sandbox is non-root), removes it in `DisposeAsync` and on the start-failure path before rethrowing, and a startup sweep removes directories older than `WallClockTimeout+ContainerStartTimeout` so a crashed bot does not accumulate them. The reaper keeps its current responsibility (stop labelled containers); it does not need to know about sockets because the startup sweep is sufficient for crash recovery.

## Risks / Trade-offs

- **Hosts without `runsc` lose all sandbox tools after upgrade** → startup logs the exact fix; `.docker/README.md` documents the prerequisite; rollback is one setting (`UseGVisorRuntime=false`).
- **`enable_icc=false` also cuts the sandbox off from Docker's embedded DNS (`127.0.0.11`)** → the sandbox pins public resolvers (`1.1.1.1`, `8.8.8.8`), so name resolution should be unaffected; verify `dig` during rollout. If a path still needs embedded DNS, allow `127.0.0.11` explicitly rather than re-enabling ICC.
- **Socket directory leaks on SIGKILL** → startup sweep keyed on directory mtime.
- **AF_UNIX `sun_path` is limited to ~108 bytes** → session directories are short and the host rejects an over-long socket path with a clear startup error instead of a raw `ENAMETOOLONG`.
- **`--host-uds=create` lets the sandbox bind sockets on writable gofer mounts** (`/storage`, the IPC dir) → host never connects to an unexpected path; per-session directories and the token remain the trust boundary. `open`/`all` are explicitly not used.
- **gVisor bulk-transfer regression** → 1MiB chunk fix in the same change; the residual 3–5x versus `runc` only affects very large files.
- **Compose network recreation** → `docker compose down` + `docker network rm sandbox` + `up -d`; documented so an in-place `up -d` is not mistaken for a complete rollout.
- **Windows/Podman development** → UDS is selected only when `NetworkName` is configured; the unset case keeps the loopback TCP path unchanged.

## Migration Plan

1. Host: install the pinned gVisor release and register `runsc` with runtime args `--host-uds=create`; restart Docker; confirm `docker info` lists `runsc`.
2. Rebuild/publish `dotto-sandbox:latest` (server.py changes) and pull it on the host.
3. Update compose: add the IPC bind mount to the bot, remove `sandbox` from the bot's networks, add `enable_icc=false`.
4. `docker compose down`, `docker network rm sandbox`, `docker compose up -d`; then verify: `docker info` runtimes, `/ping` over the socket, `curl` to a public host, `nc` to a sibling sandbox (blocked), `curl` to a LAN address (blocked), 100MiB `PUT`/`GET`.
5. Rollback: `Ai__Sandbox__UseGVisorRuntime=false` for the runtime alone; revert the compose commit for the transport (TCP requires the bot back on the bridge).

## Open Questions

- Whether `CpuCount` should default above `1.0` for transcode-heavy deployments. Deferred: it is a tuning change that does not alter the approach or the specs; re-measure after the runtime switch.
