# API Reference

Complete reference for the Tempo REST API.

## Base URL

- **Development**: `http://localhost:5001`
- **Production**: Configured per deployment

## Authentication

Tempo uses JWT-based authentication with httpOnly cookies. Most endpoints require authentication.

### Register

Register the first user (only available when no users exist):

```http
POST /auth/register
Content-Type: application/json

{
  "username": "your-username",
  "password": "your-password",
  "confirmPassword": "your-password"
}
```

### Login

Authenticate and receive JWT token:

```http
POST /auth/login
Content-Type: application/json

{
  "username": "your-username",
  "password": "your-password"
}
```

The JWT token is stored in an httpOnly cookie.

### Get Current User

```http
GET /auth/me
```

Requires authentication.

### Logout

```http
POST /auth/logout
```

Clears the authentication cookie.

### Check Registration Availability

```http
GET /auth/registration-available
```

Returns whether registration is currently available.

## Workouts

### Import Workout

Import a single workout file (GPX, FIT, or CSV):

```http
POST /workouts/import
Content-Type: multipart/form-data

file: [workout file]
```

### Bulk Import

Import multiple workouts from a Strava export ZIP:

```http
POST /workouts/import/bulk
Content-Type: multipart/form-data

file: [ZIP file]
```

Supports files up to 500MB.

### Export All Data

Export all user data in a portable ZIP format:

```http
POST /workouts/export
```

**Authentication**: Required

**Response**: 
- Content-Type: `application/zip`
- Content-Disposition: `attachment; filename="tempo-export-{timestamp}.zip"`
- Body: ZIP file stream

The export includes:
- All workouts with complete data (stats, metadata, JSONB fields)
- Workout routes (GeoJSON)
- Workout splits and time series data
- Media files (photos and videos) as binary files
- Raw workout files (GPX, FIT, CSV) as binary files
- Shoes with GUIDs and relationships
- User settings (heart rate zones, unit preferences, default shoe)
- Best efforts

**Export Format Structure**:

```
tempo-export-{timestamp}.zip
├── manifest.json          # Export metadata and version info
├── data/                  # JSON files with all database records
│   ├── settings.json
│   ├── shoes.json
│   ├── workouts.json
│   ├── routes.json
│   ├── splits.json
│   ├── time-series.json
│   ├── media-metadata.json
│   └── best-efforts.json
├── workouts/              # Binary files organized by workout
│   ├── {workoutId}/
│   │   ├── raw/          # Original workout files
│   │   └── media/        # Photos and videos
│   │       └── {mediaId}/
│   │           └── {filename}
└── README.txt            # Export format documentation
```

**Manifest File** (`manifest.json`):

```json
{
  "version": "1.0.0",
  "tempoVersion": "1.4.0",
  "exportDate": "2024-01-15T10:30:00Z",
  "exportedBy": "username",
  "statistics": {
    "workouts": 150,
    "shoes": 5,
    "mediaFiles": 45,
    "totalSizeBytes": 1234567890,
    "settings": 1,
    "routes": 150,
    "splits": 1500,
    "timeSeries": 45000,
    "bestEfforts": 10
  },
  "dataFormat": {
    "settings": "data/settings.json",
    "shoes": "data/shoes.json",
    "workouts": "data/workouts.json",
    "routes": "data/routes.json",
    "splits": "data/splits.json",
    "timeSeries": "data/time-series.json",
    "mediaMetadata": "data/media-metadata.json",
    "bestEfforts": "data/best-efforts.json"
  }
}
```

**Notes**:
- Export may take several minutes for large datasets
- Missing media files are logged as warnings but don't fail the export
- GUIDs are preserved to maintain relationships
- Export format version is 1.0.0 (compatible with future import feature)

### List Workouts

Get all workouts with filtering and pagination:

```http
GET /workouts?startDate=2025-01-01&endDate=2025-12-31&page=1&pageSize=20
```

Query parameters:
- `startDate` - Filter by start date (ISO 8601)
- `endDate` - Filter by end date (ISO 8601)
- `page` - Page number (default: 1)
- `pageSize` - Items per page (default: 20)

### Get Workout

Get detailed workout information:

```http
GET /workouts/{id}
```

### Update Workout

Update workout details (e.g., activity name, shoe assignment):

```http
PATCH /workouts/{id}
Content-Type: application/json

{
  "activityName": "New Activity Name",
  "shoeId": "guid-here"
}
```

Fields:
- `activityName` (string, optional) - New activity name
- `shoeId` (Guid, optional, nullable) - Shoe ID to assign to this workout. Set to `null` to remove shoe assignment.

### Delete Workout

```http
DELETE /workouts/{id}
```

Permanently deletes the workout and all associated data.

### Crop Workout

Remove time from the start and/or end:

```http
POST /workouts/{id}/crop
Content-Type: application/json

{
  "removeFromStartSeconds": 60,
  "removeFromEndSeconds": 30
}
```

### Recalculate Relative Effort (Single Workout)

```http
POST /workouts/{id}/recalculate-effort
```

### Recalculate Splits (Single Workout)

```http
POST /workouts/{id}/recalculate-splits
```

### Get Recalculation Count (Relative Effort)

Get count of workouts eligible for relative effort recalculation:

```http
GET /workouts/recalculate-relative-effort/count
```

### Recalculate All Relative Effort

Recalculate relative effort for all qualifying workouts:

```http
POST /workouts/recalculate-relative-effort
```

### Get Recalculation Count (Splits)

Get count of workouts eligible for split recalculation:

```http
GET /workouts/recalculate-splits/count
```

### Recalculate All Splits

Recalculate splits for all workouts:

