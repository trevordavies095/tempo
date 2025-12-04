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
- `DurationS` (double)
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
- `Weather` (JSONB, nullable) - Weather data from Open-Meteo API

**Indexes:**
- `StartedAt`
- Composite index on `(StartedAt, DistanceM, DurationS)` for duplicate detection
- `Source`
- `RunType`
- `ShoeId` - Foreign key index for efficient shoe queries
- GIN indexes on JSONB fields: `RawGpxData`, `RawFitData`, `RawStravaData`, `Weather`

### WorkoutRoute

One-to-one relationship storing route coordinates as GeoJSON LineString.

**Columns:**
- `WorkoutId` (Guid, Primary Key, Foreign Key to Workout)
- `Coordinates` (JSONB) - GeoJSON LineString format

**Relationship:**
- One-to-one with `Workout`

### WorkoutSplit

Distance-based splits (km or mile).

**Columns:**
- `Id` (Guid, Primary Key)
- `WorkoutId` (Guid, Foreign Key to Workout)
- `Idx` (int) - Split index (0-based)
- `DistanceM` (double)
- `DurationS` (double)
- `ElevationGainM` (double, nullable)
- `ElevationLossM` (double, nullable)
- `AvgHeartRateBpm` (int, nullable)

**Indexes:**
- Composite index on `(WorkoutId, Idx)` for efficient split queries

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

## Relationships

```
Workout (1) ── (1) WorkoutRoute
Workout (1) ── (N) WorkoutSplit
Workout (1) ── (N) WorkoutTimeSeries
Workout (1) ── (N) WorkoutMedia
Workout (N) ── (1) Shoe (via ShoeId, nullable)
UserSettings (1) ── (1) Shoe (via DefaultShoeId, nullable)
```

## Migrations

Database migrations are managed using Entity Framework Core migrations:

### Creating Migrations

```bash
cd api
dotnet ef migrations add MigrationName
```

### Applying Migrations

Migrations run automatically on API startup. To manually apply:

```bash
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
- **WorkoutSplit (WorkoutId, Idx)** - Efficient split queries
- **WorkoutTimeSeries (WorkoutId, ElapsedSeconds)** - Time-series queries

### JSONB Indexes

GIN indexes on JSONB fields enable efficient JSON queries:
- `RawGpxData`
- `RawFitData`
- `RawStravaData`
- `Weather`

## Data Storage Patterns

### Hybrid Storage

- **Core Stats**: Dedicated columns for efficient querying and indexing
- **Raw Data**: JSONB fields for flexibility and future extensibility

### Media Storage

Media files are stored on the filesystem, not in the database:
- Path: `media/{workoutId}/filename.ext`
- Database stores metadata only (filename, MIME type, size, upload date)

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

