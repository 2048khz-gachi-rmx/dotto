# Tasks

## 1. Persistence

- [x] 1.1 Add `ConfigurationScope` enum (`Global`, `Guild`, `Channel`, `User`) and the `ConfigurationOverride` entity with non-null `TargetId` (`0` = global), normalized key, value, and timestamps; verify the solution builds.
- [x] 1.2 Add the EF configuration (table name, max lengths, unique index on `(InstanceName, Scope, TargetId, Key)`, lookup index) and `DbSet<ConfigurationOverride>`; verify `dotnet build Dotto.sln` succeeds.
- [x] 1.3 Generate the `AddConfigurationOverrides` migration and verify it contains the table, both indexes, and no unintended changes to existing tables.
- [x] 1.4 Add an integration test using `TestDatabaseFixtureBase` that inserts, updates, and deletes an override and verifies the unique index prevents duplicate `(instance, scope, target, key)` rows.

## 2. Configuration store and resolution

- [x] 2.1 Add `IConfigurationOptionSchema` in `Dotto.Application` and register descriptors from each module (`AiSettings`/`"Ai"`, `SandboxOptions`/`"Ai:Sandbox"`, `DownloaderSettings`/`"Downloader"`, `CompressionSettings`/`"Compress"`, `S3Settings`/`"Minio"`, `AutoDownloadSettings`/`"Discord:AutoDownload"`, `UrlCorrectionSettings`/`"UrlCorrection"`); verify a unit test resolves every registered section and type.
- [x] 2.2 Implement the scoped `IConfigurationOverrideStore` with normalized (lowercase) keys, filtered lookup for global + current guild + current channel + current user, upsert, delete, and list operations; verify integration tests cover each scope and instance isolation.
- [x] 2.3 Implement `IInteractionConfiguration.Build` so it layers the host configuration plus broad-to-narrow override sources, and `Get<T>` so it binds the registered section into typed options; verify unit tests prove user > channel > guild > global > base precedence.
- [x] 2.4 Implement write validation: reject empty/malformed keys, convert values with the property's type converter, reject conversion failures, validate known properties with `Validator.TryValidateObject`, and accept unmatched keys with an "unrecognized key" warning; verify unit tests cover a non-numeric numeric value, an unknown key, and a malformed key.
- [x] 2.5 Add the startup loader that reads global rows for `Bot:InstanceName` after migrations and adds them as an in-memory configuration source; verify a test host resolved after the loader observes a global override for a typed option.

## 3. Instance, owner, and startup wiring

- [x] 3.1 Add the `Bot` settings section (`InstanceName` defaulting to `dotto`, `OwnerIds` array) and bind it with validation; verify a configuration-binding test covers the default and an explicit value.
- [x] 3.2 Implement `IBotOwnerService` with the `Bot:OwnerIds` check and lazy Discord application-owner fallback; verify unit tests cover a listed owner, a non-owner, the fallback, a team-owned application with no owner, and an API failure that denies access.
- [x] 3.3 Wire `Startup.cs` to load global overrides between `MigrateDatabase()` and `host.RunAsync()`, and register the store/configuration/owner services in the appropriate `Add*` methods; verify startup logging shows the instance name and the number of loaded global overrides.
- [x] 3.4 Document `Bot:InstanceName` and `Bot:OwnerIds` in `appsettings.json`, `docker-compose.yml` comments, and the README configuration section; verify the documented keys match the bound settings.

## 4. Owner commands

- [x] 4.1 Implement `ConfigCommandHandler` for `set`, `unset`, `get`, and `list`, including owner authorization, scope/target defaults, warnings, and secret masking; verify unit tests cover each operation, authorization denial, `user` targeting another user, and masked output.
- [x] 4.2 Add the slash command module with `scope` choices, optional `user`, known-key autocomplete, and ephemeral responses; verify the command definitions build and the autocomplete test returns the registered keys.
- [x] 4.3 Add the text command module with the same operations and DM-based result delivery, falling back to a short instruction when the DM fails; verify the command definitions build and the fallback branch is unit-tested.
- [x] 4.4 Document the command syntax, scope precedence, validation behavior, and masking in the configuration docs; verify every documented example matches the implemented parameter names.

## 5. AI assistant integration

- [x] 5.1 Add the scoped `IAiAgentFactory` that builds `IChatClient` and `AIAgent` from supplied `AiSettings`, and remove the keyed singleton `IChatClient`/`AIAgent` registrations; verify the updated sandbox-disabled tests pass against the factory.
- [x] 5.2 Change `AiCommandHandler`, `ChatAssistant`, and the AI slash/text command modules to resolve `AiSettings` from `IInteractionConfiguration` using the interaction IDs; verify tests prove different guild/user scopes produce different effective settings.
- [x] 5.3 Build the chat client per run from the effective base URL, API key, and model, keeping the existing logging/turn-tracking wrappers; verify a test captures the model passed to the client for two different scopes.
- [x] 5.4 Add tests that cover guild-specific model resolution and user-specific API key resolution through the full AI settings path.

## 6. Integration verification

- [x] 6.1 Run `dotnet test Dotto.sln` and verify all new and existing tests pass.
- [ ] 6.2 Verify the documented end-to-end flow against a running instance: set a global override, restart, observe the new value; set a guild override without restart, observe it on the next AI run; unset and observe fallback.
- [x] 6.3 Confirm the new migration applies cleanly on a database containing existing data and that no existing table is altered.

## 7. Review follow-up

- [x] 7.1 Add `ConfigurationScopeSupport` and `ConfigurationScopeSupportAttribute` in `Dotto.Common`; annotate `AiSettings` as `Any` and leave infrastructure sections (`Ai:Sandbox`, `Downloader`, `Compress`, `Minio`, `Discord:AutoDownload`, `UrlCorrection`) at the startup-bound `Global` default.
- [x] 7.2 Move key-path resolution into `ConfigurationSchemaRegistry.TryResolvePath` and add `GetScopeSupport` plus scope-filtered `GetKnownKeys`, so validation, autocomplete, and scope support share one resolver.
- [x] 7.3 Reject unknown sections, unknown properties (with the closest known keys suggested), reserved `Bot`/`ConnectionString` keys, and scoped writes to startup-bound keys; warn on global writes to startup-bound keys. Verify the validator tests cover each case and that `unset` still bypasses validation so renamed keys stay removable.
- [x] 7.4 Bind the interaction scope on `IInteractionConfiguration` (`SetScope`/`GetCurrentAsync`) and memoize both the snapshot and bound options per scope; set the scope at the AI command boundaries and resolve through it in `AiCommandHandler` and `ChatAssistant`. Verify a test proves one store query per interaction and that rebinding to another interaction throws.
- [x] 7.5 Make `IPromptProvider` scoped, resolving `AiSettings.Prompts` per interaction, with a singleton `PromptTemplateCache` keyed by `(PromptType, Path)` so resource/file caching survives.
- [x] 7.6 Mask `AccessKey`, `SecretKey`, and `Credential` suffixes in addition to the existing ones, and cover `Minio:SecretKey` in the handler tests.
- [x] 7.7 Normalize line endings (`.gitattributes` with `eol=lf`) and re-run `dotnet test Dotto.sln` (250 passing).

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
- Verify the archived result.
