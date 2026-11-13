# Proposal

## Why

An `/ai` run is a conversation with a model that cannot hear the channel it is working for. A run
that picked the wrong repository, or is transcoding the wrong file, keeps going until the turn
budget is gone; the only correction available to the user is to wait for the answer and start over.
The model is already listening through history at the start of a run — there is no reason the
middle of one should be deaf.

## What Changes

- **Live steering**: while a run executes, messages arriving in the run's channel are attached to the
  run's next model request, after the previous turn's tool results. The request was going to be made
  anyway, so a steer costs no turn by construction — it is extra context at the start of a turn, not
  a turn of its own.
- **Framing**: each arrival is delivered as its own user-role message holding a `<system>` envelope
  around the same `<message user="…"><content>…</content></message>` element the run's initial
  history uses, so steering cannot be read as tool output or as part of the original request.
- **Anyone in the channel may steer**: the caller, another member, whoever is there. Bot messages and
  command invocations are not steering. No eligibility configuration.
- **No budget**: nothing is capped, coalesced away, trimmed or counted. Every message that arrived
  since the last request is delivered verbatim.
- **Late messages are dropped silently**: the interaction is finished once the answer is given, so a
  message arriving after the run's last request changes nothing and is not reported.
- **Disclosure**: the system prompt explains steering while it is enabled.
- **Non-goals**: per-run steering caps or accounting, a hard stop/cancel that aborts a run (a steer is
  advice to the model; the cancel button remains deferred from `ai-run-observability`), steering from
  another channel, requiring a reply to the run's message, and any archive format.

## Capabilities

### New Capabilities

- `ai-run-steering`: how a running `/ai` session accepts new chat messages from its channel — who may
  send them, where they enter the model's context, how they are framed, and when they stop applying.

### Modified Capabilities

None. `openspec/specs/` is currently empty because the in-flight changes that created the `/ai`
capabilities (`ai-sandbox`, `ai-run-observability`) have not been archived, so there is no archived
capability to modify; the requirements below extend that design.

## Impact

- **`Dotto.Ai`**: a `SteeringInbox` and a `SteeringChatClient : DelegatingChatClient` in
  `Dotto.Ai/Agents/Steering/`; the run-wide `AiRunSteeringRegistry` singleton; `AgentContext.Steering`;
  `IAiAgentFactory.BuildAgent` wiring; `ChatAssistant.Invoke` registers and disposes the run's
  subscription; `ChatAssistant.liquid` gains the steering paragraph; `ChatAssistantSettings` gains a
  `Steering` block with a single `Enabled` switch.
- **`Dotto.Discord`**: a new scoped `AiSteeringMessageProcessor : IGatewayEventProcessor<Message>`
  registered in `AddMessageListeners`, which looks the channel's runs up in the singleton registry.
- Tests: unit coverage for the inbox (order, delivery, disposal), the chat-client injection point
  (message order, accumulation, no extra requests, no mutation of the framework's list), the registry
  (scope crossing, concurrent runs, disposal), the message filters, and the prompt paragraph.
- No changes to the Discord message surface, the sandbox image or API, the database, the downloader,
  or the provider request shape beyond one additional user message.
