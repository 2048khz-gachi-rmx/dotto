# Design

## Context

See `proposal.md` — Why. The implementation constraints that shape this design:

- `/storage` is already per-caller and persistent: `Sandbox.InitializeAsync` creates
  `<StorageBasePath>/<callerId>` and chmods it `0777` (`Dotto.Ai/Sandbox/Sandbox.cs:88-101`), and
  `EnsureStartedAsync` bind-mounts it as `/storage` plus `/run/dotto` and the resolver files
  (`Sandbox.cs:131-146`). Storage is provisioned eagerly; the container starts lazily
  (`Dotto.Ai/Agents/ChatAssistant/ChatAssistant.cs:43-47`).
- The run's full state is only in memory and only at the end: `AgentProgress` holds `ToolCall`s with
  the tool's own result type (`TextToolResult`, `CommandToolResult` with `Stdout`/`Stderr`), but
  `ToolCall.ArgumentPreview` is capped at 120 characters (`Dotto.Ai/Tools/TurnLimitedAIFunction.cs:74`).
- `ChatAssistant.GenerateSessionId` already produces an ordered, unique id
  (`{callerId}_{yyyyMMdd_HHmmss}_{sequence}`), and the answer text handed back is the full answer
  before `AgentRunMessage` composes the 2000-character message.
- `Ai:Sandbox` is startup-bound on purpose (`Dotto.Ai/Settings/SandboxOptions.cs`); `Ai:ChatAssistant`
  is per-scope overridable.
- The sandbox mount list is built once per container; a bind source must exist before start, and
  nested binds are applied by destination depth, so a child mount can narrow a parent's mode.

## Goals / Non-Goals

**Goals:**

- A transcript that is faithful enough to replace re-deriving prior work: exact prompt, exact
  answer, exact arguments, exact output.
- Zero new failure modes for the run: archiving is best-effort and off the answer's critical path in
  the sense that it can never fail or cancel it.
- The store is not a trust problem: the model reads it, never writes it.

**Non-Goals:**

