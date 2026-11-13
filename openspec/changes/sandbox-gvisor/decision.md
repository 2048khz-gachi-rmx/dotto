# Decision Record: gVisor as the default sandbox runtime

Benchmark run 2026-11-13 on the target deployment host. All values below are measured, not estimated.
No code default was changed in this change — `SandboxOptions.UseGVisorRuntime` is still `false`
(`Dotto.Ai/Settings/SandboxOptions.cs`) and the runtime assignment
(`Dotto.Ai/Sandbox/Sandbox.cs:176-177`, `createParams.HostConfig.Runtime = "runsc"`) is untouched.

## Environment

- Host: self-hosted bare metal (`systemd-detect-virt` = none)
- CPU: 16-thread x86-64; 31GB RAM
- Storage: ZFS dataset (the deployment pool)
- OS: Debian 13 (kernel `7.2.11+deb13-amd64`)
- Docker 29.9.3; gVisor release-20261109.0 (`runsc`), default platform (systrap, not KVM)
- CPU governor `powersave` (`energy_performance_preference` = `power`); observed 1.5–4.9 GHz ramp during runs
- Benchmarks ran in a throwaway second `dockerd` (config `/tmp/dockerd-bench.json`, socket
  a socket and a scratch data-root under `/tmp`, `iptables=false`, `bridge=none`)
  so the production daemon (live workload) was never touched. `/etc/docker/daemon.json`
  was not modified. The bench daemon has been stopped.
- Runtimes registered in the bench daemon: `runsc` and `runsc-uds` (`--host-uds=create`)
- Image: `dotto-sandbox:latest`, transferred via `docker save | docker load`

## Method

- Container flags mirror `Dotto.Ai/Sandbox/Sandbox.cs` exactly: read-only rootfs, `cap-drop ALL`,
  `no-new-privileges`, `--pids-limit=256`, `--cpus=1.0`, `--memory=1536m` with `--memory-swap=1536m`,
  tmpfs `/work` (1024m, mode 1777, exec), tmpfs `/tmp` (64m), per-run bind mount at `/storage`,
  env `SANDBOX_TOKEN` + `SANDBOX_STORAGE_QUOTA_MB`.
- Driven through the real sandbox HTTP API, not `docker exec`: `PUT /file` to place input,
  `POST /execute` with `timeout:"long"`, wall clock taken from the HTTP response.
  Peak memory sampled from `docker stats` once per second.
- Input: 30s 1080p25 H.264 clip (19.6MB) extracted from a local 1080p25 HEVC source.
- Workload: `ffmpeg -i in -an -c:v libx264 -preset veryfast -crf 20`, output to `/work` (tmpfs —
  the real bot path, downloads land in `/work/downloads`) and to `/storage` (bind mount — exercises
  the gofer).
- 3 interleaved rounds per runtime per variant (round-by-round, to cancel frequency drift), medians
  reported. Output bytes verified equal across runtimes.

## Results

### Controlled, `-threads 1`

| Workload | runc | runsc | delta |
|---|---|---|---|
| 30s 1080p → H.264, `/work` | 24.37s (24.366, 24.515, 24.217) | 26.53s (25.695, 26.543, 26.533) | +8.9% |
| same, `/storage` | 24.07s (24.067, 24.068, 24.216) | 26.64s (26.638, 26.648, 26.455) | +10.7% |
| startup → first `/ping` | 0.65s | 0.77s | +0.11s |
| peak RSS, `/work` | 226MB | 242MB | +16MB |
| peak RSS, `/storage` | 192MB | 208MB | +16MB |

Output bytes identical across runtimes: 17,197,581.

### Production config (no `-threads`, `--cpus=1.0`)

| Metric | runc | runsc |
|---|---|---|
| transcode `/work` | 76.60s (77.226, 76.602, 75.318) | 71.85s (71.848, 71.898, 71.709) |
| peak RSS | ~799MB | ~331MB |
| output bytes | 17,185,722 | 17,198,855 |

runsc was **6.2% faster**. Cause: gVisor's `--cpu-num-from-quota` flag (default `true`) derives the
guest CPU count from the cgroup quota as `max(2, ceil(quota))`. Verified: `--cpus` 1.0→`nproc` 2,
2.0→2, 4.0→4, 8.0→8. `runc` always exposes all 16 host CPUs, so under a 1-CPU quota `runc` spawns 16
encoder threads and thrashes against the quota while gVisor spawns 2. Higher peak RSS under `runc`
is the same effect.

Single 8-core sanity runs (no `-threads`, one run each): runc 8.99s peak 807MB, runsc 4.67s peak
531MB — indicative only (single run, frequency ramp).

