# Architecture Overview

Tempo is a self-hosted running tracker built as a full-stack application with a clear separation between frontend and backend.

## System Components

### Frontend

- **Framework**: Next.js 16 with React 19
- **Language**: TypeScript
- **Styling**: Tailwind CSS with semantic identity tokens (`canvas`, `ink`, `muted`, `volt`, and related). Geist is the UI typeface. Do not use tutorial `blue-600` as the brand accent.
- **UI kit**: Hand-rolled primitives in `frontend/components/ui/` — `PageShell` (density `control` | `overview`), `Card`, `Button`, `Dialog`, `EmptyState`, `Tabs`
- **Appearance**: Dark-first. Tailwind `class` strategy on `<html>` (`dark`). Preference is `system` | `dark` | `light` in `localStorage` (`tempo-appearance`); not UserSettings. See `frontend/lib/appearance.ts`.
- **Icons**: Tabler Icons (`@tabler/icons-react`) for UI icons. Brand marks live in `frontend/public/` (`tempo-mark-volt.png`, `tempo-mark-ink.png`).
- **State Management**: TanStack Query for server state
- **Maps**: Leaflet/React-Leaflet. Carto Dark Matter in Dark appearance, Voyager in Light; polyline and Highlight from tokens; OSM + CARTO attribution (no OSM-only fallback).
- **Charts**: Recharts; series colors from identity tokens. Workout overview charts HR, pace, and elevation via `getWorkoutTimeSeries` in `frontend/lib/api.ts` (pages until complete or 20,000 samples).
- **Highlight**: Shared overview focus (`splitIdx` and/or `elapsedSeconds`) in `frontend/lib/workoutHighlight.ts` — map, splits, and charts follow together.

### Backend

- **Framework**: ASP.NET Core 9 Minimal APIs
- **Language**: C#
- **Database**: PostgreSQL 16 with JSONB fields for raw workout data
- **ORM**: Entity Framework Core
- **Logging**: Serilog

### Database

- **PostgreSQL 16** with JSONB support
- Hybrid storage: Core stats in dedicated columns, raw data in JSONB
- Automatic migrations on startup

## Key Architectural Patterns

### 1. Minimal APIs

Endpoints are organized in static extension methods that extend `WebApplication`:

- `MapWorkoutsEndpoints` - Workout management endpoints
- `MapSettingsEndpoints` - Settings and configuration endpoints
- `MapShoesEndpoints` - Shoe management endpoints
- `MapAuthEndpoints` - Authentication endpoints
- `MapVersionEndpoints` - Version information endpoints

Each extension method:
- Creates a `MapGroup` for route organization (e.g., `/workouts/*`, `/settings/*`, `/auth/*`)
- Uses `WithTags()` for Swagger documentation grouping
- Defines endpoints as private static methods with XML documentation comments
- Uses dependency injection from the service container
- Maps private methods to HTTP verbs using `MapGet`, `MapPost`, `MapPut`, `MapPatch`, `MapDelete`
- All endpoint methods return `Task<IResult>` and use `Results.*` helper methods

### 2. Service Layer

- `GpxParserService` / `FitParserService` — decode adapters: `TrackPoint`s, raw JSON, optional device summary (and GPX name). They do not expose `CalculateSplits` and do not hand FIT `RecordMesg` to callers. The FIT SDK is compiled from `api/Libraries/FitSDK/` (not a NuGet package).
- `StravaCsvParserService` — parses Strava export CSV metadata for bulk ZIP import.
- `TrackGeometry` — in-process: `TrackPoint`s in; elevation gain, `WorkoutRoute`, `WorkoutSplit`s, `WorkoutTimeSeries` out. No `DbContext`.
- `WorkoutIntake` — one persist pipeline (parse, geometry, duplicate policy, default shoe, weather, relative effort, incremental best efforts). HTTP import is a thin adapter. Bulk calls intake per activity file.
- `TrackPointRehydration` — stored Workout fields → `TrackPoint`s for crop and split recalc.

All services are registered as `Scoped` in `Program.cs` except for configuration objects (`MediaStorageConfig`, `ElevationCalculationConfig`) which are `Singleton`.

### 3. Hybrid Data Storage

- **Core Stats**: Stored in dedicated database columns for efficient querying
- **Raw Data**: Stored as JSONB in PostgreSQL for flexibility
- Allows querying both structured and unstructured data efficiently

### 4. Media Storage

- Files stored on filesystem in `media/` directory
- Organized by workout GUID: `media/{workoutId}/filename.ext`
- Metadata stored in database for quick access

