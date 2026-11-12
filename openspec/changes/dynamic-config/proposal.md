# Proposal

## Why

Every setting is currently fixed by `appsettings.json` or environment variables, so tuning the bot (model, API key, limits, timeouts) requires a redeploy/restart, and one deployment cannot vary behavior per guild, channel, or user. The bot owner needs to change configuration at runtime from Discord, scoped to where it matters.

## What Changes

- Add a persisted configuration-override store in PostgreSQL. Each row holds: bot instance name, scope (`global`, `guild`, `channel`, `user`), scope target ID (`0` for global), configuration key (e.g. `Ai:ChatAssistant:Model`), value, and timestamps.
- Partition all rows by a new `Bot:InstanceName` setting so several bot instances can share one database. Defaults to `dotto` when unset.
- Layer overrides on top of environment variables and `appsettings.json`; a scoped override beats a broader one: `user` > `channel` > `guild` > `global` > env/appsettings.
- Apply global overrides to the host `IConfiguration` after migrations, so startup-time consumers (downloaders, S3, sandbox, compression) see them.
- Resolve scoped overrides per interaction by building a layered `IConfiguration` snapshot and binding the typed options against it. This reuses the standard options binder and `DataAnnotations` validation; no new raw-key lookup API for consumers.
- Wire the AI assistant end-to-end as the first consumer: history limits, max turns, transcript threshold, base URL, API key, and model resolve from the interaction's snapshot. The keyed `IChatClient` is no longer a startup singleton; it is built from the effective settings per run (cached later if needed).
- Add owner-only `/config` slash commands and `.config` text commands to `set`, `unset`, `get`, and `list` overrides. `user` overrides are instance-wide and may target any user, not only the invoker.
- Identify the owner via a new `Bot:OwnerIds` array. When it is empty, fall back to the Discord application's owner; team ownership is not supported (teams must use `Bot:OwnerIds`).
- Validate a value by binding it to the known options type when the key maps to one (type conversion plus `DataAnnotations`); accept unrecognized keys with a warning. Reject syntactically invalid keys.
- Mask values whose key looks secret (`ApiKey`, `Password`, `Token`, `Secret`) in `get`/`list` output. Store values as plaintext, readable only by the owner.
- `unset` deletes the row so resolution falls back to the next scope.

## Capabilities

### New Capabilities

- `dynamic-configuration`: persistence, precedence, and runtime resolution of instance-scoped, optionally guild/channel/user-scoped configuration overrides, plus owner-only management commands.

### Modified Capabilities

None. No existing specs.

## Impact

- **Dotto.Domain**: new `ConfigurationOverride` entity (`InstanceName`, `Scope`, `TargetId` with `0` meaning global, `Key`, `Value`, `CreatedOn`, `UpdatedOn`).
- **Dotto.Database**: EF configuration, unique index on `(InstanceName, Scope, TargetId, Key)`, new migration.
- **Dotto.Application**: override store with cache invalidation, per-interaction layered configuration/options snapshot builder, validation.
- **Dotto.Bot**: `Bot` settings section (`InstanceName`, `OwnerIds`), load global overrides after `MigrateDatabase` and before `host.RunAsync`.
- **Dotto.Discord**: `/config` slash + text commands, owner authorization, result messages.
- **Dotto.Ai**: `IChatClient`/`AIAgent` construction moved from startup to per-run based on the interaction snapshot; AI command handlers and prompt lookup read snapshot values.
- **Configuration**: new `Bot:InstanceName` and `Bot:OwnerIds` keys; docs for the resolution order and supported scopes.
- **Tests**: unit tests for precedence, owner fallback, validation, masking; database integration tests for storage and `unset`; AI settings-resolution tests.
