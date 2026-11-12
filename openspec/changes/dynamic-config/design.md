# Design

## Context

See `proposal.md` for motivation and `specs/dynamic-configuration/spec.md` for the behavior contract.

Current constraints found in the code:

- Settings are bound into `IOptions<T>` singletons at startup (`AddOptions<T>().BindConfiguration(...)`), and most consumers inject `IOptions<T>`.
- The AI chat client and agent are keyed singletons built once from `AiSettings` in `Dotto.Ai/DependencyInjection.cs`; `ChatAssistant` and `AiCommandHandler` are scoped and already receive the caller, channel, and guild IDs.
- Command modules and scoped event processors run inside a DI scope created per interaction; gateway coordinators create scopes explicitly.
- `Dotto.Ai` references `Dotto.Application`, so shared configuration services can live in `Dotto.Application` and be consumed by both AI and Discord.
- `Dotto.Application` cannot reference the modules that own options types (`AiSettings`, `DownloaderSettings`, `S3Settings`, ...), so a schema registry is assembled from descriptors registered by each module.
- There is no owner or instance identity today. `RestClient.GetCurrentApplicationAsync()` exposes the Discord application, but it must be called lazily (not during configuration binding).
- PostgreSQL unique indexes treat `NULL` values as distinct, which matters for a global scope with no target ID.

## Goals / Non-Goals

**Goals:**

- Persistent, instance-partitioned overrides with global/guild/channel/user scopes.
- One typed resolution path that reuses the standard options binder and `DataAnnotations`.
- Immediate runtime effect for per-interaction consumers, starting with the AI assistant.
- Owner-only management from Discord.
- Preserve the existing startup behavior for consumers not yet migrated.

**Non-Goals:**

- Making every startup-captured service (S3 client, downloaders, compression strategies, sandbox reaper) reload live.
- Encrypting values at rest or storing arbitrary structured values (arrays, dictionaries, whole objects). Overrides target scalar leaves.
- Composite scopes such as guild+user.
- Team ownership.
- A web/admin UI.

## Decisions

### Typed per-interaction snapshots, not a raw key resolver

Consumers receive a bound options object, not a string lookup. For an interaction we build a fresh `IConfigurationRoot` from the host configuration plus the matching override rows, then call the normal binder (`GetSection(section).Get<T>()`). This reuses existing conversion rules (`Uri`, `TimeSpan`, enums), `DataAnnotations`, and nested options shapes, and keeps consumer code close to what it is today.

This directly replaces the notion of mutating global `IOptions<T>`: those are singletons shared by all interactions, so they cannot hold per-guild values. A scoped snapshot is isolated per interaction and safe under concurrency. A raw `GetString("Ai:...")` API was rejected because every consumer would re-implement conversion and validation.

Interface (in `Dotto.Application`):

```csharp
public readonly record struct InteractionScope(ulong? GuildId, ulong ChannelId, ulong UserId);

public interface IInteractionConfiguration
{
    InteractionScope? CurrentScope { get; }
    void SetScope(InteractionScope scope);
    Task<IConfiguration> BuildAsync(InteractionScope scope, CancellationToken ct = default);
    Task<T> GetAsync<T>(InteractionScope scope, CancellationToken ct = default) where T : class;
    Task<IConfiguration> BuildCurrentAsync(CancellationToken ct = default);
    Task<T> GetCurrentAsync<T>(CancellationToken ct = default) where T : class;
}
```

`GetAsync<T>` looks up the registered schema descriptor for `T` to find the section (`"Ai"`, `"Ai:Sandbox"`, `"Minio"`, ...) and binds that section from the snapshot.

### One scope per DI scope, cached

