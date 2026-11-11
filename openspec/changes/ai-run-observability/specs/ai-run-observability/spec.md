# Spec Delta

## Purpose

What a caller can see about an `/ai` run: live progress while the agent is working, the turn
budget, a post-run transcript of what the agent actually did, and a summary footer on the answer.

## ADDED Requirements

### Requirement: Live Run Activity

While an agent run executes tool calls, the bot SHALL update the run's message in place with the
most recent tool calls, so a long run looks different from a slow answer. Edits SHALL be coalesced
to roughly one per second per run. A run that makes no tool calls SHALL NOT produce an activity
update.

#### Scenario: Activity appears during a run

- **WHEN** a run executes its first tool call
- **THEN** the run's message is updated with a line naming that tool call

#### Scenario: Recent calls replace older ones

- **WHEN** a run has executed more than five tool calls
- **THEN** the activity shows only the five most recent calls, not the full history

#### Scenario: Edits are coalesced

- **WHEN** five tool calls complete within one second
- **THEN** the bot sends at most one message edit for that second

#### Scenario: No tool calls, no activity

- **WHEN** a run answers without calling any tool
- **THEN** no intermediate activity update is shown before the answer

### Requirement: Tool Call Preview

Each recorded tool call SHALL carry the tool name, an argument preview and the tool's result
object as returned. Results SHALL be typed: a plain text result carries only its model-facing
`Text`; a sandbox command result also carries `ExitCode`, `Stdout` and `Stderr`, and reports
failure when the exit status is non-zero. Text and previews SHALL be collapsed to single-line text
and truncated with a truncation marker when cut: arguments to 120 characters, results to 180.

#### Scenario: Long command is truncated

- **WHEN** a tool is called with an argument longer than 120 characters
- **THEN** the recorded preview contains at most 120 characters plus a truncation marker

#### Scenario: A command's status and streams are recorded

- **WHEN** a sandbox command exits non-zero after writing to stderr
- **THEN** the recorded result carries the exit status and both streams, and reports failure

#### Scenario: The rendered stream matches the outcome

- **WHEN** a command wrote to both streams
- **THEN** a successful command shows stdout and a failed one shows stderr, falling back to the
  other stream when the preferred one is empty

#### Scenario: A text result carries no status

- **WHEN** a tool that is not a sandbox command completes
- **THEN** its recorded result is text-only and NOT marked failed, whatever the text says

#### Scenario: Silent success is not padded

- **WHEN** a shell command exits zero without output
- **THEN** the rendered call has no result part

#### Scenario: Multiline output is collapsed

- **WHEN** a tool returns output containing newlines
- **THEN** the recorded result preview is a single line

#### Scenario: Failed call is marked

- **WHEN** a tool call fails (non-zero exit status, or a tool-reported failure)
- **THEN** the recorded call is marked as failed

### Requirement: Activity Line Format

A rendered call SHALL be `-# <Tool>: `<args>` **#>** [<exit>] `<output>``. The bracketed exit status
SHALL appear only when it was non-zero; both previews SHALL be inline code; a backtick inside either
preview SHALL be replaced so the span stays intact.

#### Scenario: Successful call omits the exit status

- **WHEN** a command exits zero after printing output
- **THEN** the line names the tool, the command and the output, with no bracketed status

#### Scenario: Failed call names the exit status

- **WHEN** a command exits non-zero
- **THEN** the line carries `[<n>]` between `**#>**` and the output

#### Scenario: Backticks in the content do not break the formatting

- **WHEN** a command or its output contains a backtick
- **THEN** the backtick is replaced before the preview is wrapped in a code span, so the span stays
  intact

### Requirement: Turn Budget

A run SHALL be limited to a configurable number of tool calls (default 20). Once the limit is
reached, further tool calls requested by the model SHALL NOT be executed, and the model SHALL
receive a result explaining that the limit was reached and that it must answer with the
information it has.

