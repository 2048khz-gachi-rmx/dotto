# Development environment

Services necessary to start developing.  
<sup><sub>currently: postgres, sandbox-image; S3 TBD...</sub></sup>

## Starting

```bash
cd .docker
docker compose up -d
```

- **postgres** — listens on `localhost:5999`
- **sandbox-image** — builds `dotto-sandbox:latest` locally so the bot can spin up ephemeral containers. Exits after build.

## Running the bot

The bot needs access to the Docker/Podman socket to manage sandbox containers. Example with Podman:

```bash
# Build + start infra
cd .docker && docker compose up -d

# Run the bot (needs DOCKER_HOST to find the sandbox daemon)
cd ..
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock dotnet run --project Dotto.Bot
```

With Docker for Desktop (Linux containers), `DOCKER_HOST` is usually auto-configured — just run the bot directly.

## Sidecar images

`sandbox-firewall` and `socket-protector` (the deployment compose) are built and
pushed by CI. To build them locally:

```sh
docker build -f .docker/sandbox-egress/Dockerfile \
  -t ghcr.io/2048khz-gachi-rmx/dotto-sandbox-firewall:latest .docker
docker build -f .docker/socket-protector/Dockerfile \
  -t ghcr.io/2048khz-gachi-rmx/dotto-socket-protector:latest .docker
```

## Sandbox egress in development

In dev the bot runs on the host, so `Ai:Sandbox:NetworkName` is unset and sandbox
containers are created on the default `docker0` bridge with their API published on
`127.0.0.1`. The production egress firewall (the `sandbox-firewall` sidecar, which
targets the `br-sandbox` bridge) therefore does not apply to dev sandboxes. Two
consequences:

- postgres is published on `127.0.0.1` only, so a dev sandbox cannot reach it via
the host.
- everything else on the host/LAN is a best-effort concern in dev; use the
  production compose network or apply equivalent rules to `docker0` if you need
  the same guarantees locally.