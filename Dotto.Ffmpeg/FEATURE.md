## Compression

Video compression via ffmpeg with VP9 and AV1. Supports reaction-based message control on auto-compressed replies.

### Architecture

```
Dotto.Ffmpeg.Contracts (interfaces)
  └── Dotto.Ffmpeg (implementations)
        ├── FfmpegRunner            ← spawns ffmpeg process, returns stderr
        ├── FfmpegTempCleanupService ← IHostedService, wipes stale temp files on start/stop
        ├── Vp9CompressionStrategy  ← IVideoCompressorStrategy, 2-pass VP9
        └── Av1CompressionStrategy  ← IVideoCompressorStrategy, single-pass AV1
              └── Dotto.Application
                    └── VideoCompressionService ← resolves strategy by key, delegates compression
                          └── Dotto.Discord
                                ├── CompressCommandHandler ← downloads video, compresses, builds response
                                ├── Commands/Compress/ApplicationCommand.cs ← /compress slash + context menus
                                ├── Commands/Compress/TextCommand.cs ← .compress prefix command
                                ├── Commands/Compress/AutoVideoCompressor ← auto-compress event processor
                                └── Commands/Compress/AutoCompressReactionProcessor ← reaction handler for auto-compress replies
                                      ├── Services/ReactionManager ← generic session tracker for reaction-based workflows
                                      └── EventHandlers/MessageReactionHandlerCoordinator ← dispatches reaction events to scoped processors
```

### FfmpegRunner Process Pattern

`FfmpegRunner` spawns ffmpeg using the same `TaskCompletionSource<int>` + `process.Exited` pattern as `YtdlDownloaderService`:

- Cross-platform binary: `ffmpeg.exe` on Windows, `ffmpeg` otherwise (via `RuntimeInformation.IsOSPlatform`)
- `EnableRaisingEvents = true` + `process.Exited` callback sets `TaskCompletionSource` result
- `ct.Register(() => process.Kill())` for proper cancellation
- Captures `StandardError` output; returns stderr on success, throws `ApplicationException` with stderr on non-zero exit code

### FfmpegTempCleanupService

An `IHostedService` registered via `AddHostedService` in `AddFfmpeg()`:

- On `StartAsync`: wipes all stale files from the `dotto_ffmpeg` temp dir, then ensures the directory exists
- On `StopAsync`: also cleans up stale files for graceful shutdown
- Only deletes files matching known patterns (`ffmpeg_{guid}`, `out_{guid}.webm`, `out_{guid}.mp4`) to avoid removing unrelated temp files

### Compression Strategies

Strategies are registered as keyed scoped services by `CompressionMethod` enum:

- `CompressionMethod.Vp9` → `Vp9CompressionStrategy` (2-pass VP9 + Opus audio)
- `CompressionMethod.Av1` → `Av1CompressionStrategy` (single-pass SVT-AV1 + Opus audio)

`CompressionResult` includes an `Extension` field set by the strategy (currently `.webm` for both). `VideoCompressionService` resolves the strategy by key and `CompressCommandHandler` uses `result.Extension` for output filenames.

### Compression Commands

| Command | Type | Description                                                                                    |
|---------|------|------------------------------------------------------------------------------------------------|
| `/compress` | Slash | Compress a video attachment; `attachment` (required) + `format` (vp9/av1, default from config) |
| `Compress` | Context menu | Compress video attachments from target message                                                 |
| `Compress (private)` | Context menu | Same as Compress but ephemeral                                                                 |
| `.compress` | Prefix | Compress video attachments from the invoking message; optional `format` parameter              |

All commands use `CompressCommandHandler` which downloads each video URL, compresses via `VideoCompressionService`, and returns a `CompressMediaResult<T>` with attachments and size comparison text.

### Thresholds

Configured under `Compress.Thresholds` in `appsettings.json`:

- `NeverCompressRatio` (0.90): skip if compressed size is >90% of original
- `AlwaysCompressRatio` (0.50): compress regardless of minimum saving if ratio is ≤50%
- `MinimumSavingBytes` (1048576 = 1MB): skip if savings are below 1MB (unless ratio ≤ `AlwaysCompressRatio`)

Thresholds are applied by `CompressCommandHandler.CreateMessage` when `applyThresholds` is true. The `/compress` and `.compress` commands currently pass `false` for `applyThresholds`.

### Temp File Lifecycle

