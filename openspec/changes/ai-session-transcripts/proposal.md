# Proposal

## Why

Everything an `/ai` run produces beyond its final text is destroyed when the run ends, and even that
text is destroyed in practice: the channel message is capped at 2000 characters, so a long answer is
replaced by an excerpt and the whole answer rides in an `answer.txt` attachment that the next run can
only see as `<attachment name="answer.txt" url="…">`. Tool calls are worse — the run knows every
command, its full stdout/stderr and its exit status, then keeps at most a 1200-character collapsed
transcript, and for exactly the runs that overflow (the long ones) not even that.

The next run therefore starts blind. It re-downloads, re-probes and re-derives what it already knew,
and continuity across sessions is limited to "what survived 2000 characters of chat history".

`/storage` already persists per caller across sessions, so the archive has a home that the model can
read with the tools it already has.

## What Changes

- **Per-run transcript archive**: when a run ends, the bot writes
  `/storage/.dotto/sessions/<SessionId>.md` — the prompt, the complete answer (not the Discord
  excerpt), every tool call with full arguments and full output, exit status, refused calls,
  warnings, and outcome. Written by the host through the storage bind mount, so it works whether or
  not the container ever started.
- **Read-only archive, writable parent**: `/storage/.dotto` is bind-mounted writable and
  `/storage/.dotto/sessions` read-only, so the model can create files beside the archive but cannot
  edit or delete transcripts. Being mount points, neither directory can be removed.
- **Bounded archive**: after each write the oldest sessions are deleted until the store is under a
  configurable budget (default 1 MiB), counting each session as `file size + 16 KiB` so that many
  tiny sessions cannot accumulate. A single session is capped on write so one runaway command cannot
  evict the store.
- **Discovery by convention, not index**: the system prompt describes the archive once (location,
  file naming, contents, pruning); no index file, no README inside the store. `ls -t` and `rg` are
  the query language, with `ripgrep` added to the sandbox image.
- **Session id in the run footer**: `-# <n> turns · <s>s · <n> file(s) · <SessionId>`, so a later
  run reading channel history can open the exact transcript instead of guessing at timestamps.
- **Full call arguments retained**: `ToolCall` keeps the complete argument text (the 120-character
  preview stays for activity lines and transcripts), which is the enabler for a faithful archive.
- **Non-goals**: a database table or admin UI for runs, cross-caller or cross-guild archives, an
  index/search endpoint, replaying a stored run as a live conversation, storing model reasoning or
  attachment bytes, making the archive mutable, and any Discord-side command to list or fetch a
  transcript (the model already holds `UploadFile`, so that need is covered).

## Capabilities

### New Capabilities

- `ai-session-transcripts`: what the bot archives from a completed or failed `/ai` run, where it
  lives, how long it survives, what the model may see of it, and how the model discovers it.

### Modified Capabilities

None. `openspec/specs/` is empty (in-flight changes have not been archived), so this extends the
`ai-sandbox` storage design rather than amending an archived requirement.

## Impact

- **Code**: `Dotto.Ai/Sandbox/Sandbox.cs` (nested binds, directory creation and modes),
  `Dotto.Ai/Settings/SandboxOptions.cs` (archive budget and per-session cap),
  `Dotto.Ai/Agents/Base/AgentProgress.cs` / `ToolCall` (full arguments, outcome),
  `Dotto.Ai/Tools/TurnLimitedAIFunction.cs` (pass full arguments),
  `Dotto.Ai/Agents/ChatAssistant/ChatAssistant.cs` and `ChatAssistantResponse` (session id,
  prompt context), `Dotto.Ai/Resources/ChatAssistant.liquid`, a new archive writer under
  `Dotto.Ai/Sandbox/`, `Dotto.Discord/CommandHandlers/Ai/AiCommandHandler.cs` (write on success and
  on failure), `Dotto.Discord/Services/AgentRunMessage.cs` (footer id).
- **Sandbox image**: `Dotto.Ai/sandbox-image/Dockerfile` gains `ripgrep`.
- **Configuration**: `Ai:Sandbox:SessionArchive*` keys in `Dotto.Bot/appsettings.json`; the archive
  counts against `StorageQuotaMb`.
- **Tests**: nested bind parameters and directory modes, archive rendering, retention/pruning,
  per-session cap, write-on-failure, footer id, prompt rendering, and that a failed archive write
  never fails the run.
- **No** database, migration, Discord API, downloader or ffmpeg changes.
