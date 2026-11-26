# Development Setup

Run the API, command center, and PostgreSQL locally for development. Read this once per machine; for running tests see [Testing](testing.md), for operator config see [Configuration](../getting-started/configuration.md).

## Prerequisites

- **.NET 10 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Node.js 20.9+** and npm (Next.js 16 minimum; CI and the frontend image use Node 20) - [Download](https://nodejs.org/)
- **Docker** - for the Compose Postgres, the API test suite (Testcontainers), and regenerating `docs/openapi.json`
- **PostgreSQL 16** - only if you do not use Docker for the database
- **Git**

## Quick Setup

### 1. Clone and Restore Tools

```bash
git clone https://github.com/trevordavies095/tempo.git
cd tempo
dotnet tool restore
```

`dotnet tool restore` installs the local tools pinned in `.config/dotnet-tools.json`: `dotnet-ef` 10.0.0 (matches EF Core 10) and the Swashbuckle CLI (`dotnet swagger`). Run it once per clone, from the repo root.

### 2. Start PostgreSQL

Run only the database from Compose (user `postgres`, password `postgres`, database `tempo`, port 5432):

```bash
docker compose up -d postgres
```

Or use a local PostgreSQL 16 and create the database:

```bash
createdb tempo
```

### 3. Configure Backend

No changes are needed for the Compose database: `api/appsettings.json` already points at `Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres`, and `api/appsettings.Development.json` supplies a local-only JWT key. Outside `Development`, the placeholder `JWT:SecretKey` in `appsettings.json` makes startup fail.

For a different database, override the connection string without editing the committed file:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=tempo;Username=me;Password=secret'
```

PostgreSQL is required; the API refuses to start with a missing or SQLite (`Data Source=`) connection string.

### 4. Start Backend

```bash
cd api
dotnet watch run
```

The API runs at `http://localhost:5001` in `Development` (from `api/Properties/launchSettings.json`). Migrations apply automatically on startup. Swagger UI is at `http://localhost:5001/swagger` and the OpenAPI JSON at `http://localhost:5001/swagger/v1/swagger.json` (Development only).

To apply migrations without starting the API:

```bash
cd api
dotnet ef database update
```

### 5. Start Frontend

In a new terminal:

```bash
cd frontend
npm install
npm run dev
```

The command center runs at `http://localhost:3000`. Requests to `/api/*` are rewritten to `http://localhost:5001/*`. The first visit asks you to register the single user, then runs [onboarding](../getting-started/onboarding.md).

## Development Commands

### Backend (API)

From `api/`:

```bash
# Run with hot reload
dotnet watch run

# Create a migration (model change in api/Models + api/Data/TempoDbContext.cs first)
dotnet ef migrations add MigrationName

# Apply migrations
dotnet ef database update

# Roll back to an earlier migration
dotnet ef database update PreviousMigrationName

# Build
dotnet build
dotnet build -c Release
```

Tests run from the repo root against `Tempo.sln`; see [Testing](testing.md). After changing routes or Swagger metadata, regenerate `docs/openapi.json` ([command](testing.md#openapi-drift-check)).

Host commands (run and exit; from `api/` use `dotnet run -- <verb>`, in a container `dotnet Tempo.Api.dll <verb>`):

- `reset-password` — reset a user's password (`--help` for options)
- `backfill [--force]` — run pending [startup backfills](architecture.md#6-startup-backfills)

### Frontend

From `frontend/`:

```bash
npm install      # install dependencies
npm run dev      # development server
npm run build    # production build
npm start        # serve the production build
npm run lint     # ESLint (CI runs this)
```

### Docker Compose

`docker-compose.yml` builds the API and frontend from local Dockerfiles. The API container runs as `Production` (no Swagger).

```bash
# Start all services (postgres, api, frontend)
docker compose up -d

# Rebuild after code changes
docker compose up -d --build

# View logs
docker compose logs -f [service-name]

# Stop all services
docker compose down

# Stop and remove volumes (clears database)
docker compose down -v
```

## Configuration

Full reference: [Configuration](../getting-started/configuration.md). Keys you are most likely to touch in development:

- `ConnectionStrings:DefaultConnection` - PostgreSQL connection string
- `MediaStorage:RootPath` - Filesystem path for media storage (default: `./media`)
- `MediaStorage:MaxFileSizeBytes` - Maximum media upload size (default: 50MB)
- `ElevationCalculation:NoiseThresholdMeters` / `ElevationCalculation:MinDistanceMeters` - Elevation smoothing (defaults 2.0 / 10.0)
- `CORS:AllowedOrigins` - Comma-separated list of allowed origins (default: `http://localhost:3000`)
- `Tempo:Logging:Profile` - `standard` (default) or `debug` (adds EF SQL and request traces)
- `CartoBasemaps:ApiKey` - optional CARTO basemaps key

**Note**: Command-center Strava/Tempo ZIP uploads use **512 KiB** chunks. Whole-ZIP adapters (`POST /workouts/import/bulk`, `POST /workouts/import/export`) allow up to 500MB (Kestrel and form limits in `Program.cs`).

### Frontend Configuration (`next.config.ts`)

- API rewrites: `/api/*` → `http://localhost:5001/*` (dev) or `http://api:5001/*` (production build, Docker service name)
- `API_SERVICE_URL`: environment variable to override the rewrite target
- Command-center clients always call same-origin `/api` (including chunked import jobs); do not post to `:5001` from the browser

## Sample Data

`test_data/` is gitignored: put your own GPX/FIT files or Strava export ZIPs there for manual import testing. Committed test fixtures live in `api/Tempo.Api.Tests/Fixtures/`.

## Troubleshooting

### Database Connection Issues

- Verify PostgreSQL is running: `docker ps` or `pg_isready`
- Check the connection string matches your setup
- Ensure the database exists: `createdb tempo`

### Port Conflicts

- Default ports: Frontend (3000), API (5001), PostgreSQL (5432)
- Change the host side of the mappings in `docker-compose.yml` if they conflict

### Migration Errors

- Migrations run automatically on startup; a failure stops the API with a fatal log line
- For a manual run: `dotnet tool restore` at the repo root, then `cd api && dotnet ef database update`
- `DatabaseMigrationHelper` reconciles databases whose tables exist but are missing from migration history

## Next Steps

- [Read the architecture documentation](architecture.md)
- [Run the tests](testing.md)
- [Review the contributing guide](contributing.md)
