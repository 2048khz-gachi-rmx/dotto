# AI Agents

Provides the AI functionality for the Discord bot. This project encapsulates
prompts, logic, tools, and access control, wrapping Microsoft's agent
framework to add per-invocation context that the underlying agents otherwise
have no support for.

## Why the context accessor?

Tools are ordinary scoped services, resolved from the DI scope the interaction
already runs in, so they get their dependencies normally. What they cannot get
that way is *data*: who invoked the assistant, in which guild/channel, and how
much of the turn budget is left. DI cannot inject a value that only exists for
this one run, and threading it through every tool signature would leak into the
model-visible schema.

`ContextAccessor` is the mutable scoped holder for that data (the same trick as
`HttpContextAccessor`): the assistant sets it before the run, and tool code reads
it through the accessor. It is deliberately a crutch, but it keeps every
consumer and tool free of scope-management boilerplate.

## Structure

`Agents/Base/` — `AgentContext` (base record for invocation data),
`ContextAccessor` (scoped holder described above), `AgentProgress` (turn budget
and tool call log), `TurnTrackerChatClient` (counts model requests, feeds the
progress record), attachments and notes.

`Agents/ChatAssistant/` — the chat assistant: `ChatAssistant` (entry point),
`ChatAssistantContext`, `ChatAssistantResponse`.

`Agents/IAiAgentFactory.cs` — builds the `IChatClient` from the effective
`AiSettings` (per-assistant values override the top-level ones) and the `AIAgent`
with our own `FunctionInvokingChatClient`, so the iteration cap is
`MaxTurns` + headroom and the framework's default 40-turn cap never bites first.
Sandbox-backed tools are dropped when the sandbox is disabled.

`Tools/` — `ToolAttribute` plus reflection-based discovery, `DiscordTools`,
`TerminalTools`, `TurnLimitedAIFunction` (refuses calls once the turn budget is
spent), and tool result records.

`Prompts/` — `IPromptProvider`, `PromptSource`, `PromptTemplateCache`,
`PromptTemplate`, and the embedded `Resources/ChatAssistant.liquid`.

`Sandbox/` — the per-invocation Docker sandbox (`ISandbox`): storage
provisioning, lazy container start, dispose-driven teardown, plus
`SandboxReaper`.

`DependencyInjection.cs` — registers options and configuration schemas, the
scoped context/accessor, tool classes, the agent factory, the assistant, and
the sandbox services.

## Invocation flow

1. The command boundary binds the interaction's scope on
   `IInteractionConfiguration` (`SetScope`), so every later resolution in this
   DI scope reads the same cached snapshot. See `openspec/changes/dynamic-config/`.
2. The consumer (`AiCommandHandler`) builds a `ChatAssistantContext` with the
   caller, guild, channel, message history, and a progress record, then calls
   `ChatAssistant.Invoke(context, ct)`.
3. `ChatAssistant` resolves the effective `AiSettings` from that snapshot,
   initializes sandbox storage when enabled, renders the system prompt, and asks
   `IAiAgentFactory` for an agent.
4. `ContextAccessor` is populated before the run, so tools resolved from the
   same scope can enforce access control and read the turn budget.
5. The agent runs; `TurnLimitedAIFunction` refuses tool calls past the budget and
   tells the model to answer with what it has. Attachments produced by tools ride
   back on the response; the consumer materializes them and disposes the sandbox.

## Configuration

`Ai` — `BaseUrl`, `ApiKey`, `AssistantModel`, the `ChatAssistant` limits
(`HistoryContextSize`, `HistoryMaxAge`, `MaxAttachments`, `MaxTurns`,
`TranscriptTurns`, plus per-assistant `Model`/`BaseUrl`/`ApiKey` overrides), and
`Prompts`.

`Ai:Sandbox` — sandbox image, limits, DNS, and network. Read once at startup:
it is instance-wide on purpose, so a guild cannot move its sandbox onto a
different network or path.

