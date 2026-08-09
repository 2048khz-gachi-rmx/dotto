## Purpose

Provides an isolated HTTP API for executing terminal commands (ffmpeg, curl, jq) inside a per-session Docker container, with configurable timeout tiers.

## ADDED Requirements

### Requirement: Sandbox HTTP API
The sandbox container SHALL expose an HTTP API with the following endpoints:
- `GET /ping` — returns `{"status":"ok"}` for health checks
- `POST /execute` — accepts a JSON body and executes a command

#### Scenario: Health check
- **WHEN** the bot sends `GET /ping` to the sandbox container
- **THEN** the sandbox responds with HTTP 200 and body `{"status":"ok"}`

#### Scenario: Execute simple command
- **WHEN** the bot sends `POST /execute` with `{"command": "echo hello", "cwd": "/work"}`
- **THEN** the sandbox responds with HTTP 200 and body containing `{"exitCode": 0, "stdout": "hello\n", "stderr": ""}`

### Requirement: Command Execution Model
The `POST /execute` endpoint SHALL accept a single `command` string that is executed by a shell, so it supports full shell syntax (pipes, redirection, `&&`, globs). The endpoint does NOT take a separate args array.

#### Scenario: Shell syntax is supported
- **WHEN** the bot sends `POST /execute` with `{"command": "jq --version | cut -d' ' -f1", "cwd": "/work"}`
- **THEN** the sandbox executes the piped command and returns its output

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

### Requirement: Working Directory
The server SHALL default the working directory to `/work` when no `cwd` is specified.
Relative `cwd` values SHALL be resolved against `/work`. Absolute paths SHALL be used as-is.
No explicit path traversal check is needed — the container's read-only rootfs and bind mount to `/work`
prevent commands from modifying anything outside the session directory.

#### Scenario: Default working directory
- **WHEN** the bot sends `POST /execute` with `{"command": "pwd"}`
- **THEN** the sandbox responds with stdout containing `/work\n`

#### Scenario: Relative cwd resolved
- **WHEN** the bot sends `POST /execute` with `{"command": "pwd", "cwd": "downloads"}`
- **THEN** the sandbox responds with stdout containing `/work/downloads\n`

#### Scenario: Absolute cwd accepted
- **WHEN** the bot sends `POST /execute` with `{"command": "ls", "cwd": "/work"}`
- **THEN** the sandbox executes the command successfully

### Requirement: Available Binaries
The sandbox container SHALL have the following binaries installed and available on PATH: ffmpeg, ffprobe, curl, jq, python3.

#### Scenario: ffmpeg is available
- **WHEN** the bot sends `POST /execute` with `{"command": "ffmpeg -version"}`
- **THEN** the sandbox responds with `exitCode: 0` and stdout containing "ffmpeg version"

#### Scenario: jq is available
- **WHEN** the bot sends `POST /execute` with `{"command": "jq --version"}`
- **THEN** the sandbox responds with `exitCode: 0` and stdout containing a version string

### Requirement: No pip or extra dependencies
The sandbox image SHALL install ffmpeg and ffprobe from a static release tarball, and curl and jq via apt. No pip packages SHALL be installed. The HTTP server SHALL use only the Python 3 standard library.

#### Scenario: Verify no pip packages
- **WHEN** the sandbox container starts
- **THEN** the server runs successfully with only Python stdlib modules imported
