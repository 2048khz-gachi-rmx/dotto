## Purpose

Provides an isolated, token-authenticated HTTP API for executing terminal commands (ffmpeg, curl, jq, python3 and a standard Debian userland) and moving files in and out of a per-session Docker container, with configurable timeout tiers and bounded output.

## ADDED Requirements

### Requirement: Sandbox HTTP API
The sandbox container SHALL expose an HTTP API with the following endpoints:
- `GET /ping` — returns `{"status":"ok"}` for health checks
- `POST /execute` — accepts a JSON body and executes a command
- `GET /file?path=...` — streams a file out of the sandbox
- `PUT /file?path=...` — streams a file into the sandbox

Every request SHALL carry the per-container token in the `X-Sandbox-Token` header. Requests with a missing or incorrect token SHALL be rejected with HTTP 401. If no token is configured in the container, every request SHALL be rejected with HTTP 503.

#### Scenario: Health check
- **WHEN** the bot sends `GET /ping` with a valid token
- **THEN** the sandbox responds with HTTP 200 and body `{"status":"ok"}`

#### Scenario: Unauthenticated request rejected
- **WHEN** a request omits `X-Sandbox-Token` or supplies the wrong value
- **THEN** the sandbox responds with HTTP 401 and does not execute anything
- **AND** the connection is closed rather than kept alive, because the unread request body (a `PUT /file` may carry hundreds of megabytes) would otherwise be parsed as the next request

#### Scenario: Execute simple command
- **WHEN** the bot sends `POST /execute` with `{"command": "echo hello", "cwd": "/work"}`
- **THEN** the sandbox responds with HTTP 200 and body containing `{"exitCode": 0, "stdout": "hello\n", "stderr": ""}`

### Requirement: Command Execution Model
The `POST /execute` endpoint SHALL accept a single `command` string that is executed by a shell, so it supports full shell syntax (pipes, redirection, `&&`, globs). The endpoint does NOT take a separate args array. Commands SHALL run in their own process group, and the whole group SHALL be killed on timeout or quota breach.

#### Scenario: Shell syntax is supported
- **WHEN** the bot sends `POST /execute` with `{"command": "jq --version | cut -d' ' -f1", "cwd": "/work"}`
- **THEN** the sandbox executes the piped command and returns its output

#### Scenario: Child processes do not outlive a timeout
- **WHEN** a command spawns children and exceeds its timeout
- **THEN** the entire process group is terminated

### Requirement: Timeout Tiers
The `POST /execute` endpoint SHALL enforce a hard timeout on every command based on the `timeout` field:
- `"short"` — 5 second hard timeout
- `"long"` — 300 second hard timeout
- unset or any other value — 20 second hard timeout

A command that runs past its tier's hard timeout SHALL be terminated and reported with `exitCode: -1` and a stderr note indicating the timeout.

#### Scenario: Short tier terminates long commands
- **WHEN** a command runs longer than the 5 second `"short"` timeout
- **THEN** the sandbox terminates it and responds with `exitCode: -1` and stderr indicating timeout

#### Scenario: Long tier allows long-running commands
- **WHEN** a command finishes within the 300 second `"long"` timeout
- **THEN** the sandbox responds with `exitCode: 0` after the command completes

### Requirement: Bounded Command Output
The `POST /execute` response SHALL cap combined stdout and stderr at 10,000 characters, treated as one shared budget rather than a per-stream limit. When the output exceeds the cap, the server SHALL keep the tail and include a truncation marker. stderr SHALL keep at least half the budget (diagnostics matter more than chatter) and stdout SHALL receive whatever stderr does not need, so output that fits in the budget is never truncated merely because the other stream was non-empty.

#### Scenario: Chatty command is truncated
- **WHEN** a command emits more than 10,000 characters
- **THEN** the response contains at most 10,000 characters and a marker stating how much was truncated

#### Scenario: Small stream does not truncate the large one
- **WHEN** a command emits 9,000 characters of stdout and 200 characters of stderr
- **THEN** both streams are returned in full (9,200 characters, under the cap)

#### Scenario: One chatty stream does not starve the other
- **WHEN** a command emits 20,000 characters of stdout and 100 characters of stderr
- **THEN** stderr is returned in full and stdout keeps the remainder of the 10,000-character budget

