# Design

See `proposal.md` for motivation and `specs/ai-run-observability/spec.md` for the behaviour
contract.

## Context

The agent loop is a black box: `ChatAssistant.Invoke` calls `ChatClientAgent.RunAsync(messages)`
with tools built by reflection in `Dotto.Ai/DependencyInjection.cs` (`AIFunctionFactory.Create`
over `[Tool]` methods, resolved from the interaction's DI scope), and the bot learns about the run
only when it returns. Tools already share per-invocation state through `ContextAccessor`
(`AgentContext` in a scoped accessor), which is how tools know the caller and channel. The
Discord layer owns the interaction: the slash command defers, then sends the answer as a followup;
the text command replies after the run.

## Goals / Non-Goals

**Goals:**

- Observe tool calls without replacing the agent framework or rewriting tools.
- One visible message per run: the answer replaces whatever progress was shown.
- Keep Discord API usage bounded (edit rate limits, 2000-character messages).

**Non-Goals:**

- Streaming assistant tokens or exposing the model's reasoning.
- A cancel button or cooperative cancellation plumbing (needs a component-interaction handler;
  deliberately deferred).
- Per-tool bespoke instrumentation; tools stay unaware of observability.

## Decisions

### Progress lives in the agent context, not in the tools

`AgentContext` gains an optional `AgentProgress`, created by the caller (the Discord command) with
the configured turn limit. `ContextAccessor` exposes the current context, so the tool wrapper and
the caller reach the same instance without touching tool constructors.

*Alternatives:* scoped singleton injected into every tool (touches every tool and every test);
ambient static (breaks concurrent runs).

### Turn counting and limits are enforced by an `AIFunction` decorator

`DelegatingAIFunction` wraps each tool at registration time. It counts the call, records the
previews, applies warnings, and refuses calls past the limit. This keeps the framework's loop
intact and works for any future tool added to the `[Tool]` set.

*Alternatives:* replacing `ChatClientAgent.RunAsync` with a hand-written loop or a custom
`DelegatingChatClient` (far more code, duplicates the framework's tool handling); wrapping each
tool method by hand (duplication, easy to miss).

### Warnings are appended to tool results

The model only sees tool results, so the "approaching limit" and "last turn" notices are prepended
to the result string of the threshold calls. No framework support is needed and the warning arrives
in the same turn the limit applies.

*Alternatives:* injecting a synthetic user message mid-loop (not possible with `RunAsync`);
counting turns in the prompt (the model cannot count reliably).

### Calls past the limit are refused, not thrown

Returning a notice keeps the run alive so the model can still produce an answer; an exception
would abort the run and lose the accumulated state. The residual cost of a model that keeps calling
is one cheap, non-executing tool result per call.

### The run's message is reused for activity

The slash command edits its deferred response (`ModifyResponseAsync`) and the text command edits
its reply message; when the run ends, the same message is edited into the final answer. A separate
"activity" message would need creation, deletion and a second rate-limit budget.

Edits are coalesced by a pump that debounces signals from `AgentProgress.Changed` (bounded channel,
one render per ~1.3 s), which keeps the message under Discord's per-message edit limit even for
bursts of tool calls. Rendering is delegated through `Func<Action<MessageOptions>, Task>`, so the
renderer is testable without a Discord client and both command types share it.

*Alternatives:* editing on every change (rate limits, bursty API traffic); a background thread per
run (leaks if the run throws).

### Delivery of the final answer degrades gracefully

The final edit can fail if the interaction token has expired. In that case the answer is sent as a
normal followup/reply instead, so the user always receives it.

### Transcript trigger and budget

The transcript is appended when the run exceeded `TranscriptTurns`, or when the caller passed
`verbose`. Failed calls do not trigger it — a command failing is ordinary (a missing binary, a
non-matching `grep`) and a short run's history is noise; the answer already says what went wrong,
and a failed call inside a long transcript is marked `(failed)`. The transcript is capped (last
calls, ~1200 characters) so the answer together with attachment notes stays inside 2000 characters;
omitted calls are summarised as a count.

### Oversized answers become a file, never a truncation

If the composed message still exceeds the limit, the full text (answer, transcript, links) is sent
as `answer.txt`, and the message keeps the prompt line, a truncated excerpt, a note naming the file
and the footer. The prompt line stays inline because channel history is rebuilt from message
content; the footer stays last. `answer.txt` counts against Discord's 10-attachment limit, so if
the run already produced 10 files the last one is dropped with a warning — the answer text wins,
and the media still exists in the sandbox/S3.

*Alternatives:* truncating the answer (the original behaviour — silently loses content, and the
user cannot tell what was cut); splitting the answer across followup messages (multi-message
answers pollute history and can be reordered); always attaching a file (pointless for the common
short answer).

### Failure is structural, not word-matched

A call counts as failed when the tool says so: tools throw `ToolFailureException` for outcomes the
model should see (download failed, sandbox rejected the request, file missing), and the wrapper
records the call as failed while returning the exception message as the tool result — so the
model's view of the failure is unchanged. Shell calls are judged by their `Exit code: N` line
(`N != 0`). No result text is pattern-matched, so a successful command whose output merely mentions
"error" or "not found" is not a failure.

*Alternatives:* substring markers (false positives on ordinary output — the original approach,
rejected); parsing each tool's prose (fragile and tool-specific); returning a result object from
every tool (would change the model-facing return schema for no extra benefit).

## Risks / Trade-offs

- [Turn limit reached but the model keeps calling tools] → each call only returns a refusal; the
  answer still arrives. A hard abort can be added later if long tail runs become a problem.
- [Interaction token expires before the final edit (runs up to the 15-minute wall clock)] → fall
  back to a followup/reply so the answer is never lost.
- [Edit bursts hit a 429] → edits are debounced and `RestException`/HTTP failures during rendering
  are swallowed with a debug log; the next edit repairs the display.
- [A shell command exits non-zero for a non-error reason (e.g. `grep` with no match)] → the call is
  marked failed but the run keeps going; only the `(failed)` marker inside an already-triggered
  transcript shows it.
- [A tool forgets to throw on a failure path] → the call looks successful and carries no
  `(failed)` marker; the model still sees the message, so the run is not broken.
- [The answer overflows while the run already attached 10 files] → the last file is dropped to make
  room for `answer.txt`; the media is still addressed in the sandbox/S3 and this is logged.
- [Activity text leaks tool arguments into the channel] → arguments and results are user-initiated
  data in the caller's own channel; previews are the same data the final answer already exposes.

## Migration Plan

Config additions are optional with defaults (`MaxTurns = 20`, `TranscriptTurns = 5`), so existing
deployments keep working. No database, schema, or protocol changes; rollback is a code revert.
