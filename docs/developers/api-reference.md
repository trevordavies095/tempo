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

Update workout details (e.g., activity name):

```http
PATCH /workouts/{id}
Content-Type: application/json

{
  "activityName": "New Activity Name"
}
```

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

### Recalculate Relative Effort

```http
POST /workouts/{id}/recalculate-effort
```

### Recalculate Splits

```http
POST /workouts/{id}/recalculate-splits
```

## Statistics

### Weekly Statistics

```http
GET /workouts/stats/weekly?startDate=2025-01-01&endDate=2025-01-07
```

### Yearly Statistics

```http
GET /workouts/stats/yearly?year=2025
```

### Relative Effort Statistics

```http
GET /workouts/stats/relative-effort?startDate=2025-01-01&endDate=2025-12-31
```

### Combined Yearly and Weekly Stats

```http
GET /workouts/stats/yearly-weekly?year=2025&weekStartDate=2025-01-01
```

### Available Periods

```http
GET /workouts/stats/available-periods
```

### Available Years

```http
GET /workouts/stats/available-years
```

### Best Efforts

Get your fastest times for standard distances:

```http
GET /workouts/stats/best-efforts
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
POST /workouts/stats/best-efforts/recalculate
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

### Get Recalculation Count

```http
GET /settings/recalculate-relative-effort/count
```

### Recalculate All Relative Effort

```http
POST /settings/recalculate-relative-effort
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

### Get Split Recalculation Count

```http
GET /settings/recalculate-splits/count
```

### Recalculate All Splits

```http
POST /settings/recalculate-splits
```

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

