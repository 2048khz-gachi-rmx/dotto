# Tasks

## 1. Settings and the per-run inbox

- [ ] 1.1 Add `SteeringSettings` with a single `Enabled` (default `true`) as
      `AiSettings.ChatAssistant.Steering`, and add the same default to `Dotto.Bot/appsettings.json`;
      verify with a unit test that the default binds and with the existing
      `ConfigurationSchemaRegistrationTests` that the key is discoverable per scope
- [ ] 1.2 Implement `SteeringInbox` in `Dotto.Ai/Agents/Steering/`: a lock-guarded pending list and a
      lock-guarded list of delivered messages, `Post` for gateway threads, `Drain` returning the
      messages received since the last drain (verbatim, in arrival order) and everything already
      delivered, plus disposal; verify with unit tests for ordering, empty drains, accumulation across drains,
      and posting after disposal (`dotnet test Dotto.Tests --filter FullyQualifiedName~SteeringInbox`)

## 2. Injection into the run

- [ ] 2.1 Implement `SteeringChatClient : DelegatingChatClient` that copies the incoming messages,
      appends the pending steering messages as user-role `<system>` envelopes around a
      `<message><content>…</content></message>` element, and delegates;
      verify with a stub provider that records requests: the envelope follows the tool results, three
      arrivals become three envelopes in arrival order, the system prompt stays
      first, an earlier envelope is still present on later requests, and the number of requests and
      `AgentProgress.Turns` are unchanged
      (`dotnet test Dotto.Tests --filter FullyQualifiedName~SteeringChatClient`)
- [ ] 2.2 Assert the framework's message list is not mutated: after a run with steering, the agent's
      input messages are unchanged and no steering content is appended to them; verify with the same
      stub provider recording the request lists by reference
- [ ] 2.3 Make a drain failure non-fatal: catch exceptions from the inbox inside the client, log them,
      and send the request without steering; verify with a throwing inbox stub that the response still
      arrives (`dotnet test Dotto.Tests --filter FullyQualifiedName~SteeringChatClient`)
- [ ] 2.4 Add `SteeringInbox? Steering` to `AgentContext`, extend `IAiAgentFactory.BuildAgent` with the
      steering accessor and place the client between `FunctionInvokingChatClient` and
      `TurnTrackerChatClient`, and pass `() => accessor?.Current?.Steering` from `CreateAgent`; verify
      with the existing `AgentSessionShapeTests` (tool list and prompt still fixed) and by running
      `dotnet test Dotto.Tests --filter FullyQualifiedName~AgentSessionShape`
- [ ] 2.5 Implement the singleton `AiRunSteeringRegistry`: register a run's inbox under its channel,
      offer each message to every run registered there, copy-on-write snapshot lookup, idempotent
      removal on disposal, silent drop when nothing matches; verify with unit tests for two concurrent
      runs in one channel both receiving a message, a run in another channel not receiving it,
      disposal, double disposal, and concurrent posting from several threads
      (`dotnet test Dotto.Tests --filter FullyQualifiedName~SteeringRegistry`)
- [ ] 2.6 Create and register the inbox in `ChatAssistant.Invoke` right after `accessor.Set(context)` and
      dispose it in the existing `finally`; verify with a test using a stub registry that the
      subscription exists while the agent runs and is gone after both a successful and a throwing run
      (`dotnet test Dotto.Tests --filter FullyQualifiedName~ChatAssistantSteering`)

## 3. The Discord-side listener

- [ ] 3.1 Add the scoped `AiSteeringMessageProcessor : IGatewayEventProcessor<Message>` in
      `Dotto.Discord/Commands/Ai/` and register it in `AddMessageListeners`; it skips bot authors,
      empty content and messages carrying an interaction, offers everything else to the registry, and
      swallows and logs every exception; verify with unit tests for each filter, for a throwing
      registry (the processor must not throw), for a message from a user who did not invoke the run,
      and for a text-command-looking message being delivered like any other
- [ ] 3.2 Verify the coordinator's reflection discovery finds the new processor (`dotnet test
      Dotto.Tests --filter FullyQualifiedName~EventProcessor` or the existing discovery test)

## 4. Prompt disclosure

- [ ] 4.1 Add `SteeringEnabled` to `ChatAssistant.BuildTemplateContext` and a steering paragraph to
      `Dotto.Ai/Resources/ChatAssistant.liquid`, gated on its own flag rather than `ToolsEnabled`:
      messages may arrive mid-run, they appear as a `<system>` envelope after tool results, they are
      chat input rather than tool output, the newest instruction wins on conflict; verify with
      `ChatAssistantPromptTests` asserting the paragraph is present when enabled and absent when
      disabled, and that the base prompt is otherwise unchanged

## 5. Integration checks

- [ ] 5.1 Run the full suite (`dotnet test Dotto.Tests`) and verify it passes, including the new
      steering tests and the existing prompt, session-shape and handler tests
- [ ] 5.2 Manually exercise one long `/ai` run in a test guild: send a correction from the caller and
      one from a different member while a tool is running, and confirm the answer follows both; confirm
      a bot message does not steer and that a message sent after the answer changes nothing and is not
      mentioned anywhere
- [ ] 5.3 Set `Ai:ChatAssistant:Steering:Enabled=false` through `/config` for one channel, verify the
      prompt paragraph disappears and messages no longer affect runs there, then remove the override
- [ ] 5.4 Ask the user to record the new behaviour in `Dotto.Ai/FEATURE.md` (per AGENTS.md), and verify
      the file describes steering once they do

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
