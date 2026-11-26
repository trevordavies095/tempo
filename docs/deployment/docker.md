# Docker Deployment

The production install and upgrade procedure: `docker-compose.prod.yml` with pinned images from GitHub Container Registry, behind your HTTPS reverse proxy. Settings are in [Configuration](../getting-started/configuration.md); hardening and operations in [Production Setup](production.md).

## What production Compose gives you

- `postgres` (`postgres:16-alpine`), `api` (`ghcr.io/trevordavies095/tempo/api:<tag>`), `frontend` (`ghcr.io/trevordavies095/tempo/frontend:<tag>`), all `restart: unless-stopped` with health checks.
- `JWT_SECRET_KEY` and `POSTGRES_PASSWORD` required from `.env`; Compose refuses to start without them.
- Only the command center is published, on `127.0.0.1:3004`. Postgres (`5432`) and the API (`5001`) stay on the Compose network.
- Data: Docker volume `postgres_data` and the `./media` directory next to the Compose file (mounted at `/app/media`).

## Prerequisites

- Docker with Compose v2 (`docker compose`).
- A reverse proxy for HTTPS on the same host ([Reverse Proxy](reverse-proxy.md)). The session cookie is `Secure`, so login needs HTTPS (or `http://localhost`).

## Install

### 1. Get the files

