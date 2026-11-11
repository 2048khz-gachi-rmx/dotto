# Sandbox Image

Build with:

```powershell
docker build -f Dotto.Ai/sandbox-image/Dockerfile -t dotto-sandbox:latest .
```

Run (the token is required; every request must carry it in `X-Sandbox-Token`):

```powershell
docker run --rm -p 127.0.0.1:8080:8080 \
  -e SANDBOX_TOKEN=dev-token \
  -e SANDBOX_STORAGE_QUOTA_MB=200 \
  -v "$PWD/storage:/storage" \
  --tmpfs /work:size=1024m \
  dotto-sandbox:latest
```

Test:

```powershell
curl -H "X-Sandbox-Token: dev-token" http://localhost:8080/ping
# → {"status": "ok"}

curl -X POST http://localhost:8080/execute \
  -H "X-Sandbox-Token: dev-token" \
  -H "Content-Type: application/json" \
  -d '{"command": "jq --version"}'
# → {"exitCode":0, "stdout":"jq-1.7.1","stderr":""}
```

## Endpoints

| Method | Path | Request Body | Response |
|--------|------|-------------|----------|
| GET | `/ping` | — | `{"status":"ok"}` |
| POST | `/execute` | `{"command":"...","cwd":"...","timeout":"..."}` | `{"exitCode":int,"stdout":"...","stderr":"..."}` |
| GET | `/file?path=/storage/x.mp4` | — | raw bytes, `Content-Length` |
| PUT | `/file?path=/work/downloads/x.mp4&dedupe=1` | raw bytes, explicit `Content-Length` | `{"path":"/work/downloads/x_1.mp4"}` |

All requests require `X-Sandbox-Token`. Paths are confined to `/work` and
`/storage` using `realpath`, so symlinks cannot escape. `/work` is ephemeral
tmpfs scratch; `/storage` is the persistent per-user volume and is quota
checked (`507` when full).

The file endpoints stream in 64 KiB chunks. `PUT` requires an explicit
`Content-Length` because Python's `http.server` mishandles `Transfer-Encoding:
chunked`.

## Limits

- **Output budget:** stdout and stderr share one 10,000-character budget. stderr
  keeps at least half of it; stdout gets whatever stderr does not need. A stream
  that exceeds its share keeps its tail and gains a `[... N bytes truncated]`
  marker, and the response body is always small enough for the bot's 128 KiB
  buffer.
- **Storage quota:** usage charges every file `max(256 KiB, size)`, so many tiny
  files cannot make the quota meaningless. A `PUT` that would exceed it returns
  `507`; a command that pushes usage over it is killed.
- **Auth failures close the connection** (`Connection: close`): the request body
  is not read, so keeping the socket alive would desync the next request.
- **Errors are model-facing:** a missing `cwd` returns `exitCode: -1` naming the
  path, and an unexecutable command returns the OS message — never a traceback.

## Timeout Tiers

| Value | Hard timeout | Use case |
|-------|-------------|----------|
| `"short"` | 5s | Quick probes, `jq`, `ls`, `stat` |
| `"long"` | 300s | ffmpeg transcodes, large curl downloads |
| unset / any other | 20s | Default |

## What's inside

python:3.13-slim plus `ffmpeg`/`ffprobe` (static builds) and a Debian userland an assistant
reasonably expects: `bash`, coreutils, `grep`/`sed`/`awk`, `find`/`xargs`, `tar`/`gzip`, and
additionally `curl`, `jq`, `wget`, `git`, `file`, `less`, `tree`, `bc`, `ps`/`top`/`free`/`pkill`,
`lsof`, `time`, `hexdump`/`column`, `xxd`, `zip`/`unzip`/`xz`/`bzip2`/`zstd`, `dig`/`nslookup`/`host`,
`ping`, `nc`, `ip`/`ss`, `whois`, `cal`/`ncal`, `openssl`, `python3` + `pip`.

For charting and image work, `matplotlib` (Agg), `numpy` and `Pillow` are installed at pinned
versions, with DejaVu fonts under `/usr/share/fonts/truetype/dejavu/`. `MPLCONFIGDIR` points at
`/tmp/matplotlib` so font caches work with the read-only root filesystem.

Deliberately absent: compilers and build tools, ImageMagick, media suites beyond ffmpeg, and
`sudo`. The container runs as an unprivileged user with a read-only root filesystem.

