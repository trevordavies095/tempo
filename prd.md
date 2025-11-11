# 🏃‍♂️ Product Requirements Document (PRD)  
### **Project:** Self-Hostable Running Tracker

---

## 1. Overview

**Project Name:** _(TBD – e.g., “RunTrackr,” “StrideLog,” “Tempo”)_  
**Type:** Self-hosted personal fitness tracker  
**Primary Goal:** Provide a simple, privacy-first alternative to Strava for runners who just want to collect and analyze their own workout data — no social features, no subscriptions.

### Background
Strava has evolved into a social and subscription-oriented platform. For runners who primarily care about performance insights and personal tracking, this creates unnecessary noise and recurring costs.  
This project aims to replicate the *core running analytics* experience — importing, visualizing, and analyzing run data — in a self-hostable, private environment.

### Vision
> “A minimal, privacy-first running log that lives on your own server — data in, insights out, nothing else.”

---

## 2. Goals & Non-Goals

### Goals
- Manual import of GPX workout files (from Apple Watch exports).  
- Bulk import of Strava GPX/FIT files for history migration.  
- View key workout data:
  - Route map
  - Distance, duration, average pace
  - Elevation and splits
  - Weather conditions
  - Run type (easy, tempo, long, etc.)
  - Private notes/journal
  - Media attachments (photos and videos)
- View weekly and monthly performance trends.
- Self-hostable with Docker.
- 100% local data ownership (no cloud dependency).

### Non-Goals
- No social network or activity sharing features.
- No real-time or live tracking.
- No mobile app (v1 is web-first).
- No multi-sport support (v1 = running only).

---

## 3. Users & Use Cases

### Primary User
A privacy-conscious runner who uses an Apple Watch and wants to track workouts locally without social features or subscriptions.

### Key Use Cases
1. **Import a Run**  
   Upload a GPX file manually from Apple Watch export.
2. **Bulk Import from Strava**  
   Import a ZIP or directory of GPX/FIT files for historical data migration.
3. **View Run Details**  
   View map, stats, pace, and weather data.
4. **Track Trends**  
   Aggregate mileage and pace trends across time.
5. **Add Notes**  
   Write reflections or workout-specific notes for future reference.

---

## 4. Functional Requirements

| # | Feature | Description | Priority |
|---|----------|--------------|-----------|
| F1 | **Manual GPX Import** | Users can upload single GPX files. System parses metadata (distance, time, GPS). Media files can be uploaded after import. | ⭐⭐⭐⭐ |
| F2 | **Bulk Import (Strava Export)** | Accept ZIP folder of GPX/FIT files, batch-process them into database. Automatically imports associated media files from Strava export. | ⭐⭐⭐ |
| F3 | **Map Visualization** | Display route via Leaflet + OpenStreetMap tiles. | ⭐⭐⭐⭐ |
| F4 | **Workout Stats** | Distance, time, avg pace, elevation, date/time. | ⭐⭐⭐⭐ |
| F5 | **Splits Calculation** | Calculate mile/km splits automatically. | ⭐⭐⭐⭐ |
| F6 | **Weather Integration** | Use Open-Meteo API (no key required) to fetch historical weather at GPS/time. | ⭐⭐⭐ |
| F7 | **Run Type Tagging** | Assign a type (easy, tempo, long, race). | ⭐⭐⭐ |
| F8 | **Private Notes** | Text field for comments and reflections. | ⭐⭐⭐ |
| F9 | **Analytics Dashboard** | Display charts for total mileage and avg pace trends. | ⭐⭐⭐ |
| F10 | **Data Export/Backup** | JSON export of all workouts. | ⭐⭐⭐ |
| F11 | **Self-Hosting** | Packaged Docker deployment with persistent storage volume. | ⭐⭐⭐⭐ |
| F12 | **Media Attachments** | Attach photos and videos to workouts. Support automatic import from Strava exports and manual upload after GPX import. | ⭐⭐⭐ |

---

## 5. Non-Functional Requirements

