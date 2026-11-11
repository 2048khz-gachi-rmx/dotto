# Tasks

## 1. Progress model and settings

- [x] 1.1 Add `AgentProgress` (turn count, `ToolCall` records, warnings, transcript trigger) and its
      preview/format helpers to `Dotto.Ai/Agents/Base/`, and verify with unit tests covering
      120/180-character truncation, whitespace collapsing, activity rendering, footer text and
      `ShouldShowTranscript` (`dotnet test Dotto.Tests --filter FullyQualifiedName~AgentProgress`)
- [x] 1.2 Add `MaxTurns` (default 20) and `TranscriptTurns` (unset by default, falling back to
      `MaxTurns`) to `ChatAssistantSettings` with range validation, and verify defaults and validation
      with a unit test on the options object

## 2. Instrumentation

- [x] 2.1 Add `Progress` to `AgentContext`, expose the current context on `ContextAccessor`, and
      pass a progress instance from `AiCommandHandler.Invoke` into `ChatAssistantContext`; verify by
      building the solution and running the existing `AiCommandHandler*` tests unchanged
- [x] 2.2 Wrap registered tools in a `DelegatingAIFunction` decorator that records calls, applies the
      approaching-last-turn warnings and refuses calls past the limit, and verify with unit tests
      driving a fake `AIFunction` turn by turn (warnings in turns 15 and 19, refusal in turn 21, no
      warning at 14, several calls in one turn all allowed and warned once, failure flag for a
      non-zero exit result)
- [x] 2.2a Count turns in `TurnTrackerChatClient` (a `DelegatingChatClient` calling
      `AgentProgress.BeginTurn()` per model request) so a batched response costs one turn, not one
      per call; verify with a stub chat client and by the turn-based limit tests
- [x] 2.2b Mark `UploadFile` exempt from the turn limit via the `[Tool]` attribute so a file the
      model already produced still reaches the user; verify an exempt tool executes past the limit
      while a normal tool in the same turn is refused
- [x] 2.3 Add the turn-limit disclosure to `Dotto.Ai/Resources/ChatAssistant.liquid` and verify with
      a prompt test asserting the rendered system prompt mentions the turn limit and warnings

## 3. Discord rendering

- [x] 3.1 Add the coalescing activity renderer (`AgentProgress.Changed` → at most one edit per
      ~1.3 s, newest calls only, silent on edit failures) taking update delegates so it needs no
      Discord client, and verify with a unit test using fake delegates (several signals render once;
      dispose stops further edits)
- [x] 3.2 Wire the `/ai` slash command: deferred response edited as the run's message, final answer
      replaces it (with attachments), transcript appended per the trigger rules, `verbose` option
      forced transcript, footer always appended, followup fallback if the final edit fails; verify by
      building and by unit tests for the composed message text (transcript trigger + footer + 2000-
      character cap)
- [x] 3.3 Wire the `ai` text command the same way against its reply message, and verify it compiles
      and reuses the shared renderer and text builders

## 4. Integration checks

- [x] 4.1 Run the full suite (`dotnet test Dotto.Tests`) and verify it passes, including the new
      observability tests and the existing prompt/handler tests
- [ ] 4.2 Manually exercise one `/ai` run that calls a missing binary and confirm the activity line
      shows the failing command, the answer carries the transcript and footer, and the run stops at
      the turn limit
- [ ] 4.3 Ask the user to record the new behaviour in `Dotto.Ai/FEATURE.md` (per AGENTS.md), and
      verify the file describes the activity line, turn budget and transcript once they do

## Workflow follow-up

- Archive the change after the project's review requirements are satisfied.