- Reconstructing history from the archive at invocation time (that stays the channel's job).
- Any schema, table, API or UI for runs.
- Sharing archives between callers, or between a guild's members.
- Guaranteeing that a transcript survives: retention is explicitly lossy and the prompt says so.

## Decisions

### Store layout and mount topology

```
<StorageBasePath>/<callerId>/.dotto/            → /storage/.dotto            (rw)
<StorageBasePath>/<callerId>/.dotto/sessions/   → /storage/.dotto/sessions   (ro)
```

Two binds added in `EnsureStartedAsync`, both directories pre-created in `InitializeAsync` with
explicit modes (`0755` dirs, `0644` files) rather than relying on the bot's umask — the sandbox runs
as a non-root user and would otherwise hit `EACCES` on a `0600` root-owned file with no useful error.

Why the split: `.dotto` writable lets the model keep notes next to the archive; `sessions` read-only
means transcripts cannot be rewritten, and because both are mount points, `rmdir`/`rm -rf` on either
fails `EBUSY`, so the archive cannot be deleted wholesale either.

Alternatives: single read-only `.dotto` (simplest and equally safe — viable if the model never needs
to write there; rejected only because it forecloses per-caller notes for no gain); a read-only bind
per transcript (impossible — the source file does not exist when the container starts).

### Host-side write, not the sandbox API

The transcript is written by the bot through `Sandbox.Metadata.HostStorageDir`. The sandbox file API
was rejected: it requires a started container, so exactly the runs that never started (tool failure,
disabled sandbox, start error) would produce no archive, and it would add a network round trip to
the shutdown path. A host write works with or without a container and costs a local `write` plus
`rename`.

### Write point and failure semantics

`AiCommandHandler.Invoke` owns the run's end: it has the context, the progress record and the
response, and it is the only place that observes both success and exception. The write goes in a
`finally`-adjacent path after `sandbox.DisposeAsync()`, with `CancellationToken.None` and a small
internal timeout, and every exception is logged and swallowed. `ChatAssistantResponse` gains
`SessionId` so the id used for the filename is the one the footer shows.

Alternatives: write in `ChatAssistant.Invoke` (misses handler-level failures such as attachment
materialization and runs cancelled before the agent returns); write in the command module after the
message is sent (only benefit is the Discord message id, which channel history already carries).

### File format: markdown, one file per run

Named `<SessionId>.md`. Header block (session id, caller, guild, channel, started, duration, turns,
outcome, file count), then `## prompt`, `## answer`, `## tool calls`, one `### <tool>` per call with
arguments and output in fenced blocks, then warnings and refusals.

Chosen because the reader is a model paging with `sed`/`grep`, not a program: fenced blocks keep
output boundaries unambiguous when it contains its own markdown, and a truncated transcript can be
read by line range. pi's JSONL layout was rejected for this reason — it is a machine format, and one
long line per entry defeats line-range reads.

### Retention: byte budget with a per-session floor and cap

Default budget 1 MiB, each transcript counted as `size + 16 KiB`, so the budget also bounds *count*
(~64 sessions) and the store cannot become thousands of one-line files. After each successful write:
enumerate direct children only, sort by filename (the id's timestamp prefix is ordered), delete
oldest until `sum ≤ budget`, never touching the file just written or any path outside `sessions/`.
A single transcript is capped on write (default 256 KiB per call output, with a truncation marker)
so one chatty command cannot evict the rest of the store.

Alternatives: prune by age (unpredictable size), prune by count only (unbounded bytes), prune in a
background job (a leased service to solve a once-per-run 1 MiB scan).

Write and prune share a per-caller semaphore — the same pattern as `Sandbox.CallerGates`
(`Sandbox.cs:42`) but scoped to the archive writer — so two concurrent runs from the same caller
cannot prune each other's fresh file or interleave a write. Writes are `temp file → File.Move` so a
concurrent reader never sees a partial transcript.

### Full arguments on `ToolCall`

`ToolCall` gains `Arguments` (complete text of the argument bag) next to `ArgumentPreview`;
`TurnLimitedAIFunction` serializes once and passes both. The preview stays the sole input to activity
lines, transcripts and tests, so nothing user-visible changes.

### Session id in the footer

`AgentRunMessage.Compose` takes the session id and appends it to the footer
(`-# 6 turns · 41s · 2 file(s) · 4f2a…`). Long ids are abbreviated to a suffix in the footer while
the filename keeps the full id; the suffix is enough to match a file by `ls | grep`. Rejected: a
separate footer line (costs a line in every answer for one token of information).

### Prompt: one paragraph, no index

`ChatAssistant.liquid` gains a paragraph inside the `{% if ToolsEnabled %}` block naming
`/storage/.dotto/sessions`, the naming scheme, that transcripts hold the complete answer and full
tool output, and that old ones are pruned. An index injected through `Notes` was rejected: it would
be recomputed per invocation, add prompt tokens to every run, and duplicate what `ls -t` already
answers for a store of this size.

### Configuration placement

Retention keys go under `Ai:Sandbox` (`SessionArchiveBytes` = 1 MiB, `SessionArchiveMaxFileBytes` =
256 KiB), next to `StorageQuotaMb`, because the store is instance-wide storage; putting them under
`Ai:ChatAssistant` would imply per-guild retention, which would let one guild's budget evict
another's view of the same directory.

### Search tooling

`sandbox-image/Dockerfile` gains `ripgrep`, and the prompt names `rg` as the way to search the store.
Without it the model falls back to `grep -r`, which is slower and noisier on the archive's fenced
binary-ish output.

## Risks / Trade-offs

- [The archive becomes a persistent prompt-injection channel if the model can write it] → `sessions`
  is read-only and a mount point; the writer only ever writes host-composed filenames; prune never
  recurses and never follows a path derived from directory listings.
- [A single huge transcript evicts everything] → per-session cap with a truncation marker, plus the
  16 KiB floor so one session costs at most one slot.
- [Transcripts hold secrets that appeared in tool output] → same exposure boundary as the tool output
  already had (the caller's own storage, read only by that caller's sandbox), plus a prompt rule not
  to recite prior sessions unprompted.
- [Turn wasted on archaeology] → the prompt scopes reading to continuity ("when the user refers to
  earlier work"), and the existing turn budget backstops it; worst case a full 1 MiB read is ~250k
  tokens, which is why the prompt also says to search rather than read.
- [A footer id points at a pruned transcript] → the prompt states that pruning happens; a missing
  file is defined as eviction, not corruption.
- [Docker nested binds or gVisor handle the read-only child differently than expected] → verify with
  a smoke test that writes to `sessions` fail and writes to `.dotto` succeed under `runsc`; the
  fallback is a single read-only `.dotto`.
- [Write latency in the shutdown path] → ~1 ms for ≤256 KiB; bounded timeout and log-only failures
  keep it off the answer's critical path.
- [Windows/dev hosts] → `StorageBasePath` already falls back to `%TEMP%`; the mode setting stays
  behind the existing `OperatingSystem.IsWindows()` guard.

## Migration Plan

No data migration: directories are created on first use, existing `/storage` contents are untouched,
and the store starts empty. Deploy order is image (ripgrep) → bot; a bot on the new image with no
archive directory simply creates one. Rollback is removing the two binds and the prompt paragraph;
leftover transcripts are inert and eventually pruned by a later version, or deleted by hand.

## Rejected

- Discord-side listing or fetching of transcripts: useless surface area. The model can read the
  store and already holds `UploadFile`, so "show me what you did last time" resolves through the
  existing tool instead of a new command.