### Requirement: Bounded Output Memory
The server SHALL read both pipes incrementally as bytes, keeping a bounded tail (40,000 bytes per stream — four bytes per character of the 10,000-character cap, enough for all-UTF-8-maximal output) and counting dropped bytes, so a child that emits gigabytes cannot grow the server's memory. Decoding SHALL happen once, after the drain, with invalid UTF-8 replaced rather than raising an error.

#### Scenario: Runaway output does not exhaust memory
- **WHEN** a command emits gigabytes of output
- **THEN** the sandbox drains the pipes, keeps only the bounded tail, and responds with a bounded body

### Requirement: Bounded Drain
A detached background child that inherits the pipes SHALL NOT hang the request: the response SHALL be produced after at most 5 seconds of draining, and the pipe readers SHALL be stopped afterwards so threads and file descriptors do not outlive the request. Process count is bounded by the container's pids limit; threads are not, which is why the readers must be stopped rather than left blocking on a pipe a grandchild still holds.

#### Scenario: Background child does not hang the session
- **WHEN** a command starts a child that keeps running after the shell exits (e.g. `sleep 30 &`)
- **THEN** the response is returned within the drain bound and the reader threads are stopped

#### Scenario: Binary output is sanitized
- **WHEN** a command writes invalid UTF-8 to stdout (e.g. `cat /bin/ls`)
- **THEN** the response succeeds with replacement characters instead of failing the request

### Requirement: Bounded Response Size
There SHALL be a single output cap: 10,000 characters. The bot's buffered-response limit SHALL be large enough for the cap's worst-case JSON encoding (12 bytes per character, because astral characters serialize as surrogate-pair escapes), so the character cap binds for every kind of output and no response-side truncation changes the limit.

#### Scenario: Worst-case escaping stays under the client buffer
- **WHEN** a command emits text that JSON-escapes to the maximum size (astral characters)
- **THEN** the response carries at most 10,000 characters and still fits the bot's buffer

### Requirement: Working Directory
The server SHALL default the working directory to `/work` when no `cwd` is specified.
Relative `cwd` values SHALL be resolved against `/work`. Absolute paths SHALL be used as-is.
No explicit path traversal check is needed for execution — the container's read-only rootfs and tmpfs mounts prevent commands from modifying anything outside the session's writable areas.

A `cwd` that does not exist, and a command that cannot be executed, SHALL be reported as `exitCode: -1` with the OS message (no traceback) in stderr, so the model can correct the command instead of guessing at an opaque failure.

#### Scenario: Missing working directory is named
- **WHEN** the bot sends `POST /execute` with `{"command": "ls", "cwd": "/work/nope"}`
- **THEN** the sandbox responds with `exitCode: -1` and stderr naming the missing directory

#### Scenario: Default working directory
- **WHEN** the bot sends `POST /execute` with `{"command": "pwd"}`
- **THEN** the sandbox responds with stdout containing `/work\n`

#### Scenario: Relative cwd resolved
- **WHEN** the bot sends `POST /execute` with `{"command": "pwd", "cwd": "downloads"}`
- **THEN** the sandbox responds with stdout containing `/work/downloads\n`

#### Scenario: Absolute cwd accepted
- **WHEN** the bot sends `POST /execute` with `{"command": "ls", "cwd": "/work"}`
- **THEN** the sandbox executes the command successfully

### Requirement: File Transfer
The file endpoints SHALL confine every path to `/work` or `/storage`. Paths SHALL be resolved with `realpath` so symlinks cannot escape those roots; writes SHALL resolve the parent directory. Rejected or malformed paths SHALL return HTTP 400, and missing files SHALL return HTTP 404.

`PUT /file` SHALL require an explicit `Content-Length` (HTTP 411 otherwise) because the server does not accept chunked bodies. When `dedupe=1` is supplied, a colliding filename SHALL receive a numeric suffix (e.g. `file_1.mp4`) and the effective path SHALL be returned as `{"path": ...}`. Bytes SHALL be streamed in 64 KiB chunks.

