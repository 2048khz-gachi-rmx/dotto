# Design

See `proposal.md` for motivation and `specs/ai-run-steering/spec.md` for the behaviour contract.

## Context

An agent run is one `ChatClientAgent.RunAsync(context.Messages)` call in
`Dotto.Ai/Agents/ChatAssistant/ChatAssistant.cs`, invoked from
`Dotto.Discord/CommandHandlers/Ai/AiCommandHandler.cs`. The agent's chat client is built in
`Dotto.Ai/Agents/IAiAgentFactory.cs`:

```
FunctionInvokingChatClient(                       // ours, MaximumIterationsPerRequest = MaxTurns + 5
    TurnTrackerChatClient(providerClient, () => accessor?.Current?.Progress))
    { UseProvidedChatClientAsIs = true }
```

Three facts drive this design, verified against Microsoft.Extensions.AI 10.6.0 (the version
`Microsoft.Agents.AI` 1.13.0 resolves) rather than assumed:

1. `FunctionInvokingChatClient.GetResponseAsync` copies its input
   (`var originalMessages = messages.ToList()`), and on the first iteration after a response builds
   `augmentedHistory = originalMessages.ToList()`. After every turn it appends the response's
   messages and then `ProcessFunctionCallsAsync(augmentedHistory, ...)` appends the tool-result
   messages. The next request is made with that same `List<ChatMessage>`, i.e. the inner client is
   handed the accumulated history whose tail is the previous turn's tool results.
2. Therefore a `DelegatingChatClient` placed immediately inside `FunctionInvokingChatClient` sees,
   on every request, exactly "system, user, assistant tool calls, tool results" — the point where
   the user asked steering to land.
3. `augmentedHistory` is cleared and rebuilt when a response carries a `ConversationId` (server-side
   history), so a message injected by mutating that list can disappear between turns. Anything we
   want the model to keep seeing must be re-applied by us on every request, not written into the
   framework's list once.

On the Discord side, gateway events do not run in the interaction's scope:
`Dotto.Discord/EventHandlers/MessageCreateHandlerCoordinator.cs` is a singleton
`IMessageCreateGatewayHandler` that creates a fresh DI scope per message and runs every
`IGatewayEventProcessor<Message>` found by reflection in it, concurrently, via `Task.WhenAll`. A
listener and a live run therefore meet through a singleton.

## Goals / Non-Goals

**Goals:**

- One extension point for injection, with no change to the agent framework, the tool set or the
  turn accounting.
- A channel listener that is free of Discord-side state and cheap enough to run for every message.
- No per-run budget: steering is request shaping, not a resource to ration, so nothing is capped,
  trimmed or counted.
- Failure of any steering component degrades to "no steering", never to a failed run.

**Non-Goals:**

- Aborting a run (that is cancellation, not steering; the cancel button is still deferred).
- Steering across channels, or requiring a reply to the run's message.
- Persisting steering outside the run's own lifetime (archive format belongs to whoever writes the
  archive).

## Decisions

### Steering is injected by a delegating chat client, not by polling or a tool

`SteeringChatClient : DelegatingChatClient` sits between `FunctionInvokingChatClient` and
`TurnTrackerChatClient`. On each request it copies the incoming messages into a fresh list, appends
the pending steering messages if the run's inbox has any, and delegates. Nothing else in the
pipeline changes.

*Alternatives:*

- **Polling channel history each turn.** Needs a Discord `RestClient` inside `Dotto.Ai`, one REST
  call per turn (rate limits, latency on the critical path), and it re-reads messages that the run's
  initial history already contains — deduplication by message id, which the bot has no stable view
  of, would become a correctness problem. Rejected.
- **A model-invocable `CheckForMessages` tool.** Cheapest to build and needs no listener, but it
  costs the caller a turn and tokens per check, and it depends on the model choosing to poll at the
  moment a correction matters — precisely when it believes it is on track. The user's whole point is
  that the correction must arrive without the model asking. Rejected.
- **Mutating the framework's `augmentedHistory` list in place.** Works on the multi-turn path but
  silently loses steers when the provider returns a `ConversationId` (point 3 above) and depends on
  a list we do not own. Rejected.

Consequence: because the framework's list never contains our messages, the inbox keeps what it has
already delivered and the client re-appends them on every later request. That is also what makes
steering survive a rebuilt history. The re-sent text grows with what arrived during the run, which
is bounded by the run's own duration — seconds to minutes — not by a cap.

There is no coalescing step: an arrival is delivered as it arrived, so the model sees who said what
in what order rather than a digest the bot assembled.

Injection therefore cannot cost a turn, and that is structural rather than careful accounting: the
request exists whether or not there is steering to attach, so the number of requests, the turn count
and the turn budget are identical with or without it.

### Each steering message is its own `<system>` envelope

