# Tasks

## 1. Runtime default and host prerequisite

- [x] 1.1 Flip `UseGVisorRuntime` to `true` in `Dotto.Ai/Settings/SandboxOptions.cs:50` and `Dotto.Bot/appsettings.json:72`, and set `UseGVisorRuntime = false` explicitly in the option builders of the explicit integration suite (`Dotto.Tests/IntegrationTests/Ai/SandboxIntegrationTests.cs`) so it still runs on runc-only hosts. Verify: `SandboxContainerParametersTests` asserts `Runtime == "runsc"` by default and `null` when the option is false, and the test passes.
- [x] 1.2 Add `Dotto.Bot/appsettings.Development.json` pinning `Ai:Sandbox:UseGVisorRuntime=false`, and include it as a `Content` item in `Dotto.Bot/Dotto.Bot.csproj` next to `appsettings.json`. Verify: the file is copied to the build output and a configuration-binding test resolves `false` under the Development environment.
- [x] 1.3 Add a startup probe (hosted service or startup step) that resolves `IDockerClient`, calls `System.GetSystemInfoAsync()`, and logs an error naming `runsc` and the install step when `UseGVisorRuntime` is true but the daemon's runtime list lacks it; no probe when the sandbox is disabled. Verify: unit tests with NSubstitute cover present/missing/disabled and no exception escapes startup.
- [x] 1.4 Document the prerequisite in `.docker/README.md`: install the pinned gVisor release, register `runsc` with `runsc install -- --host-uds=create`, restart Docker, confirm `docker info` lists `runsc`, and explain the Development override. Verify: the documented commands run as written on a Linux host, or are explicitly marked manual-check. (Install/restart steps are manual-check: this host has no runsc.)

## 2. Unix-socket server (sandbox image)

- [x] 2.1 Add socket mode to `Dotto.Ai/sandbox-image/server.py`: when `SANDBOX_SOCKET` is set, unlink any stale path, bind a `ThreadingUnixStreamServer` with `BaseHTTPRequestHandler`, `protocol_version = "HTTP/1.1"`, `chmod 0666`, and an `address_string` override (AF_UNIX `client_address` is an empty string and the base implementation raises), and keep the TCP server when it is unset. Verify: `docker run -v <tmp>:/run/dotto -e SANDBOX_SOCKET=/run/dotto/sandbox.sock ...` then `curl --unix-socket <tmp>/sandbox.sock http://localhost/ping` returns 200, and `/execute` works over the socket.
- [x] 2.2 Keep the token check identical on both transports: missing or invalid `X-Sandbox-Token` returns 401 with `Connection: close` and no command runs. Verify: `curl --unix-socket` without the header gets 401, with the header gets 200.
- [x] 2.3 Update `Dotto.Ai/sandbox-image/README.md` with the socket transport (`SANDBOX_SOCKET`), the unchanged auth requirement, and a runnable `curl --unix-socket` example. Verify: every command in the README runs as written against the built image. (Socket block run as written; the TCP block was exercised with --network host because this host has no docker0.)

## 3. Unix-socket client and sandbox lifecycle (.NET)

