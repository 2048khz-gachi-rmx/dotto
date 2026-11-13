# Tasks

## 1. Full tool-call arguments

- [ ] 1.1 Add `Arguments` (complete argument text) to `ToolCall` and pass it from
      `TurnLimitedAIFunction` alongside the existing preview; verify with a unit test asserting a
      long argument keeps its full text in `Arguments` while `ArgumentPreview` stays capped.
- [ ] 1.2 Confirm nothing user-visible changed: run the existing `AgentProgressTests` and
      `AgentRunMessageTests` suites and verify activity lines and transcripts still render from
      previews only.

## 2. Archive storage layout

- [ ] 2.1 Add `SessionArchiveBytes` (default 1 MiB) and `SessionArchiveMaxFileBytes` (default
      256 KiB) to `SandboxOptions` with validation ranges, and set them in
      `Dotto.Bot/appsettings.json`; verify options validation fails on a non-positive value
      (`dotnet test --filter SandboxOptions` or the settings tests).
- [ ] 2.2 In `Sandbox.InitializeAsync`, create `.dotto` and `.dotto/sessions` under the caller's
      storage with explicit `0755` modes; verify with a test that both exist and are
      world-readable/executable after initialization.
- [ ] 2.3 In `Sandbox.EnsureStartedAsync`, add the two nested binds (`.dotto` rw,
      `.dotto/sessions` ro); verify with the existing container-parameter tests that both binds are
      present, in that order, and that the child carries `:ro`.
- [ ] 2.4 Update `Dotto.Ai/FEATURE.md` (Sandbox section) with the mount topology and the retention
      settings; verify by re-reading the section against `Sandbox.cs`.

## 3. Transcript writer and retention

- [ ] 3.1 Add an archive writer (`Dotto.Ai/Sandbox/SessionArchive.cs` or similar) that renders a
      run as markdown — header block, prompt, full answer, every tool call with arguments and
      output, warnings, refusals, outcome — verified by a unit test comparing rendered output for a
      run with a long answer and a failing command.
- [ ] 3.2 Enforce `SessionArchiveMaxFileBytes` per call output with a visible truncation marker;
      verify with a test that an oversized stdout keeps its head, is marked truncated, and the file
      stays under the cap.
- [ ] 3.3 Write atomically (`temp` + `File.Move`) and serialize write+prune per caller behind a
      semaphore; verify with a test that a second write does not observe a partial file and that
      concurrent writers for one caller do not interleave.
- [ ] 3.4 Implement pruning: enumerate direct children of `sessions/` only, count `size + 16 KiB`
      each, delete oldest by name until under `SessionArchiveBytes`, never the fresh file and never
      anything outside `sessions/`; verify with tests for the many-small-sessions case, the
      single-oversized-session case, and that a stray user file survives.
- [ ] 3.5 Verify a failing archive write cannot fail a run: test that an unwritable storage directory
      is logged and the run's answer is still returned unchanged.

## 4. Run integration

- [ ] 4.1 Add `SessionId` to `ChatAssistantResponse` (populated from `ChatAssistantContext`); verify
      with the existing chat-assistant tests that the returned id matches the id logged at start.
- [ ] 4.2 Write the transcript from `AiCommandHandler.Invoke` after the sandbox is disposed, with
      `CancellationToken.None` and a bounded timeout, on both success and exception paths, recording
      the outcome; verify with integration tests for an answered run and for a run whose agent
      throws (a transcript exists in both, with the right outcome).
- [ ] 4.3 Skip archiving when `Ai:Sandbox:Enabled` is false; verify with a test that no directory is
      created and the run still returns its answer.
- [ ] 4.4 Append the (abbreviated) session id to the run footer in `AgentRunMessage.Compose` and
      pass it from both the `/ai` slash command and the `ai` text command; verify with
      `AgentRunMessageTests` that the footer contains the id, that the id is not duplicated when the
      message overflows into `answer.txt`, and that the excerpt path still fits 2000 characters.

## 5. Model-facing discovery

- [ ] 5.1 Add the archive paragraph to `Dotto.Ai/Resources/ChatAssistant.liquid` inside the
      `{% if ToolsEnabled %}` block: location, filename convention, complete contents, pruning, and
      when to read it; verify with the prompt tests that it renders when tools are enabled and is
      absent when they are disabled.
- [ ] 5.2 Add `ripgrep` to `Dotto.Ai/sandbox-image/Dockerfile` and mention it in the prompt's tool
      list and `sandbox-image/README.md`; verify by building the image and running `rg --version`
      inside it.
- [ ] 5.3 Verify the read-only guarantee end to end: start a sandbox, attempt to append to a
      transcript and to `rm -rf` the store, and confirm both fail while a new file in `.dotto`
      succeeds (also confirms nested binds work under `runsc`).

## 6. Integration verification

- [ ] 6.1 Run a real `/ai` session that overflows the Discord message limit and one that exhausts
      the turn budget; verify each transcript holds the full answer/output, the outputs render
      correctly when paged with `sed`, and the footer id resolves to the file.
- [ ] 6.2 Fill the store past its budget in a scratch caller directory and verify pruning keeps the
      newest transcripts, respects the 16 KiB floor, and leaves the user's own files alone.
- [ ] 6.3 Start a follow-up run and verify the model can locate and read a prior transcript by
      searching the store (behavioral check with the prompt as written).

## Workflow follow-up

- Archive the change once review is complete.
- Prompt the user to record the feature in `Dotto.Ai/FEATURE.md` if the mechanics change during
  implementation.
