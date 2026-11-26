# Installation

Run the API and frontend from source, for development or to run Tempo without the app containers. To try Tempo with Docker, use [Quick Start](quick-start.md); to deploy a server, use [Docker Deployment](../deployment/docker.md). Contributor workflow (tests, OpenAPI, linting) is in [Developer Setup](../developers/setup.md).

## Prerequisites

- .NET 10 SDK
- Node.js 20.9+ and npm (Next.js 16 minimum; the frontend image uses Node 20)
- PostgreSQL 16, or Docker to run only the database

## 1. Database

Run only the Postgres service from the development Compose file (publishes `5432`, user/password `postgres`, database `tempo`):

```bash
docker compose up -d postgres
```

Or use your own PostgreSQL 16 and create a `tempo` database (`createdb tempo`).

The API's default connection string (`api/appsettings.json`) is `Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres`, which matches the Compose database. For any other database, override it rather than editing the file:

```bash
export ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=tempo;Username=me;Password=secret'
```

## 2. API

From the repository root:

```bash
dotnet tool restore      # once per clone; pins dotnet-ef and the Swagger CLI
cd api
dotnet watch run
```

- Listens on `http://localhost:5001` as `Development` (`api/Properties/launchSettings.json`).
- Applies migrations on start. `dotnet ef database update` is only needed to migrate without starting the API.
- Uses the local-only JWT key from `api/appsettings.Development.json`.
- Swagger UI: `http://localhost:5001/swagger`.
- Media goes to `api/media/` (`MediaStorage:RootPath` defaults to `./media`, relative to the working directory).

## 3. Frontend

In a second terminal:

```bash
cd frontend
npm install
npm run dev
```

Open `http://localhost:3000`. The dev server rewrites `/api/*` to `http://localhost:5001` (`frontend/next.config.ts`). Then [register and complete onboarding](quick-start.md#3-register).

## Ports

| Service | Port |
|---|---|
| Command center (frontend) | `3000` |
| API | `5001` |
| PostgreSQL | `5432` |

## System requirements

Rough guidance, not measured limits. Storage grows with media and stored raw workout files.

| | Minimum | Recommended |
|---|---|---|
| CPU | 1 core | 2+ cores |
| RAM | 2 GB | 4 GB+ |
| Disk | 10 GB | 50 GB+ |

Import jobs stage the uploaded ZIP (up to 500 MB) under the media directory while they run, so keep at least that much free space there.

## Next Steps

- [Configuration](configuration.md) reference
- [Import your first workout](../user-guide/importing-workouts.md)