1. Strategy writes input to `input_{guid}{extension}` in system temp dir
2. Strategy writes output to `dotto_ffmpeg` temp dir (VP9 also writes 2-pass log files there)
3. Strategy reads output into a `MemoryStream`, then deletes its' temp files in `finally`
4. On startup/shutdown, `FfmpegTempCleanupService` wipes any leftover files from `dotto_ffmpeg`

### Reaction-Based Message Control

After `AutoVideoCompressor` replies with a compressed video, it adds 👍 and 🖕 reactions (1% chance of `<:ouse:1164630871589003326>` instead of 🖕) and tracks the session via `ReactionManager`. The original message author can react to control the messages:

| Reaction | Action |
|----------|--------|
| 👍 | Delete the original message, remove all control reactions from the bot reply |
| 🖕 or ouse | Delete the bot reply message |

**ReactionManager** (`Dotto.Discord/Services/ReactionManager.cs`) is a generic singleton that tracks reaction sessions keyed by bot reply message ID. It stores `ReactionSession` records (original message ID, bot reply ID, original author ID, channel ID, 6-hour TTL expiry). Subscribers call `TrackMessage` to register and `TryGetSession`/`RemoveSession` to consume. It has no knowledge of compression or actions — it only manages session lifecycle.

**MessageReactionHandlerCoordinator** (`Dotto.Discord/EventHandlers/`) implements `IMessageReactionAddGatewayHandler`. Follows the same pattern as `MessageCreateHandlerCoordinator`: discovers `IGatewayEventProcessor<MessageReactionAddEventArgs>` via `AppDomain` reflection, resolves from scope, runs concurrently. Requires `GatewayIntents.GuildMessageReactions | GatewayIntents.DirectMessageReactions` in `Startup.cs`.

**AutoCompressReactionProcessor** (`Dotto.Discord/Commands/Compress/`) implements `IGatewayEventProcessor<MessageReactionAddEventArgs>`. On each reaction event it checks if the reacted message is tracked, validates the user is the original author, and executes the appropriate action. Bot reactions are ignored.

Reaction flow:
```
User posts video → AutoVideoCompressor compresses → replies with compressed video + 👍/🖕 reactions
                                                         ↓
                                                  reactionManager.TrackMessage(originalId, replyId, authorId, channelId)

User reacts 👍 → MessageReactionAdd event → MessageReactionHandlerCoordinator
                                                ↓
                                         AutoCompressReactionProcessor
                                                ↓
                                         ReactionManager.TryGetSession() → check TTL
                                                ↓
                                         IsAuthorized? (original author only, no admin check yet)
                                                ↓
                                         Delete original message + remove all control reactions

User reacts 🖕 or ouse → same flow → delete bot reply message
```

## Quirks & Gotchas

- **Compression temp files** use the system temp dir for input/output files and a dedicated `dotto_ffmpeg` subdirectory for ffmpeg logs and pass output.
- **`CompressCommandHandler`** creates new `HttpClient` instances per video (not injected) — matches the pre-existing pattern in the codebase.
- **Reaction emoji duplication**: `AutoVideoCompressor` defines emojis to add (`_thumbsUp`, `_middleFinger`, `_ouse`) and `AutoCompressReactionProcessor` defines separate arrays to recognize them (`AcceptEmojis`, `RejectEmojis`). Changing an emoji requires updating both files. The ouse emoji (`ouse`, `1164630871589003326`) is hardcoded in both places with a TODO to unhardcode later.
- **Reaction tracking is best-effort**: `AutoVideoCompressor` wraps reaction tracking in a bare `catch` block to avoid disrupting the compression flow if tracking fails.
- **Admin check is stubbed**: `AutoCompressReactionProcessor.IsAuthorized` only allows the original message author to trigger actions. Admin check (via guild member lookup) is not implemented yet.

## TODO

- **Unhardcode reaction emojis**: The ouse emoji (`ouse`, `1164630871589003326`) is hardcoded in both `AutoVideoCompressor` and `AutoCompressReactionProcessor`. Consolidate into a shared settings object (e.g., `CompressionReactionSettings`) bound from `appsettings.json`, injected into both classes. This would also resolve the emoji duplication issue.
- **Implement admin check for reactions**: `AutoCompressReactionProcessor.IsAuthorized` currently only allows the original message author. Add guild member admin/role check so admins can also trigger actions on other users' messages.
