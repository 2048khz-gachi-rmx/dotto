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