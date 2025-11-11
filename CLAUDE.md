CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Tempo is a self-hostable, privacy-first running tracker. It allows users to import GPX workout files, view analytics, and track running data locally without social features or subscriptions. The project uses a Next.js frontend and ASP.NET Core backend with PostgreSQL.

## Development Commands

### Backend (ASP.NET Core .NET 9)
- **Run**: `cd api && dotnet run` (API at http://localhost:5001)
- **Watch mode**: `cd api && dotnet watch run` (auto-reload on changes)
- **Database migrations**: `cd api && dotnet ef database update`
- **Create migration**: `cd api && dotnet ef migrations add <MigrationName>`
- **Swagger UI**: Available at http://localhost:5001/swagger in development only

### Frontend (Next.js 16)
- **Development**: `cd frontend && npm run dev` (runs at http://localhost:3000)
- **Build**: `cd frontend && npm run build`
- **Production**: `cd frontend && npm run start`
- **Lint**: `cd frontend && npm run lint`
- **Install dependencies**: `cd frontend && npm install`

### Database
- **Start PostgreSQL**: `docker-compose up -d postgres`
- **Stop**: `docker-compose down`
- **Connection string**: `Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres` (configured in `api/appsettings.json`)

## Architecture

### High-Level Structure
- **Frontend** (`frontend/`): Next.js 16 with App Router, React 19.2.0, TypeScript, Tailwind CSS v4
- **Backend** (`api/`): ASP.NET Core .NET 9 Minimal APIs with EF Core
- **Database**: PostgreSQL 16 (via Docker Compose)

### Backend Architecture

**Endpoint Pattern**: Endpoints are organized as extension methods on `WebApplication` in the `Endpoints/` directory. The main endpoint group is `WorkoutsEndpoints` which maps to `/workouts`.

**Data Layer**:
- `TempoDbContext`: EF Core DbContext with three main entities
- **Workout**: Core workout data (distance, duration, pace, elevation, notes, run type, weather JSONB)
- **WorkoutRoute**: One-to-one relationship storing route as GeoJSON (LineString)
- **WorkoutSplit**: One-to-many relationship storing calculated splits (1km segments by default)
- Indexes: `StartedAt` on Workout, composite index on `(StartedAt, DistanceM, DurationS)` for duplicate detection, `(WorkoutId, Idx)` on splits

**Services**:
- `GpxParserService`: Parses GPX XML files, calculates distance using Haversine formula, computes elevation gain, and generates splits. Returns `GpxParseResult` with track points, timing, and metrics. Handles GPX 1.1 namespace (`http://www.topografix.com/GPX/1/1`).
- `FitParserService`: Parses Garmin FIT files (including `.fit.gz` compressed) using the embedded FIT SDK (from `FitSDKRelease_21.171.00/cs/`). The FIT SDK C# source files are compiled directly into the project via `<Compile Include>` directives in `Tempo.Api.csproj`. Converts FIT coordinate format (semicircles) to degrees.
- `StravaCsvParserService`: Parses Strava export CSV files (`activities.csv`) to extract activity metadata and match files for bulk import. Filters for "Run" activity type only.

**Key Implementation Details**:
- GPX parsing uses XML namespaces (`http://www.topografix.com/GPX/1/1`). Requires at least 2 track points. Extracts lat/lon, elevation (optional), and time (optional) from `<trkpt>` nodes.
- Distance calculation: Haversine formula with Earth radius 6,371,000m, calculated between consecutive track points
- Splits: Default 1000m (1km) segments, calculated from accumulated distance between track points using `GpxParserService.CalculateSplits()`
- Routes stored as GeoJSON LineString in PostgreSQL JSONB column (stored as JSON string, parsed on retrieval using `JsonSerializer.Deserialize<object>()`)
- Weather data stored as JSONB string (Open-Meteo integration planned per PRD)
- Duplicate detection: Exact match on `StartedAt`, tolerance of 1.0m for `DistanceM`, and 1s for `DurationS`. Uses database query before creating workout entity.
- DateTime handling: All `StartedAt` values are normalized to UTC in both import endpoints using defensive conversion (handles UTC, Local, and Unspecified DateTimeKind)
- Bulk import: Extracts ZIP to temp directory (using `Path.GetTempPath()` + GUID), processes all files sequentially, collects entities in lists, batch inserts with single `SaveChangesAsync()`, then cleans up temp directory in `finally` block

**API Endpoints** (in `WorkoutsEndpoints`):
- `POST /workouts/import` - Single GPX file import (multipart/form-data, file field named "file"). Validates `.gpx` extension. Returns workout summary with ID and metrics.
- `POST /workouts/import/bulk` - Bulk import from Strava ZIP export (requires `activities.csv` in root and GPX/.fit.gz files). Supports duplicate detection: exact match on `StartedAt`, tolerance of 1.0m for `DistanceM`, and 1s for `DurationS`. Processes files sequentially, collects all workouts/routes/splits, then batch inserts with single `SaveChangesAsync()`. Combines CSV metadata (ActivityName, ActivityDescription, ActivityPrivateNote) into Notes field. Returns summary with `totalProcessed`, `successful`, `skipped`, `errors`, and `errorDetails` array.
- `GET /workouts` - List workouts with pagination and filtering (query params: page, pageSize, startDate, endDate, minDistanceM, maxDistanceM). Default pageSize is 20, max is 100. Returns 404 if page exceeds total pages. Uses `.Include()` to eager load Route and Splits for counting, then `.AsNoTracking()` after pagination for read-only performance.
- `GET /workouts/{id}` - Get workout details including route GeoJSON (parsed from JSONB string) and splits (ordered by `Idx`). Returns 404 if not found. Includes weather JSON if available. Uses `.AsNoTracking()` for read-only query.
- `GET /health` - Health check endpoint (mapped in `Program.cs`)

**Configuration**:
- Connection string in `appsettings.json` (defaults to Docker Compose PostgreSQL)
- CORS configured for `http://localhost:3000` and `http://localhost:3001` (all methods, all headers)
- Serilog for structured logging (console output, configured via `appsettings.json`)
- Swagger enabled in development only (`if (app.Environment.IsDevelopment())`)
- Large file upload support: 500MB limit configured for bulk imports (Kestrel `MaxRequestBodySize` and `FormOptions.MultipartBodyLengthLimit`)
- Database initialization: `Program.cs` calls `db.Database.EnsureCreated()` as fallback, but migrations are preferred (see Important Notes)

### Frontend Architecture

**State Management**: TanStack Query (`@tanstack/react-query`) for server state and API calls.

**API Client**: `lib/api.ts` exports functions for API communication:
- `importGpxFile(file: File)` - POSTs FormData to `/workouts/import`, throws on errors
- `importBulkStravaExport(zipFile: File)` - POSTs ZIP file to `/workouts/import/bulk`, returns `BulkImportResponse` with success/error counts
- `getWorkouts(params?: WorkoutsListParams)` - GETs paginated workout list from `/workouts` with query params, throws on 404 or other errors
- `getWorkout(id: string)` - GETs workout details from `/workouts/{id}`, throws specific error for 404
Uses `NEXT_PUBLIC_API_URL` environment variable (defaults to `http://localhost:5001`). All functions use CORS mode and throw on HTTP errors.

**Components**: 
- `FileUpload`: Handles single GPX file upload UI
- `BulkImport`: Handles ZIP file bulk import UI
- `WorkoutMap`: Leaflet-based map component for visualizing workout routes (client-side only, dynamically imported to avoid SSR issues)
- `app/workouts/page.tsx`: Workout list page with pagination, filtering, and table view
- `app/workouts/[id]/page.tsx`: Workout detail page with stats, splits table, route map, and notes

**Utilities**: `lib/format.ts` provides formatting helpers for distance, duration, pace, dates, and date-time.

**Styling**: Tailwind CSS v4 with PostCSS. Dark mode support via `dark:` classes. Uses zinc/gray color palette.

### Data Flow

**Single GPX Import:**
1. User uploads GPX file via frontend `FileUpload` component
2. Frontend sends multipart/form-data POST to `/workouts/import`
3. Backend `WorkoutsEndpoints` receives file, validates `.gpx` extension
4. `GpxParserService.ParseGpx()` parses XML, extracts track points, calculates distance/elevation
5. Backend creates `Workout`, `WorkoutRoute` (GeoJSON LineString), and `WorkoutSplit` entities
6. EF Core saves to PostgreSQL via `TempoDbContext`
7. Response returns workout summary with ID and metrics

**Bulk Import:**
1. User uploads Strava ZIP export via `BulkImport` component
2. Frontend sends ZIP file to `/workouts/import/bulk`
3. Backend extracts ZIP to temp directory
4. Parses `activities.csv` using `StravaCsvParserService`, filters for "Run" activities
5. For each activity, parses GPX or FIT file, checks for duplicates in database
6. Batch creates all workouts, routes, and splits
7. Single `SaveChangesAsync()` call for all entities
8. Cleans up temp directory
9. Returns summary with counts and error details

### Database Migrations

Migrations are in `api/Migrations/`. The initial migration creates the three main tables with proper foreign keys and indexes. Use `dotnet ef database update` to apply migrations. 

**⚠️ Important**: `Program.cs` calls `db.Database.EnsureCreated()` as a fallback, which may conflict with migrations in production. Consider removing `EnsureCreated()` in favor of migrations-only approach for production deployments.

**FIT SDK Integration**: The FIT SDK C# source files are located in `api/Libraries/FitSDK/` (version 21.171.00) and are automatically compiled into the project via `<Compile Include>` directives in `Tempo.Api.csproj`. The SDK handles parsing Garmin FIT files including compressed `.fit.gz` files. Coordinate conversion from semicircles (FIT format unit) to degrees is handled in `FitParserService` using the conversion factor `180.0 / 2^31`. The SDK is embedded directly in the repository (not a NuGet package).

### Test Data

The `test_data/` directory contains sample files for development:
- `20251110.gpx` - Sample GPX file for testing single import
- `strava_export/` - Sample Strava export ZIP structure with `activities.csv` and associated GPX/FIT files in subdirectories (`routes/`, `activities/`, etc.)

## Implementation Status

### ✅ Implemented
- **Single GPX import**: `POST /workouts/import` endpoint fully functional
- **Bulk import**: `POST /workouts/import/bulk` - accepts Strava ZIP exports with `activities.csv` and processes GPX/.fit.gz files. Includes duplicate detection based on `(StartedAt, DistanceM, DurationS)`. Batch inserts workouts for performance.
- **Workout browsing**: `GET /workouts` (list with pagination/filtering) and `GET /workouts/{id}` (detail) endpoints functional
- **Frontend pages**: Workout list (`/workouts`) and detail (`/workouts/[id]`) pages fully implemented with TanStack Query
- **FIT file support**: Garmin FIT SDK (v21.171.00) embedded and compiled into backend for `.fit.gz` file parsing. Converts FIT coordinate format (semicircles) to degrees.
- **Map visualization**: `WorkoutMap` component using Leaflet and OpenStreetMap tiles. Dynamically imported to avoid SSR issues. Displays route as polyline with auto-fit bounds.

### ❌ Not Yet Implemented
- **Tests**: PRD mentions Vitest/Playwright for frontend and xUnit for backend, but no test infrastructure exists yet
- **Weather API**: Open-Meteo integration planned (per PRD). Weather field exists in database but is not populated.
- **Analytics endpoints**: `GET /analytics/summary` planned but not yet implemented (per PRD)
- **Workout editing**: `PATCH /workouts/{id}` endpoint planned but not implemented (per PRD)
- **Data export**: `GET /export/json` endpoint planned but not implemented (per PRD)

