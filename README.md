# Tempo - Self-Hostable Running Tracker

> A privacy-first, self-hosted Strava alternative. Import GPX, FIT, and CSV files from Garmin, Apple Watch, Strava, and more. Keep all your data local—no subscriptions, no cloud required.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Version](https://img.shields.io/badge/version-1.3.0-green.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Next.js](https://img.shields.io/badge/Next.js-16-black.svg)
[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/9Svd99npyj)

## Screenshots

![Dashboard](https://i.imgur.com/pURdx2e.png)
*Dashboard view*

![My Activities](https://i.imgur.com/nZEt9mN.png)
*Activities list*

![Activity Details](https://i.imgur.com/aj671gl.png)
*Activity details view*

## Quick Start

Get Tempo running in minutes with Docker Compose:

**Prerequisites:**
- Docker and Docker Compose installed

```bash
# Clone the repository
git clone https://github.com/trevordavies095/tempo.git
cd tempo

# Start all services
docker-compose up -d

# Access the application
# Frontend: http://localhost:3000
# API: http://localhost:5001
# API Swagger UI (development): http://localhost:5001/swagger
```

That's it! The database migrations run automatically on first startup. Your data is persisted in Docker volumes, so it will survive container restarts.

## Features

- **Multi-Format Support** - Import GPX, FIT (.fit, .fit.gz), and Strava CSV files from Garmin, Apple Watch, and other devices
- **Workout Analytics** - Track distance, pace, elevation, splits, and time series data
- **Interactive Maps** - Visualize routes with elevation profiles
- **Media Support** - Attach photos and videos to workouts
- **Weather Data** - Automatic weather conditions for each workout
- **Bulk Import** - Import multiple workouts at once via ZIP file (up to 500MB)
- **Heart Rate Zones** - Calculate zones using Age-based, Karvonen, or Custom methods
- **Relative Effort** - Automatic calculation of workout intensity based on heart rate zones
- **Workout Editing** - Crop/trim workouts and edit activity names
- **Statistics Dashboards** - Weekly and yearly statistics with relative effort tracking
- **Unit Preferences** - Switch between metric and imperial units
- **100% Local** - All data stays on your machine, no cloud sync required

## Tech Stack

- **Frontend**: Next.js 16, React 19, TypeScript, Tailwind CSS
- **Backend**: ASP.NET Core (.NET 9) Minimal APIs
- **Database**: PostgreSQL 16
- **State Management**: TanStack Query

## Local Development

For development without Docker:

### Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- PostgreSQL (or use Docker Compose for database only)

### Setup

1. **Start PostgreSQL** (if not using Docker):
   ```bash
   docker-compose up -d postgres
   ```

2. **Configure Backend**:
   Update `api/appsettings.json` with your database connection string if needed.

3. **Run Migrations**:
   ```bash
   cd api
   dotnet ef database update
   ```

4. **Start Backend**:
   ```bash
   cd api
   dotnet watch run
   ```
   API runs at `http://localhost:5001`

5. **Start Frontend**:
   ```bash
   cd frontend
   npm install
   npm run dev
   ```
   Frontend runs at `http://localhost:3000`

## Usage

### Single Workout Import

1. Export a workout file from your device:
   - **Garmin**: Export FIT files (.fit) directly from your device
   - **Apple Watch**: Export GPX files from the Health app
   - **Strava**: Download individual GPX files
2. Open `http://localhost:3000` in your browser
3. Drag and drop your file (GPX, FIT, or CSV) or use the import page
4. View your workout with maps, analytics, and splits

### Bulk Import from Strava

Import your entire Strava history at once using a Strava data export ZIP file.

**ZIP File Structure:**
```
your-strava-export.zip
├── activities.csv          # Required: CSV file with activity metadata
└── activities/            # Folder containing workout files
    ├── 1234567890.gpx     # GPX files (supported)
    ├── 1234567891.fit.gz  # Gzipped FIT files (supported)
    └── ...
```

**Requirements:**
- `activities.csv` must be in the root of the ZIP file
- Workout files (`.gpx` or `.fit.gz`) should be in the `activities/` folder
- The CSV `Filename` column should reference the file path (e.g., `activities/1234567890.gpx`)
- Only "Run" activities are imported (other activity types are skipped)

**Steps:**
1. Request your data export from Strava (Settings → My Account → Download or Delete Your Account → Request Archive)
2. Extract and re-zip if needed to match the structure above
3. Go to the Import page in Tempo
4. Upload the ZIP file under "Bulk Import Strava Export" (files up to 500MB are supported)
5. Wait for processing to complete (you'll see a summary of imported/skipped workouts)

## Configuration

Tempo can be configured via `appsettings.json` (for local development) or environment variables (for Docker deployments).

### Database Connection

**Local Development (`appsettings.json`):**
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres"
  }
}
```

**Docker (Environment Variable):**
```bash
ConnectionStrings__DefaultConnection="Host=postgres;Port=5432;Database=tempo;Username=postgres;Password=postgres"
```

### Media Storage

**Local Development (`appsettings.json`):**
```json
{
  "MediaStorage": {
    "RootPath": "./media",
    "MaxFileSizeBytes": 52428800
  }
}
```

**Docker (Environment Variables):**
```bash
MediaStorage__RootPath="/app/media"
MediaStorage__MaxFileSizeBytes="52428800"  # 50MB default
```

### Elevation Calculation

Configure elevation smoothing thresholds:
```json
{
  "ElevationCalculation": {
    "NoiseThresholdMeters": 2.0,
    "MinDistanceMeters": 10.0
  }
}
```

### CORS Configuration

Allow specific origins for API access:
```json
{
  "CORS": {
    "AllowedOrigins": "http://localhost:3000,http://localhost:3004"
  }
}
```

In Docker, use: `CORS__AllowedOrigins="http://localhost:3000,http://localhost:3004"`

## Data Management

### Storage Locations

**Database:**
- PostgreSQL data is stored in Docker volumes (`postgres_data`) or your configured PostgreSQL instance
- Contains all workout metadata, routes, splits, time series data, and settings

**Media Files:**
- Stored in the `media/` directory (or configured `MediaStorage:RootPath`)
- Organized by workout GUID: `media/{workoutId}/filename.ext`
- Includes photos and videos attached to workouts

### Backup Recommendations

**Database Backup:**
```bash
# Using Docker
docker exec tempo-postgres pg_dump -U postgres tempo > backup.sql

# Restore
docker exec -i tempo-postgres psql -U postgres tempo < backup.sql
```

**Media Backup:**
- Copy the entire `media/` directory to your backup location
- Media files are referenced by workout GUID, so maintain the directory structure

**Complete Backup:**
For a complete backup, save both:
1. Database dump (as shown above)
2. Media directory (`./media`)

### Data Migration

Database migrations run automatically on API startup. If you need to manually apply migrations:

```bash
cd api
dotnet ef database update
```

## Deployment

For production deployment, use Docker Compose with the provided `docker-compose.prod.yml` file.

**Key differences from development:**
- Uses pre-built images from `ghcr.io/trevordavies095/tempo/api` and `ghcr.io/trevordavies095/tempo/frontend`
- Images are tagged with version numbers (e.g., `v1.2.0`) for stability
- Uses a dedicated Docker network (`tempo-network`) for service isolation
- Frontend runs on port 3004 by default (configurable)

**Environment Variables:**
Configure the following in `docker-compose.prod.yml` or via environment files:
- `ConnectionStrings__DefaultConnection` - PostgreSQL connection string
- `MediaStorage__RootPath` - Media storage path (default: `/app/media`)
- `MediaStorage__MaxFileSizeBytes` - Maximum upload size (default: 50MB)
- `CORS__AllowedOrigins` - Comma-separated list of allowed origins
- `ElevationCalculation__NoiseThresholdMeters` - Elevation smoothing threshold
- `ElevationCalculation__MinDistanceMeters` - Minimum distance for elevation calculation

**Data Persistence:**
- Database data is stored in the `postgres_data` Docker volume
- Media files are stored in the `./media` directory (mounted as a volume)
- Back up both the database volume and media directory for complete data protection

## API

Tempo provides a RESTful API for managing workouts, settings, and statistics. In development mode, interactive API documentation is available at `http://localhost:5001/swagger`.

### Workouts Endpoints

**Import:**
- `POST /workouts/import` - Import single or multiple GPX, FIT, or CSV workout files
- `POST /workouts/import/bulk` - Bulk import from Strava export ZIP file (up to 500MB)

**Workout Management:**
- `GET /workouts` - List all workouts with filtering and pagination
- `GET /workouts/{id}` - Get detailed workout information
- `PATCH /workouts/{id}` - Update workout (e.g., activity name)
- `DELETE /workouts/{id}` - Delete workout and associated data

**Workout Operations:**
- `POST /workouts/{id}/crop` - Crop/trim workout by removing time from start and/or end
- `POST /workouts/{id}/recalculate-effort` - Recalculate relative effort for a workout
- `POST /workouts/{id}/recalculate-splits` - Recalculate splits for a workout

**Statistics:**
- `GET /workouts/stats/weekly` - Get weekly statistics
- `GET /workouts/stats/yearly` - Get yearly statistics
- `GET /workouts/stats/relative-effort` - Get relative effort statistics
- `GET /workouts/stats/yearly-weekly` - Get combined yearly and weekly stats
- `GET /workouts/stats/available-periods` - Get available time periods for stats
- `GET /workouts/stats/available-years` - Get available years for stats

**Media:**
- `POST /workouts/{id}/media` - Upload media (photos/videos) to a workout
- `GET /workouts/{id}/media` - List all media for a workout
- `GET /workouts/{id}/media/{mediaId}` - Get/download specific media file
- `DELETE /workouts/{id}/media/{mediaId}` - Delete media file

### Settings Endpoints

- `GET /settings/heart-rate-zones` - Get current heart rate zone configuration
- `PUT /settings/heart-rate-zones` - Update heart rate zones (Age-based, Karvonen, or Custom)
- `POST /settings/heart-rate-zones/update-with-recalc` - Update zones and optionally recalculate all workouts
- `GET /settings/recalculate-relative-effort/count` - Get count of workouts eligible for recalculation
- `POST /settings/recalculate-relative-effort` - Recalculate relative effort for all qualifying workouts
- `GET /settings/unit-preference` - Get unit preference (metric/imperial)
- `PUT /settings/unit-preference` - Update unit preference
- `GET /settings/recalculate-splits/count` - Get count of workouts eligible for split recalculation
- `POST /settings/recalculate-splits` - Recalculate splits for all workouts

### System Endpoints

- `GET /version` - Get application version, build date, and git commit
- `GET /health` - Health check endpoint

### API Testing

A Bruno API testing collection is included in `api/bruno/Tempo.Api/` with test requests for all endpoints. Open the collection in [Bruno](https://www.usebruno.com/) to interactively test the API without requiring the frontend.

## Troubleshooting

### Database Migration Errors

If you encounter migration errors on startup:
- Ensure PostgreSQL is running and accessible
- Check connection string configuration
- Migrations are idempotent and handle existing tables gracefully
- For manual migration: `cd api && dotnet ef database update`

### Large File Upload Issues

- Bulk import supports files up to 500MB
- Ensure sufficient disk space for media storage
- Check `MediaStorage:MaxFileSizeBytes` configuration
- For very large imports, monitor API logs for progress

### CORS Errors

If you see CORS errors in the browser:
- Verify `CORS:AllowedOrigins` includes your frontend URL
- In Docker, use double underscores: `CORS__AllowedOrigins`
- Restart the API container after changing CORS settings

### Media Storage Permissions

If media uploads fail:
- Ensure the media directory exists and is writable
- Check `MediaStorage:RootPath` configuration
- In Docker, verify volume mount permissions

### Connection Issues

**Port Conflicts:**
- Default ports: Frontend (3000), API (5001), PostgreSQL (5432)
- Change ports in `docker-compose.yml` if conflicts occur

**Database Connection:**
- Verify PostgreSQL is running: `docker ps` or `pg_isready`
- Check connection string matches your setup
- Ensure network connectivity between services

## Support

- **Discord**: Join our community on [Discord](https://discord.gg/9Svd99npyj) for support and discussions
- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/trevordavies095/tempo/issues)
- **Changelog**: See [CHANGELOG.md](CHANGELOG.md) for version history and updates

## License

MIT License - see LICENSE file for details