### VP9 spot check (the bot's actual codec)

10s 1080p, two-pass `libvpx-vp9 -crf 35 -row-mt 1 -speed 2` + libopus, `-threads 1`:
runc 254.9s, runsc 222.8s. One run each, ordered, so **not conclusive** — a spot check only.

### Bulk file transfer (200MB incompressible file, real `server.py`, single run per cell)

`PUT` into the sandbox (MiB/s). 64KiB chunk = current code:

| chunk | runc `/work` | runc `/storage` | runsc `/work` | runsc `/storage` |
|---|---|---|---|---|
| 64K | 1172 | 1265 | 226 | 314 |
| 256K | 1354 | 1535 | 323 | 643 |
| 1M | 1382 | 1750 | 447 | 882 |
| 4M | 1224 | 1415 | 490 | 708 |
| 8M | 1300 | 1333 | 492 | 846 |

`GET` out of the sandbox (MiB/s):

| chunk | runc `/work` | runc `/storage` | runsc `/work` | runsc `/storage` |
|---|---|---|---|---|
| 64K | 1495 | 1308 | 376 | 278 |
| 256K | 1631 | 1656 | 735 | 779 |
| 1M | 2357 | 3283 | 880 | 1026 |
| 4M | 1955 | 1843 | 960 | 966 |
| 8M | 2403 | 1803 | 825 | 726 |

gVisor's per-syscall interception dominates the 64KiB chunk loop: 226–376 MiB/s versus
1172–1495 MiB/s. 1MB chunks recover roughly 2–3x and are at the knee (4–8MB is within noise).

### UDS + gVisor spike

Socket bound inside the sandbox on a bind-mounted directory, host connects via `curl --unix-socket`:

| runtime | host sees socket | host connects |
|---|---|---|
| runc | yes | yes |
| runsc (default) | no | no |
| runsc `--host-uds=create` | yes | yes |

## Analysis

Threshold (Decision 1 in `design.md`): ≤30% transcode overhead is "acceptable".

- Controlled `-threads 1`: **+8.9%** (`/work`) and **+10.7%** (`/storage`) — well inside the
  threshold. This is the cost of syscall interception itself, isolated from thread-count effects.
- Production config: runsc is **6.2% faster**, because the quota-derived CPU count matches the
  one-CPU quota. `runc`'s 16-thread oversubscription is the real problem there, not gVisor.
- Startup: +0.11s absolute, irrelevant for an async bot workload.
- Memory: +16MB baseline under gVisor; in the threaded production config gVisor uses ~470MB less
  peak RSS (331MB vs 799MB) for the same work.

Classification: **acceptable** on both variants.

File transfer is the outlier: at the current 64KiB `CHUNK_SIZE` gVisor is 3–5x slower on bulk
`PUT`/`GET`. This is a plain-chunk-size problem, fixable in `server.py` (see Follow-ups) and not a
reason to reject the runtime — but it should land before enabling gVisor by default if large file
transfers are a normal path.

## Recommendation

**`UseGVisorRuntime = true` as the default**, recommended alongside the bulk-transfer mitigation
(1MB chunk size for `_get_file`/`_put_file` in `Dotto.Ai/sandbox-image/server.py`); the chunk fix
is not a hard prerequisite — at the current 64KiB chunk size a 100MiB attachment still moves in
well under a second.

Rationale:
- Transcode overhead is 8.9–10.7% isolated, and gVisor is actually *faster* under the bot's real
  `--cpus=1.0` configuration, with much lower peak memory.
- gVisor adds a userspace kernel, which materially reduces the blast radius of a sandbox escape
  for code paths that execute LLM-generated commands.
- The one real regression (bulk byte transfer) is a chunk-size artifact with a cheap fix.

Not applied here: flipping the default is implementation work, out of scope for this change.

Additionally, `--host-uds=create` makes a UDS control channel viable under gVisor. It must be set
runtime-wide (daemon.json `runtimeArgs`), not per container. `create` permits `bind()` on gofer
mounts; `open`/`all` would also permit `connect()` to pre-existing host sockets, so only `create`
is appropriate. This enables `enable_icc=false` on the sandbox bridge and removal of the bot
container from the `sandbox` network, closing gap 10.5 in the `ai-sandbox` change; it needs
per-session IPC directories and unlink-before-bind.

## Caveats

- Single host, single CPU model; no cross-machine validation.
- x264 (`libx264 veryfast`) was the primary proxy for the bot's VP9 path. The VP9 spot check
  agreed in direction but is one ordered run per runtime — not evidence.
