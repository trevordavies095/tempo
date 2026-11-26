# Architecture Overview

How the API, command center, and database fit together, and where to change things. Read this before your first change; use [Where to change X](#where-to-change-x) as the task map.

Sources of truth that this page does not duplicate:

- Domain vocabulary: [`CONTEXT.md`](https://github.com/trevordavies095/tempo/blob/develop/CONTEXT.md) (Workout intake, Workout derivations, Import job, Command center, …)
- HTTP contract: [`docs/openapi.json`](https://github.com/trevordavies095/tempo/blob/develop/docs/openapi.json) (CI fails if it drifts from the code; see [Testing](testing.md#openapi-drift-check))
- Schema: `api/Data/TempoDbContext.cs` and `api/Migrations/` (summarized in [Database Schema](database.md))
- Service registration and DI lifetimes: `api/Program.cs`

## System Components

### Frontend

- **Framework**: Next.js 16 with React 19
- **Language**: TypeScript
- **Styling**: Tailwind CSS with semantic identity tokens (`canvas`, `ink`, `muted`, `volt`, and related). Geist is the UI typeface. Do not use tutorial `blue-600` as the brand accent.
- **UI kit**: Hand-rolled primitives in `frontend/components/ui/` — `PageShell` (density `control` | `overview`), `Card`, `Button`, `Dialog`, `EmptyState`, `Tabs`
- **Appearance**: Dark-first. Tailwind `class` strategy on `<html>` (`dark`). Preference is `system` | `dark` | `light` in `localStorage` (`tempo-appearance`); not UserSettings. See `frontend/lib/appearance.ts`.
- **Icons**: Tabler Icons (`@tabler/icons-react`) for UI icons. Brand marks live in `frontend/public/` (`tempo-mark-volt.png`, `tempo-mark-ink.png`).
- **State Management**: TanStack Query for server state
- **Maps**: Leaflet/React-Leaflet. Carto Dark Matter in Dark appearance, Voyager in Light; polyline and Highlight from tokens; OSM + CARTO attribution. Operators configure a CARTO basemaps API key via `CartoBasemaps:ApiKey` / `CartoBasemaps__ApiKey` on the API; the command center reads it from `GET /settings/carto-basemaps`.
- **Charts**: Recharts; series colors from identity tokens. Workout overview charts HR, pace, elevation, cadence, and power via `getWorkoutTimeSeries` in `frontend/lib/api.ts` (pages until complete or 20,000 samples).
- **Highlight**: Shared overview focus (`splitIdx` and/or `elapsedSeconds`) in `frontend/lib/workoutHighlight.ts` — map, splits, and charts follow together.
- **API access**: The browser always calls same-origin `/api/*`; `frontend/next.config.ts` rewrites it to the API (see [Development Setup](setup.md#frontend-configuration-nextconfigts)).

### Backend

- **Framework**: ASP.NET Core Minimal APIs on .NET 10 (`net10.0` in `api/Tempo.Api.csproj`)
- **Language**: C#
- **ORM**: Entity Framework Core 10 with Npgsql
- **Database**: PostgreSQL 16 only. Startup throws if `ConnectionStrings:DefaultConnection` is missing or a SQLite `Data Source=` string.
- **FIT decoding**: `Garmin.FIT.Sdk` NuGet package (`Dynastream.Fit` namespace)
- **Logging**: Serilog with named profiles (`standard` | `debug`) via `Tempo:Logging:Profile`
- **OpenAPI**: Swashbuckle, configured in `api/OpenApi/TempoOpenApi.cs`; Swagger UI only in Development

### Database

- **PostgreSQL 16** with JSONB support
- Hybrid storage: core stats in dedicated columns, raw data in JSONB
- Automatic migrations on startup

## Key Architectural Patterns

### 1. Minimal APIs

Each file in `api/Endpoints/` exposes one static extension method, called from `api/Program.cs`:

| File | Method | Routes |
|------|--------|--------|
| `AuthEndpoints.cs` | `MapAuthEndpoints` | `/auth/*` — register, login, logout, me, onboarding, change password, API keys. Authorization is per route; API-key and change-password routes use the `JwtSessionOnly` policy. |
| `WorkoutsEndpoints.cs` | `MapWorkoutsEndpoints` | `/workouts/*` — import (file, HealthKit, bulk, export restore, Import jobs), list, detail, edit, crop, recalculations, media, time series, export |
| `SettingsEndpoints.cs` | `MapSettingsEndpoints` | `/settings/*` — units, heart-rate zones, thresholds, default shoe, CARTO key |
| `SettingsIntervalsIcuEndpoints.cs` | `MapIntervalsIcuSettings` (on the `/settings` group) | `/settings/intervals-icu*` |
| `ShoesEndpoints.cs` | `MapShoesEndpoints` | `/shoes/*` |
| `StatsEndpoints.cs` | `MapStatsEndpoints` | `/stats/*` — weekly/yearly stats, relative effort, weekly recap, best efforts, insights |
| `VersionEndpoints.cs` | `MapVersionEndpoints` | `GET /version` (public) |
| `HealthEndpoints.cs` | `MapHealthEndpoints` | `GET /health` (liveness), `GET /ready` (Postgres reachable; public) |

Conventions:

- Grouped files call `app.MapGroup(...)` with `WithTags()` for Swagger grouping. The `/workouts`, `/settings`, `/shoes`, and `/stats` groups call `.RequireAuthorization()` on the whole group.
- Handlers are `private static async Task<IResult>` methods with XML doc comments, returning `Results.*`. Services arrive as handler parameters via DI.
- Route metadata (`WithName`, `WithSummary`, `WithDescription`, `Produces`) feeds `docs/openapi.json`.

### 2. Service Layer

All in `api/Services/`. Names in bold are `CONTEXT.md` terms.

**Decode and geometry**

- `GpxParserService` / `FitParserService` — decode adapters: `TrackPoint`s, raw JSON, optional device summary (and GPX name). They do not expose `CalculateSplits` and do not hand FIT `RecordMesg` to callers. FIT session JSON may include raw `workoutRpe` (Borg CR10 × 10). `FitProductNames` names Garmin devices from the FIT SDK product tables.
- `HealthKitWorkoutDecoder` — tempo-ios HealthKit JSON → `DecodedWorkout` + overlay.
- `StravaCsvParserService` — parses Strava export CSV metadata for bulk ZIP import.
- `DeviceExtractionService` — device info from FIT files. `DeviceLapMapper` — device-authored laps → `device_lap` splits.
- `TrackGeometry` (**Track geometry**) — in-process: `TrackPoint`s in; elevation gain, `WorkoutRoute` (empty when no GPS), `WorkoutSplit`s (Haversine or cumulative `DistanceM` stream), `WorkoutTimeSeries` out. No `DbContext`.
- `TrackPointRehydration` — stored Workout fields → `TrackPoint`s for crop and split recalc.

**Persist and recompute**

- `WorkoutIntake` (**Workout intake**) — `ProcessAsync` (file stream + filename → decode adapter) and `PersistAsync` (decoded input: geometry, duplicate policy, weather, default shoe, then Workout derivations). Persist is the single pipeline; HTTP import, Strava bulk per-file, and Intervals.icu sync all call it. FIT session `workoutRpe` fills `Workout.Rpe` (1–10) when null.
- `WorkoutDerivations` (**Workout derivations**) — `RecomputeAsync(workout, WorkoutChange.Samples | HeartRateZones)` and `RecomputeHeartRateZonesAsync`: time in zones, Relative Effort, and (for `Samples`) Best efforts. Used by intake, duplicate upgrade, crop, and the Relative Effort recalculations.
- `HeartRateZoneService`, `RelativeEffortService`, `BestEffortService`, `SplitHeartRateService` — the calculations Workout derivations and splits use.
- `WorkoutCropService` — trims time from the start or end of a Workout.
- `SplitRecalculationService` — rebuilds `distance` splits after a unit-preference change.
- `WeatherService` — weather for a Workout's first GPS point and start time.

**Import, export, sync**

- `ImportJobService` — create/chunk/complete/current/get/cancel, one-active-job rules, archive staging under `media/imports/{jobId}/`.
- `ImportJobWorker` — hosted service; wakes on `ImportJobQueue`, new DI scope per job; branches on `kind` (`strava_bulk` | `tempo_export`). After a Tempo restore it clears backfill completion records and runs every backfill.
- `StravaBulkImportOrchestrator` — Strava ZIP extract/CSV loop calling `BulkImportService` + Workout intake; writes job counters.
- `BulkImportService` — ZIP safety, `activities.csv`, non-run skip, per-file intake mapping, Strava media copy.
- `ExportService` — full-library export ZIP.
- `ImportService` — Tempo export ZIP restore (`ImportExportAsync` with progress + cancel); not Workout intake.
- `IntervalsIcuSyncService` — one tick: decrypt key, list recent activities (`oldest` = max(connect−2d, lastSuccess−2d, today−14d)), skip non-runs / junk `file_type` and existing `intervals_icu` identities before file fetch, then `WorkoutIntake.ProcessAsync` with overlay identity `intervals_icu`. Stats-key attach does not rename `Workout.Source`. 401/decrypt disables and keeps the ciphertext; 429/5xx stay enabled. HTTP via `IntervalsIcuClient`.
- `IntervalsIcuSyncWorker` — hosted service (not registered in Testing); timer every `IntervalsIcu:PollIntervalMinutes` (default 15) plus Sync now via `IntervalsIcuSyncQueue`; new DI scope per tick. Not an Import job.

**Queries and analytics**

- `WorkoutQueryService` — common Workout queries and filtering.
- `InsightsService` (+ `InsightsThresholds`) — `GET /stats/insights`.
- `ThresholdSuggestionService` (+ `DanielsVdot`) — suggested threshold pace from recent 5K/10K best efforts for `/settings/thresholds`; never saved.
- `RouteMatchingService` — similar routes and similarity scores (Similar runs / Route Comparison).
- `ShoeMileageService` — shoe mileage from assigned Workouts.

**Auth and secrets**

- `PasswordService`, `PasswordPolicy` (16+ characters), `JwtService`, `ApiKeyService` (one active `tmp_` key per user).
- `SecretProtector.cs` — `IntervalsIcuSecretProtector` and `ApiKeySecretProtector` encrypt stored secrets.

**Startup and maintenance**

- `DatabaseMigrationHelper` — see [Automatic Migrations](#5-automatic-migrations).
- `StartupBackfills`, `BackfillPass`, `BackfillRunner`, and the `*BackfillService` classes — see [Startup Backfills](#6-startup-backfills).

**DI lifetimes** (`api/Program.cs`):

- `Scoped`: almost every service above, including `TempoDbContext`. `IRelativeEffortService`, `IBestEffortService`, and `IWeatherService` resolve to the concrete scoped services so tests can fake them.
- `Singleton`: config objects (`MediaStorageConfig`, `ElevationCalculationConfig`, `CartoBasemapsConfig`, auth cookie settings), `ImportJobQueue`, `IntervalsIcuSyncQueue`, both secret protectors, the logging profile, and the backfill list.
- `HttpClient`: `WeatherService` is a typed client (`AddHttpClient<WeatherService>`); intervals.icu uses a named client (`IntervalsIcuClient.HttpClientName`, base `https://intervals.icu/api/v1/`, 15 s timeout).
- Hosted services: `ImportJobWorker`; `IntervalsIcuSyncWorker` (skipped in Testing); `BackfillRunner` (only when `Tempo:Backfills:OnStartup` is `run` or `force`).

### 3. Hybrid Data Storage

- **Core stats**: dedicated columns for efficient querying
- **Raw data**: JSONB (`RawGpxData`, `RawFitData`, `RawStravaData`, `RawHealthKitData`, `Weather`) plus original file bytes, so derived data can be rebuilt later

### 4. Media Storage

- Workout media on filesystem: `media/{workoutId}/filename.ext` (root from `MediaStorage:RootPath`, default `./media`)
- Import-job ZIP staging: `media/imports/{jobId}/archive.zip` (deleted on completed, failed, cancelled, or startup interrupt)
- Metadata for workout media stored in the database

### 5. Automatic Migrations

`DatabaseMigrationHelper.ApplyMigrations` runs on API startup (skipped in the Testing environment, where the test fixture owns the schema). Before `Database.Migrate()` it:

- Creates `__EFMigrationsHistory` if missing (databases once created with `EnsureCreated()`)
- Drops leftover experimental coach schema
- Queries `information_schema` and marks legacy migrations as applied when their tables/columns already exist, using hardcoded table/column → migration ID maps

A failed migration stops the API from starting.

### 6. Startup Backfills

Upgrade backfills fill derived data that older versions did not store (route previews, split heart rate, FIT cadence, timer time, device laps, zone seconds, best efforts, device names). The ordered list is `StartupBackfills.All` in `api/Services/StartupBackfills.cs`; completion is recorded per name and version in the `CompletedBackfills` table.

- `Tempo:Backfills:OnStartup` (`Tempo__Backfills__OnStartup`; prod compose maps `TEMPO_BACKFILLS_ON_STARTUP`): `skip` (default) runs none and only checks after migrations, warning if any are pending on a library with Workouts; `run` / `force` register `BackfillRunner`, a one-shot background pass that never gates `/ready`.
- Backfills also run after a Tempo ZIP restore and from the host command `dotnet Tempo.Api.dll backfill [--force]`.
- Operator details: [Configuration](../getting-started/configuration.md) (startup backfills) and the upgrade steps in [Docker deployment](../deployment/docker.md).

### 7. Host Commands

`api/Commands/` holds one-shot verbs parsed in `Program.cs` before the web host starts; they run and exit without listening:

- `dotnet Tempo.Api.dll reset-password` — `ResetPasswordCommand`
- `dotnet Tempo.Api.dll backfill [--force]` — `BackfillCommand` (applies migrations first)

### 8. Logging

- Serilog console output; levels owned by named profile `Tempo:Logging:Profile` (`standard` | `debug`), not MEL `Logging:LogLevel`
- Startup logs `Logging profile: …` once so pasted dumps declare the mode
- `standard` (default): Tempo Information; Microsoft / System Warning (hosting lifetime Information); request middleware logs 5xx / unhandled exceptions only
- `debug`: Information including EF SQL and non-probe request traces (not Serilog level Debug)
- Successful `/health` and `/ready` request lines omitted on both profiles
- Official Compose Postgres uses `log_checkpoints=off` (not driven by the API profile)

## Data Model

Entities in `api/Models/`; columns, relationships, and indexes are in [Database Schema](database.md) and `api/Data/TempoDbContext.cs`. Definitions of the domain terms live in `CONTEXT.md`.

- **Workout**: core stats plus JSONB raw data and weather
- **TrackPoint**: in-memory sample on a path (not a table)
- **WorkoutRoute**: one-to-one GeoJSON LineString
- **WorkoutSplit**: one-to-many segment rows with `Kind` (`distance` | `device_lap`); `Idx` unique per kind
- **WorkoutTimeSeries**: one-to-many samples (heart rate, pace, elevation, cadence, power, …)
- **WorkoutMedia**: one-to-many photos/videos
- **WorkoutBestEffort**: per-Workout fastest time per standard distance (derived; recomputed, not exported)
- **WorkoutExternalIdentity**: `(Source, ExternalId)` link to an upstream system, used for intake idempotency
- **Shoe**: shoes with mileage and Workout assignments
- **User**: account (`Username`, password hash, `OnboardingCompleted`)
- **ApiKey**: hashed + encrypted machine key; at most one active per user
- **UserSettings**: single-row preferences (heart-rate zones, units, default shoe, thresholds). Command-center appearance is not UserSettings.
- **ImportJob**: background import (`strava_bulk` | `tempo_export`) with status, byte/progress counters, `ErrorDetailsJson` (Strava), `ResultJson` (Tempo), and archive path. At most one row in `receiving` | `queued` | `running`.
- **IntervalsIcuConnection**: optional 0-or-1 row for a BYO intervals.icu API key (encrypted). Not UserSettings.
- **CompletedBackfill**: one row per backfill recorded complete

## Data Flow

### Workout Import Flow

1. File uploaded to `POST /workouts/import`, or HealthKit JSON posted to `POST /workouts/import/healthkit`
2. HTTP passes the `IFormFile` stream + filename to `WorkoutIntake.ProcessAsync`, or `HealthKitWorkoutDecoder` maps JSON → `DecodedWorkout` + overlay for `PersistAsync`
3. File decode adapter: GPX or FIT → `DecodedWorkout` (`TrackPoint`s, raw JSON, optional device summary); HealthKit skips file parse
4. `PersistAsync` → `TrackGeometry.Derive` builds elevation, route (omitted when there are no GPS coordinates), splits (Haversine or DistM stream), and time series (device summary wins for distance/duration when present)
5. Duplicate policy: HealthKit UUID first when present (`skipped` before geometry/enrichment); then Workout external identity when present (`skipped` before geometry; on a stats-key match, attach a row only if this Workout has no row for that source; if the pair is already stored on another Workout, skip the owner — identity wins). Then the start/distance/elapsed stats key (`StartedAt`, `DistanceM`, `DurationS`). Incomplete raw JSON/bytes can `updated`; complete duplicates `skipped` (HealthKit vs complete GPX/FIT is always `skipped`, but may stamp `HealthKitUuid` onto the existing row for client badging). Intake never overwrites a stored external `ExternalId` and does not rename `Workout.Source` on attach.
6. Weather and default shoe, then the Workout, route, splits, time series, and external identity are saved
7. Workout derivations (`WorkoutChange.Samples`): time in zones, Relative Effort, Best efforts. Failures are logged, not fatal to the import.

### Bulk Import Flow

1. Command center creates a receiving job and PUTs 512 KiB ZIP chunks (or Bruno/curl POSTs the whole ZIP to `/workouts/import/bulk`, which returns **202**)
2. Complete (or adapter accept) queues the job; `ImportJobWorker` runs `StravaBulkImportOrchestrator`
3. Orchestrator uses `BulkImportService` for ZIP safety, `activities.csv`, non-run skip, per-file Workout intake, and Strava media
4. Intake outcomes map to job counters (`created` / `updated` / `skipped` / error). Poll `GET /workouts/import/jobs/{id}` until `completed` or `failed`

### Tempo Export Restore Flow

1. Command center creates a receiving job with `kind: tempo_export` and PUTs 512 KiB ZIP chunks (or Bruno/curl POSTs the whole ZIP to `/workouts/import/export`, which returns **202**)
2. Complete (or adapter accept) queues the job; `ImportJobWorker` runs `ImportService.ImportExportAsync`
3. Restore walks settings, shoes, workouts, routes, splits, time series, media, and raw files with progress/cancel between items; the worker then clears backfill completion records and runs every backfill to rebuild derived data such as Best efforts
4. Nested `statistics` / `warnings` / `errorMessages` land on the job document. Same global one-active-job mutex as Strava bulk

## Authentication

- One policy scheme (`TempoAuthentication`) forwards `Authorization: Bearer tmp_…` to the API-key handler and everything else to JWT bearer. Browser sessions carry the JWT in the httpOnly `authToken` cookie.
- Registration only available when no users exist (single-user deployment)
- Password hashing using BCrypt; `PasswordPolicy` requires 16+ characters
- All `/workouts`, `/settings`, `/shoes`, and `/stats` routes require authentication; `/health`, `/ready`, and `/version` are public
- `JwtSessionOnly` policy (requires a `jti` claim, which API-key principals lack) guards API-key management and password change
- **First-run onboarding**: `User.OnboardingCompleted` (new registrations `false`; migration backfills existing users `true`). `GET /auth/me` exposes `onboardingCompleted`; `POST /auth/onboarding/complete` sets it `true` only (idempotent). `AuthGuard` sends app routes to `/onboarding` until complete.

Details: [API Reference](api-reference.md#authentication), [Security](../deployment/security.md).

## Where to change X

| Task | Touch | Then |
|------|-------|------|
| Add or change an endpoint | Handler + route metadata in the matching `api/Endpoints/*Endpoints.cs` (new file: add a `Map…Endpoints` call in `api/Program.cs`); client function in `frontend/lib/api.ts` | Regenerate `docs/openapi.json` ([command](testing.md#openapi-drift-check)); add an integration test in `api/Tempo.Api.Tests/IntegrationTests/` |
| Add a service | `api/Services/`; register in `api/Program.cs` (`AddScoped` unless it holds state across requests) | Unit test in `api/Tempo.Api.Tests/Services/` |
| Change the schema | Model in `api/Models/`, mapping in `api/Data/TempoDbContext.cs`, then `dotnet ef migrations add <Name>` from `api/` | Migration applies on next start; update [Database Schema](database.md) |
| Add an import format | New decode adapter → `DecodedWorkout` → `WorkoutIntake.PersistAsync` | Never add a second persist pipeline in HTTP or bulk code |
| Change derived Workout data | `WorkoutDerivations` (callers pass what changed, not which steps to run) | If existing Workouts need it, add a backfill (next row) |
| Add a startup backfill | A `*BackfillService` with `Task<int> RunAsync(CancellationToken)` returning rows processed (0 = done); `AddScoped` it in `Program.cs`; append a `BackfillDefinition` to `StartupBackfills.All` | Bump that entry's `Version` when its logic changes so installs rerun it; test in `api/Tempo.Api.Tests/Services/` |
| Add a frontend page | `frontend/app/<route>/page.tsx` wrapped in `<AuthGuard>` and `PageShell`; nav link in `frontend/components/Navbar.tsx` | `npm run lint` |
| Add a server read or a mutation (frontend) | Read: a builder in `frontend/lib/queries.ts`, used as `useQuery(workoutQueries.detail(id))`. Mutation: call `invalidateAfter(queryClient, change)` with the `DataChange` that happened, never `invalidateQueries` with hand-picked keys | Add or update the change's row in `frontend/lib/queries.test.ts`; `npm test` |
| Add a config key | Read it in `api/Program.cs` (or the service); default in `api/appsettings.json`; env var in `docker-compose.yml` / `docker-compose.prod.yml` | Document in [Configuration](../getting-started/configuration.md) |
| Add a domain term | `CONTEXT.md` | Use the term in code and docs |

## Key File Locations

- **API endpoints**: `api/Endpoints/*.cs`
- **Startup, DI, middleware**: `api/Program.cs`
- **Auth**: `api/Authentication/`, `api/Authorization/`, `api/Endpoints/AuthEndpoints.cs`
- **Host commands**: `api/Commands/`
- **Models**: `api/Models/*.cs`
- **Database context / migrations**: `api/Data/TempoDbContext.cs`, `api/Migrations/`
- **Workout intake / geometry / derivations**: `api/Services/WorkoutIntake.cs`, `api/Services/HealthKitWorkoutDecoder.cs`, `api/Services/TrackGeometry.cs`, `api/Services/TrackPointRehydration.cs`, `api/Services/WorkoutDerivations.cs`
- **Import jobs**: `api/Services/ImportJobService.cs`, `api/Services/ImportJobWorker.cs`, `api/Services/StravaBulkImportOrchestrator.cs`, `api/Services/BulkImportService.cs`, `api/Services/ImportService.cs`
- **Backfills**: `api/Services/StartupBackfills.cs`, `api/Services/BackfillPass.cs`, `api/Services/*BackfillService.cs`
- **OpenAPI config**: `api/OpenApi/`
- **Frontend API client**: `frontend/lib/api.ts` (includes `getWorkoutTimeSeries`, `completeOnboarding`)
- **Auth gate**: `frontend/components/AuthGuard.tsx`, `frontend/contexts/AuthContext.tsx`
- **Onboarding**: `frontend/app/onboarding/page.tsx`, `frontend/components/onboarding/`
- **Appearance**: `frontend/lib/appearance.ts`
- **Highlight**: `frontend/lib/workoutHighlight.ts`
- **Frontend pages**: `frontend/app/*/page.tsx` — Workout overview composer is `frontend/app/dashboard/[id]/page.tsx`
- **Frontend components**: `frontend/components/*.tsx`; UI kit in `frontend/components/ui/`

## Next Steps

- [Set up your development environment](setup.md)
- [Explore the API reference](api-reference.md)
- [Understand the database schema](database.md)