#### Scenario: Stream a file in
- **WHEN** the bot sends `PUT /file?path=/work/downloads/video.mp4&dedupe=1` with a body and explicit length
- **THEN** the sandbox writes the file and returns its effective path

#### Scenario: Stream a file out
- **WHEN** the bot sends `GET /file?path=/work/outputs/result.mp4`
- **THEN** the sandbox responds with the raw bytes and a `Content-Length`

#### Scenario: Path outside the allowed roots rejected
- **WHEN** a path resolves outside `/work` and `/storage` (including via a symlink)
- **THEN** the sandbox responds with HTTP 400

### Requirement: Storage Quota
`/storage` SHALL be subject to `SANDBOX_STORAGE_QUOTA_MB`. Usage SHALL be computed file by file, charging each file `max(256 KiB, size)` so that a very large number of tiny files cannot evade the quota while still costing inodes and making the (O(files)) usage walk expensive. A `PUT /file` that would exceed the quota SHALL be rejected with HTTP 507, charging the incoming file the same way. During command execution, a watchdog SHALL kill the process group if `/storage` usage exceeds the quota and has increased during the command; commands that reduce usage SHALL be allowed to finish.

The quota is best-effort: it is enforced by polling, not by the filesystem, so a command can briefly overshoot between checks and concurrent writers can race the pre-write check.

#### Scenario: Write over quota rejected
- **WHEN** a write to `/storage` would push usage above the quota
- **THEN** the sandbox responds with HTTP 507 and does not keep the file

#### Scenario: Command filling storage is killed
- **WHEN** a running command pushes `/storage` usage above the quota
- **THEN** the watchdog kills the process group, reports `exitCode: -1` (a server-side kill, not the command's own status), and names the quota breach in stderr

### Requirement: Available Binaries
The sandbox SHALL provide the binaries an assistant expects from a small Linux userland, not only media tools: ffmpeg, ffprobe, curl, wget, jq, python3, git, openssl, common file, process, archive, time and network-diagnostic utilities. Compilers and heavy media suites SHALL NOT be installed.

#### Scenario: ffmpeg is available
- **WHEN** the bot sends `POST /execute` with `{"command": "ffmpeg -version"}`
- **THEN** the sandbox responds with `exitCode: 0` and stdout containing "ffmpeg version"

#### Scenario: jq is available
- **WHEN** the bot sends `POST /execute` with `{"command": "jq --version"}`
- **THEN** the sandbox responds with `exitCode: 0` and stdout containing a version string

#### Scenario: basic userland tools are available
- **WHEN** the bot sends `POST /execute` with `{"command": "dig -v; git --version; file --version; ps --version"}`
- **THEN** the sandbox responds with `exitCode: 0`, so an assistant reaching for DNS, git, `file` or `ps` is not failed by a missing binary

#### Scenario: no compilers
- **WHEN** the bot sends `POST /execute` with `{"command": "command -v gcc make"}` with a non-zero status expected
- **THEN** neither is found, keeping the image small and the sandbox free of a build toolchain

### Requirement: Python charting libraries
The sandbox SHALL have matplotlib, numpy and Pillow installed at pinned versions, plus DejaVu fonts on disk, so the assistant renders charts and annotates images instead of hand-rolling an encoder. Rendering SHALL work with the container's read-only root filesystem and a writable `/work`.

#### Scenario: matplotlib renders a chart
- **WHEN** the bot sends `POST /execute` running a script that plots with the Agg backend and saves a PNG under `/work`
- **THEN** the command exits 0 and the PNG exists with non-trivial size

#### Scenario: Pillow can draw text with a system font
- **WHEN** the bot draws text with `ImageFont.truetype` on the installed DejaVu font
- **THEN** the image is written without error, so no font hunt is needed

### Requirement: Minimal dependencies
The sandbox image SHALL install ffmpeg and ffprobe from a static release tarball, the shell utilities via apt (no third-party repositories), and only the charting trio via pip. No other pip packages SHALL be installed, and the HTTP server SHALL use only the Python 3 standard library.

#### Scenario: Verify no extra pip packages
- **WHEN** `pip list` runs in the container
- **THEN** it shows only matplotlib, numpy, Pillow and their direct dependencies, and the server still starts with only stdlib modules imported
