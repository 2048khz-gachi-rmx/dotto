# Sandbox Image

```bash
docker build -t dotto-sandbox:latest Dotto.Ai/sandbox-image
```

The API uses TCP in development or a Unix socket in production; the HTTP contract is identical.

## TCP

```bash
docker run --rm -p 127.0.0.1:8080:8080 \
  -e SANDBOX_TOKEN=dev-token \
  -e SANDBOX_STORAGE_QUOTA_MB=200 \
  -v "$PWD/storage:/storage" \
  --tmpfs /work:size=1024m \
  dotto-sandbox:latest

curl -H "X-Sandbox-Token: dev-token" http://localhost:8080/ping
# → {"status": "ok"}
```

## Unix socket

`SANDBOX_SOCKET` makes the server bind that path instead of TCP. The directory must be writable by the image's non-root `dotto` user (`0777`).

```bash
mkdir -m 0777 /tmp/sandbox-ipc
docker run --rm -d --name sandbox \
  -e SANDBOX_TOKEN=dev-token \
  -e SANDBOX_STORAGE_QUOTA_MB=200 \
  -e SANDBOX_SOCKET=/run/dotto/sandbox.sock \
  -v /tmp/sandbox-ipc:/run/dotto \
  --tmpfs /work:size=1024m \
  dotto-sandbox:latest

curl --unix-socket /tmp/sandbox-ipc/sandbox.sock \
  -H "X-Sandbox-Token: dev-token" http://localhost/ping
# → {"status": "ok"}
```

The server unlinks a stale socket before binding and chmods it `0666` for the bot's uid.

## Auth

Every request needs `X-Sandbox-Token`. Missing or invalid tokens return `401` with `Connection: close` on both transports; the socket is not an authentication substitute.

## Endpoints

| Method | Path | Request Body | Response |
|--------|------|-------------|----------|
| GET | `/ping` | — | `{"status":"ok"}` |
| POST | `/execute` | `{"command":"...","cwd":"...","timeout":"..."}` | `{"exitCode":int,"stdout":"...","stderr":"..."}` |
| GET | `/file?path=/storage/x.mp4` | — | raw bytes, `Content-Length` |
| PUT | `/file?path=/work/downloads/x.mp4&dedupe=1` | raw bytes, explicit `Content-Length` | `{"path":"/work/downloads/x_1.mp4"}` |

Paths are confined to `/work` and `/storage` with `realpath`, so symlinks cannot escape. `/work` is ephemeral tmpfs; `/storage` is quota-checked (`507` when full).

File endpoints stream in 1 MiB chunks (`FILE_CHUNK_SIZE`); exec pumps keep 64 KiB (`CHUNK_SIZE`) because their tail buffer is only 40 KiB. `PUT` requires an explicit `Content-Length` (Python's `http.server` mishandles chunked bodies).

## Limits

- **Output:** stdout and stderr share 10,000 characters; stderr keeps at least half, and an overflowing stream keeps its tail with a truncation marker. Responses stay under the bot's 128 KiB buffer.
- **Quota:** each file charges `max(256 KiB, size)`. A `PUT` over quota returns `507`; a command that pushes usage over is killed.
- **Errors:** model-facing only — a missing `cwd` or unexecutable command returns `exitCode: -1` plus the OS message, never a traceback.

## Timeouts

| Value | Hard timeout | Use case |
|-------|-------------|----------|
| `"short"` | 5s | Quick probes, `jq`, `ls` |
| `"long"` | 300s | ffmpeg, large downloads |
| unset / other | 20s | Default |

## Contents

`python:3.13-slim`, static ffmpeg/ffprobe, and a Debian userland (`bash`, coreutils, grep/sed/awk, find, tar, curl, jq, wget, git, file, less, tree, bc, ps, lsof, time, xxd, zip/unzip/xz/zstd, dig, ping, nc, ip/ss, whois, openssl, python3 + pip). Charting: pinned matplotlib (Agg), numpy, Pillow, DejaVu fonts; `MPLCONFIGDIR=/tmp/matplotlib`.

No compilers, ImageMagick, media suites beyond ffmpeg, or sudo. Runs as an unprivileged user with a read-only root filesystem.