Channel history is already delivered to the model as XML built by `BuildSimpleHistoryElement`
(`<message user="…"><content>…</content></message>` in
`AiCommandHandler.MessageToAiHistoryMessage`), so steering reuses that element verbatim and wraps it
in a `<system>` envelope:

```
<system>
New chat messages received:
<message user="alice"><content>no, use the other file</content></message>
</system>
```

Arrivals are not merged: everything received since the last request is appended in arrival order,
one message each. A steering message is always a new message, never a merge into a tool result, an
assistant message or the original prompt: the model must not be able to read steering as tool
output, and a provider that validates tool-call/tool-result adjacency sees an untouched tool-result
run followed by ordinary user input.

The envelopes are `user`-role. A mid-run system message would change the session shape
`ai-run-observability` pinned (the system prompt is the first message and stays unchanged), and a
second system message mid-conversation is not universally accepted by providers.

*Alternatives:* an attribute form, `<message user="…" content="…" />` (one line per message, but a
second XML dialect for the same datum — the model would have to learn two shapes for chat content);
one envelope with several `<message>` children (invents a grouping the channel did not have, and a
single long arrival would sit inside a container implying more); prefixing the text into the next
tool result (indistinguishable from tool output — the exact confusion the envelope exists to prevent).

### The bridge is a singleton registry keyed by channel, holding per-run inboxes

`AiRunSteeringRegistry` (singleton, `Dotto.Ai/Agents/Steering/`) maps a channel id to the inboxes of
the runs currently executing there. `ChatAssistant.Invoke` registers an inbox for
`(channelId, callerId, guildId, sessionId)` and disposes it in the existing `finally`; the scoped
`AiSteeringMessageProcessor` in `Dotto.Discord/Commands/Ai/` resolves the singleton and offers each
message to every run registered in that channel — eligibility is a filter in the listener, not a
per-run policy in the registry.

*Alternatives:* keying by `channelId + callerId` (collapses two runs by the same caller in one
channel and would drop every other participant's steering); keying by session id (the gateway event
carries no session id); a per-message query of a run-tracking table (persistence is not needed for a
process-local, seconds-long relationship); having the listener and the run share the interaction
scope (impossible — the coordinator creates a new scope per message).

The registry holds only live runs: removal is idempotent and happens on disposal, success or
failure. A message that arrives when nothing is registered is dropped without logging noise (debug
only), which is the overwhelmingly common case for a busy channel.

### Anyone in the channel may steer

Every human with access to the channel can steer every run executing there — the caller, another
member, whoever is present. That is the requested policy, and it is also the simplest one: the model
already treats channel text as user input, so steering carries exactly the authority that history
already carries.

Only three things are filtered, and all are facts about the message rather than about the author:
`Message.Author.IsBot` (as `MessageUrlDownload` and `AutoVideoCompressor` already do), empty content,
and messages carrying an interaction/command payload (`Message.Interaction`), which are command
invocations rather than conversation. Prefix text commands are not distinguished: a `.ai …` message
arrives as ordinary channel content and steers like anything else, because distinguishing it would
mean reading `Discord:Prefix`, which `Dotto.Discord` does not read today, for a case where being
steered with a command string is harmless.

*Alternatives:* caller-only authority (protects a run from other participants, but the person
watching the run is not necessarily the one who started it); requiring a reply to the run's message
(one more rule the user has to know, and in a busy channel the correction is rarely a reply).

Accepted risk: in a public channel anyone can redirect someone else's run. The envelope names the
author, the model can weigh that, and `Enabled=false` at guild or channel scope is the off switch.

### No per-run caps

`SteeringInbox` is a lock-guarded pending list plus a lock-guarded list of already-delivered
messages; `Post` runs on gateway threads, `Drain` on the run's request path. No queue capacity, no
per-run quota, no character cap, no drop counters: a correction someone typed is a correction they
meant to send, and silently keeping the newest N would defeat the purpose. Messages are delivered
verbatim.

The only natural bound is the run's duration. A run lasts seconds to minutes, so its steering list is
a handful of chat messages, and the delivered messages are re-sent on later requests the way any other
conversation turn is.

*Alternatives:* a bounded queue with `DropWrite` (silent loss the user cannot see); a per-run applied
cap (after N corrections the channel goes deaf, which is the very failure being fixed); character
trimming with an `(N omitted)` marker (the marker can hide the operative clause of the instruction).

### Late steering is dropped, not reported

The interaction is finished the moment the answer is given. A message arriving after the run's last
model request is therefore never delivered: the result is unchanged, nothing is counted and nothing
is said. The message is still in the channel, so the next run's history already carries it.

*Alternatives:* an emoji reaction acknowledging receipt (an extra REST call per message, and it
promises attention a finished run cannot give); editing the activity message as steering lands
(competes with tool activity for the same debounce budget and is replaced by the answer anyway); a
footer line listing unfollowed messages (adds noise to an answer the message could no longer change).

### The prompt discloses steering while it is enabled

