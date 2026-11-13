# Spec Delta

## Purpose

Gives the assistant a durable, per-caller record of its own past runs — prompts, complete answers and
full tool output — stored where the sandbox can read it, so a later run can retrieve earlier work
instead of re-deriving it from a 2000-character chat excerpt.

## ADDED Requirements

### Requirement: Every run is archived

Each `/ai` run SHALL write exactly one transcript to the caller's session store before the run is
reported as finished, whatever its outcome (answered, turn budget exhausted, tool failure, or an
exception). A run that ends in an exception SHALL still be archived, marked with that outcome.

#### Scenario: Successful run
- **WHEN** a run produces an answer
- **THEN** one transcript exists for that run's session id and records outcome `ok`

#### Scenario: Turn budget exhausted
- **WHEN** a run stops because the turn budget is spent
- **THEN** its transcript records the turn-limit outcome and the refused calls

#### Scenario: Run throws
- **WHEN** the agent or the command handler throws during a run
- **THEN** a transcript is still written, recording the error outcome and the calls made so far

#### Scenario: Sandbox disabled
- **WHEN** the sandbox is disabled and therefore has no storage
- **THEN** no transcript is written and the run behaves exactly as it does today

### Requirement: Transcript contents are complete

A transcript SHALL contain the run's prompt, its complete answer, and every tool call with its full
arguments, full output and exit status — not the truncated previews used in chat. It SHALL also
record the run's warnings, the session id, caller, guild, channel, timestamps, duration and turn
count. Content that exceeds the per-session size cap SHALL be truncated with a visible marker rather
than dropped silently.

#### Scenario: Long answer
- **WHEN** a run's answer exceeds the channel message limit
- **THEN** its transcript contains the whole answer, not the excerpt sent to Discord

#### Scenario: Large command output
- **WHEN** a command produces output larger than the per-session cap allows
- **THEN** the transcript keeps the beginning and marks the truncation

### Requirement: The archive is read-only to the model

The transcript directory SHALL be mounted read-only in the sandbox and the model SHALL NOT be able
to edit, delete or replace stored transcripts or remove the archive directory. The surrounding
`.dotto` directory SHALL remain writable so the model can keep its own files beside the archive.

#### Scenario: Model tries to modify a transcript
- **WHEN** the model writes to or deletes a stored transcript
- **THEN** the operation fails with a permission error and the transcript is unchanged

#### Scenario: Model deletes the archive directory
- **WHEN** the model tries to remove the session store directory
- **THEN** the removal fails and the store remains usable

### Requirement: Archive size is bounded

After writing, the bot SHALL delete the oldest transcripts until the store is within its configured
byte budget, counting each transcript as its file size plus a fixed per-session floor so that many
tiny transcripts cannot accumulate. Pruning SHALL only ever consider transcripts it wrote, and SHALL
never delete the transcript just written.

#### Scenario: Budget exceeded by many sessions
- **WHEN** the store exceeds its budget after a write
- **THEN** the oldest transcripts are deleted until it is under budget, and the newest survives

#### Scenario: Budget exceeded by one large session
- **WHEN** one session alone exceeds the budget
- **THEN** it is kept, older sessions are removed, and the store may remain above budget until the
  next prune

#### Scenario: Unrelated files in the store directory
- **WHEN** the caller has placed other files in storage
- **THEN** pruning leaves them untouched

### Requirement: The model discovers the archive from its prompt

The system prompt SHALL describe the archive — its location, file naming, what a transcript
contains, and that old transcripts are pruned — so a run knows the archive exists without an index
file or a README inside the store. The sandbox SHALL provide a recursive search tool for it.

#### Scenario: Prompt names the archive
- **WHEN** the sandbox is enabled and a run starts
- **THEN** the system prompt states where transcripts live and that they are pruned over time

#### Scenario: Prompt omits the archive
- **WHEN** the sandbox is disabled
- **THEN** the system prompt does not advertise an archive the run cannot reach

#### Scenario: Searching the archive
- **WHEN** the model searches the store for a topic
- **THEN** a recursive text search is available in the sandbox

### Requirement: The footer identifies the session

The final message of a run SHALL include the run's session id, and that id SHALL be the one in the
transcript's filename, so a later run reading channel history can open precisely the right
transcript.

#### Scenario: Later run resolves a referenced session
- **WHEN** channel history shows a footer with a session id
- **THEN** a transcript with that id exists in the store unless it has been pruned

### Requirement: Archiving never breaks a run

Failures while writing, pruning or rendering a transcript SHALL be logged and SHALL NOT change,
delay beyond a small bound, or abort the run's answer.

#### Scenario: Storage unwritable
- **WHEN** the transcript cannot be written
- **THEN** the run still delivers its answer and the failure is logged

### Requirement: Archives are per caller

The store SHALL live inside the invoking caller's own persistent storage, and a run SHALL have no
access to another caller's transcripts.

#### Scenario: Different callers
- **WHEN** two callers each run `/ai`
- **THEN** each sandbox can read only the transcripts of the caller it was started for
