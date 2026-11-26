# Quick Start

Try Tempo on your own machine in a few minutes. This uses the development `docker-compose.yml`, which builds images from source and serves `http://localhost:3000`.

!!! warning "Local trial only"
    `docker-compose.yml` hard-codes the JWT secret and the database password (`postgres`), and publishes Postgres (`5432`) and the API (`5001`) on all interfaces. Do not expose it to a network. For a server, follow [Docker Deployment](../deployment/docker.md).

## Prerequisites

- [Docker](https://docs.docker.com/get-docker/) with Compose v2 (`docker compose`). The legacy `docker-compose` binary also reads this file.
- Free ports `3000`, `5001`, `5432`.

## 1. Start

```bash
git clone https://github.com/trevordavies095/tempo.git
cd tempo
docker compose up -d --build
```

Compose starts `postgres`, then `api` once Postgres is healthy, then `frontend` once the API's `/ready` passes. The API applies database migrations on start.

## 2. Check

```bash
docker compose ps                  # all three services "healthy"
curl -f http://localhost:5001/ready
```

`/ready` returns `200` once the API reaches Postgres. `/health` (API liveness) can be `200` while Postgres is down. `http://localhost:3000/health` is the command center's own process check, not the API.

This stack runs the API as `Production`, so Swagger UI is off. For Swagger, run the API from source ([Installation](installation.md)).

## 3. Register

1. Open `http://localhost:3000`. You are sent to the login page.
2. Register. Only possible while no user exists; registration locks after the first account. Passphrases are 16–64 characters ([rules](../deployment/security.md#password-requirements)).
3. Complete [onboarding](onboarding.md): optional Tempo export restore, units and heart rate zones, optional Strava archive.
4. Import day-to-day GPX/FIT files from the Import page ([Importing Workouts](../user-guide/importing-workouts.md)).

## Data and teardown

- Database: Docker volume `postgres_data` (prefixed with the project name, e.g. `tempo_postgres_data`).
- Media and import staging: `./media` in the repo checkout, mounted at `/app/media`.

```bash
docker compose down        # stop; data kept
docker compose down -v     # stop and delete the database volume
rm -rf ./media             # delete media too (may need sudo: the container writes as root)
```

Optional settings for this stack (CARTO map key, logging profile) go in a `.env` next to `docker-compose.yml`; see [Configuration](configuration.md#production-compose-env).

## Next Steps

- [Docker Deployment](../deployment/docker.md) for a real install
- [Configuration](configuration.md) reference
- [Troubleshooting](../troubleshooting/index.md)
