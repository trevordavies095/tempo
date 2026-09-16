# Database Schema

Complete reference for the Tempo database schema.

## Overview

Tempo uses PostgreSQL 16 with JSONB fields for flexible storage of raw workout data. The schema follows a hybrid approach: core statistics in dedicated columns, raw file data in JSONB.

## Entities

### Workout

Core workout entity with statistics and metadata.

**Columns:**
- `Id` (Guid, Primary Key)
- `ActivityName` (string, nullable)
- `StartedAt` (DateTime)
- `DurationS` (int) - Total elapsed time (wall clock, including pauses)
- `TimerTimeS` (int, nullable) - FIT timer time (`total_timer_time`); Garmin’s primary duration when present
- `DistanceM` (double)
- `ElevationGainM` (double, nullable)
- `ElevationLossM` (double, nullable)
- `MinElevationM` (double, nullable)
- `MaxElevationM` (double, nullable)
- `AvgHeartRateBpm` (int, nullable)
- `MaxHeartRateBpm` (int, nullable)
- `RelativeEffort` (double, nullable)
- `Source` (string, nullable) - Device or source identifier
- `RunType` (string, nullable)
- `Device` (string, nullable)
- `ShoeId` (Guid, nullable, Foreign Key to Shoe) - Assigned running shoe
- `RawGpxData` (JSONB, nullable) - Raw GPX XML data
- `RawFitData` (JSONB, nullable) - Raw FIT file data
- `RawStravaData` (JSONB, nullable) - Raw Strava CSV data
- `RawHealthKitData` (JSONB, nullable) - Schema-versioned HealthKit import payload from tempo-ios
- `HealthKitUuid` (Guid, nullable, unique) - HKWorkout UUID for HealthKit import idempotency; null for file imports
- `Weather` (JSONB, nullable) - Weather data from Open-Meteo API

**Indexes:**
- `StartedAt`
- Composite index on `(StartedAt, DistanceM, DurationS)` for duplicate detection
- Unique index on `HealthKitUuid` (multiple NULLs allowed)
- `Source`
- `RunType`
- `ShoeId` - Foreign key index for efficient shoe queries
- GIN indexes on JSONB fields: `RawGpxData`, `RawFitData`, `RawStravaData`, `RawHealthKitData`, `Weather`

### Workout external identity

A `(source, externalId)` row linking a Workout to one upstream system for intake idempotency. Not `Workout.Source` (ingest provenance such as `gpx_import` / `fit_import` / `healthkit`). HealthKit remains on `Workout.HealthKitUuid` until a later move; this table is not written with `healthkit` rows yet.

**Columns:**
- `Id` (Guid, Primary Key)
- `WorkoutId` (Guid, Foreign Key to Workout, cascade delete)
- `Source` (string, max 50) — upstream that minted the id (e.g. `intervals_icu`)
- `ExternalId` (string, max 128) — opaque token, trimmed, case-sensitive
- `CreatedAt` (DateTime UTC) — when the row was attached; not a sync cursor

**Indexes:**
- Unique composite on `(Source, ExternalId)` — one Workout owns a given upstream id
- Unique composite on `(WorkoutId, Source)` — one id per upstream per Workout

**Relationship:**
- Many-to-one with `Workout` (cascade delete)

### WorkoutRoute

One-to-one relationship storing route coordinates as GeoJSON LineString.

**Columns:**
- `WorkoutId` (Guid, Primary Key, Foreign Key to Workout)
- `Coordinates` (JSONB) - GeoJSON LineString format

**Relationship:**
- One-to-one with `Workout`

### WorkoutSplit

Segment rows for a Workout (`Kind`: `distance` or `device_lap`). Distance rows are km/mile per UserSettings; device_lap rows are device ranges when present (FIT import copies LapMesg into `device_lap` when 2+ kept; startup `DeviceLapBackfillWorker` fills older FIT library rows from `RawFitData.laps` or reparsed bytes, stamped with `lapsBackfill`).

