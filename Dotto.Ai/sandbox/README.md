# Sandbox Image

Build with:

```powershell
docker build -f Dotto.Ai/sandbox/Dockerfile -t dotto-sandbox:latest .
```

Run:

```powershell
docker run --rm -p 8080:8080 dotto-sandbox:latest
```

Test:

```powershell
curl http://localhost:8080/ping
# → {"status": "ok"}

curl -X POST http://localhost:8080/execute -H "Content-Type: application/json" -d '{"command": "jq --version"}'
# → {"exitCode":0, "stdout":"jq-1.7.1","stderr":""}
```

## Endpoints

| Method | Path | Request Body | Response |
|--------|------|-------------|----------|
| GET | `/ping` | — | `{"status":"ok"}` |
| POST | `/execute` | `{"command":"...","cwd":"...","timeout":"..."}` | `{"exitCode":int,"stdout":"...","stderr":"..."}` |

## Timeout Tiers

| Value | Hard timeout | Use case |
|-------|-------------|----------|
| `"short"` | 5s | Quick probes, `jq`, `ls`, `stat` |
| `"long"` | 300s | ffmpeg transcodes, large curl downloads |
| unset / any other | 20s | Default |
