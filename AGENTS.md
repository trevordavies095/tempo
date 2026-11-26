# AGENTS.md

Tempo: self-hosted running tracker. ASP.NET Core (.NET 10) Minimal API in `api/`, Next.js 16 / React 19 command center in `frontend/`, PostgreSQL 16, Docker Compose. The iOS app (daily driver) lives in a separate repo.

## Where to look

- **Domain vocabulary**: `CONTEXT.md`. Use its terms (Workout intake, Workout derivations, Import job, Device lap, elapsed vs timer vs moving time) in code, docs, and commits.
- **Architecture, service map, "where to change X"**: `docs/developers/architecture.md`.
- **HTTP contract**: `docs/openapi.json` (generated; CI fails on drift). Task guide and auth: `docs/developers/api-reference.md`.
- **Schema, migrations, startup backfills**: `docs/developers/database.md`; truth is `api/Data/TempoDbContext.cs` + `api/Migrations/`.
- **Tests**: `docs/developers/testing.md`.
- **Config keys and env vars**: `docs/getting-started/configuration.md`.

## Commands

Run `dotnet tool restore` once at the repo root (provides `dotnet ef` and `dotnet swagger`).

```bash
# API (from api/): http://localhost:5001, Swagger at /swagger in Development
dotnet watch run
dotnet ef migrations add <Name>        # migrations also apply automatically on startup

# Tests (from repo root): integration tests use Testcontainers, so Docker must be running
dotnet test Tempo.sln

# Frontend (from frontend/): http://localhost:3000
npm run dev
npm run lint
npm test            # Vitest unit tests

# Docs site
mkdocs serve        # pip install -r requirements.txt first
```

## Before you finish a change

- **Routes or Swagger metadata changed** → regenerate `docs/openapi.json` with the command in `docs/developers/testing.md` (OpenAPI Drift Check), against `bin/Debug/net10.0/Tempo.Api.dll` and `ASPNETCORE_ENVIRONMENT=Development` (`Testing` breaks Swashbuckle).
- **API change** → `dotnet test Tempo.sln`; CI enforces 45% line coverage.
- **Frontend change** → `npm run lint` and `npm test`. A new read or mutation goes through `frontend/lib/queries.ts` (`invalidateAfter` with what changed).
- **User-visible behavior changed** → update the matching page under `docs/` and add a line to `CHANGELOG.md`.

## Gotchas

- New derived data goes through `WorkoutDerivations` (recompute) or `WorkoutIntake.PersistAsync` (new Workouts); keep it to one pipeline, not a parallel path.
- Data that older Workouts lack gets a startup backfill (`api/Services/*BackfillService.cs`, wired in `StartupBackfills`). `dotnet Tempo.Api.dll backfill [--force]` reruns them; `reset-password` is the other host command (`api/Commands/`).
- The `Testing` environment skips migrations, JWT secret validation, and `IntervalsIcuSyncWorker`.
- Cadence columns are named `*CadenceRpm`, but the values are steps/min.

## Git

`develop` is the integration branch. Feature branches PR into `develop`, and releases PR `develop` → `main`. Commit only when asked.
