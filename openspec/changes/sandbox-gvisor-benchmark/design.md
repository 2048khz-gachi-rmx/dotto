## Context

The sandbox container system (`Dotto.Ai/Internal/Sandbox.cs`) already supports gVisor as an optional runtime via `SandboxOptions.UseGVisorRuntime`. When enabled, the container is created with `HostConfig.Runtime = "runsc"` (`Sandbox.cs:115-116`). The option defaults to `false` because gVisor's userspace kernel intercepts syscalls, which may slow down I/O-heavy workloads like ffmpeg transcoding. The impact is unknown and needs measured data before flipping the default.

gVisor is Linux-only — it cannot be benchmarked on Windows or Podman. The benchmark requires a Linux host with Docker and `runsc` installed.

See `proposal.md` — Why for the motivation.

## Goals / Non-Goals

**Goals:**
- Produce measured benchmark data comparing ffmpeg transcode under `runc` vs `runsc`
- Document a clear recommendation for the `UseGVisorRuntime` default
- If the recommendation is to enable gVisor by default, flip the default in `SandboxOptions.cs` and `appsettings.json`

**Non-Goals:**
- Changing the gVisor runtime selection mechanism (already implemented)
- Benchmarking workloads other than ffmpeg transcode (curl/jq are fast enough that overhead is irrelevant)
- Testing gVisor on Windows/Podman (not supported)
- MicroVM alternatives (Firecracker, Kata) — separate evaluation if gVisor is rejected

## Decisions

### Decision 1: 30% overhead threshold

**Chosen:** ≤30% transcode wall-clock overhead is "acceptable", above is "excessive"

**Alternatives considered:** 10%, 50%, no threshold

**Rationale:** gVisor provides significant security value (a userspace kernel that intercepts syscalls, preventing container breakouts via kernel exploits). A 30% slowdown on a 30-second transcode adds ~9 seconds — noticeable but not prohibitive for an async Discord bot workload where the LLM response time dominates. Below 10% would be unrealistically strict; above 50% would be hard to justify to users waiting for results.

### Decision 2: Median of 3+ runs

**Chosen:** Run each workload at least 3 times, report the median

**Alternatives considered:** Single run, mean of 5

**Rationale:** A single run is noisy (container cold start, filesystem cache state). Three runs with median is the standard minimum for stable results without excessive runtime. The median is more robust than the mean against outliers from scheduling jitter.

### Decision 3: 1080p test input

**Chosen:** At least 30 seconds of 1080p H.264 video as the benchmark input

**Alternatives considered:** 720p, 4K, shorter clip

**Rationale:** 1080p H.264 is the most common format the bot will process (YouTube, Instagram, TikTok defaults). 30 seconds is long enough to amortize startup overhead but short enough to keep the benchmark under a minute per run. 4K would stress the encoder but isn't representative of typical usage.

## Risks / Trade-offs

- **[Risk] gVisor version differences** → Mitigation: pin and document the gVisor version in the decision record. gVisor is actively developed; results may not generalize across versions.
- **[Risk] Hardware-specific results** → Mitigation: document the test machine's CPU, RAM, and disk type. Results on an SSD may differ from HDD or network-attached storage.
- **[Risk] ffmpeg codec variation** → Mitigation: use a single fixed transcode command (H.264 → VP9 or AV1, matching the bot's `Compress` defaults) across all runs.

## Open Questions

- Should the benchmark also measure concurrent sessions (2+ sandboxes running gVisor simultaneously)? This may reveal contention in gVisor's userspace kernel. Defer to a follow-up if single-session results are inconclusive.
