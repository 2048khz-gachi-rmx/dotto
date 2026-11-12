# Spec Delta

## Purpose

Lets the bot owner change configuration values at runtime without redeploying, with overrides scoped to the whole instance, a guild, a channel, or a user.

## ADDED Requirements

### Requirement: Instance-partitioned persistence

The system SHALL persist configuration overrides in the database, partitioned by the bot instance name from `Bot:InstanceName`. A key MUST be unique per instance, scope, and scope target.

#### Scenario: Updating an existing override

- **WHEN** the owner sets an already-existing key for the same instance and scope target
- **THEN** the stored value is replaced and its update timestamp changes, with no duplicate row

#### Scenario: Instances are isolated

- **WHEN** two bot instances share a database and one instance sets `Ai:ChatAssistant:Model`
- **THEN** the other instance continues to resolve its own value

#### Scenario: Unset deletes the override

- **WHEN** the owner unsets a key for a scope target
- **THEN** the row is deleted and resolution falls back to the next applicable layer

### Requirement: Resolution precedence

The system SHALL resolve a configuration key by checking overrides from narrowest to broadest scope, then environment variables and `appsettings.json`: user, channel, guild, global, then base configuration. The guild layer MUST be skipped when the operation has no guild.

#### Scenario: User beats channel

- **WHEN** the same key is set for a user and for the channel the user is acting in
- **THEN** the user value is used

#### Scenario: Channel beats guild

- **WHEN** the same key is set for a channel and for its guild
- **THEN** the channel value is used

#### Scenario: Global beats base configuration

- **WHEN** a key is set globally and is also present in `appsettings.json` or an environment variable
- **THEN** the globally stored value is used

#### Scenario: Broadest fallback

- **WHEN** no override exists at any scope for a key
- **THEN** the value from environment variables or `appsettings.json` is used

#### Scenario: Direct message without a guild

- **WHEN** a key has a guild override and the operation happens in a direct message
- **THEN** the guild override is ignored and resolution continues with the channel, user, global, and base layers

### Requirement: Per-interaction options snapshot

The system SHALL build a layered configuration snapshot for an interaction from the invoking guild, channel, and user, and bind typed options against that snapshot. Snapshots MUST be isolated so concurrent interactions in different scopes do not observe each other's values. Within one interaction, every consumer MUST resolve the same cached snapshot and bind each options type at most once, so a single interaction queries the store once.

#### Scenario: Different guilds use different values

- **WHEN** two guilds concurrently trigger an operation that reads the same typed option
- **THEN** each operation observes its own guild's effective value

#### Scenario: Typed binding and validation apply

- **WHEN** an override supplies a value for a typed option property
- **THEN** the snapshot binds it with the same conversion and `DataAnnotations` rules used for `appsettings.json`

#### Scenario: One query per interaction

- **WHEN** a single interaction resolves several options types, or resolves the same type in the command module and in the command handler
- **THEN** all of them read one snapshot built from one store query, and they observe identical values

#### Scenario: Unscoped resolution is an error

- **WHEN** a consumer resolves per-interaction configuration before the command boundary has bound the interaction's scope
- **THEN** resolution fails instead of silently using a different or empty scope

### Requirement: Runtime effect

Overrides resolved per interaction MUST take effect on the next interaction without restarting the bot. Global overrides MUST also be applied to the host configuration after database migration so components that read configuration once at startup observe them on that start. Settings that are only read at startup MUST be marked as such; a scoped override for them MUST be rejected rather than stored as a value that can never apply.

#### Scenario: Change applies without restart

- **WHEN** the owner sets a scoped override while the bot is running
- **THEN** the next matching interaction uses the new value without a restart

#### Scenario: Startup consumer reads global override

- **WHEN** a global override exists for a setting consumed during startup
- **THEN** the component observes the overridden value during that startup

### Requirement: Owner-only management

The system SHALL allow configuration changes only from user IDs listed in `Bot:OwnerIds`. When that list is empty, the system SHALL fall back to the Discord application owner's user ID. Team ownership is not supported: a team-owned application with an empty list has no managing owner.

#### Scenario: Listed owner is allowed

- **WHEN** a user in `Bot:OwnerIds` runs a configuration command
- **THEN** the change is applied

#### Scenario: Non-owner is rejected

- **WHEN** a user not in `Bot:OwnerIds` (or the application owner when the list is empty) runs a configuration command
- **THEN** the command is refused and no override is stored

#### Scenario: Application owner fallback

- **WHEN** `Bot:OwnerIds` is empty and the Discord application reports an owner
- **THEN** that owner's user ID is authorized

#### Scenario: Team application without configured owners

- **WHEN** `Bot:OwnerIds` is empty and the application has no single owner
- **THEN** no user is authorized to change configuration

### Requirement: Management commands

The system SHALL expose owner-only `set`, `unset`, `get`, and `list` operations through both slash and prefix commands. Each operation MUST accept a scope of `global`, `guild`, `channel`, or `user`; `user` overrides are instance-wide and MAY target any user, not only the invoker. Responses MUST be visible only to the owner.

