# Architecture Overview

Tempo is a self-hosted running tracker built as a full-stack application with a clear separation between frontend and backend.

## System Components

### Frontend

- **Framework**: Next.js 16 with React 19
- **Language**: TypeScript
- **Styling**: Tailwind CSS
- **State Management**: TanStack Query for server state
- **Maps**: Leaflet/React-Leaflet for route visualization
- **Charts**: Recharts for analytics

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

Parser services handle file format conversion:
- `GpxParserService` - Parses GPX XML files
- `FitParserService` - Parses binary FIT files (uses FIT SDK)
- `StravaCsvParserService` - Parses Strava export CSV files

The FIT SDK is included as source files in `api/Libraries/FitSDK/` and compiled directly into the project (not a NuGet package).

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
- **WorkoutRoute**: One-to-one relationship storing GeoJSON LineString coordinates
- **WorkoutSplit**: One-to-many relationship for distance-based splits (km or mile)
- **WorkoutTimeSeries**: One-to-many relationship for time-series data (heart rate, pace, elevation over time)
- **WorkoutMedia**: One-to-many relationship for photos/videos attached to workouts
- **Shoe**: Running shoe entity for tracking shoe mileage and assignments
- **User**: User accounts for authentication
- **UserSettings**: Single-row table for user preferences (heart rate zones, unit preferences, default shoe)

## Data Flow

### Workout Import Flow

1. File uploaded to `POST /workouts/import`
2. File type detected (GPX, FIT, or CSV)
3. Appropriate parser service extracts data
4. Weather data fetched from Open-Meteo API based on workout location/time
5. Elevation data smoothed using configurable thresholds
6. Splits calculated based on unit preference (1km for metric, 1 mile for imperial)
7. Default shoe assigned (if configured in UserSettings)
8. Workout saved to database with raw data in JSONB fields
9. Route stored as GeoJSON LineString in `WorkoutRoute` table

### Bulk Import Flow

1. ZIP file uploaded to `POST /workouts/import/bulk`
2. ZIP extracted and validated
3. `activities.csv` parsed for metadata
4. Workout files processed from `activities/` folder
5. Only "Run" activities imported
6. Duplicate detection using `StartedAt`, `DistanceM`, and `DurationS`
7. Default shoe assigned to each workout (if configured in UserSettings)
8. All workouts saved to database

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
- **Services**: `api/Services/*.cs`
- **Database Context**: `api/Data/TempoDbContext.cs`
- **Frontend API Client**: `frontend/lib/api.ts`
- **Frontend Pages**: `frontend/app/*/page.tsx`
- **Frontend Components**: `frontend/components/*.tsx`

## Next Steps

- [Set up your development environment](setup.md)
- [Explore the API reference](api-reference.md)
- [Understand the database schema](database.md)

