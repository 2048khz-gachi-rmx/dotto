## Purpose

Evaluates gVisor (`runsc`) as the sandbox container runtime by benchmarking ffmpeg transcode workloads against the standard Docker runtime, producing a documented recommendation for the default value of `SandboxOptions.UseGVisorRuntime`.

## ADDED Requirements

### Requirement: Benchmark Methodology
The benchmark SHALL compare the same ffmpeg transcode workload under two container runtimes: the default Docker runtime (`runc`) and gVisor (`runsc`). Each run SHALL measure:
- Container startup latency (time from `create`+`start` to first successful `/ping`)
- Transcode wall-clock time (time from `POST /execute` with the ffmpeg command to response)
- Peak memory usage (via `docker stats` or container inspect)

The benchmark SHALL use a representative input file (at least 30 seconds of 1080p video) and run each workload at least 3 times, reporting the median.

#### Scenario: Standard runtime baseline
- **WHEN** the benchmark runs the ffmpeg transcode under the default `runc` runtime
- **THEN** startup latency, transcode time, and peak memory are recorded as the baseline

#### Scenario: gVisor runtime measurement
- **WHEN** the benchmark runs the same ffmpeg transcode under the `runsc` runtime
- **THEN** startup latency, transcode time, and peak memory are recorded for comparison

### Requirement: Overhead Threshold
The benchmark results SHALL report the percentage overhead of gVisor relative to the standard runtime for each metric. An overhead of ≤30% on transcode wall-clock time SHALL be considered "acceptable"; above 30% SHALL be considered "excessive".

#### Scenario: Acceptable overhead
- **WHEN** gVisor's median transcode time is ≤130% of the standard runtime's median
- **THEN** the results document the overhead as "acceptable"

#### Scenario: Excessive overhead
- **WHEN** gVisor's median transcode time is >130% of the standard runtime's median
- **THEN** the results document the overhead as "excessive"

### Requirement: Decision Record
The benchmark SHALL produce a decision record documenting:
- The test environment (OS, kernel, CPU, Docker version, gVisor version)
- The measured values for each metric under each runtime
- The computed overhead percentages
- The recommended default value for `SandboxOptions.UseGVisorRuntime` with justification

#### Scenario: gVisor recommended as default
- **WHEN** the overhead is "acceptable" and the security benefit of gVisor's userspace kernel justifies the cost
- **THEN** the decision record recommends `UseGVisorRuntime = true` as the default

#### Scenario: gVisor not recommended as default
- **WHEN** the overhead is "excessive" or the security benefit does not justify the cost
- **THEN** the decision record recommends `UseGVisorRuntime = false` as the default (current state)
