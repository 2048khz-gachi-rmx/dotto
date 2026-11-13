# Tasks

## 1. Runtime default and host prerequisite

- [ ] 1.1 Flip `UseGVisorRuntime` to `true` in `Dotto.Ai/Settings/SandboxOptions.cs:50` and `Dotto.Bot/appsettings.json:72`, and add `Dotto.Bot/appsettings.Development.json` pinning it to `false` for the `dotnet run` Development profile. Verify: `SandboxContainerParametersTests` asserts `Runtime == "runsc"` by default and `null` when the option is false, and that test passes.
- [ ] 1.2 Add a startup probe (hosted service or startup step) that resolves `IDockerClient`, calls `System.GetSystemInfoAsync()`, and logs an error naming `runsc` and the install step when `UseGVisorRuntime` is true but the daemon's runtime list lacks it; no probe when the sandbox is disabled. Verify: unit tests with NSubstitute cover present/missing/disabled and no exception escapes startup.
- [ ] 1.3 Document the prerequisite in `.docker/README.md`: install the pinned gVisor release, register `runsc` with runtime args `--host-uds=create`, restart Docker, confirm `docker info` lists `runsc`, and explain the Development override. Verify: the documented commands run as written on a Linux host, or are explicitly marked manual-check.

## 2. Unix-socket server (sandbox image)

- [ ] 2.1 Add socket mode to `Dotto.Ai/sandbox-image/server.py`: when `SANDBOX_SOCKET` is set, unlink any stale path, bind a `ThreadingUnixStreamServer` with `BaseHTTPRequestHandler` and `protocol_version = "HTTP/1.1"`, and keep the TCP server when it is unset. Verify: `docker run -v <tmp>:/run/dotto -e SANDBOX_SOCKET=/run/dotto/sandbox.sock ...` then `curl --unix-socket <tmp>/sandbox.sock http://localhost/ping` returns 200, and `/execute` works over the socket.
- [ ] 2.2 Keep the token check identical on both transports: missing or invalid `X-Sandbox-Token` returns 401 with `Connection: close` and no command runs. Verify: `curl --unix-socket` without the header gets 401, with the header gets 200.
- [ ] 2.3 Update `Dotto.Ai/sandbox-image/README.md` with the socket transport (`SANDBOX_SOCKET`), the unchanged auth requirement, and a runnable `curl --unix-socket` example. Verify: every command in the README runs as written against the built image.

## 3. Unix-socket client and sandbox lifecycle (.NET)

- [ ] 3.1 Introduce a sandbox endpoint abstraction (URI or socket path) and teach `Dotto.Ai/Sandbox/SandboxContainer.cs` to connect through `SocketsHttpHandler.ConnectCallback` with `UnixDomainSocketEndPoint`; use a synthetic `http://sandbox/` base URI for socket mode and keep `AllowAutoRedirect=false`/`UseCookies=false`. Verify: a unit test serves `/ping` on a temporary Unix listener and asserts the client succeeds; the test is skipped on Windows.
- [ ] 3.2 Update `ISandboxContainer` and its call sites so no code assumes a network URL for production. Verify: solution builds and all sandbox tests pass.
- [ ] 3.3 Add `IpcBasePath` (Linux default `/var/lib/dotto-sandbox-ipc`, temp dir on Windows) and `UseUnixSocket` (default `false`) to `SandboxOptions`; compose enables the socket. Verify: `AiSettingsResolutionTests` covers the defaults and overrides.
- [ ] 3.4 In `Dotto.Ai/Sandbox/Sandbox.cs`, create `<IpcBasePath>/<sessionId>` before start (mode `0777`, like the storage directory), bind it at `/run/dotto`, pass `SANDBOX_SOCKET=/run/dotto/sandbox.sock` when socket mode is on, and remove the directory in `DisposeAsync` and on the start-failure path. Verify: `SandboxContainerParametersTests` asserts the bind and env entry; a cleanup test asserts the directory is gone after dispose.
- [ ] 3.5 Add a startup sweep that removes IPC directories older than `WallClockTimeout + ContainerStartTimeout` so a crashed bot does not accumulate them. Verify: a unit test with pre-created old/new directories keeps the new one and removes the old one.

## 4. Network topology

- [ ] 4.1 Update `docker-compose.yml`: mount the IPC host path into the bot, set `Ai__Sandbox__UseUnixSocket: "true"`, remove `sandbox` from the bot's `networks`, and add `com.docker.network.bridge.enable_icc: "false"` to the network's `driver_opts`. Verify: `docker compose config` shows the bot without `sandbox`, the ICC option present, and the IPC mount.
- [ ] 4.2 Update the comments in `.docker/sandbox-egress.sh` and `docker-compose.yml` that explain the same-bridge `RETURN`, so they no longer claim the bot↔sandbox API uses the bridge. Verify: `sandbox-egress.sh --check` still passes after the stack is up.

## 5. Bulk file transfer

- [ ] 5.1 Add `FILE_CHUNK_SIZE = 1024 * 1024` in `server.py` and use it only in `_get_file` and `_put_file`; leave `CHUNK_SIZE` on the exec pumps. Verify: a 200MiB `PUT` and `GET` over the socket complete with unchanged byte counts, and gVisor throughput is in the 447–1026MiB/s range from `decision.md`.
- [ ] 5.2 Update the sandbox image README's transfer-size note (1MiB for `/file`, 64KiB for exec pumps). Verify: the text matches the constants in `server.py`.

## 6. Integration, docs, and gaps

- [ ] 6.1 Run `dotnet test` and `openspec validate sandbox-gvisor --strict`; both pass with no new warnings.
- [ ] 6.2 On a host with `runsc`: bring up the compose stack, confirm `docker info` lists `runsc`, `/ping` works over the socket, `/ai` exec + upload + download work, public `curl` works, sibling-sandbox connection is blocked, and a LAN/host destination is blocked. Verify: record the commands and outcomes in the change's final notes.
- [ ] 6.3 Update `openspec/changes/ai-sandbox/tasks.md` 10.5/10.6 to reference this change and the resolved behavior, and adjust the Decision 13/14 notes in `openspec/changes/ai-sandbox/design.md` that describe the bot on the sandbox bridge. Verify: `openspec validate ai-sandbox` passes and no new warnings appear (its two pre-existing strict warnings are unchanged).

## Workflow follow-up

- Ask the user to update `Dotto.Ai/FEATURE.md` for the runtime default, socket transport, and network topology changes.
- Archive `sandbox-gvisor` after review; keep `decision.md` as the benchmark record.