| Category | Requirement |
|-----------|--------------|
| **Performance** | Handle ~10 years of workout data with responsive queries. |
| **Privacy** | No external telemetry; only optional weather API requests. |
| **Portability** | Dockerized deployment; works on NAS/home servers. |
| **Storage** | Local-only; JSON backups supported. |
| **Security** | Local authentication (future); HTTPS support recommended. |
| **Simplicity** | Minimal dependencies; configuration via `.env`. |

---

## 6. Technical Overview (Popular & Proven Stack)

| Area | Tech Choice | Why (Popularity & Fit) |
|---|---|---|
| **Frontend** | **React + Next.js (TypeScript)** | React dominates web UIs; Next.js is the most widely adopted React framework for routing, SSR, and static builds. |
| **UI / Styling** | **Tailwind CSS** + Headless UI | Most popular utility-first CSS; fast styling, responsive layouts. |
| **State & Data** | **TanStack Query** (server state) + React state | Industry standard for API caching and async states. |
| **Charts** | **Chart.js** (`react-chartjs-2`) | Widely used, easy to integrate and theme. |
| **Maps** | **Leaflet** + OpenStreetMap | Lightweight, proven for route visualizations; fully open-source. |
| **Date/Time** | **date-fns** | Popular, modern, and tree-shakeable date library. |
| **Backend** | **ASP.NET Core (.NET 8) Minimal APIs (C#)** | High performance, popular, and a natural fit for a senior C# developer. |
| **Validation** | **FluentValidation** or **MiniValidation** | Common .NET libraries for request/DTO validation. |
| **ORM** | **Entity Framework Core (EF Core)** | Mainstream ORM with migrations, LINQ, and strong ecosystem. |
| **Database** | **PostgreSQL** (optional **PostGIS**) | Most popular OSS relational DB; PostGIS adds spatial support. |
| **Weather API** | **Open-Meteo** | Free, no API key, open-source friendly. |
| **Containerization** | **Docker Compose** | Simple multi-service local deployment. |
| **Testing** | **Frontend:** Vitest/Playwright • **Backend:** xUnit | Well-supported testing ecosystems. |
| **Logging** | **Serilog** | Common .NET logging framework with flexible sinks. |

### Service Layout
```
frontend/   → Next.js (React, Tailwind, TypeScript)
api/        → ASP.NET Core Minimal APIs + EF Core (Postgres)
db/         → PostgreSQL (optionally with PostGIS)
docker-compose.yml  → Brings up all services
```

### API Endpoints (v1)
```
POST /workouts/import        → Upload GPX file
POST /workouts/import/bulk   → Upload ZIP (Strava export)
GET /workouts                → List/filter workouts
GET /workouts/{id}           → Retrieve workout details
PATCH /workouts/{id}         → Update notes/run type
POST /workouts/{id}/media    → Upload media files (multipart/form-data, accepts multiple files)
GET /workouts/{id}/media     → List all media for a workout
GET /workouts/{id}/media/{mediaId} → Retrieve/serve media file
DELETE /workouts/{id}/media/{mediaId} → Delete media (removes file and database record)
GET /analytics/summary       → Aggregate pace/distance
GET /export/json             → Full JSON backup
```

### Data Model (Postgres)

**workouts**
| Field | Type | Description |
|--------|------|-------------|
| id | UUID | Unique ID |
| started_at | timestamptz | Start time |
| duration_s | int | Duration (seconds) |
| distance_m | double | Distance (meters) |
| avg_pace_s | int | Avg pace (seconds/km or mi) |
| elev_gain_m | double | Elevation gain |
| run_type | text | easy, tempo, long, race, recovery |
| notes | text | User notes |
| source | text | apple_watch, strava_import |
| weather | jsonb | Open-Meteo snapshot |

**workout_routes**
| Field | Type | Description |
|--------|------|-------------|
| workout_id | FK | Related workout |
| route_geojson | jsonb | Route data (GeoJSON or encoded polyline) |

**workout_splits**
| Field | Type | Description |
|--------|------|-------------|
| workout_id | FK | Related workout |
| idx | int | Split index |
| distance_m | double | Split distance |
| duration_s | int | Split time |
| pace_s | int | Pace per split |

**workout_media**
| Field | Type | Description |
|--------|------|-------------|
| id | UUID | Unique ID (primary key) |
| workout_id | UUID | Foreign key to workouts (indexed) |
| filename | text | Original filename from upload |
| file_path | text | Filesystem path (relative to media root or absolute) |
| mime_type | text | MIME type (e.g., "image/jpeg", "video/mp4") |
| file_size_bytes | bigint | File size in bytes |
| caption | text | Optional caption/description (nullable) |
| created_at | timestamptz | Creation timestamp (default: now()) |

---

## 7. Import Pipeline

### Single GPX Import

1. User uploads a GPX file.  
2. Backend parses metadata (start time, elapsed, GPS track).  
3. Computes:
   - Splits
   - Average pace
   - Elevation gain  
4. Queries **Open-Meteo** with coordinates + timestamp.  
5. Saves all data to Postgres.  
6. Returns summary + computed metrics to frontend.  
7. User can optionally upload media files after import via `POST /workouts/{id}/media`.

### Bulk Import (Strava Export)

1. User uploads a ZIP file containing Strava export (activities.csv + activity files).  
2. Backend extracts ZIP to temporary directory.  
3. Parses `activities.csv` to identify run activities and their associated files.  
4. For each activity:
   - Parses GPX/FIT file to extract metadata (start time, elapsed, GPS track)
   - Computes splits, average pace, elevation gain
   - Queries **Open-Meteo** with coordinates + timestamp
   - Checks for duplicates using `(start_time, distance, duration)` checksum
   - **Extracts media files:** Parses "Media" column from activities.csv (contains comma-separated media file paths)
   - **Matches media files:** Locates media files from `media/` directory in ZIP export
   - **Copies media files:** Saves media files to persistent filesystem storage (organized by workout ID)
   - **Creates media records:** Creates `workout_media` database records with file paths, MIME types, and metadata
   - Handles missing media files gracefully (skips if file not found or Media column is empty)
5. Batch saves all workouts, routes, splits, and media records to Postgres.  
6. Cleans up temporary directory.  
7. Returns summary with counts and error details.

**Media Import Details:**
- Media column in activities.csv may contain comma-separated paths (e.g., "media/file1.jpg,media/file2.mp4")
- Media files are stored in organized directory structure: `media/{workoutId}/{filename}`
- Original filenames are preserved or unique names are generated to avoid conflicts
- MIME types are detected from file extensions
- File sizes are recorded for storage management

---

## 7.1. Media Storage Strategy

### Storage Approach
Media files are stored on the filesystem with database references. The database stores file paths, metadata, and relationships, while actual media files are stored in a configured directory on the server's filesystem.

### Supported File Types

**Images:**
- JPG/JPEG
- PNG
- GIF
- WEBP

**Videos:**
- MP4
- MOV
- AVI

### File Size Limits
- Recommended default: 50MB per file
- Configurable via application settings
- Validation enforced at upload time

### File Organization
Media files are organized in a hierarchical directory structure:
```
{mediaRoot}/
  {workoutId}/
    {filename1}.jpg
    {filename2}.mp4
    ...
```

This structure:
- Groups media by workout for easy management
- Simplifies cleanup when workouts are deleted
- Supports efficient file serving
- Allows for future organization enhancements

### File Naming
- Original filenames are preserved when possible
- Unique names are generated if conflicts occur (e.g., using GUIDs or timestamps)
- Database records maintain both original filename and actual file path

### MIME Type Detection
- MIME types are detected from file extensions during upload
- Stored in database for proper content-type headers when serving files
- Supports proper browser rendering of images and videos

### Storage Configuration
- Media root directory configurable via environment variable or appsettings
- Should be included in Docker volume mounts for persistence
- Consider backup strategy for media files alongside database backups

---

## 8. Implementation Status: Workout Browsing & Viewing

### Current State
✅ **Implemented:**
- `POST /workouts/import` - GPX file import endpoint
- Database models: `Workout`, `WorkoutRoute`, `WorkoutSplit`
- Frontend: File upload component

❌ **Not Yet Implemented:**
- `GET /workouts` - List workouts endpoint
- `GET /workouts/{id}` - Get workout details endpoint
- Frontend: Workout list view
- Frontend: Workout detail view
- Frontend: Navigation between views
- Media attachments:
  - Database model: `WorkoutMedia`
  - `POST /workouts/{id}/media` - Upload media files endpoint
  - `GET /workouts/{id}/media` - List media for workout endpoint
  - `GET /workouts/{id}/media/{mediaId}` - Retrieve/serve media file endpoint
  - `DELETE /workouts/{id}/media/{mediaId}` - Delete media endpoint
  - Media import during Strava bulk import (parse Media column, copy files, create records)
  - Frontend: Media upload component for post-import uploads
  - Frontend: Media display on workout detail page (image preview, video playback)

### Backend Implementation Requirements

#### 8.1 GET /workouts - List Workouts Endpoint

**Location:** `api/Endpoints/WorkoutsEndpoints.cs`

**Requirements:**
- Return paginated list of workouts (default: 20 per page)
- Support query parameters:
  - `page` (int, default: 1)
  - `pageSize` (int, default: 20, max: 100)
  - `startDate` (ISO 8601 date, optional)
  - `endDate` (ISO 8601 date, optional)
  - `minDistanceM` (double, optional)
  - `maxDistanceM` (double, optional)
- Order by `StartedAt DESC` (newest first)
- Include basic workout fields only (no route GeoJSON or splits)
- Use EF Core `.Include()` to eager load `Route` and `Splits` counts if needed for performance

**Response Structure:**
```json
{
  "items": [
    {
      "id": "uuid",
      "startedAt": "2024-01-15T10:30:00Z",
      "durationS": 3600,
      "distanceM": 10000.0,
      "avgPaceS": 360,
      "elevGainM": 150.5,
      "runType": "easy",
      "source": "apple_watch",
      "hasRoute": true,
      "splitsCount": 10
    }
  ],
  "totalCount": 42,
  "page": 1,
  "pageSize": 20,
  "totalPages": 3
}
```

**Implementation Notes:**
- Use `TempoDbContext.Workouts` with `.AsQueryable()`
- Apply filters using LINQ `.Where()` clauses
- Use `.Skip()` and `.Take()` for pagination
- Count total records with `.Count()` before pagination
- Consider adding `.AsNoTracking()` for read-only queries (performance optimization)
- Return 404 if page exceeds total pages

#### 8.2 GET /workouts/{id} - Get Workout Details Endpoint

**Location:** `api/Endpoints/WorkoutsEndpoints.cs`

**Requirements:**
- Return complete workout data including:
  - All workout fields
  - Route GeoJSON (from `WorkoutRoute.RouteGeoJson`)
  - All splits (ordered by `Idx` ascending)
- Return 404 if workout not found
- Use EF Core `.Include()` to load related `Route` and `Splits`

**Response Structure:**
```json
{
  "id": "uuid",
  "startedAt": "2024-01-15T10:30:00Z",
  "durationS": 3600,
  "distanceM": 10000.0,
  "avgPaceS": 360,
  "elevGainM": 150.5,
  "runType": "easy",
  "notes": "Great run today!",
  "source": "apple_watch",
  "weather": { "temperature": 15, "conditions": "sunny" },
  "createdAt": "2024-01-15T11:00:00Z",
  "route": {
    "type": "LineString",
    "coordinates": [[lon1, lat1], [lon2, lat2], ...]
  },
  "splits": [
    {
      "idx": 0,
      "distanceM": 1000.0,
      "durationS": 360,
      "paceS": 360
    },
    ...
  ]
}
```

**Implementation Notes:**
- Parse `RouteGeoJson` JSON string and include in response (it's stored as JSONB string)
- Order splits by `Idx` using `.OrderBy(s => s.Idx)`
- Handle case where `Route` or `Splits` might be null/empty
- Use `.FirstOrDefaultAsync()` with `.Include()` for efficient loading

**Example Endpoint Code Structure:**
```csharp
group.MapGet("/{id:guid}", async (
    Guid id,
    TempoDbContext db,
    ILogger<Program> logger) =>
{
    var workout = await db.Workouts
        .Include(w => w.Route)
        .Include(w => w.Splits.OrderBy(s => s.Idx))
        .AsNoTracking()
        .FirstOrDefaultAsync(w => w.Id == id);

    if (workout == null)
        return Results.NotFound(new { error = "Workout not found" });

    // Parse route GeoJSON if exists
    var routeGeoJson = workout.Route != null
        ? JsonSerializer.Deserialize<object>(workout.Route.RouteGeoJson)
        : null;

    return Results.Ok(new
    {
        id = workout.Id,
        startedAt = workout.StartedAt,
        // ... map all fields
        route = routeGeoJson,
        splits = workout.Splits.Select(s => new { ... })
    });
})
.Produces(200)
.Produces(404)
.WithSummary("Get workout details")
.WithDescription("Retrieves complete workout data including route and splits");
```

### Frontend Implementation Requirements

#### 8.3 Workout List Page

**Location:** `frontend/app/workouts/page.tsx` (Next.js App Router)

**Requirements:**
- Display paginated list of workouts
- Show key metrics: date, distance, duration, pace, elevation
- Link to workout detail page
- Support pagination controls (Previous/Next, page numbers)
- Optional: Add filters UI (date range, distance range)
- Use TanStack Query for data fetching and caching
- Show loading states and error handling

**Component Structure:**
```
app/
  workouts/
    page.tsx          # List page
    [id]/
      page.tsx        # Detail page (dynamic route)
```

**API Client Function:**
Add to `frontend/lib/api.ts`:
```typescript
export interface WorkoutListItem {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  runType: string | null;
  source: string | null;
  hasRoute: boolean;
  splitsCount: number;
}

export interface WorkoutsListResponse {
  items: WorkoutListItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface WorkoutsListParams {
  page?: number;
  pageSize?: number;
  startDate?: string;
  endDate?: string;
  minDistanceM?: number;
  maxDistanceM?: number;
}

export async function getWorkouts(
  params?: WorkoutsListParams
): Promise<WorkoutsListResponse> {
  const searchParams = new URLSearchParams();
  if (params?.page) searchParams.set('page', params.page.toString());
  if (params?.pageSize) searchParams.set('pageSize', params.pageSize.toString());
  // ... add other params

  const response = await fetch(
    `${API_BASE_URL}/workouts?${searchParams.toString()}`,
    { method: 'GET', headers: { 'Content-Type': 'application/json' } }
  );

  if (!response.ok) {
    throw new Error(`Failed to fetch workouts: ${response.status}`);
  }

  return response.json();
}
```

**TanStack Query Hook:**
```typescript
// In a hooks file or component
import { useQuery } from '@tanstack/react-query';

export function useWorkouts(params?: WorkoutsListParams) {
  return useQuery({
    queryKey: ['workouts', params],
    queryFn: () => getWorkouts(params),
  });
}
```

#### 8.4 Workout Detail Page

**Location:** `frontend/app/workouts/[id]/page.tsx`

**Requirements:**
- Display complete workout information
- Show route map using Leaflet (if route exists)
- Display splits in a table or chart
- Show weather data if available
- Display notes and run type
- Format metrics nicely (distance in km, pace as min/km, duration as HH:MM:SS)
- Use TanStack Query for data fetching
- Handle 404 case (workout not found)

**API Client Function:**
Add to `frontend/lib/api.ts`:
```typescript
export interface WorkoutDetail {
  id: string;
  startedAt: string;
  durationS: number;
  distanceM: number;
  avgPaceS: number;
  elevGainM: number | null;
  runType: string | null;
  notes: string | null;
  source: string | null;
  weather: any | null;
  createdAt: string;
  route: {
    type: string;
    coordinates: [number, number][];
  } | null;
  splits: Array<{
    idx: number;
    distanceM: number;
    durationS: number;
    paceS: number;
  }>;
}

export async function getWorkout(id: string): Promise<WorkoutDetail> {
  const response = await fetch(`${API_BASE_URL}/workouts/${id}`, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });

  if (response.status === 404) {
    throw new Error('Workout not found');
  }

  if (!response.ok) {
    throw new Error(`Failed to fetch workout: ${response.status}`);
  }

  return response.json();
}
```

**Map Integration:**
- Install Leaflet: `npm install leaflet react-leaflet @types/leaflet`
- Use `MapContainer`, `TileLayer`, `Polyline` from `react-leaflet`
- Import Leaflet CSS: `import 'leaflet/dist/leaflet.css'`
- Center map on route bounds using `L.latLngBounds()`

#### 8.5 Navigation Updates

**Update Home Page:**
- Add link/navigation to workouts list page
- Consider adding a "Recent Workouts" preview section

**File:** `frontend/app/page.tsx`
- Add navigation link: `<Link href="/workouts">View All Workouts</Link>`
- Or add a section showing last 5 workouts

### Data Formatting Utilities

**Create:** `frontend/lib/format.ts` for helper functions:
```typescript
// Format distance: meters → "10.5 km"
export function formatDistance(meters: number): string;

// Format duration: seconds → "1:30:45"
export function formatDuration(seconds: number): string;

// Format pace: seconds/km → "5:30 /km"
export function formatPace(secondsPerKm: number): string;

// Format date: ISO string → "Jan 15, 2024"
export function formatDate(dateString: string): string;
```

### Testing Considerations

**Backend:**
- Test pagination edge cases (empty results, page beyond total)
- Test filtering combinations
- Test 404 handling for invalid workout IDs
- Test EF Core includes load correctly

**Frontend:**
- Test loading states
- Test error handling (network errors, 404s)
- Test pagination navigation
- Test map rendering with/without route data

### Performance Considerations

- Use `.AsNoTracking()` for read-only queries (faster, less memory)
- Consider adding database indexes if filtering becomes slow:
  - Index on `started_at` (already exists)
  - Composite index on `(started_at, distance_m)` if filtering by both
- For large datasets, consider cursor-based pagination instead of offset-based
- Cache workout list with TanStack Query (default: 5 minutes stale time)
- Lazy load route GeoJSON only on detail page (not in list)

### Implementation Order Recommendation

1. **Backend: GET /workouts** (list endpoint)
2. **Backend: GET /workouts/{id}** (detail endpoint)
3. **Frontend: API client functions** (`getWorkouts`, `getWorkout`)
4. **Frontend: Workout list page** (`/workouts`)
5. **Frontend: Workout detail page** (`/workouts/[id]`)
6. **Frontend: Navigation updates** (link from home page)
7. **Frontend: Map integration** (Leaflet for route visualization)
8. **Polish: Formatting utilities, loading states, error handling**

---

## 9. Future Considerations

| Feature | Description |
|----------|--------------|
| **Direct HealthKit Sync** | Support for automatic Apple Health import (iCloud or API proxy). |
| **Multi-sport Support** | Add biking, hiking, swimming, etc. |
| **Authentication** | Optional local login (for multi-user setups). |
| **Enhanced Analytics** | Cadence, HR zones, VO₂ Max estimates. |
| **Offline Mode / PWA** | Offline-first support for mobile use. |
| **Map Tile Caching** | Allow self-hosted OSM tiles for total privacy. |

---

## 10. Open Questions (Updated)

| Question | Decision |
|-----------|-----------|
| Support direct HealthKit sync? | **Eventually**, not in v1. Manual GPX import first. |
| Bulk imports supported? | **Yes**, ZIP imports from Strava. |
| Backups/export format? | **JSON**, preferred over CSV for reliability. |
| Weather API? | **Open-Meteo**, free, no key, open-source friendly. |