- [x] 3.1 Introduce a sandbox endpoint abstraction (URI or socket path) and teach `Dotto.Ai/Sandbox/SandboxContainer.cs` to connect through `SocketsHttpHandler.ConnectCallback` with `UnixDomainSocketEndPoint`; use a synthetic `http://sandbox/` base URI for socket mode and keep `AllowAutoRedirect=false`/`UseCookies=false`. Update the `Sandbox.ContainerFactory` seam (`Func<Uri, string, string, ISandboxContainer>`) in the same step. Verify: a unit test serves `/ping` on a temporary Unix listener and asserts the client succeeds; the test is skipped on Windows.
- [x] 3.2 Update `ISandboxContainer` (`ApiUrl`, and the `Sandbox` log/readiness messages that use it) and all stubs (`SandboxContainerParametersTests`, `SandboxStartCleanupTests`) so no code assumes a network URL for production. Verify: solution builds and all sandbox tests pass.
- [x] 3.3 Add `IpcBasePath` to `SandboxOptions` (Linux default `/var/lib/dotto-sandbox-ipc`, temp dir on Windows); socket transport is selected when `NetworkName` is configured, so no separate switch is added. Verify: `AiSettingsResolutionTests` covers the default and an override.
- [x] 3.4 In `Dotto.Ai/Sandbox/Sandbox.cs`, create `<IpcBasePath>/<sessionId>` before start (mode `0777`, like the storage directory), bind it at `/run/dotto`, pass `SANDBOX_SOCKET=/run/dotto/sandbox.sock` when socket mode is on, and remove the directory in `DisposeAsync` and on the start-failure path. Unit tests that set `NetworkName` must point `IpcBasePath` at a temp directory. Verify: `SandboxContainerParametersTests` asserts the bind and env entry; a cleanup test asserts the directory is gone after dispose.
- [x] 3.5 Add a startup sweep that removes IPC directories older than `WallClockTimeout + ContainerStartTimeout` so a crashed bot does not accumulate them. Verify: a unit test with pre-created old/new directories keeps the new one and removes the old one.

## 4. Network topology

- [x] 4.1 Update `docker-compose.yml`: mount the IPC host path into the bot, remove `sandbox` from the bot's `networks`, and add `com.docker.network.bridge.enable_icc: "false"` to the network's `driver_opts`. Verify: `docker compose config` shows the bot without `sandbox`, the ICC option present, and the IPC mount.
- [x] 4.2 Update the comments in `.docker/sandbox-egress.sh` and `docker-compose.yml` that explain the same-bridge `RETURN`, so they no longer claim the bot↔sandbox API uses the bridge. Verify: `sandbox-egress.sh --check` still passes after the stack is up. (Live --check deferred to production with 6.2; script logic is unchanged and `bash -n` passes.)

## 5. Bulk file transfer

- [x] 5.1 Add `FILE_CHUNK_SIZE = 1024 * 1024` in `server.py` and use it only in `_get_file` and `_put_file`; leave `CHUNK_SIZE` on the exec pumps. Verify: a 200MiB `PUT` and `GET` over the socket complete with unchanged byte counts. **Done:** sha256 match; 0.66s/0.20s on runc. The gVisor throughput figure was already measured in `decision.md` and was not re-measured.
- [x] 5.2 Update the sandbox image README's transfer-size note (1MiB for `/file`, 64KiB for exec pumps). Verify: the text matches the constants in `server.py`.

## 6. Integration, docs, and gaps

- [x] 6.1 Run `dotnet test` and `openspec validate sandbox-gvisor --strict`; both pass with no new warnings. **Done:** 273/273 pass after recreating the host `docker0`; `openspec validate sandbox-gvisor --strict` is valid. (The earlier 26 DB failures were the host's missing `docker0`, not the change.)
- [ ] 6.2 On a host with `runsc`: bring up the compose stack, confirm `docker info` lists `runsc`, `/ping` works over the socket, `/ai` exec + upload + download work, public `curl` works, sibling-sandbox connection is blocked, and a LAN/host destination is blocked. Verify: record the commands and outcomes in the change's final notes. **Deferred:** verify in production after deploy; this host registers only `runc`.
- [x] 6.3 Update `openspec/changes/ai-sandbox/tasks.md` 10.5/10.6 to reference this change and the resolved behavior, and adjust the Decision 13/14 notes in `openspec/changes/ai-sandbox/design.md` that describe the bot on the sandbox bridge. Verify: `openspec validate ai-sandbox` passes and no new warnings appear (its two pre-existing strict warnings are unchanged).

## Workflow follow-up

- Ask the user to update `Dotto.Ai/FEATURE.md` for the runtime default, socket transport, and network topology changes.
- Archive `sandbox-gvisor` after review; keep `decision.md` as the benchmark record.