#### Scenario: Set a global key

- **WHEN** the owner sets `Ai:ChatAssistant:Model` with scope `global`
- **THEN** the key is stored as a global override for the current instance

#### Scenario: Set a guild, channel, or user key

- **WHEN** the owner sets a key with scope `guild`, `channel`, or `user`
- **THEN** the key is stored against the current guild, current channel, or the selected user respectively

#### Scenario: Set a user override for another user

- **WHEN** the owner sets a key with scope `user` and selects a user other than themselves
- **THEN** the override applies to that user across all guilds of the instance

#### Scenario: List effective or stored values

- **WHEN** the owner lists overrides
- **THEN** the response shows the matching stored keys and values for the requested scope and target

### Requirement: Value validation

The system SHALL accept only keys that resolve to a scalar leaf of a registered option type, and SHALL validate the value with that leaf's conversion and the option's `DataAnnotations` rules before storing it. Keys outside the registered set MUST be rejected: an unknown section, or a property that the section's options type does not declare, is an error rather than a stored value that no consumer reads.

#### Scenario: Invalid typed value is rejected

- **WHEN** the owner sets a numeric option key to a non-numeric value
- **THEN** the command fails with an error and no row is stored

#### Scenario: Unknown section is rejected

- **WHEN** the owner sets a syntactically valid key whose section no module registered
- **THEN** the command fails with an error naming the registered sections, and no row is stored

#### Scenario: Unknown property is rejected

- **WHEN** the owner sets a key under a registered section but the options type declares no such property
- **THEN** the command fails with an error, suggests the closest known keys, and stores nothing

#### Scenario: Dictionary and list leaves are addressed by shape

- **WHEN** the owner sets `Ai:Prompts:MyPrompt:Path` or `UrlCorrection:Rules:0:Patterns:1`
- **THEN** the key is accepted because it resolves to a scalar leaf, even though its concrete key cannot be listed in autocomplete

#### Scenario: Reserved identity keys are rejected

- **WHEN** the owner sets any key under `Bot` or the root `ConnectionString`
- **THEN** the command fails and no row is stored

#### Scenario: Malformed key is rejected

- **WHEN** the owner supplies an empty key or one containing invalid separators
- **THEN** the command fails with an error and no row is stored

### Requirement: Scope support

Each key SHALL declare which scopes may target it: resolved per interaction (global, guild, channel, and user), or read once at startup (global only). A scoped write to a startup-bound key MUST be rejected with an explanation. A global write to it MUST succeed with a warning that it applies after a restart. The bot's own identity and database connection MUST be reserved and never overridable.

#### Scenario: Startup-bound key rejects a scoped write

- **WHEN** the owner sets a startup-bound key such as `Downloader:MaxFileSize` with scope `guild`
- **THEN** the command fails and explains that the key is read once at startup and must be set globally

#### Scenario: Startup-bound key accepts a global write with a warning

- **WHEN** the owner sets a startup-bound key with scope `global`
- **THEN** the value is stored and the response warns that it applies after a restart

#### Scenario: Per-interaction key accepts every scope

- **WHEN** the owner sets `Ai:ChatAssistant:MaxTurns` with scope `guild`, `channel`, or `user`
- **THEN** the value is stored without a warning and applies on the next matching interaction

### Requirement: Removable rows

The system SHALL allow deleting an override for a key that the current build no longer declares, so that renaming or removing a setting never strands rows.

#### Scenario: Renamed key can still be removed

- **WHEN** a row exists for a key that the current build no longer declares
- **THEN** `unset` still deletes it and `list` still shows it, even though `set` would reject that key

### Requirement: Secret masking

The system SHALL mask stored values whose key indicates a secret (for example keys ending in `ApiKey`, `AccessKey`, `SecretKey`, `Password`, `Token`, `Secret`, or `Credential`) in `get` and `list` output.

#### Scenario: API key is masked in output

- **WHEN** the owner lists an override for `Ai:ApiKey`
- **THEN** the response shows a masked value instead of the stored secret

### Requirement: AI assistant uses effective configuration

The AI assistant SHALL resolve its base URL, API key, model, history limits, turn limit, transcript threshold, attachment limit, and prompt sources from the interaction's effective snapshot, and SHALL build its chat client from those values. Sandbox infrastructure settings are not part of this surface: they are read once at startup and may only be set globally.

#### Scenario: Guild-specific model

- **WHEN** a guild has an override for `Ai:ChatAssistant:Model`
- **THEN** assistant runs in that guild use the overridden model while other guilds keep their own effective model

#### Scenario: User-specific key

- **WHEN** a user has an override for `Ai:ApiKey`
- **THEN** assistant runs invoked by that user use the overridden key

#### Scenario: Prompt source is scoped

- **WHEN** a guild has an override for `Ai:Prompts:ChatAssistant:Path`
- **THEN** assistant runs in that guild render that prompt while other guilds keep the base one

#### Scenario: Sandbox settings are instance-wide

- **WHEN** the owner tries to set `Ai:Sandbox:MemoryMb` for a guild
- **THEN** the command fails and explains that the key is read once at startup
