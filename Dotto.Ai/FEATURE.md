# AI Agents

Provides the AI functionality for the Discord bot. This project encapsulates
prompts, logic, tools, and access control, wrapping Microsoft's agent
framework to add per-invocation context that the underlying agents
otherwise have no support for.

## Why wrapper agents?

Tools may require the caller context. For example, Discord tools such as "get the channel's messages"
may want to know the invoking user to control access to those channels; therefore, the tools need to inject state.
Without wrappers, the consumer would need to create a DI scope, populate it with invocation data,
resolve the keyed inner agent from that scope, run it, and dispose the scope. That infrastructure
boilerplate would be duplicated by every consumer.

Wrappers encapsulate all of it. Each wrapper can also expose agent-specific
signatures and configuration while sharing scope-management machinery via a
common base class.

## Structure

`Agents/` — Wrapper agents and the base class they derive from, plus
AgentContext, the scoped state holder carrying invocation information to
tools (who launched the agent, from what channel, etc.). Each wrapper
resolves a keyed inner agent from a fresh scope populated with context
before running.

`Tools/` — Auxiliary classes (ToolAttribute, reflection-based discovery) and
stateful classes providing tools, generally with transient lifetimes. Tool
methods can be flagged with a ToolType so they're targeted at specific
agents — a ban-user tool might be flagged Moderation only, while a
get-channel-info tool might be flagged for both Moderation and
UserAssistant.

`DependencyInjection.cs` — Registers options, context, tool classes, keyed chat clients, and keyed
agents.

## Invocation flow

1. Consumer calls the wrapper agent with invocation data and message history.
2. The base class creates a DI scope and populates AgentContext with
   invocation data (caller, guild, channel, etc.).
3. It resolves the keyed inner agent from the same scope. The agent factory
   resolves fresh tool instances from the scope — each picks up the
   populated AgentContext — and binds their tool methods.
4. The inner agent runs, calling tools as the LLM sees fit. Each tool reads
   from AgentContext to enforce access control.
5. The scope is disposed.

## Adding a new agent

Create a tool class in Tools/ with appropriately flagged tool methods, add
the tool class and a new keyed inner agent in DependencyInjection.cs, then
add a wrapper class in Agents/ extending the base class with the new
keyed agent key.

## Prompt System

System prompts are rendered at invocation time using **Fluid** (Liquid templates).

### Configuration

Under `Ai:Prompts` in appsettings, each key names a prompt consumer (e.g.
`ChatAssistant`) and points to a source:

```json
"Ai": {
  "Prompts": {
    "ChatAssistant": { "Type": "Resource", "Path": "Resources/ChatAssistant.liquid" },
    "SomethingElse":  { "Type": "File", "Path": "/mnt/prompts/Blah.txt" }
  }
}
```

- `Resource` — loaded from embedded assembly resources. Path is relative to
  the project root (e.g. `Resources/ChatAssistant.liquid` → manifest name
  `Dotto.Ai.Resources.ChatAssistant.liquid`). Cached indefinitely (immutable
  at runtime).
- `File` — loaded from an absolute filesystem path. Cached with
  `LastWriteTime` invalidation — edit the file and the next invocation picks
  it up.

### Template context

The following variables are available in every Liquid template:

| Variable | Type | Source |
|---|---|---|
| `CallerName` | string | Invoking user's display name |
| `CallerId` | ulong | Invoking user's Discord snowflake |
| `UtcNow` | DateTime | Current UTC time |

### How it flows

1. `AddAi()` eagerly configures `AiSettings` to discover the `Prompts` dict.
2. Each entry is registered as a keyed singleton `IPromptTemplate` — the key
   is the JSON key name (e.g. `"ChatAssistant"`).
3. `IPromptRenderer` (singleton) wraps `FluidParser` + `UnsafeMemberAccessStrategy`.
4. In `ChatAssistant.Invoke()`, the wrapper resolves
   `IPromptTemplate("ChatAssistant")` and `IPromptRenderer`, gets the raw
   template, renders it with the context dictionary, then prepends the result
   as a `ChatRole.System` message.
5. If no `ChatAssistant` prompt is configured, the system message is skipped
   (graceful fallback).

### Adding a new prompt

1. Add an entry to `Ai:Prompts` in config.
2. Create the template file (embedded `.liquid` or absolute path).
3. In the consumer, resolve `IPromptTemplate("YourKey")` from the DI scope.
4. Call `GetAsync()` → `RenderAsync()` → prepend the rendered text.

## Registration

In the host project, call AddAi on the service collection with an
Action<AiSettings> delegate. This follows the standard .NET options pattern,
keeping the library decoupled from IConfiguration.