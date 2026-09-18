# Docker Deployment

Deploy Tempo using Docker Compose for easy production deployment.

## Overview

Docker Compose provides the simplest way to deploy Tempo in production. The `docker-compose.prod.yml` file is configured for production use with pre-built images.

## Prerequisites

- Docker and Docker Compose installed
- Sufficient disk space for database and media
- Network access (if using pre-built images from GitHub Container Registry)

## Production Docker Compose

The production configuration (`docker-compose.prod.yml`) includes:

- Pre-built, version-pinned images from GitHub Container Registry
- Required secrets from `.env` (JWT and database password)
- Command center published only on `127.0.0.1:3004` (Postgres and API stay on the Compose network)
- Health checks and automatic restarts

## Deployment Steps

### 1. Download Configuration

Ensure you have `docker-compose.prod.yml` and `.env.example` in your deployment directory.

### 2. Configure Environment Variables

Copy the example and fill in the required values. Compose refuses to start if either is missing or empty (do not leave them blank after `cp`).

```bash
cp .env.example .env
```

**Required** (in `.env`):

- `JWT_SECRET_KEY` — generate with `openssl rand -base64 32`
- `POSTGRES_PASSWORD` — same value for Postgres and the API connection string. Do not use `;` (it splits the Npgsql connection string). Prefer a long random value for new installs (`openssl rand -base64 32`).

**Existing Postgres volumes:** `POSTGRES_PASSWORD` is applied only on first database init. If you already have a `postgres_data` volume, set `POSTGRES_PASSWORD` to the password already in that cluster (older installs often used `postgres`). Editing `.env` alone does not rotate the role password.

**Rotating the database password:** run `ALTER USER postgres WITH PASSWORD '…';` inside Postgres, then update `POSTGRES_PASSWORD` in `.env` and recreate the API container so it picks up the new connection string.

**Optional:**

- `CARTO_BASEMAPS_API_KEY` — free [CARTO basemaps API key](https://carto.com/basemaps/apikey) (removes map watermark)

### 3. Start Services

```bash
docker compose -f docker-compose.prod.yml up -d
```

### 4. Verify Deployment

Check that all services are running:

```bash
docker compose -f docker-compose.prod.yml ps
```

Check logs:

```bash
docker compose -f docker-compose.prod.yml logs -f
```

### 5. Access Application

Put a reverse proxy (Caddy, nginx, Traefik) in front of **one** upstream: `127.0.0.1:3004`. Use that public HTTPS origin for both the command center and the daily driver.

- Command center: `https://your.domain`
- Daily driver: the same origin (the app talks to `/api/...`; Next.js rewrites `/api` to the API on the Compose network)
- Ready (verify a deploy): `https://your.domain/api/ready` (or `docker compose -f docker-compose.prod.yml exec api curl -f http://localhost:5001/ready`). `/health` is liveness only and can be `200` while Postgres is down.

**Upgrading from a split proxy** (`/` → `:3004`, `/api` → `:5001`): merge to a single upstream on `127.0.0.1:3004`, or uncomment the loopback API publish in `docker-compose.prod.yml` (`127.0.0.1:5001:5001`) until you migrate.

Postgres and the API are not published on the host by default.

## Image Versions

Production images are on GitHub Container Registry. The **current tags are whatever `docker-compose.prod.yml` pins** — update those pins when deploying a new release (do not treat `latest` as the production contract).

```bash
# After editing the tags in docker-compose.prod.yml:
docker compose -f docker-compose.prod.yml pull
docker compose -f docker-compose.prod.yml up -d
```

## Network Configuration

Services share the default Compose project network and talk by service name:

- `postgres` — database
- `api` — API
- `frontend` — command center (Next.js; rewrites `/api` to `api:5001`)

## Data Persistence

### Database

Database data is stored in the `postgres_data` Docker volume. This persists across container restarts.

### Media Files

Media files are stored in the `./media` directory, mounted as a volume. Ensure this directory:
- Exists and is writable
- Has sufficient disk space
- Is included in backups

## Updating

To update to a new version:

1. Update image tags in `docker-compose.prod.yml`
2. Pull new images: `docker compose -f docker-compose.prod.yml pull`
3. Restart services: `docker compose -f docker-compose.prod.yml up -d`

Database migrations run automatically on API startup.

## Stopping Services

```bash
docker compose -f docker-compose.prod.yml down
```

To remove volumes (clears database):

```bash
docker compose -f docker-compose.prod.yml down -v
```

## Password recovery

Forgot the only Tempo passphrase: do not delete volumes and do not hand-edit BCrypt in Postgres. From the directory with your Compose file:

```bash
docker compose -f docker-compose.prod.yml exec -it api dotnet Tempo.Api.dll reset-password
```

Scripts (no TTY):

```bash
docker compose -f docker-compose.prod.yml exec -T api dotnet Tempo.Api.dll reset-password --password-stdin
```

If the API container is not running:

```bash
docker compose -f docker-compose.prod.yml run --no-deps --rm api reset-password
```

The image `ENTRYPOINT` is already `dotnet Tempo.Api.dll`, so `reset-password` is passed as the verb. On a bare API host: `dotnet Tempo.Api.dll reset-password` from the API working directory. After a successful reset, log in again (old sessions are invalid). Full notes: [How do I reset my password?](../troubleshooting/faq.md#how-do-i-reset-my-password).

## Troubleshooting

### Services Not Starting

- Check logs: `docker compose -f docker-compose.prod.yml logs`
- Verify `.env` has non-empty `JWT_SECRET_KEY` and `POSTGRES_PASSWORD` (copy from `.env.example`)
- Ensure port `3004` is not in use on loopback
- Check disk space

### Database Connection Issues

- Verify PostgreSQL container is healthy
- Confirm `POSTGRES_PASSWORD` in `.env` matches the password in an existing volume (init only runs once)
- Ensure network connectivity between services

### Image Pull Failures

- Verify network connectivity
- Check image tags are correct
- Ensure authentication if using private registry

## Next Steps

- [Configure for production](production.md)
- [Review security settings](security.md)
- [Set up backups](backup-restore.md)