```http
POST /workouts/recalculate-splits
```

## Statistics

### Weekly Statistics

```http
GET /stats/weekly?startDate=2025-01-01&endDate=2025-01-07
```

### Yearly Statistics

```http
GET /stats/yearly?year=2025
```

### Relative Effort Statistics

```http
GET /stats/relative-effort?startDate=2025-01-01&endDate=2025-12-31
```

### Combined Yearly and Weekly Stats

```http
GET /stats/yearly-weekly?year=2025&weekStartDate=2025-01-01
```

### Available Periods

```http
GET /stats/available-periods
```

### Available Years

```http
GET /stats/available-years
```

### Best Efforts

Get your fastest times for standard distances:

```http
GET /stats/best-efforts
```

Returns your best effort times for all supported distances (400m, 1/2 mile, 1K, 1 mile, 2 mile, 5K, 10K, 15K, 10 mile, 20K, Half-Marathon, 30K, Marathon). Best efforts are calculated from any segment within any workout, not just workouts of that exact distance.

Response format:
```json
{
  "distances": [
    {
      "distance": "5K",
      "distanceM": 5000,
      "timeS": 1200,
      "workoutId": "guid-here",
      "workoutDate": "2025-01-15T10:30:00Z"
    }
  ]
}
```

### Recalculate Best Efforts

Recalculate all best efforts across all workouts:

```http
POST /stats/best-efforts/recalculate
```

Performs a full recalculation of all best efforts. This may take some time depending on the number of workouts and time series data.

Response format:
```json
{
  "message": "Best efforts recalculated successfully",
  "count": 13
}
```

## Media

### Upload Media

```http
POST /workouts/{id}/media
Content-Type: multipart/form-data

file: [image or video file]
```

Maximum file size: 50MB (configurable).

### List Media

```http
GET /workouts/{id}/media
```

### Get Media

```http
GET /workouts/{id}/media/{mediaId}
```

Returns the media file.

### Delete Media

```http
DELETE /workouts/{id}/media/{mediaId}
```

## Settings

### Get Heart Rate Zones

```http
GET /settings/heart-rate-zones
```

### Update Heart Rate Zones

```http
PUT /settings/heart-rate-zones
Content-Type: application/json

{
  "method": "Karvonen",
  "age": 30,
  "restingHeartRate": 60,
  "zones": [...]
}
```

### Update Zones with Recalculation

```http
POST /settings/heart-rate-zones/update-with-recalc
Content-Type: application/json

{
  "method": "Karvonen",
  "age": 30,
  "restingHeartRate": 60,
  "recalculateAll": true
}
```

### Get Unit Preference

```http
GET /settings/unit-preference
```

### Update Unit Preference

```http
PUT /settings/unit-preference
Content-Type: application/json

{
  "unit": "metric"
}
```

### Get Default Shoe

Get the currently set default shoe:

```http
GET /settings/default-shoe
```

Returns the default shoe object or `null` if no default is set.

### Set Default Shoe

Set a shoe as the default for automatic assignment to new workouts:

```http
PUT /settings/default-shoe
Content-Type: application/json

{
  "shoeId": "guid-here"
}
```

Set `shoeId` to `null` to remove the default shoe.

## Shoes

### List Shoes

Get all shoes with calculated mileage:

```http
GET /shoes
```

Returns a list of all shoes with their current total mileage (calculated from assigned workouts plus initial mileage).

### Create Shoe

Create a new shoe:

```http
POST /shoes
Content-Type: application/json

{
  "brand": "Nike",
  "model": "Pegasus 40",
  "initialMileageM": 0.0
}
```

Fields:
- `brand` (string, required, max 100 chars) - Shoe manufacturer
- `model` (string, required, max 100 chars) - Shoe model name
- `initialMileageM` (double, optional) - Initial mileage in meters when adding the shoe

### Update Shoe

Update shoe details:

```http
PATCH /shoes/{id}
Content-Type: application/json

{
  "brand": "Nike",
  "model": "Pegasus 41",
  "initialMileageM": 50.0
}
```

All fields are optional. Only provided fields will be updated.

### Delete Shoe

Delete a shoe:

```http
DELETE /shoes/{id}
```

When a shoe is deleted, all workouts assigned to that shoe will have their `shoeId` set to `null`. The workouts themselves are not deleted.

### Get Shoe Mileage

Get calculated total mileage for a specific shoe:

```http
GET /shoes/{id}/mileage?unitPreference=metric
```

Query parameters:
- `unitPreference` (string, optional) - "metric" or "imperial" (defaults to user's preference)

Returns the total mileage in the requested units (sum of all assigned workout distances plus initial mileage).

## System

### Version

```http
GET /version
```

Returns application version, build date, and git commit.

### Health Check

```http
GET /health
```

Public endpoint (no authentication required).

## Error Responses

All endpoints may return standard HTTP error codes:

- `400 Bad Request` - Invalid request data
- `401 Unauthorized` - Authentication required
- `403 Forbidden` - Insufficient permissions
- `404 Not Found` - Resource not found
- `500 Internal Server Error` - Server error

Error response format:

```json
{
  "error": "Error message",
  "details": "Additional error details"
}
```

## Interactive Documentation

In development mode, interactive API documentation is available at `/swagger`. This provides:
- Complete endpoint documentation
- Request/response examples
- Interactive testing interface
- XML documentation comments

## API Testing

A Bruno API testing collection is available in `api/bruno/Tempo.Api/` with test requests for all endpoints.

## Next Steps

- [Set up your development environment](setup.md)
- [Understand the database schema](database.md)
- [Review the contributing guide](contributing.md)