One DI scope serves one interaction (NetCord's hosting handler and our gateway coordinators both create a scope per event), so the scoped `InteractionConfiguration` also holds that interaction's `InteractionScope`. The command boundary calls `SetScope` once; consumers call `GetCurrentAsync<T>()` and never re-derive the IDs. Rebinding to a different interaction throws, which catches a future caller that reuses a scope across events.

Both the snapshot and each bound options type are memoized in `ConcurrentDictionary` entries keyed by scope (and by scope + type), so an interaction that resolves `AiSettings` in the command module, again in `AiCommandHandler`, and again in `ChatAssistant` performs one store query and sees one identical set of values. A failed build is evicted so a transient error does not poison the interaction. The cache is deliberately scoped, not process-wide: another instance sharing the database must not have its writes hidden.

Boundaries that own the scope: `Dotto.Discord/Commands/Ai/ApplicationCommand.cs`, `Commands/Ai/TextCommand.cs`, and defensively `AiCommandHandler.Invoke`. NetCord offers no per-invocation scope hook, so this explicit call is unavoidable — but it stays at the two command modules while every consumer below them is scope-free.

### Layer overrides as configuration sources

`Build` starts with `AddConfiguration(hostConfiguration)` and then adds one in-memory source per matching row, broadest first (global, guild, channel, user), so the narrowest value wins. Base `appsettings`/environment values remain the fallback. A row whose value is not a valid scalar is rejected at write time, so the snapshot never contains unusable values.

### Query overrides per interaction

`ConfigurationOverrideStore` is scoped (uses the scoped `DottoDbContext`) and issues one filtered query per snapshot:

```
InstanceName = <Bot:InstanceName> AND
(Scope = Global OR (Scope = Guild AND TargetId = guildId)
              OR (Scope = Channel AND TargetId = channelId)
              OR (Scope = User AND TargetId = userId))
```

The volume is low and interactions already hit the database, and each interaction now performs at most one such query (see the cached snapshot above), so a shared cache is unnecessary. A process-wide long-lived cache was rejected because another instance sharing the database would not see changes.

### Store scope target as non-null with `0` meaning global

`ConfigurationOverride.TargetId` is `ulong` and `0` for global rows. This avoids PostgreSQL's `NULL` distinctness in the unique index on `(InstanceName, Scope, TargetId, Key)`. Discord IDs are non-zero.

Keys are case-insensitive in the configuration system, so they are normalized to lowercase for storage and lookup. Scope is stored as an enum value.

### Apply global overrides to host configuration after migration

`Startup.cs` already builds the host and runs `MigrateDatabase()` before `RunAsync()`. Immediately after migration, load global rows for the instance and add them as an in-memory configuration source to `builder.Configuration`. Options are first resolved on host start (`ValidateOnStart`) and later lazily, so everything created after this point sees the values. This avoids a custom configuration provider that would need its own database connection, and it avoids a first-run failure when the table does not exist yet.

Global changes therefore apply on the next restart for startup-captured services, while scoped and global values for per-interaction consumers are fresh on every interaction.

### Owner identity service

`IBotOwnerService` (Discord project) reads `Bot:OwnerIds`. When the array is non-empty, it is the complete authorized set. When empty, it lazily calls `RestClient.GetCurrentApplicationAsync()` once and caches `Application.Owner?.Id`; a null owner means no one is authorized. Failures are logged and deny access rather than opening it. This supports the team-owned case only via explicit `Bot:OwnerIds`, matching the requirement.

### Optional schemas for validation and autocomplete

`Dotto.Application` defines:

```csharp
public interface IConfigurationOptionSchema
{
    string Section { get; }   // e.g. "Ai", "Ai:Sandbox", "Minio"
    Type OptionsType { get; }
}
```

Each module registers descriptors as singletons (`AiSettings`, `SandboxOptions`, `DownloaderSettings`, `CompressionSettings`, `S3Settings`, `AutoDownloadSettings`, `UrlCorrectionSettings`). The write path:

1. Rejects empty keys, whitespace, empty segments, and keys that cannot be normalized.
2. Rejects keys under the reserved sections `Bot` and `ConnectionString` before any resolution, so no override can change the bot's identity or data source.
3. Finds the longest matching section; if none matches, rejects the key and names the registered sections.
4. If a section matches but the property path does not exist, rejects the key and suggests the closest known keys. `ConfigurationSchemaRegistry.TryResolvePath` owns this walk, so validation, autocomplete, and scope support cannot disagree about what a key addresses.
5. Resolves the key's `ConfigurationScopeSupport` (see below); a scoped write to a startup-bound key is rejected, and a global write to one warns that it applies after a restart.
6. Converts the value with the leaf's type converter. On failure, rejects without writing.
7. Binds the full section from a candidate snapshot (base + existing scoped rows + this value) and runs `Validator.TryValidateObject`. Rejects if a validation error maps to the edited member.

Nothing is stored that no consumer reads: unknown keys are errors, not warnings. `unset` deliberately skips validation, so rows for keys that a later build renamed or removed remain removable, and `list`/`get` keep showing them.

The same schema list feeds slash-command autocomplete; when the command's `scope` option is filled in, only keys that support that scope are offered.

### Declare each key's scopes where the options type lives

`Dotto.Common` defines:

```csharp
[Flags] enum ConfigurationScopeSupport { None, Global, Scoped, Any = Global | Scoped }
[AttributeUsage(Class | Property)] sealed class ConfigurationScopeSupportAttribute(ConfigurationScopeSupport support);
```

Resolution walks the resolved key path outward and takes the nearest declaration: the leaf property, then its declaring type, then the closest ancestor property (which is how dictionary/list leaves such as `Ai:Prompts:MyPrompt:Path` inherit from `AiSettings`). Keys that resolve to nothing annotated default to `Global`: a setting is startup-bound unless the consumer opts in by resolving it per interaction, which keeps the conservative default.

`AiSettings` is annotated `Any`, so the assistant's base URL, key, model, prompts, and `ChatAssistant` limits are live per scope. `SandboxOptions` keeps the default `Global` rather than becoming per-guild: `NetworkName`, `DnsServers`, `StorageBasePath`, `ImageTag`, and `Enabled` decide whether the sandbox exists and what it can reach, so a per-guild override would be an egress-escaping footgun rather than a feature. The sandbox code therefore keeps reading `IOptions<SandboxOptions>`, and the validator is what makes that boundary explicit instead of silently ineffective.

`None` is available for keys that must never be overridden; the reserved sections cover the identity case today.

### Command surface

`Dotto.Discord/Commands/Config/ApplicationCommand.cs` and `TextCommand.cs` delegate to `ConfigCommandHandler`:

- `/config set <key> <value> [scope] [user]`
- `/config unset <key> [scope] [user]`
- `/config get <key> [scope] [user]`
- `/config list [scope] [user]`
- `.config ...` mirrors the same operations.

`scope` defaults to `global`. `guild` and `channel` always use the current guild/channel; `user` defaults to the invoker and may select any user. Slash responses are ephemeral. Prefix-command responses are sent to the owner by DM, because normal messages cannot be ephemeral; if the DM fails, the command replies with a short instruction instead of leaking values. Values for keys matching secret patterns are masked in `get`/`list`.

### AI rewiring

- Register a scoped `IAiAgentFactory` in `Dotto.Ai` that builds the `IChatClient` and `AIAgent` from a supplied `AiSettings` plus the scoped tool services and the (startup-bound) `SandboxOptions`.
- Delete the keyed singleton `IChatClient` and keyed `AIAgent` registrations; update `SandboxDisabledTests` to call the factory with global settings.
- `ChatAssistant.Invoke` resolves the effective `AiSettings` from the boundary's snapshot and uses the factory.
- `AiCommandHandler` resolves `AiSettings` from the same snapshot (cached, so no second query) using the IDs it already receives.
- AI slash/text command modules bind the scope (`SetScope`) after extracting context IDs and resolve the same snapshot, replacing their constructor-injected `IOptions<AiSettings>`.
- `IPromptProvider` is scoped and resolves `AiSettings.Prompts` per interaction, so prompt sources can be overridden per scope. A singleton `PromptTemplateCache` keyed by `(PromptType, Path)` keeps the expensive part — reading and caching an embedded resource or a file with its mtime check — shared across interactions.
- `SandboxOptions` stays on `IOptions` (startup-bound) as described above; the sandbox infrastructure is instance-wide by design.
- Chat-client creation stays cheap (OpenAI client creation is lightweight); if profiling shows otherwise, cache by `(baseUrl, apiKey, model)` behind the factory without changing consumers.


### Database shape

`Dotto.Domain/Entities/ConfigurationOverride.cs`:

```csharp
public record ConfigurationOverride
{
    public int Id { get; init; }
    public required string InstanceName { get; init; }
    public required ConfigurationScope Scope { get; init; }
    public required ulong TargetId { get; init; } // 0 for global
    public required string Key { get; init; }
    public required string Value { get; init; }
    public required DateTime CreatedOn { get; init; }
    public DateTime UpdatedOn { get; set; }
}
```

EF configuration adds a unique index on `(InstanceName, Scope, TargetId, Key)`, max lengths (instance 100, key 200, value 8000), and an index for the filtered lookup. A new migration creates the table; no backfill is needed.

## Risks / Trade-offs

- **Building a configuration root and binding per interaction adds overhead** → interactions already perform network and DB work; the row set is small. Add the cache if profiling shows a hotspot.
- **Startup-captured services do not reload** → documented in the spec requirement; only per-interaction resolution is promised live. Migrate further consumers to `IInteractionConfiguration` later.
- **Strict rejection requires a deploy before a new key can be overridden** → inherent to a whitelist, and harmless: a new setting ships with the code that knows it. Verified that no live setting is read outside an options type — the only unmodeled reads (`Discord:Token` in `AddDiscordGateway`, `Logging:*` when the providers are built, and `ConnectionString` in `AddDatabase`) are registration-time and could not have been affected by an override anyway, so rejecting them loses no capability.
- **The whitelist is per shape, not per enumerated key** → dictionary and list leaves stay writable by index/key (`Ai:Prompts:MyPrompt:Path`), so autocomplete cannot list every accepted key; `set` accepts them, `list` shows what is stored.
- **Secrets are plaintext in the database** → owner-only access, masked in output; full encryption is out of scope.
- **Owner fallback depends on a Discord API call** → failure denies rather than allows; `Bot:OwnerIds` avoids the dependency.
- **Same key written concurrently** → last write wins via the unique-index upsert path; acceptable for a single owner.
- **Global `TargetId = 0` sentinel leaks into the domain** → documented on the entity; simplifies uniqueness and avoids raw SQL indexes.

## Migration Plan

1. Add the entity, EF configuration, and migration `AddConfigurationOverrides`.
2. Deploy with `Bot:InstanceName` set (defaults to `dotto`) and, for team-owned apps, `Bot:OwnerIds`.
3. Migration runs automatically in `MigrateDatabase()`; no data backfill.
4. Rollback: the table is additive; an older binary ignores it. Drop the table if a full rollback is required. Overrides are lost only if the table is dropped.
