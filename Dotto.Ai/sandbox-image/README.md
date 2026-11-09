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

## Timeout Tiers

| Value | Hard timeout | Use case |
|-------|-------------|----------|
| `"short"` | 5s | Quick probes, `jq`, `ls`, `stat` |
| `"long"` | 300s | ffmpeg transcodes, large curl downloads |
| unset / any other | 20s | Default |
