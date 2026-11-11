# Proposal

## Why

An `/ai` run is currently opaque: the bot defers the interaction and says nothing until the final
answer, so a 20-turn tool loop looks identical to a two-second reply. A real run asked for
`whois` (not installed in the sandbox), retried variations of a missing binary for dozens of
turns, and surfaced none of it — the user only saw a long silence.

## What Changes

- **Live run activity**: while tools execute, the interaction response is edited in place with the
  most recent tool calls (`ExecuteCommand: whois -h whois.radb.net … // result: Exit code: 127
  sh: 1: whois: not found`), coalesced to stay under Discord's edit rate limit.
- **Turn budget**: runs are capped at `MaxTurns` (default 20, configurable). The system prompt
  states that a limit exists, a warning is prepended to the tool result at `MaxTurns - 5`
  ("approaching the limit") and at `MaxTurns - 1` ("last turn — respond now"), and tool calls
  beyond the cap are refused instead of executed.
- **Bounded previews**: each tool call records a 120-character argument preview and up to 180
  characters of its result (whitespace collapsed), so activity lines and transcripts stay one line
  per call. A command's result shows stdout when it succeeded and stderr when it failed, falling
  back to the other stream when the preferred one is empty.
- **Run transcript**: the recorded tool calls are appended to the final answer when the run
  exceeded `TranscriptTurns` (unset by default, meaning the turn limit); `/ai` gains a `verbose`
  option that always includes it.
  Failed calls are marked in the transcript but do not trigger it.
- **Run footer**: the final answer ends with `-# <n> turns · <duration> · <n> file(s)`.
- **Non-goals**: a cancel/stop button, token-by-token streaming of assistant text, server-side
  telemetry changes, and adding shell utilities (e.g. `whois`) to the sandbox image. The motivating
  failure is fixed by making tool failures visible, not by shipping more binaries.

## Capabilities

### New Capabilities

- `ai-run-observability`: what a user can see about an in-flight and completed `/ai` run — live
  activity, the turn budget and its warnings, per-call previews, the transcript and the summary
  footer.

### Modified Capabilities

None.

## Impact

- `Dotto.Ai`: new per-invocation progress record, a tool-function decorator that counts turns and
  applies the limit, `AgentContext`/`ContextAccessor` plumbing, the chat prompt template, and
  `AiSettings.ChatAssistant` options (`MaxTurns`, `TranscriptTurns`).
- `Dotto.Discord`: the `/ai` slash command and the `ai` text command render the activity line and
  the final message (content, transcript, footer, attachments); `AiCommandHandler.Invoke` accepts
  the progress record.
- Tests: unit tests for preview/formatting, warning thresholds, limit enforcement, transcript
  trigger and footer.
- No changes to the sandbox image, the sandbox HTTP API, the database, or the downloader.