**Columns:**
- `Id` (Guid, Primary Key)
- `WorkoutId` (Guid, Foreign Key to Workout)
- `Kind` (string, max 32) - `distance` | `device_lap` (check constraint; default `distance`)
- `Idx` (int) - Index within kind (0-based; same Idx may exist on both kinds)
- `DistanceM` (double)
- `DurationS` (int)
- `PaceS` (double) - seconds per kilometer
- `StartElapsedS` (int) - wall elapsed from Workout.StartedAt at segment start
- `EndElapsedS` (int) - wall elapsed at segment end; windows are `[Start, End)` (last inclusive)
- `StartDistanceM` (double, nullable) - device-distance cursor at start when DistM stream exists; null for Haversine distance splits
- `AvgHeartRateBpm` (byte, nullable) - time-weighted average from WorkoutTimeSeries; null when the split has no HR samples

**Indexes:**
- Unique composite index on `(WorkoutId, Kind, Idx)`

**API / write paths:**
- `GET /workouts/{id}` and crop responses return the **display list**: all `device_lap` rows if any exist, otherwise all `distance` rows (ordered by `Idx`)
- List `splitsCount` matches that display-list rule
- Unit-preference **recalc** deletes and rewrites `distance` only; `device_lap` rows are preserved
- **Crop** deletes all kinds for the workout, then writes new `distance` rows for the remaining slice
- Tempo **export** dumps every split row (both kinds) with the new columns; restore copies rows and does not rebuild splits via track geometry

**Relationship:**
- Many-to-one with `Workout`

### WorkoutTimeSeries

Time-series data (heart rate, pace, elevation over time).

**Columns:**
- `Id` (Guid, Primary Key)
- `WorkoutId` (Guid, Foreign Key to Workout)
- `ElapsedSeconds` (double)
- `HeartRateBpm` (int, nullable)
- `PaceSecondsPerKm` (double, nullable)
- `ElevationM` (double, nullable)

**Indexes:**
- Composite index on `(WorkoutId, ElapsedSeconds)` for time-series queries

**Relationship:**
- Many-to-one with `Workout`

### WorkoutMedia

Photos and videos attached to workouts.

**Columns:**
- `Id` (Guid, Primary Key)
- `WorkoutId` (Guid, Foreign Key to Workout)
- `FileName` (string)
- `MimeType` (string)
- `FileSizeBytes` (long)
- `UploadedAt` (DateTime)

**Relationship:**
- Many-to-one with `Workout`

### User

User accounts for authentication.

**Columns:**
- `Id` (Guid, Primary Key)
- `Username` (string, unique)
- `PasswordHash` (string) - BCrypt hashed password
- `CreatedAt` (DateTime)
- `OnboardingCompleted` (bool) - `true` after first-run onboarding finishes. New registrations default to `false`. The EF migration that adds this column backfills existing user rows to `true` so upgrades are not forced through the wizard.

**Indexes:**
- Unique index on `Username`

### UserSettings

Single-row table for user preferences.

**Columns:**
- `Id` (Guid, Primary Key) - Always the same GUID (single-row pattern)
- `HeartRateZoneMethod` (string) - "AgeBased", "Karvonen", or "Custom"
- `Age` (int, nullable)
- `RestingHeartRate` (int, nullable)
- `CustomZones` (JSONB, nullable) - Custom zone boundaries
- `UnitPreference` (string) - "metric" or "imperial"
- `DefaultShoeId` (Guid, nullable, Foreign Key to Shoe) - Default shoe for automatic assignment to new workouts

**Indexes:**
- Unique index on `Id` (ensures single row)

**Relationship:**
- Many-to-one with `Shoe` (via `DefaultShoeId`)

### Shoe

Running shoe entity for tracking shoe mileage.

**Columns:**
- `Id` (Guid, Primary Key)
- `Brand` (string, max 100 chars) - Shoe manufacturer
- `Model` (string, max 100 chars) - Shoe model name
- `InitialMileageM` (double, nullable) - Initial mileage in meters when shoe was added
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)

**Indexes:**
- No specific indexes (relatively small table, queries typically by Id)

**Relationship:**
- One-to-many with `Workout` (via `Workout.ShoeId`)
- One-to-many with `UserSettings` (via `UserSettings.DefaultShoeId`)

### ImportJob

Background import for Strava bulk ZIP (`strava_bulk`) or Tempo export restore (`tempo_export`). At most one row may be in `receiving`, `queued`, or `running` at a time.

