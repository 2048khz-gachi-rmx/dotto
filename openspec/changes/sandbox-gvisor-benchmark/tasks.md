## 1. Environment Setup

- [ ] 1.1 Provision a Linux test machine (or VM) with Docker installed
- [ ] 1.2 Install gVisor `runsc` runtime on the test machine (`install_runsc.sh` from gVisor's repo)
- [ ] 1.3 Register `runsc` as a Docker runtime: `sudo /usr/local/bin/runsc install` then restart Docker
- [ ] 1.4 Verify `runsc` is available: `docker run --rm --runtime=runsc hello-world`
- [ ] 1.5 Build the `dotto-sandbox:latest` image on the test machine (`podman build` or `docker build` from `Dotto.Ai/sandbox/`)
- [ ] 1.6 Prepare a 30+ second 1080p H.264 test input file in the sandbox's `/work/downloads/` directory

## 2. Benchmark Execution

- [ ] 2.1 Run the ffmpeg transcode workload 3 times under the default `runc` runtime, recording startup latency, transcode wall-clock time, and peak memory for each run
- [ ] 2.2 Run the same ffmpeg transcode workload 3 times under the `runsc` (gVisor) runtime, recording the same metrics
- [ ] 2.3 Compute the median for each metric under each runtime

## 3. Analysis & Decision

- [ ] 3.1 Compute the percentage overhead of gVisor relative to `runc` for each metric (startup, transcode, memory)
- [ ] 3.2 Classify the transcode overhead as "acceptable" (≤30%) or "excessive" (>30%) per the spec threshold
- [ ] 3.3 Write the decision record (environment details, measured values, overhead percentages, recommendation with justification) to a markdown file in the change directory
- [ ] 3.4 If gVisor is recommended as default: flip `UseGVisorRuntime` default to `true` in `Dotto.Ai/Settings/SandboxOptions.cs` and `Dotto.Bot/appsettings.json`
- [ ] 3.5 If gVisor is NOT recommended: document why and confirm `UseGVisorRuntime` stays `false`