The `Ai` section is resolved per interaction, so any of its keys (including
`Ai:Prompts:*`) can be overridden at guild, channel, or user scope with
`/config`. `Ai:Sandbox` accepts only a global override, which applies after a
restart.

## Prompt System

System prompts are rendered at invocation time using **Fluid** (Liquid templates).

### Configuration

Under `Ai:Prompts`, each key names a prompt consumer (e.g. `ChatAssistant`) and
points to a source:

```json
"Ai": {
  "Prompts": {
    "ChatAssistant": { "Type": "Resource", "Path": "Resources/ChatAssistant.liquid" },
    "SomethingElse":  { "Type": "File", "Path": "/mnt/prompts/Blah.txt" },
    "InlineOverride": { "Type": "Text", "Path": "You are {{CallerName}}. Be brief." }
  }
}
```

- `Resource` — loaded from embedded assembly resources. The path is relative to
  the project's `Resources/` folder (`ChatAssistant.liquid` and
  `Resources/ChatAssistant.liquid` both resolve to the manifest name
  `Dotto.Ai.Resources.ChatAssistant.liquid`); a full manifest name also works.
  Cached indefinitely (immutable at runtime).
- `File` — read from the given filesystem path. Cached with `LastWriteTime`
  invalidation — edit the file and the next invocation picks it up.
- `Text` — the `Path` value *is* the template. Nothing is looked up or read from
  disk, which makes it the natural choice for `Ai:Prompts:*` overrides set
  through `/config` (override rows are `varchar(8000)`, so long prompts should
  stay `Resource`/`File`).

All three are rendered as Liquid either way; the type only selects where the raw
text comes from.

### Template context

The chat assistant's system prompt receives:

| Variable | Type | Source |
|---|---|---|
| `CallerName` | string | Invoking user's display name |
| `CallerId` | ulong | Invoking user's Discord snowflake |
| `ChannelId` | ulong | Channel the assistant was invoked in |
| `GuildId` | ulong | Guild ID (`0` in DMs) |
| `UtcTime` | DateTime | Current UTC time |
| `Notes` | list | Context notes; each has `Title` and `Body` |
| `ToolsEnabled` | bool | Whether sandbox-backed tools are registered for this run |

### How it flows

1. `IPromptProvider` is **scoped**: `RenderAsync` resolves the interaction's
   `AiSettings` from `IInteractionConfiguration.GetCurrentAsync`, so prompt
   sources follow the same per-guild/channel/user overrides as the rest of the
   AI settings.
2. A singleton `PromptTemplateCache` owns one `PromptTemplate` per
   `(PromptType, Path)`, so the expensive part — reading an embedded resource,
   or a file plus its `LastWriteTime` check — is shared across interactions
   even though the provider itself is per interaction. The provider owns the
   `FluidParser` and the `UnsafeMemberAccessStrategy`.
3. In `ChatAssistant.Invoke()`, the assistant calls
   `promptProvider.RenderAsync("ChatAssistant", context)`: one call that looks up
   the source, loads the raw text, renders the Liquid template, and returns the
   result. The rendered text is prepended as a `ChatRole.System` message.
4. If no `ChatAssistant` prompt is configured, `RenderAsync` returns `null` and
   the system message is skipped (graceful fallback).

### Adding a new prompt

1. Add an entry to `Ai:Prompts` in config (or set the same key through
   `/config … scope:guild` for a scoped variant).
2. Create the template (embedded `.liquid`, absolute path, or inline `Text`).
3. In the consumer, inject `IPromptProvider` and call
   `RenderAsync("YourKey", context)`.
4. Prepend the returned text as a system message (null means no prompt
   configured).

## Registration

In the host project, call `AddAi` on the service collection with an
`Action<AiSettings>` delegate. This follows the standard .NET options pattern,
keeping the library decoupled from `IConfiguration`. `AddAi` also registers the
`Ai` and `Ai:Sandbox` configuration schemas used by validation, autocomplete,
and per-interaction resolution.
