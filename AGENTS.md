# Dotto - Discord Bot

A .NET 9 Discord bot that downloads media from URLs and posts it to Discord. Uses NetCord, PostgreSQL, optional S3 overflow storage, yt-dlp/Cobalt download backends, and ffmpeg video compression.

## Instructions

Some folders or projects may contain FEATURE.md that describes a feature with additional detail - explanations, implementation quirks, requirements, etc.
You should generally only read them if you're planning to actively work on that feature.
Conversely, if you work on the feature and edit its' mechanics, you should prompt the user to write down the changes in those files when done.

## Architecture

Layered architecture with projects in `Dotto.sln`:

```
Dotto.Bot (entry point)
  └── Dotto.Discord (Discord integration)
        └── Dotto.Application (business logic)
              ├── Dotto.Common (shared utilities)
              ├── Dotto.Downloader.Contracts (downloader interfaces)
              └── Dotto.Ffmpeg.Contracts (compression interfaces)
  └── Dotto.Database (PostgreSQL data access)
  └── Dotto.Downloader (media download implementations)
  └── Dotto.FileUpload (S3 storage)
  └── Dotto.Ffmpeg (ffmpeg compression)
```

| Project | Purpose |
|---------|---------|
| Dotto.Bot | Entry point. `Startup.cs` bootstraps DI, NetCord, and hosted services. |
| Dotto.Discord | NetCord commands, event handlers, result handlers. |
| Dotto.Application | Business logic. |
| Dotto.Common | Shared utilities. No project dependencies. |
| Dotto.Downloader.Contracts | Pure interfaces: `IDownloaderService`, `DownloadedMedia`, `DownloaderType`. |
| Dotto.Downloader | `YtdlDownloaderService` (yt-dlp process) and `CobaltDownloaderService` (HTTP API). |
| Dotto.Database | EF Core + Npgsql. `DottoDbContext` with `ChannelFlags` and `DownloadedMedia` tables. |
| Dotto.FileUpload | S3 upload via `S3UploadService`. Optional — skipped if `Minio.BaseUrl` is null. |
| Dotto.Ffmpeg.Contracts | Pure interfaces: `IVideoCompressorStrategy`, `CompressionResult`, `CompressionMethod`, `CompressionOptions`. |
| Dotto.Ffmpeg | `FfmpegService` (ffmpeg process), `Vp9CompressionStrategy`, `Av1CompressionStrategy`, `FfmpegTempCleanupService`. |
| Dotto.Tests | NUnit + NSubstitute + Shouldly + Testcontainers (PostgreSQL). |

### DI Patterns

- Keyed services: Downloaders by `DownloaderType` via `AddKeyedSingleton`; compression strategies by `CompressionMethod` via `AddKeyedScoped`
- Options: `AddOptions<T>().BindConfiguration().ValidateDataAnnotations().ValidateOnStart()`
- Scoped: command handlers (transient), event processors, `ChannelFlagsService`, compression strategies
- Singleton: `DateTimeProvider`, `UrlCorrector`, `MediaProcessingService`, `FfmpegService`, downloader settings, resolved options values

## Commands

### Command Structure

Commands follow a strict separation: command definitions in `Dotto.Discord/Commands/` are thin invocation glue that delegate to command handlers in `Dotto.Discord/CommandHandlers/`. The same handler can be shared across slash commands, text commands, context menus, and auto-download.

#### Command Module Anatomy

```
Dotto.Discord/Commands/<Feature>/
  ├── ApplicationCommand.cs   ← Slash commands + context menus (ApplicationCommandModule<ApplicationCommandContext>)
  └── TextCommand.cs          ← Prefix text commands (CommandModule<CommandContext>)
```

Classes implementing CommandModule/ApplicationCommandModule/etc... are NOT manually registered in DI. NetCord discovers them via `host.AddModules(typeof(CommandAssemblyMarker).Assembly)` in `Startup.cs`.

### Adding a New Command

1. Create handler in `Dotto.Discord/CommandHandlers/<Feature>/`
2. Register as transient in `AddCommandHandlers()` in `Dotto.Discord/DependencyInjection.cs`
3. Create `ApplicationCommand.cs` and/or `TextCommand.cs` under `Dotto.Discord/Commands/<Feature>/`
4. Command modules are NOT registered in DI — NetCord discovers them via `host.AddModules(typeof(CommandAssemblyMarker).Assembly)`

### Generic Message Properties Pattern

Handlers use `T : IMessageProperties` to unify responses across command types:

- `InteractionMessageProperties` — slash commands (`RespondAsync`/`FollowupAsync`)
- `ReplyMessageProperties` — text commands (`ReplyAsync`)

Handler returns `Task<T>`, command module invokes with the appropriate type. Inject handlers via constructor parameters, not method parameters.

### Result Handlers

Custom result handlers in `Dotto.Discord/ResultHandlers/` send error embeds on command failures. The application command handler has special handling for error 40060 (interaction already acknowledged) — falls back to `ModifyResponseAsync`.

## Event Processor Pattern

NetCord gateway handlers are singletons. The codebase uses a scoped processor pattern for per-event DI:

`MessageCreateHandlerCoordinator` (singleton) implements `IMessageCreateGatewayHandler`. On each event it creates a DI scope, discovers all `IGatewayEventProcessor<T>` via `AppDomain` reflection, resolves them from scope, and runs them concurrently via `ValueTaskUtils.WhenAll`.

### Adding a New Event Processor

1. Implement `IGatewayEventProcessor<T>` where `T` is the event payload (e.g., `Message`)
2. Register as scoped in `AddCommands()` in `Dotto.Discord/DependencyInjection.cs`
3. Coordinator's `DiscoverHandlerTypes()` picks it up automatically

Gateway handlers are registered from `EventHandlerAssemblyMarker` (Discord project).

## Testing

Two fixture bases:

| Base | Use when | Database |
|------|----------|----------|
| `TestFixtureBase` | Unit tests, no DB | No |
| `TestDatabaseFixtureBase` | Integration tests needing DB | Yes (Testcontainers) |

`TestDatabaseFixtureBase` calls `TestRun.EnsureInitialized()` to spin up a PostgreSQL Testcontainer (requires Docker running). Database is truncated between tests, not re-migrated.

Tests use `TestDateTimeProvider` (mock) instead of real `DateTimeProvider`. Tests that need DB override `BuildServiceCollection()` to call `AddDatabase(TestRun.GetConnectionString())`.

## Quirks & Gotchas

- Downloader priority: Instagram URLs: Cobalt first, then Ytdl. Other URLs: Ytdl first, then Cobalt.
- Discord upload limits: No Nitro 10MB, Tier 2 50MB, Tier 3 100MB, NitroClassic/Basic 50MB, full Nitro 500MB.
- Optional dependency registration: some features/implementations are optional (for example, Cobalt DI in Downloader). You may `BuildServiceProvider()` during setup to check if the dependency is configured — a known hack.
- Dockerfile installs yt-dlp (Python), ffmpeg (static build from johnvansickle), and Deno at runtime.

## TODO

- S3 `ServiceURL` is hardcoded to `s3.badcoder.dev` in `FileUpload/DependencyInjection.cs`, not from config.
- `appsettings.json` uses section name `Minio` for S3 settings (not `S3`).
- IDateTimeProvider is no longer necessary; .NET 8+ ships with a built-in TimeProvider.