### 5. Automatic Migrations

Database migrations run automatically on API startup. The `DatabaseMigrationHelper` implements idempotent migrations that:
- Create the `__EFMigrationsHistory` table if missing
- Detect existing tables and columns by querying `information_schema`
- Mark migrations as applied if their corresponding tables/columns already exist
- Use hardcoded mappings to associate database objects with migration IDs

This ensures migrations can be safely applied even when database state doesn't match migration history.

### 6. Logging

- Serilog configured for structured logging
- Console output in development
- Request logging enabled via `UseSerilogRequestLogging()`

## Data Model

### Core Entities

- **Workout**: Core entity with stats (distance, pace, elevation, heart rate, etc.) and JSONB fields for raw GPX/FIT/Strava data
- **TrackPoint**: In-memory sample on a path (not a table). Geometry and parsers use it; see `CONTEXT.md`.
- **WorkoutRoute**: One-to-one relationship storing GeoJSON LineString coordinates
- **WorkoutSplit**: One-to-many relationship for distance-based splits (km or mile)
- **WorkoutTimeSeries**: One-to-many relationship for time-series data (heart rate, pace, elevation over time)
- **WorkoutMedia**: One-to-many relationship for photos/videos attached to workouts
- **Shoe**: Running shoe entity for tracking shoe mileage and assignments
- **User**: User accounts for authentication
- **UserSettings**: Single-row table for user preferences (heart rate zones, unit preferences, default shoe). Command-center appearance is not UserSettings.

## Data Flow

### Workout Import Flow

1. File uploaded to `POST /workouts/import`
2. HTTP maps `IFormFile` to `WorkoutIntake` (stream + filename)
3. Parser decode: GPX or FIT → `TrackPoint`s, raw JSON, optional device summary
4. `TrackGeometry.Derive` builds elevation, route, splits, and time series (device summary wins for distance/duration when present)
5. Duplicate policy: same key (`StartedAt`, `DistanceM`, `DurationS`); incomplete raw JSON/bytes can `updated`; complete duplicates `skipped`
6. Weather, default shoe, relative effort, incremental best efforts
7. Workout persisted with JSONB raw data and `WorkoutRoute`

### Bulk Import Flow

1. Command center creates a receiving job and PUTs 512 KiB ZIP chunks (or Bruno/curl POSTs the whole ZIP to `/workouts/import/bulk`)
2. Complete (or adapter accept) queues the job; `ImportJobWorker` runs `StravaBulkImportOrchestrator`
3. Orchestrator uses `BulkImportService` for ZIP safety, `activities.csv`, non-run skip, per-file Workout intake, and Strava media
4. Intake outcomes map to job counters (`created` / `updated` / `skipped` / error). Poll GET until `completed` or `failed`

## Authentication

- JWT-based authentication with httpOnly cookies
- Registration only available when no users exist (single-user deployment)
- Password hashing using BCrypt
- All workout and settings endpoints require authentication (except `/health` and `/version`)

## Database Indexing

The `TempoDbContext` configures several important indexes:
- **Workout indexes**: `StartedAt`, composite index on `(StartedAt, DistanceM, DurationS)` for duplicate detection
- **JSONB GIN indexes**: On `RawGpxData`, `RawFitData`, `RawStravaData`, and `Weather` fields
- **WorkoutSplit**: Composite index on `(WorkoutId, Idx)`
- **WorkoutTimeSeries**: Composite index on `(WorkoutId, ElapsedSeconds)`
- **User**: Unique index on `Username`

## Key File Locations

- **API Endpoints**: `api/Endpoints/*.cs`
- **Models**: `api/Models/*.cs`
- **Workout intake / geometry**: `api/Services/WorkoutIntake.cs`, `api/Services/TrackGeometry.cs`, `api/Services/TrackPointRehydration.cs`
- **Services**: `api/Services/*.cs`
- **Database Context**: `api/Data/TempoDbContext.cs`
- **Frontend API Client**: `frontend/lib/api.ts` (includes `getWorkoutTimeSeries`)
- **Appearance**: `frontend/lib/appearance.ts`
- **Highlight**: `frontend/lib/workoutHighlight.ts`
- **Frontend Pages**: `frontend/app/*/page.tsx` — Workout overview composer is `frontend/app/dashboard/[id]/page.tsx`
- **Frontend Components**: `frontend/components/*.tsx`
- **UI kit**: `frontend/components/ui/`

## Next Steps

- [Set up your development environment](setup.md)
- [Explore the API reference](api-reference.md)
- [Understand the database schema](database.md)