**Columns:**
- `Id` (Guid, Primary Key)
- `Kind` (string) - `strava_bulk` or `tempo_export`
- `Status` (string) - `receiving` | `queued` | `running` | `completed` | `failed`
- `Filename` (string)
- `ByteSize` (long) - Declared ZIP size at create
- `BytesReceived` (long) - Bytes assembled from chunks
- `Processed` / `Total` (int) - Progress while running
- `Successful` / `Skipped` / `Updated` / `Errors` (int) - Flat counters (Tempo rollups nested stats into these)
- `ErrorDetailsJson` (string, nullable) - Strava per-file `{ filename, error }` array
- `ResultJson` (string, nullable) - Tempo `{ statistics, warnings, errors }` payload
- `ErrorMessage` (string, nullable) - Job-level message (`cancelled`, `interrupted`, bad archive, etc.)
- `UnitPreference` (string, nullable) - Optional for `strava_bulk`; applied when the worker starts
- `ArchivePath` (string, nullable) - Staged ZIP under `media/imports/{jobId}/`
- `CancelRequested` (bool)
- `LastChunkAt` (DateTime, nullable) - Used for 15-minute stale `receiving` replacement
- `CreatedAt` / `StartedAt` / `FinishedAt` (DateTime)

**Notes:**
- Staged archive directory is deleted on terminal status or startup interrupt; the row remains for a late GET
- Cancel and interrupt end as `failed` with a distinct `ErrorMessage`

## Relationships

```
Workout (1) ── (1) WorkoutRoute
Workout (1) ── (N) WorkoutSplit
Workout (1) ── (N) WorkoutTimeSeries
Workout (1) ── (N) WorkoutMedia
Workout (N) ── (1) Shoe (via ShoeId, nullable)
UserSettings (1) ── (1) Shoe (via DefaultShoeId, nullable)
ImportJob (standalone; no FK to Workout)
```

## Migrations

Database migrations are managed using Entity Framework Core migrations:

Restore local .NET tools at the repo root first (`dotnet tool restore`) so `dotnet-ef` matches EF Core 10.

### Creating Migrations

```bash
dotnet tool restore
cd api
dotnet ef migrations add MigrationName
```

### Applying Migrations

Migrations run automatically on API startup. To manually apply:

```bash
dotnet tool restore
cd api
dotnet ef database update
```

### Migration History

The `__EFMigrationsHistory` table tracks applied migrations. The `DatabaseMigrationHelper` ensures idempotent migrations by:
- Creating the history table if missing
- Detecting existing tables/columns
- Marking migrations as applied if objects already exist

## Indexing Strategy

### Performance Indexes

- **Workout.StartedAt** - Fast date range queries
- **Workout composite (StartedAt, DistanceM, DurationS)** - Duplicate detection
- **Workout.HealthKitUuid** (unique) - HealthKit import idempotency
- **WorkoutExternalIdentities** unique `(Source, ExternalId)` and unique `(WorkoutId, Source)`
- **WorkoutSplit (WorkoutId, Idx)** - Efficient split queries
- **WorkoutTimeSeries (WorkoutId, ElapsedSeconds)** - Time-series queries

### JSONB Indexes

GIN indexes on JSONB fields enable efficient JSON queries:
- `RawGpxData`
- `RawFitData`
- `RawStravaData`
- `RawHealthKitData`
- `Weather`

## Data Storage Patterns

### Hybrid Storage

- **Core Stats**: Dedicated columns for efficient querying and indexing
- **Raw Data**: JSONB fields for flexibility and future extensibility

### Media Storage

Media files are stored on the filesystem, not in the database:
- Workout media: `media/{workoutId}/filename.ext`
- Import-job staging: `media/imports/{jobId}/archive.zip` (deleted when the job finishes or is interrupted)
- Database stores workout media metadata only (filename, MIME type, size, upload date)

## Backup and Restore

### Database Backup

```bash
docker exec tempo-postgres pg_dump -U postgres tempo > backup.sql
```

### Database Restore

```bash
docker exec -i tempo-postgres psql -U postgres tempo < backup.sql
```

### Complete Backup

For a complete backup, save both:
1. Database dump (as shown above)
2. Media directory (`./media`)

## Next Steps

- [Set up your development environment](setup.md)
- [Explore the API reference](api-reference.md)
- [Review the architecture documentation](architecture.md)