`ChatAssistant.BuildTemplateContext` gains `SteeringEnabled`;
`Dotto.Ai/Resources/ChatAssistant.liquid` renders, behind that flag (not behind `ToolsEnabled` —
steering does not depend on the sandbox), a paragraph stating that messages may arrive during the
run, that they appear as a `<system>` envelope after tool results, that they are chat input rather
than tool output, and that the newest instruction wins on conflict. A run with steering disabled
renders exactly today's prompt, so `ChatAssistantPromptTests` still pins the base prompt unchanged.

### Configuration is a single switch

`AiSettings` is the per-scope section (`ConfigurationOptionSchema("Ai", typeof(AiSettings))`,
`[ConfigurationScopeSupport(Any)]`), and `ConfigurationSchemaRegistry` walks nested option types, so
a nested `SteeringSettings` property is overridable at guild/channel/user scope with no plumbing:

```json
"Ai": { "ChatAssistant": { "Steering": { "Enabled": true } } }
```

One key is enough: who may steer is a constant, and the absence of budgets means there is nothing
left to tune. `Enabled` earns its place as the rollback lever — it silences collection and the prompt
paragraph without a deploy — and per-scope control matches the rest of `AiSettings`. `Ai:Sandbox`
stays startup-bound; nothing here is infrastructure.

### Wiring

- `AgentContext` gains `SteeringInbox? Steering { get; set; }`; `AiAgentFactory.CreateAgent` passes
  `() => accessor?.Current?.Steering` into `BuildAgent`, exactly as it already passes `Progress`.
- `BuildAgent`'s chain becomes
  `FunctionInvokingChatClient(SteeringChatClient(TurnTrackerChatClient(provider, progressAccessor), steeringAccessor))`.
  Steering sits outside the counter because injection is a request-shaping concern and the counter
  stays a pure counter; the extra parameter defaults to `null` so existing tests compile.
- `ChatAssistant.Invoke` creates the inbox right after `accessor.Set(context)` (before sandbox init
  and prompt rendering, so the registration window is as small as possible), registers it, and
  disposes it in the existing `finally`. `CancellationToken` and the sandbox teardown are untouched.
- `AiSteeringMessageProcessor` is registered scoped in `AddMessageListeners`
  (`Dotto.Discord/DependencyInjection.cs`) next to the other message processors; the coordinator's
  reflection picks it up.

## Risks / Trade-offs

- [The model ignores or misreads steering] → the prompt states the envelope's meaning and steering
  is structurally distinct from tool output; a model that still ignores it produces a normal answer.
- [A provider rejects a user message directly after tool results] → this is valid for the
  OpenAI-compatible chat-completions shape the bot uses; if a future provider disagrees the run
  fails loudly at the provider, and `Enabled=false` is the per-scope escape hatch.
- [Token cost of re-sending delivered steering each turn] → it grows with what arrived during the
  run, so it is proportional to the channel's chatter over a few minutes, not to a stored backlog.
- [A message arrives after the run's last request] → dropped without notice; the message stays in the
  channel for the next run, which is where it would have been useful anyway.
- [A message arrives between the history fetch and the inbox registration] → lost as steering; the
  window is reduced to sandbox init/prompt rendering by registering before them, and the message is
  still in the channel for a subsequent run.
- [A leaked registration keeps a dead run addressable] → entries are removed on disposal on both
  success and failure; a leak costs at most one small list.
- [Anyone in a public channel can redirect someone else's run] → accepted by design; the envelope
  names the author, and `Enabled=false` at guild or channel scope turns steering off there.
- [A steering-heavy channel causes many gateway enqueues] → the listener does no I/O, no Discord
  calls and one dictionary lookup; it is cheaper than the existing URL download processor.

## Interaction with other in-flight changes

- `ai-run-observability` pins that the tool list and the system prompt are fixed for the run. This
  change adds to the message tail only; it never edits, moves or trims the system prompt or the tool
  list, and warns nothing about turns. It also does not touch the activity line or the composed
  answer, so the observable run surface is unchanged.
- `ai-session-transcripts` (a separate, unmerged change; not present in this repository's
  `openspec/changes/`) archives full run transcripts. Nothing here depends on it: steering text is
  part of the request stream, so a future archiver that records outgoing requests (another
  delegating client, or a provider-level logger) captures it for free. Note for that change:
  `ChatAssistantContext.Messages` is *not* the full transcript — the framework copies it and appends
  to its own list — so an archiver must observe requests, not `context.Messages`.

## Migration Plan

The only default is `Enabled=true`, so no config change is required. Rolling back is a code revert;
setting `Ai:ChatAssistant:Steering:Enabled=false` (or a scoped override) disables collection and the
prompt paragraph without a deploy. No database, protocol, sandbox or image changes.

## Open Questions

- Whether a per-channel cap on concurrently steered runs is needed. Deferrable: the bot's run
  concurrency is already bounded by the sandbox and model limits, and steering adds no per-run
  bookkeeping to police.
