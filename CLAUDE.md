CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Tempo is a self-hostable, privacy-first running tracker. It allows users to import GPX workout files, view analytics, and track running data locally without social features or subscriptions. The project uses a Next.js frontend and ASP.NET Core backend with PostgreSQL.

## Development Commands

### Backend (ASP.NET Core .NET 9.0)
- **Run**: `cd api && dotnet run` (API at http://localhost:5001)
- **Watch mode**: `cd api && dotnet watch run` (auto-reload on changes)
- **Database migrations**: `cd api && dotnet ef database update` (also applied automatically on startup)
- **Create migration**: `cd api && dotnet ef migrations add <MigrationName>`
- **Swagger UI**: Available at http://localhost:5001/swagger in development only
- **Tests**: No test infrastructure configured yet (xUnit planned per PRD)

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
- **Automatic migrations**: Migrations are automatically applied on API startup via `Program.cs` (handles migration state reconciliation)
- **Data persistence**: PostgreSQL data is stored in Docker volume `postgres_data` (persists across container restarts)

### Environment Variables

**Frontend** (Next.js):
- `NEXT_PUBLIC_API_URL` - Backend API URL (defaults to `http://localhost:5001`). Set in `.env.local` or `.env` file.

**Backend** (ASP.NET Core):
- Configuration via `appsettings.json` or `appsettings.{Environment}.json`
- Connection strings, media storage paths, and other settings are configured in `appsettings.json`

## Architecture

### High-Level Structure
- **Frontend** (`frontend/`): Next.js 16 with App Router, React 19.2.0, TypeScript, Tailwind CSS v4
- **Backend** (`api/`): ASP.NET Core .NET 9 Minimal APIs with EF Core
- **Database**: PostgreSQL 16 (via Docker Compose)

### Backend Architecture

**Endpoint Pattern**: Endpoints are organized as extension methods on `WebApplication` in the `Endpoints/` directory. The main endpoint group is `WorkoutsEndpoints` which maps to `/workouts`.

**Data Layer**:
- `TempoDbContext`: EF Core DbContext with four main entities
- **Workout**: Core workout data (distance, duration, pace, elevation, notes, run type, weather JSONB). RunType field is nullable string (max 50 chars) with valid values: "Easy Run" (default), "Race", "Workout", "Long Run", or null (None). New workouts default to "Easy Run" when imported. Indexed for filtering.
- **WorkoutRoute**: One-to-one relationship storing route as GeoJSON (LineString)
- **WorkoutSplit**: One-to-many relationship storing calculated splits (1km segments by default)
- **WorkoutMedia**: One-to-many relationship storing media file metadata (photos/videos). Fields: Id, WorkoutId, Filename, FilePath (filesystem path), MimeType, FileSizeBytes, Caption, CreatedAt. Files stored on filesystem in `{mediaRoot}/{workoutId}/` directory structure.
- Indexes: `StartedAt` on Workout, composite index on `(StartedAt, DistanceM, DurationS)` for duplicate detection, `(WorkoutId, Idx)` on splits, `WorkoutId` on WorkoutMedia, `RunType` on Workout

**Services**:
- `GpxParserService`: Parses GPX XML files, calculates distance using Haversine formula, computes elevation gain, and generates splits. Returns `GpxParseResult` with track points, timing, and metrics. Handles GPX 1.1 namespace (`http://www.topografix.com/GPX/1/1`).
- `FitParserService`: Parses Garmin FIT files (including `.fit.gz` compressed) using the embedded FIT SDK (from `FitSDKRelease_21.171.00/cs/`). The FIT SDK C# source files are compiled directly into the project via `<Compile Include>` directives in `Tempo.Api.csproj`. Converts FIT coordinate format (semicircles) to degrees.
- `StravaCsvParserService`: Parses Strava export CSV files (`activities.csv`) to extract activity metadata and match files for bulk import. Filters for "Run" activity type only. Extracts media file paths from the "Media" column (comma-separated paths).
- `MediaService`: Handles media file operations (copying, validation, MIME type detection). Validates file types (JPG, PNG, GIF, WEBP, MP4, MOV, AVI) and file sizes (default 50MB max, configurable). Generates file paths organized by workout ID: `{mediaRoot}/{workoutId}/{filename}`. Uses `MediaStorageConfig` singleton for configuration (root path and max file size).

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
- `POST /workouts/import/bulk` - Bulk import from Strava ZIP export (requires `activities.csv` in root and GPX/.fit.gz files). Supports duplicate detection: exact match on `StartedAt`, tolerance of 1.0m for `DistanceM`, and 1s for `DurationS`. Processes files sequentially, collects all workouts/routes/splits/media, then batch inserts with single `SaveChangesAsync()`. Combines CSV metadata (ActivityName, ActivityDescription, ActivityPrivateNote) into Notes field. Automatically imports media files from Strava export: parses "Media" column (comma-separated paths), locates files in `media/` directory, copies to persistent storage via `MediaService`, creates `WorkoutMedia` records. Handles missing media gracefully. Returns summary with `totalProcessed`, `successful`, `skipped`, `errors`, and `errorDetails` array.
- `GET /workouts` - List workouts with pagination and filtering (query params: page, pageSize, startDate, endDate, minDistanceM, maxDistanceM). Default pageSize is 20, max is 100. Returns 404 if page exceeds total pages. Uses `.Include()` to eager load Route and Splits for counting, then `.AsNoTracking()` after pagination for read-only performance.
- `GET /workouts/{id}` - Get workout details including route GeoJSON (parsed from JSONB string) and splits (ordered by `Idx`). Returns 404 if not found. Includes weather JSON if available. Uses `.AsNoTracking()` for read-only query.
- `PATCH /workouts/{id}` - Update workout RunType and/or Notes. Accepts JSON body with optional `runType` and `notes` fields. Validates `runType`: must be "Easy Run", "Race", "Workout", "Long Run", or null. Only updates fields that are provided in the request body. Returns 404 if workout not found, 400 for invalid runType values. Uses `JsonDocument` to check which properties are provided.
- `GET /workouts/{id}/media` - List all media files for a workout. Returns array of media metadata (id, filename, mimeType, fileSizeBytes, caption, createdAt). Returns 404 if workout not found.
- `GET /workouts/{id}/media/{mediaId}` - Serve media file. Returns file stream with appropriate MIME type and filename. Supports range requests (for video seeking). Returns 404 if workout or media not found, or if file missing on filesystem.
- `GET /workouts/stats/weekly` - Get weekly statistics (daily miles for current week, Monday-Sunday). Accepts optional `timezoneOffsetMinutes` query parameter for timezone adjustment. Returns array of 7 values (Monday through Sunday) in miles.
- `GET /workouts/stats/yearly` - Get yearly statistics (total miles for current year and previous year). Accepts optional `timezoneOffsetMinutes` query parameter. Returns current year and previous year totals in miles.
- `GET /health` - Health check endpoint (mapped in `Program.cs`)

**Configuration**:
- Connection string in `appsettings.json` (defaults to Docker Compose PostgreSQL)
- CORS configured for `http://localhost:3000` and `http://localhost:3001` (all methods, all headers)
- Serilog for structured logging (console output, configured via `appsettings.json`)
- Swagger enabled in development only (`if (app.Environment.IsDevelopment())`)
- Large file upload support: 500MB limit configured for bulk imports (Kestrel `MaxRequestBodySize` and `FormOptions.MultipartBodyLengthLimit`)
- Media storage: Configurable via `MediaStorage:RootPath` (defaults to `./media` relative to API directory) and `MediaStorage:MaxFileSizeBytes` (defaults to 50MB). Media root directory is created automatically on startup. For Docker deployments, mount this directory as a volume for persistence.
- Database initialization: `Program.cs` automatically applies migrations on startup via `db.Database.Migrate()`. Includes migration state reconciliation logic to handle databases created with `EnsureCreated()` (creates migration history table and marks initial migration as applied if needed). See Database Migrations section for details.

### Frontend Architecture

**State Management**: TanStack Query (`@tanstack/react-query`) for server state and API calls.

**API Client**: `lib/api.ts` exports functions for API communication:
- `importGpxFile(file: File)` - POSTs FormData to `/workouts/import`, throws on errors
- `importBulkStravaExport(zipFile: File)` - POSTs ZIP file to `/workouts/import/bulk`, returns `BulkImportResponse` with success/error counts
- `getWorkouts(params?: WorkoutsListParams)` - GETs paginated workout list from `/workouts` with query params, throws on 404 or other errors
- `getWorkout(id: string)` - GETs workout details from `/workouts/{id}`, throws specific error for 404
- `updateWorkout(id: string, updates: UpdateWorkoutRequest)` - PATCHs workout updates to `/workouts/{id}`, updates `runType` and/or `notes`. Validates runType values: "Easy Run", "Race", "Workout", "Long Run", or null.
- `getWorkoutMedia(workoutId: string)` - GETs media list from `/workouts/{id}/media`, returns empty array on 404
- `getWorkoutMediaUrl(workoutId: string, mediaId: string)` - Returns URL for serving media file
- `getWeeklyStats(timezoneOffsetMinutes?: number)` - GETs weekly stats from `/workouts/stats/weekly`
- `getYearlyStats(timezoneOffsetMinutes?: number)` - GETs yearly stats from `/workouts/stats/yearly`
Uses `NEXT_PUBLIC_API_URL` environment variable (defaults to `http://localhost:5001`). All functions use CORS mode and throw on HTTP errors.

**Components**: 
- `FileUpload`: Handles single GPX file upload UI
- `BulkImport`: Handles ZIP file bulk import UI
- `WorkoutMap`: Leaflet-based map component for visualizing workout routes (client-side only, dynamically imported to avoid SSR issues)
- `WorkoutMediaGallery`: Displays media files (photos/videos) for a workout
- `MediaModal`: Modal component for viewing media files in fullscreen
- `WeeklyStatsWidget`: Displays weekly running statistics (daily miles)
- `YearlyComparisonWidget`: Displays year-over-year comparison of total miles
- `app/page.tsx`: Home page that redirects to `/dashboard` (if workouts exist) or `/import` (if empty)
- `app/dashboard/page.tsx`: Workout list page with pagination, filtering, table view, and stats widgets
- `app/dashboard/[id]/page.tsx`: Workout detail page with stats, splits table, route map, notes, and media gallery. Includes inline click-to-edit for RunType with dropdown (None, Easy Run, Race, Workout, Long Run). Uses TanStack Query `useMutation` for updates with auto-save on selection change.
- `app/import/page.tsx`: Import page with single GPX upload and bulk Strava import options

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
6. For each activity, processes media files: parses "Media" column (comma-separated paths), locates files in temp directory, uses `MediaService` to copy to persistent storage and create `WorkoutMedia` records
7. Batch creates all workouts, routes, splits, and media records
8. Single `SaveChangesAsync()` call for all entities
9. Cleans up temp directory
10. Returns summary with counts and error details

### Database Migrations

Migrations are in `api/Migrations/`. The initial migration creates the main tables with proper foreign keys and indexes. 

**Automatic Application**: `Program.cs` automatically applies pending migrations on startup using `db.Database.Migrate()`. It also includes logic to reconcile migration state if the database was previously created with `EnsureCreated()` (creates `__EFMigrationsHistory` table and marks initial migration as applied if needed).

**Manual Migration Commands**:
- Apply migrations: `cd api && dotnet ef database update`
- Create new migration: `cd api && dotnet ef migrations add <MigrationName>`

**⚠️ Important**: The automatic migration logic in `Program.cs` handles edge cases where tables exist but migration history is missing. For production deployments, ensure migrations are properly tracked and consider removing the migration state reconciliation logic once the database is fully migrated.

**Note**: The migration state reconciliation code in `Program.cs` has a hardcoded migration ID (`20251110232429_InitialCreate`) that does not match the actual initial migration filename (`20251111150526_InitialCreate`). This is intentional for handling legacy databases created with `EnsureCreated()`, but if creating a fresh database, ensure the migration history is properly tracked from the start. If updating this logic, use the actual migration filename from `api/Migrations/` (currently `20251111150526_InitialCreate`).

**FIT SDK Integration**: The FIT SDK C# source files are located in `api/Libraries/FitSDK/` (version 21.171.00) and are automatically compiled into the project via `<Compile Include>` directives in `Tempo.Api.csproj`. The SDK handles parsing Garmin FIT files including compressed `.fit.gz` files. Coordinate conversion from semicircles (FIT format unit) to degrees is handled in `FitParserService` using the conversion factor `180.0 / 2^31`. The SDK is embedded directly in the repository (not a NuGet package).

### Test Data

The `test_data/` directory contains sample files for development:
- `20251110.gpx` - Sample GPX file for testing single import
- `strava_export/` - Sample Strava export ZIP structure with `activities.csv` and associated GPX/FIT files in subdirectories (`routes/`, `activities/`, etc.)

## Implementation Status

### ✅ Implemented
- **Single GPX import**: `POST /workouts/import` endpoint fully functional
- **Bulk import**: `POST /workouts/import/bulk` - accepts Strava ZIP exports with `activities.csv` and processes GPX/.fit.gz files. Includes duplicate detection based on `(StartedAt, DistanceM, DurationS)`. Batch inserts workouts for performance. Automatically imports media files from Strava exports (parses Media column, copies files, creates database records).
- **Workout browsing**: `GET /workouts` (list with pagination/filtering) and `GET /workouts/{id}` (detail) endpoints functional
- **Frontend pages**: Dashboard (`/dashboard`), workout detail (`/dashboard/[id]`), and import (`/import`) pages fully implemented with TanStack Query
- **Stats endpoints**: `GET /workouts/stats/weekly` and `GET /workouts/stats/yearly` implemented for analytics widgets
- **FIT file support**: Garmin FIT SDK (v21.171.00) embedded and compiled into backend for `.fit.gz` file parsing. Converts FIT coordinate format (semicircles) to degrees.
- **Map visualization**: `WorkoutMap` component using Leaflet and OpenStreetMap tiles. Dynamically imported to avoid SSR issues. Displays route as polyline with auto-fit bounds.
- **Media support**: `WorkoutMedia` model, `MediaService`, media storage on filesystem, `GET /workouts/{id}/media` (list) and `GET /workouts/{id}/media/{mediaId}` (serve file) endpoints. Media automatically imported during bulk Strava import.
- **Run type tagging**: `PATCH /workouts/{id}` endpoint implemented for updating RunType and Notes. Frontend includes inline click-to-edit UI for RunType with options: None, Easy Run (default for new imports), Race, Workout, Long Run. Auto-saves on selection change using TanStack Query mutations.

### ❌ Not Yet Implemented
- **Tests**: PRD mentions Vitest/Playwright for frontend and xUnit for backend, but no test infrastructure exists yet. No test projects or test commands are configured.
- **Weather API**: Open-Meteo integration planned (per PRD). Weather field exists in database but is not populated. `WeatherService` exists but is not called during import.
- **Analytics endpoints**: Basic stats endpoints (`/workouts/stats/weekly` and `/workouts/stats/yearly`) are implemented. `GET /analytics/summary` (comprehensive analytics) planned but not yet implemented (per PRD)
- **Media upload**: `POST /workouts/{id}/media` endpoint for manual media upload after GPX import (planned per PRD, not yet implemented)
- **Media deletion**: `DELETE /workouts/{id}/media/{mediaId}` endpoint planned but not implemented (per PRD)
- **Data export**: `GET /export/json` endpoint planned but not implemented (per PRD)

