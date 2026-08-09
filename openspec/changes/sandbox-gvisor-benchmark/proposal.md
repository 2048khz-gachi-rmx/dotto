## Why

The sandbox already supports gVisor (`runsc`) as an optional runtime via `SandboxOptions.UseGVisorRuntime`, but it's disabled by default because the performance impact of running ffmpeg under gVisor's userspace kernel is unknown. Before enabling it by default (or deciding to leave it off), we need measured data comparing ffmpeg transcode throughput under standard Docker vs gVisor on a Linux host.

## What Changes

- Benchmark ffmpeg transcode workloads under standard Docker runtime vs gVisor (`runsc`) runtime
- Measure startup latency, transcode wall-clock time, and memory/CPU overhead
- Document the results and a recommendation for the default value of `SandboxOptions.UseGVisorRuntime`
- Based on the results, either flip the default to `true` (if overhead is acceptable) or leave it `false` with documented justification

## Capabilities

### New Capabilities
- `gvisor-benchmark`: A benchmarking methodology and decision record for evaluating gVisor as the sandbox runtime, comparing ffmpeg workloads under standard Docker vs `runsc`, and documenting the recommended default.

### Modified Capabilities
- *(None — no existing specs are affected. The `UseGVisorRuntime` option and `runsc` runtime assignment already exist in `Sandbox.cs`; this change only produces benchmark data and a default-value decision.)*

## Impact

- **Infrastructure**: Requires a Linux test machine with Docker and `runsc` installed (gVisor is Linux-only; cannot be benchmarked on Windows/podman)
- **Dotto.Ai/Settings/SandboxOptions.cs**: Potentially changing the default of `UseGVisorRuntime` from `false` to `true` if benchmarks justify it
- **appsettings.json**: Potentially updating the documented default for `Ai:Sandbox:UseGVisorRuntime`
- **No code logic changes** — the runtime selection mechanism is already implemented in `Sandbox.cs:115-116`
