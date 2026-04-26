## Compression

Video compression via ffmpeg with VP9 (implemented) and AV1 (stub pending).

### Architecture

```
Dotto.Ffmpeg.Contracts (interfaces)
  └── Dotto.Ffmpeg (implementations)
        ├── FfmpegService           ← spawns ffmpeg process, 2-pass VP9
        ├── FfmpegTempCleanupService ← IHostedService, wipes stale temp files on start/stop
        ├── Vp9CompressionStrategy  ← IVideoCompressorStrategy, full implementation
        └── Av1CompressionStrategy  ← IVideoCompressorStrategy, returns "not yet implemented"
              └── Dotto.Application
                    └── VideoCompressionService ← resolves strategy by key, delegates compression
                          └── Dotto.Discord
                                ├── CompressCommandHandler ← downloads video, compresses, builds response
                                ├── Commands/Compress/ApplicationCommand.cs ← /compress slash + context menus
                                ├── Commands/Compress/TextCommand.cs ← .compress prefix command
                                └── Commands/Compress/AutoVideoCompressor ← auto-compress event processor
```

### FfmpegService Process Pattern

`FfmpegService` spawns ffmpeg using the same `TaskCompletionSource<int>` + `process.Exited` pattern as `YtdlDownloaderService`:

- Cross-platform binary: `ffmpeg.exe` on Windows, `ffmpeg` otherwise (via `RuntimeInformation.IsOSPlatform`)
- `EnableRaisingEvents = true` + `process.Exited` callback sets `TaskCompletionSource` result
- `ct.Register(() => process.Kill())` for proper cancellation
- Captures `StandardError` output; throws `ApplicationException` with stderr on non-zero exit code
- Uses dedicated temp directory: `Path.Combine(Path.GetTempPath(), Constants.FfmpegTemp.DirName)` (i.e., `dotto_ffmpeg`)

### FfmpegTempCleanupService

An `IHostedService` registered via `AddHostedService` in `AddFfmpeg()`:

- On `StartAsync`: wipes all stale files from the `dotto_ffmpeg` temp dir, then ensures the directory exists
- On `StopAsync`: also cleans up stale files for graceful shutdown
- Only deletes files matching known patterns (`ffmpeg_{guid}`, `out_{guid}.webm`) to avoid removing unrelated temp files

### Compression Strategies

Strategies are registered as keyed scoped services by `CompressionMethod` enum:

- `CompressionMethod.Vp9` → `Vp9CompressionStrategy` (2-pass VP9 + Opus audio)
- `CompressionMethod.Av1` → `Av1CompressionStrategy` (stub, returns error)

`CompressionResult` includes an `Extension` field set by the strategy (currently `.webm` for both). `VideoCompressionService` resolves the strategy by key and `CompressCommandHandler` uses `result.Extension` for output filenames.

### Compression Commands

| Command | Type | Description |
|---------|------|-------------|
| `/compress` | Slash | Compress a video attachment; `attachment` (required) + `format` (vp9/av1, default vp9) |
| `Compress` | Context menu | Compress video attachments from target message |
| `Compress (private)` | Context menu | Same as Compress but ephemeral |
| `.compress` | Prefix | Compress video attachments from the invoking message; optional `format` parameter |

All commands use `CompressCommandHandler` which downloads each video URL, compresses via `VideoCompressionService`, and returns a `CompressMediaResult<T>` with attachments and size comparison text.

### Thresholds

Configured under `Compress.Thresholds` in `appsettings.json`:

- `NeverCompressRatio` (0.90): skip if compressed size is >90% of original
- `AlwaysCompressRatio` (0.50): compress regardless of minimum saving if ratio is ≤50%
- `MinimumSavingBytes` (1048576 = 1MB): skip if savings are below 1MB (unless ratio ≤ `AlwaysCompressRatio`)

Thresholds are applied by `CompressCommandHandler.CreateMessage` when `applyThresholds` is true. The `/compress` and `.compress` commands currently pass `false` for `applyThresholds`.

### Temp File Lifecycle

1. `Vp9CompressionStrategy` writes input to `input_{guid}{extension}` in system temp dir
2. `FfmpegService.CompressVp9Async` writes 2-pass log + output to `dotto_ffmpeg` temp dir
3. Strategy reads output into a `MemoryStream`, then deletes both temp files in `finally`
4. On startup/shutdown, `FfmpegTempCleanupService` wipes any leftover files from `dotto_ffmpeg`

## Quirks & Gotchas

- **AV1 compression** is a stub — `Av1CompressionStrategy` always returns a failure result. The `FfmpegService` currently only has `CompressVp9Async`; AV1 would need a new method.
- **Compression temp files** use the system temp dir for input/output files and a dedicated `dotto_ffmpeg` subdirectory for ffmpeg logs and pass output.
- **`CompressCommandHandler`** creates new `HttpClient` instances per video (not injected) — matches the pre-existing pattern in the codebase.

## TODO

- **Refactor `FfmpegService` out of the strategy chain**: `Vp9CompressionStrategy` delegates to `FfmpegService.CompressVp9Async`, which contains the actual VP9-specific ffmpeg arguments (2-pass, libvpx-vp9, etc.). This is semantically wrong — the strategy should own its codec logic. Plan: extract shared process-spawning (binary resolution, `TaskCompletionSource` exit pattern, stderr capture, cancellation) into a `BaseCompressionStrategy` or a generic `FfmpegRunner` utility. Move VP9 arguments and 2-pass orchestration into `Vp9CompressionStrategy` itself. When AV1 is implemented, it will follow the same pattern with its own arguments, reusing the base process logic.
