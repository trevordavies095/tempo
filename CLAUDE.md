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

### Database
- **Start PostgreSQL**: `docker-compose up -d postgres`
- **Stop**: `docker-compose down`
- **Connection string**: `Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres` (configured in `api/appsettings.json`)

## Architecture

### High-Level Structure
- **Frontend** (`frontend/`): Next.js 16 with App Router, React 19, TypeScript, Tailwind CSS v4
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
- GPX parsing uses XML namespaces (`http://www.topografix.com/GPX/1/1`)
- Distance calculation: Haversine formula with Earth radius 6,371,000m
- Splits: Default 1000m (1km) segments, calculated from accumulated distance between track points
- Routes stored as GeoJSON LineString in PostgreSQL JSONB column
- Weather data stored as JSONB (Open-Meteo integration planned per PRD)

**API Endpoints** (in `WorkoutsEndpoints`):
- `POST /workouts/import` - Single GPX file import (multipart/form-data, file field named "file")
- `POST /workouts/import/bulk` - Bulk import from Strava ZIP export (requires `activities.csv` in root and GPX/.fit.gz files). Supports duplicate detection based on `(StartedAt, DistanceM, DurationS)`.
- `GET /workouts` - List workouts with pagination and filtering (query params: page, pageSize, startDate, endDate, minDistanceM, maxDistanceM). Returns 404 if page exceeds total pages.
- `GET /workouts/{id}` - Get workout details including route GeoJSON and splits. Returns 404 if not found.
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
- `importGpxFile(file: File)` - POSTs FormData to `/workouts/import`
- `importBulkStravaExport(zipFile: File)` - POSTs ZIP file to `/workouts/import/bulk`
- `getWorkouts(params?: WorkoutsListParams)` - GETs paginated workout list from `/workouts` with query params
- `getWorkout(id: string)` - GETs workout details from `/workouts/{id}`
Uses `NEXT_PUBLIC_API_URL` environment variable (defaults to `http://localhost:5001`). All functions throw on HTTP errors.

**Components**: 
- `FileUpload`: Handles single GPX file upload UI
- `BulkImport`: Handles ZIP file bulk import UI
- `app/workouts/page.tsx`: Workout list page with pagination
- `app/workouts/[id]/page.tsx`: Workout detail page

**Utilities**: `lib/format.ts` provides formatting helpers for distance, duration, pace, and dates.

**Styling**: Tailwind CSS v4 with PostCSS. Dark mode support via `dark:` classes.

### Data Flow

1. User uploads GPX file via frontend `FileUpload` component
2. Frontend sends multipart/form-data POST to `/workouts/import`
3. Backend `WorkoutsEndpoints` receives file, validates `.gpx` extension
4. `GpxParserService.ParseGpx()` parses XML, extracts track points, calculates distance/elevation
5. Backend creates `Workout`, `WorkoutRoute` (GeoJSON), and `WorkoutSplit` entities
6. EF Core saves to PostgreSQL via `TempoDbContext`
7. Response returns workout summary with ID and metrics

### Database Migrations

Migrations are in `api/Migrations/`. The initial migration creates the three main tables with proper foreign keys and indexes. Use `dotnet ef database update` to apply migrations. 

**⚠️ Important**: `Program.cs` calls `db.Database.EnsureCreated()` as a fallback, which may conflict with migrations in production. Consider removing `EnsureCreated()` in favor of migrations-only approach for production deployments.

## Implementation Status

### ✅ Implemented
- **Single GPX import**: `POST /workouts/import` endpoint fully functional
- **Bulk import**: `POST /workouts/import/bulk` - accepts Strava ZIP exports with `activities.csv` and processes GPX/.fit.gz files. Includes duplicate detection based on `(StartedAt, DistanceM, DurationS)`.
- **Workout browsing**: `GET /workouts` (list with pagination/filtering) and `GET /workouts/{id}` (detail) endpoints functional
- **Frontend pages**: Workout list (`/workouts`) and detail (`/workouts/[id]`) pages implemented
- **FIT file support**: Garmin FIT SDK (v21.171.00) embedded and compiled into backend for `.fit.gz` file parsing

### ❌ Not Yet Implemented
- **Tests**: PRD mentions Vitest/Playwright for frontend and xUnit for backend, but no test infrastructure exists yet
- **Weather API**: Open-Meteo integration planned (per PRD). Weather field exists in database but is not populated.
- **Analytics endpoints**: `GET /analytics/summary` planned but not yet implemented (per PRD)
- **Map visualization**: Frontend components for Leaflet maps not yet implemented (per PRD)
- **Workout editing**: `PATCH /workouts/{id}` endpoint planned but not implemented (per PRD)
- **Data export**: `GET /export/json` endpoint planned but not implemented (per PRD)

