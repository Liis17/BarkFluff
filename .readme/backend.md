[← Documentation hub](README.md)

<p align="center">
  <img src="../Windows/BarkFluff.Client.WPF/Resources/Images/barkfluff_logo.png" width="88" alt="BarkFluff logo">
</p>

<h1 align="center">Backend</h1>

<p align="center">
  <strong>Run the distributed .NET platform locally or build an individual service from source.</strong>
</p>

<p align="center">
  <a href="../README.md">Overview</a> ·
  <a href="#start-the-container-stack">Run</a> ·
  <a href="#build-from-source">Build</a> ·
  <a href="#useful-references">Reference</a>
</p>

---

## Prerequisites

- Docker Engine with the Compose plugin for the container stack.
- Access to the private `docker.barkfluff.com:5000` registry when running the supplied compose file.
- .NET SDK **10.0.110** to build source projects. The exact SDK is pinned in [`global.json`](../global.json).

## Start the container stack

The tracked backend stack lives in `docker/backend/docker-compose-dev-backend.yml`. It deploys prebuilt images; it is not a source-build compose file.

1. Obtain the environment configuration and credentials for the target environment. They must stay outside Git.
2. Create `docker/backend/.env` with the required values and provide the LiveKit configuration from `docker/livekit/livekit.yaml` when Calls are enabled.
3. Authenticate to the private registry, validate the resolved configuration, then start the stack:

```bash
cd docker/backend
docker login docker.barkfluff.com:5000
docker compose -f docker-compose-dev-backend.yml config
docker compose -f docker-compose-dev-backend.yml pull
docker compose -f docker-compose-dev-backend.yml up -d
docker compose -f docker-compose-dev-backend.yml ps
```

The compose file expects database, service-to-service, Firebase, Telegram, MinIO, RabbitMQ, Seq, and port settings. Run `docker compose … config` before `up`; it is the safe way to catch an incomplete `.env` without starting containers.

> The former `Backend/docker-compose-dev.yml` path is no longer in this repository. Use the path above rather than copying commands from older documents.

## Stop the stack

```bash
cd docker/backend
docker compose -f docker-compose-dev-backend.yml down
```

`down` preserves named volumes. Add `--volumes` only when intentionally discarding local database, cache, object-storage, log, and broker data.

## Build from source

Build a specific service when changing it:

```bash
dotnet build Backend/BarkFluff.Identity/BarkFluff.Identity.csproj
```

Build the full .NET solution from the repository root:

```bash
dotnet build BarkFluff.sln
```

The solution also contains Windows projects, so a complete build is best run on Windows. On other platforms, build the service project you are working on.

## Useful references

- [Ports and environment variables](../Backend/PORTS_CONFIGURATION.md)
- [Docker setup reference](../Backend/DOCKER_SETUP.md)
- [.NET SDK requirements](../Backend/DOTNET_SDK_REQUIREMENTS.md)
- [Architecture knowledge base](../Obsidian/ClaudeVault/Архитектура.md)
