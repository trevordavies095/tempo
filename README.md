# Tempo - Self-Hostable Running Tracker

> A privacy-first, self-hosted Strava alternative. Import GPX, FIT, and CSV files from Garmin, Apple Watch, Strava, and more. Keep all your data local—no subscriptions, no cloud required.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Version](https://img.shields.io/badge/version-1.2.0-green.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Next.js](https://img.shields.io/badge/Next.js-16-black.svg)

## Screenshots

![Dashboard](https://i.imgur.com/pURdx2e.png)
*Dashboard view*

![My Activities](https://i.imgur.com/nZEt9mN.png)
*Activities list*

![Activity Details](https://i.imgur.com/aj671gl.png)
*Activity details view*

## Quick Start

Get Tempo running in minutes with Docker Compose:

```bash
# Clone the repository
git clone https://github.com/trevordavies095/tempo.git
cd tempo

# Start all services
docker-compose up -d

# Access the application
# Frontend: http://localhost:3000
# API: http://localhost:5001
```

That's it! The database migrations run automatically on first startup.

## Features

- **Multi-Format Support** - Import GPX, FIT (.fit, .fit.gz), and Strava CSV files from Garmin, Apple Watch, and other devices
- **Workout Analytics** - Track distance, pace, elevation, splits, and time series data
- **Interactive Maps** - Visualize routes with elevation profiles
- **Media Support** - Attach photos and videos to workouts
- **Weather Data** - Automatic weather conditions for each workout
- **Bulk Import** - Import multiple workouts at once via ZIP file
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
4. Upload the ZIP file under "Bulk Import Strava Export"
5. Wait for processing to complete (you'll see a summary of imported/skipped workouts)

## Deployment

For production deployment, use Docker Compose with the provided `docker-compose.prod.yml` file. Configure environment variables as needed for your deployment environment.

## API

- `POST /workouts/import` - Import GPX, FIT, or CSV workout files
- `GET /workouts` - List all workouts
- `GET /workouts/{id}` - Get workout details
- `GET /health` - Health check endpoint

## License

MIT License - see LICENSE file for details