- Bulk-transfer cells are single runs each; treat as order-of-magnitude, not precise.
- `powersave` governor with 1.5–4.9 GHz ramping; mitigated but not eliminated by interleaving.
- No concurrent-sandbox test (see Open Questions in `design.md`); contention behaviour under
  gVisor's userspace kernel is unmeasured.
- gVisor ran on the default `systrap` platform. KVM is present on the host but was not used;
  `--platform=kvm` would likely narrow the syscall gap and was deliberately not evaluated here.
- Results are tied to gVisor release-20261109.0.
- gVisor synthesizes `/proc/cpuinfo`: the model name is masked, but vendor feature flags still
  leak the host microarchitecture. Masking needs a custom `runsc` CPUID config; accepted here.

## Follow-ups

1. **`FILE_CHUNK_SIZE = 1MB`** in `Dotto.Ai/sandbox-image/server.py`, used only by
   `_get_file` (`server.py:276`) and `_put_file` (`server.py:308`), which read with `CHUNK_SIZE`
   (`server.py:39`) at lines 297 and 341. Keep 64KiB for the exec pipe pumps — their tail buffer
   is only 40KB (`MAX_OUTPUT_BYTES`, `server.py:33-34`), so a larger read there buys nothing.
2. **UDS control channel** with `--host-uds=create`, per-session IPC dirs, unlink-before-bind;
   then `enable_icc=false` and drop the bot from the `sandbox` network.
3. **Sandbox resource tuning**: with `--cpu-num-from-quota` in play, `CpuCount`/`MemoryMb` now
   interact with the guest's visible CPU count. Consider a floor above 1.0 CPU for transcode
   sessions and re-measure.
4. **`-threads` guidance for the runc fallback**: if gVisor is unavailable and the bot runs on
   `runc` with a fractional CPU quota, pass an explicit thread count to ffmpeg (or raise
   `CpuCount`) — the 16-thread oversubscription is a measurable loss independent of the runtime.

## Environment caveat: running a second dockerd

The benchmark harness started a throwaway rootful `dockerd` with `"bridge": "none"` and a separate
`data-root`. On Docker 29.9.3 (build `70efc0e`) that **deletes the host's `docker0`**:
`daemon/daemon_unix.go`, `configureNetworking`, calls `removeDefaultBridgeInterface()` on the
`--bridge=none` path, which removes the interface by its hard-coded name (moby/moby#26071). With
`docker0` gone, the harness's `docker network create bench` was allowed to allocate the now-free
`172.17.0.0/16` pool. User-defined networks persist, so after the throwaway daemon stopped its
`br-<id>` interface kept the gateway and every default-bridge container on the host failed with
`adding interface ... to bridge docker0 failed: Device does not exist` until the leftover bridge
was removed and `docker0` recreated manually.

Two rootful daemons in one network namespace are unsupported. moby/moby#54134 covers the same
collision for a user-managed `--bridge`, fixed in 29.10.0 by PR #54157, but `--bridge=none` keeps
the deletion. Future benchmarks should use DinD/rootless/VM isolation, or a user-managed bridge
with non-overlapping `default-address-pools`; do not use `--bridge=none` on a host that already
runs a live Docker daemon.

## Docker DNS under gVisor

Docker user-defined networks point the container's `/etc/resolv.conf` at the embedded
resolver on `127.0.0.11`. That resolver is a host-side listener; gVisor's isolated network stack
cannot reach it, so `getaddrinfo` fails even though direct-IP egress works (gVisor FAQ,
google/gvisor#7469). `--dns` alone does not help: Docker only uses those addresses as upstreams
for the embedded resolver and still writes `127.0.0.11` into the file. The bot therefore writes one
shared `resolv.conf` from `Ai:Sandbox:DnsServers` into the IPC base directory (outside the
per-session mounts) and bind-mounts it at `/etc/resolv.conf:ro`. The file deduplicates the
nameserver list and adds `options timeout:2 attempts:2`, so a dead resolver costs seconds rather
than glibc's default ~20s. Internal/LAN names remain intentionally unavailable: the sandbox is
supposed to reach only the internet, and public resolvers cannot answer those names anyway.

A shared `/etc/gai.conf` (`precedence ::ffff:0:0/96 100`) prefers IPv4, because the sandbox has no
IPv6 route while public DNS returns AAAA records first; without it, scripts that dial
`getaddrinfo()[0]` intermittently fail with `ENETUNREACH`. The control socket is chmod `0600`
after bind; the host bot runs as root, so this only keeps other host users out.