#### Scenario: Call beyond the limit is refused

- **WHEN** the model requests a tool call after the turn limit has been reached
- **THEN** the tool does not execute and the model receives a turn-limit notice

#### Scenario: Configured limit is honoured

- **WHEN** the turn limit is configured to a value other than the default
- **THEN** the run allows exactly that many tool calls before refusing further ones

### Requirement: Turn Limit Warnings

The run SHALL warn the model before the limit is reached: a warning at five calls before the
limit saying it is approaching, and a warning on the final permitted call saying it is the last
turn and it must respond. Warnings SHALL be delivered with the tool result so the model sees them
in its own turn.

#### Scenario: Approaching warning

- **WHEN** the tool call numbered `limit - 5` completes
- **THEN** its result carries a warning that the limit is approaching

#### Scenario: Last-turn warning

- **WHEN** the tool call numbered `limit - 1` completes
- **THEN** its result carries a warning that it is the last turn and the model must respond

#### Scenario: No warning before the threshold

- **WHEN** a tool call completes well before the warning thresholds
- **THEN** its result carries no turn-limit warning

### Requirement: Turn Limit Disclosure

The agent's system prompt SHALL state that the number of tool calls is limited, and that the
model will be warned when approaching the limit and on its last turn.

#### Scenario: Prompt mentions the limit

- **WHEN** the system prompt for a chat assistant run is rendered
- **THEN** it contains a statement that the run has a limited number of turns with advance warnings

### Requirement: Run Transcript

The final answer SHALL include the recorded tool calls when the run exceeded the configured
transcript threshold, or when the caller forces it. The threshold is unset by default, meaning the
turn limit, and SHALL be overridable through configuration. Failed tool calls alone SHALL NOT
trigger the transcript.

#### Scenario: Short successful run omits the transcript

- **WHEN** a run completes successfully in fewer turns than the threshold and no transcript was
  requested
- **THEN** the final answer contains no tool transcript

#### Scenario: Long run includes the transcript

- **WHEN** a run exceeds the transcript turn threshold (the turn limit, unless configured)
- **THEN** the final answer lists the recorded tool calls

#### Scenario: A failed call alone does not include the transcript

- **WHEN** a short run has one or more failed tool calls but stayed under the turn threshold
- **THEN** the final answer contains no tool transcript

#### Scenario: Caller forces the transcript

- **WHEN** the caller requests verbose output
- **THEN** the final answer lists the recorded tool calls regardless of turn count

#### Scenario: Transcript respects the message limit

- **WHEN** the recorded calls would push the final message over Discord's message limit
- **THEN** the transcript is trimmed, older calls are summarised as a count, and the message
  remains within the limit

### Requirement: Overflow Answer Attachment

When the composed final message would exceed Discord's 2000-character limit, the bot SHALL attach
the complete answer text (including the transcript and attachment links) as a text file named
`answer.txt`, and keep the prompt line, an excerpt of the answer, a note naming the attachment and
the footer in the message. Content SHALL NOT be silently truncated: whatever the message cannot
show is present in the file.

#### Scenario: Long answer is attached, not trimmed

- **WHEN** the answer alone exceeds Discord's message limit
- **THEN** the message carries the prompt line, an excerpt, the attachment note and the footer, and
  `answer.txt` contains the full answer

#### Scenario: Transcript survives the overflow

- **WHEN** an overflowing answer also has a transcript to show
- **THEN** the transcript is included in `answer.txt` rather than dropped

#### Scenario: Short answers get no attachment

- **WHEN** the composed message fits within the limit
- **THEN** no `answer.txt` is attached

### Requirement: Run Footer

Every completed run's final answer SHALL end with a summary line stating the number of tool
calls, the elapsed run time and the number of files delivered.

#### Scenario: Footer reports the run

- **WHEN** a run finishes and its answer is sent
- **THEN** the answer ends with a line reporting turn count, duration and delivered file count