Pick a release from [GitHub Releases](https://github.com/trevordavies095/tempo/releases) and fetch the Compose file and `.env.example` from that tag:

```bash
mkdir -p ~/tempo && cd ~/tempo
TAG=v2.12.0
curl -fsSLO "https://raw.githubusercontent.com/trevordavies095/tempo/$TAG/docker-compose.prod.yml"
curl -fsSLO "https://raw.githubusercontent.com/trevordavies095/tempo/$TAG/.env.example"
```

The file at a release tag pins that release's images.

### 2. Fill in `.env`

```bash
cp .env.example .env
```

Set both required values (empty values fail Compose):

```bash
JWT_SECRET_KEY=<output of: openssl rand -base64 32>
POSTGRES_PASSWORD=<output of: openssl rand -base64 32>
```

- `POSTGRES_PASSWORD` is used for Postgres and the API connection string. Do not use `;`.
- It is applied only when the `postgres_data` volume is first created. For an existing volume, set the password already in that cluster (older installs often used `postgres`). To change it: `ALTER USER postgres WITH PASSWORD '…';` in Postgres, update `.env`, then `docker compose -f docker-compose.prod.yml up -d` to recreate the API.
- Keep `JWT_SECRET_KEY` stable and backed up. Changing it signs everyone out and breaks stored integration secrets ([details](../getting-started/configuration.md#rotating-the-jwt-secret)).

Optional `.env` values (CARTO map key, logging profile, backfills, cookie): [Compose `.env` reference](../getting-started/configuration.md#production-compose-env).

### 3. Start

```bash
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps     # wait for all three "healthy"
```

The API applies migrations on start.

### 4. Put HTTPS in front

Point your reverse proxy at **one** upstream, `127.0.0.1:3004`. The frontend rewrites `/api` to the API on the Compose network. Use that public HTTPS origin for the command center and the daily driver. Examples: [Reverse Proxy](reverse-proxy.md).

### 5. Verify

```bash
curl -f https://tempo.example.com/api/ready
# or, without the proxy:
docker compose -f docker-compose.prod.yml exec api curl -f http://localhost:5001/ready
```

| URL (public origin) | Answers |
|---|---|
| `/api/ready` | API readiness: `200` when Postgres answers within 2 s, else `503`. Use this. |
| `/api/health` | API liveness only; `200` even when Postgres is down. |
| `/health` | Command center (Next.js) process pulse, not the API. |
| `/api/version` | Version, build, logging profile, environment. |

Then open the origin, register, and complete [onboarding](../getting-started/onboarding.md).

**Upgrading from a split proxy** (`/` → `:3004`, `/api` → `:5001`): switch to the single upstream, or uncomment the loopback API publish (`127.0.0.1:5001:5001`) in `docker-compose.prod.yml` until you do.

## Image tags

| Tag | Built from |
|---|---|
| `vX.Y.Z` | Release tag. Use this. |
| `latest` | Most recent release or push to `main`. |
| `edge` | Every push to `develop`. Unreleased. |

Pin `api` and `frontend` to the same `vX.Y.Z`. Do not run `latest` as your production contract.

## Updating

An upgrade is not a no-op restart: migrations run on API start, and some releases add backfills.

1. **Back up.** `pg_dump` plus `./media`, or a Settings → Export ZIP ([Backup and Restore](backup-restore.md)).
2. **Check no import is running.** On start the API marks any unfinished import job `failed` (interrupted) and deletes its upload. It does not resume; start it again afterwards.
3. **Bump the tags.** Set `api` and `frontend` in `docker-compose.prod.yml` to the new `vX.Y.Z` (or re-download the file at the new tag and re-apply your edits). Check the release notes for `.env.example` changes.
4. **Pull and start.**
   ```bash
   docker compose -f docker-compose.prod.yml pull
   docker compose -f docker-compose.prod.yml up -d
   ```
5. **Check backfills.**
   ```bash
   docker compose -f docker-compose.prod.yml logs api | grep -i "startup backfills"
   ```
   `all 8 complete; none run` means done. If it warns that some `have not completed on this library`, run once and wait for `Backfills finished: all 8 recorded complete`:
   ```bash
   docker compose -f docker-compose.prod.yml exec api dotnet Tempo.Api.dll backfill
   ```
   Large libraries take a while (CPU, disk, Postgres WAL); browsing works throughout. Tempo 2.11.0 and earlier ran every backfill on every start. Other options: [Startup backfills](../getting-started/configuration.md#startup-backfills).
6. **Smoke test.** `curl -f https://tempo.example.com/api/ready`. It does not wait for backfills.

### Leaving the experimental `:feat-ai-coach` image

The `feat-ai-coach` API/frontend tags are retired. Move to a release `vX.Y.Z` (or `edge`). On the next start the API **drops leftover `Coach*` tables** and their `__EFMigrationsHistory` rows, discarding coach data (goals, weeks, check-ins). Workouts, settings, and shoes are untouched. There is no opt-out.

## Stopping

```bash
docker compose -f docker-compose.prod.yml down      # data kept
docker compose -f docker-compose.prod.yml down -v   # also deletes the database volume
```

`down -v` does not delete `./media`.

## Password recovery

Forgot the only passphrase: do not delete volumes or edit BCrypt hashes in Postgres. From the Compose directory:

```bash
docker compose -f docker-compose.prod.yml exec -it api dotnet Tempo.Api.dll reset-password
# scripts (no TTY):
docker compose -f docker-compose.prod.yml exec -T api dotnet Tempo.Api.dll reset-password --password-stdin
# API container not running (the image ENTRYPOINT is already dotnet Tempo.Api.dll):
docker compose -f docker-compose.prod.yml run --no-deps --rm api reset-password
```

Without Docker: `dotnet Tempo.Api.dll reset-password` from the API directory. Existing sessions are invalidated. More: [How do I reset my password?](../troubleshooting/faq.md#how-do-i-reset-my-password).

## Troubleshooting

| Symptom | Check |
|---|---|
| `up` fails with `Set JWT_SECRET_KEY…` / `Set POSTGRES_PASSWORD…` | `.env` is in the Compose directory and both values are non-empty. |
| API unhealthy, logs show auth failure to Postgres | `POSTGRES_PASSWORD` must match the existing volume's password (init runs once). |
| API exits at startup | `docker compose -f docker-compose.prod.yml logs api`. Invalid values for `Tempo__Logging__Profile`, `Tempo__Backfills__OnStartup`, `Cookie__Secure`, or `Tempo__Proxy__TrustedNetworks` stop startup by design. |
| Port in use | Something else holds `127.0.0.1:3004`; change the left side of `127.0.0.1:3004:3000`. |
| Login succeeds, then signed out | Plain HTTP; see [Troubleshooting login](reverse-proxy.md#troubleshooting-login). |
| `pull` fails | Tag exists on GHCR (`vX.Y.Z`, with the `v`); network reaches `ghcr.io`. |

## Next Steps

- [Reverse Proxy and Login Cookies](reverse-proxy.md)
- [Production Setup](production.md)
- [Backup and Restore](backup-restore.md)
