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

### Turns are counted by the chat client, limits enforced by an `AIFunction` decorator

A turn is one model response, and a response may ask for several tools at once, so counting tool
calls would let a model that batches calls spend a different budget than one that serialises them.
`TurnTrackerChatClient` (a thin `DelegatingChatClient`) therefore counts requests and calls
`AgentProgress.BeginTurn()` before passing the request on; the `DelegatingAIFunction` wrapper then
records the call, applies warnings, and refuses calls in turns past the limit. Everything the
framework's loop does stays intact, and any future tool added to the `[Tool]` set is covered.

Delivery tools are the exception: `UploadFile` is marked `ExemptFromTurnLimit` on the `[Tool]`
attribute, because refusing an upload would silently drop a file the model already produced — the
observed failure mode that motivated this whole counter (a third of three rendered videos never
reached the user). Everything else, including downloads and commands, is still refused.

*Alternatives:* counting tool calls instead of turns (what we had first: a run of 21 calls — 18 of
them one batch — "hit the limit" while delivering two of three requested files); replacing
`ChatClientAgent.RunAsync` with a hand-written loop (far more code, duplicates the framework's tool
handling); wrapping each tool method by hand (duplication, easy to miss).

### The limit denies calls; it never edits the session

The tool list, the tool mode and the system prompt are set once when the agent is built and are
never touched afterwards: the decorator only decides whether a call runs, and the denied call still
returns a normal tool result. Pruning tools as the budget runs out looks like a cheaper way to say
"no more calls", but a model that no longer sees its tools tends to write the call into its answer
instead — which is how a raw tool call ended up in a user-visible reply. With the tool list intact,
a denied call is a plain result the model can react to.

The framework has its own stop condition — `FunctionInvokingChatClient.MaximumIterationsPerRequest`,
40 by default — and on its final iteration it stops advertising tools and returns the response with
the unanswered call in it. That is the same leak from the other side, so the agent is built with our
own function-invoking client (`UseProvidedChatClientAsIs`) whose cap is the turn budget plus a few
requests of headroom: the model needs one request to be refused, one to answer, and the rest covers
a model that argues with the notice. If the cap is ever reached anyway, the caller replaces the
empty answer with a notice that the run ran out of turns.

*Alternatives:* dropping tools on the last turn (caused the leaked call); switching
`ToolMode` to `None` (same failure, and it also breaks providers that reject a tool-call history
without tools); changing the system prompt mid-run (invalidates the prompt cache and the model's
own plan).

### Warnings are appended to tool results

The model only sees tool results, so the "approaching limit" and "last turn" notices are prepended
to the result string of the first call to complete in the threshold turn. No framework support is
needed and the warning arrives in the turn the limit applies.

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

### Activity lines are formatted for skimming, not for copying

Each line is `-# <Tool>: `<args>` **#>** [<exit>] `<output>``: subtext so it reads as chatter under
the answer, code spans so arguments and output render verbatim (no accidental bold/italics from
`*`/`_` in shell output), and the exit status bracketed and only present when non-zero. Discord
neither nests nor escapes backticks inside an inline code span, so a literal backtick in the content
is replaced with a visually similar grave accent instead of silently terminating the span and
leaking raw markdown.

### Results are typed; only the renderer knows markdown

`ToolCall` records the tool's result object as-is (`IToolResult`) instead of derived preview fields,
so nothing mutates the record after the fact: `TextToolResult` carries only the model-facing text,
`CommandToolResult` adds `ExitCode`/`Stdout`/`Stderr` and derives `Failed` from the status. The exit
code is never parsed out of text — the tool that has the streams hands them over. Presentation lives
at the edge, in one `switch` in `RenderCore`, so result types stay free of Discord formatting and a
second consumer (log line, web view) reads the same data.

`AIFunctionFactory` JSON-serializes a tool's return value by default, which would hand the recorder
and the model a `JsonElement`. Registration therefore sets `MarshalResult` to identity so the typed
result reaches `TurnLimitedAIFunction`, which is the only place that decides what text the model
sees (it always returns `Text`, never the record). `ExcludeResultSchema` drops the object schema
inferred from the record, since the model-visible return is text.

### Delivery of the final answer degrades gracefully

The final edit can fail if the interaction token has expired. In that case the answer is sent as a
normal followup/reply instead, so the user always receives it.

### Transcript trigger and budget

The transcript is appended when the run exceeded `TranscriptTurns`, or when the caller passed
`verbose`. `TranscriptTurns` is unset by default and falls back to `MaxTurns` in
`AgentProgress.ShouldShowTranscript`, so the transcript appears exactly when the run spent its whole
budget — which is also when the footer's turn count invites the question — and a deployment can
lower it through `AiSettings.ChatAssistant` without a rebuild. Failed calls do not trigger it — a
command failing is ordinary (a missing binary, a non-matching `grep`) and a short run's history is
noise; the answer already says what went wrong, and a failed call inside a long transcript is
marked `(failed)`. The transcript is capped (last calls, ~1200 characters) so the answer together
with attachment notes stays inside 2000 characters; omitted calls are summarised as a count.

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

Config additions are optional with defaults (`MaxTurns = 20`, `TranscriptTurns` unset → the turn
limit), so existing deployments keep working. No database, schema, or protocol changes; rollback is
a code revert.
