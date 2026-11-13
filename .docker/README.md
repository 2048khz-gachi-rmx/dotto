# Development environment

Local infrastructure for developing Dotto.

## Starting

```bash
cd .docker
docker compose up -d
```

- **postgres** — listens on `localhost:5999`
- **sandbox-image** — builds `dotto-sandbox:latest` locally and exits

## Running the bot

The bot needs the Docker/Podman socket to manage sandbox containers. Example with Podman:

```bash
cd .docker && docker compose up -d
cd ..
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock dotnet run --project Dotto.Bot
```

With Docker Desktop (Linux containers), `DOCKER_HOST` is usually auto-configured.

## Sandbox in development

Development uses runc and loopback TCP: `Dotto.Bot/appsettings.Development.json` sets `UseGVisorRuntime=false`, so no `runsc` install is needed. Sandboxes use the default `docker0` bridge and publish their API on `127.0.0.1`; the production socket transport and `br-sandbox` egress firewall do not apply.

Postgres is loopback-only, so a dev sandbox cannot reach it through the host. Other host/LAN access is best-effort; use the production compose if you need the same egress guarantees.

Production runtime setup is in the repository `README.md`.

## Sidecar images

CI publishes `sandbox-firewall` and `socket-protector`. To build locally:

```sh
docker build -f .docker/sandbox-egress/Dockerfile \
  -t ghcr.io/2048khz-gachi-rmx/dotto-sandbox-firewall:latest .docker
docker build -f .docker/socket-protector/Dockerfile \
  -t ghcr.io/2048khz-gachi-rmx/dotto-socket-protector:latest .docker
```
