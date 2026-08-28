# Configuration

Configure Tempo for your environment using configuration files or environment variables.

## Configuration Methods

Tempo can be configured via:

1. **Configuration files** (`appsettings.json`) - For local development
2. **Environment variables** - For Docker deployments and production

## Database Connection

### Local Development (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres"
  }
}
```

### Docker (Environment Variable)

```bash
ConnectionStrings__DefaultConnection="Host=postgres;Port=5432;Database=tempo;Username=postgres;Password=postgres"
```

**Note**: In Docker, use double underscores (`__`) for nested configuration keys.

## Media Storage

### Local Development (`appsettings.json`)

```json
{
  "MediaStorage": {
    "RootPath": "./media",
    "MaxFileSizeBytes": 52428800
  }
}
```

### Docker (Environment Variables)

```bash
MediaStorage__RootPath="/app/media"
MediaStorage__MaxFileSizeBytes="52428800"  # 50MB default
```

## Elevation Calculation

Configure elevation smoothing thresholds:

```json
{
  "ElevationCalculation": {
    "NoiseThresholdMeters": 2.0,
    "MinDistanceMeters": 10.0
  }
}
```

Or via environment variables:
```bash
ElevationCalculation__NoiseThresholdMeters="2.0"
ElevationCalculation__MinDistanceMeters="10.0"
```

## CORS Configuration

Allow specific origins for API access:

```json
{
  "CORS": {
    "AllowedOrigins": "http://localhost:3000,http://localhost:3004"
  }
}
```

In Docker:
```bash
CORS__AllowedOrigins="http://localhost:3000,http://localhost:3004"
```

## JWT Authentication

**Important**: Before deploying to production, you must set the JWT secret key.

### Generate a Secure JWT Secret Key

```bash
# Using OpenSSL (recommended)
openssl rand -base64 32
```

### Set in Docker Compose

```yaml
environment:
  JWT__SecretKey: "your-very-long-random-secret-key-here"
```

### JWT Configuration Options

- `JWT__SecretKey` - JWT signing key (REQUIRED in production, minimum 32 characters)
- `JWT__Issuer` - JWT issuer (default: "Tempo")
- `JWT__Audience` - JWT audience (default: "Tempo")
- `JWT__ExpirationDays` - Token expiration in days (default: 7)
- `JWT__RememberMeExpirationDays` - Token expiration when "Remember me" is enabled (default: 30)

### Security Requirements

- The JWT secret key should be at least 32 characters and cryptographically random
- Use HTTPS in production (required for secure cookie transmission)
- Change the default database password in production
- Store the JWT secret key securely (environment variables, secrets manager, etc.)

## CARTO Basemaps (Maps)

Workout maps use CARTO raster tiles (Dark Matter / Voyager). CARTO now requires a free API key; without one, tiles show an "API key required" watermark.

1. Request a key at [carto.com/basemaps/apikey](https://carto.com/basemaps/apikey) (free within CARTO's fair use limit; no CARTO account required).
2. Configure the key on the **API** service (not the frontend). Do not commit the key to git.

### Docker

Add to a gitignored `.env` file in your deployment directory:

```bash
CARTO_BASEMAPS_API_KEY=your-key-here
```

Both `docker-compose.yml` and `docker-compose.prod.yml` pass this to the API as `CartoBasemaps__ApiKey`. Restart the API after changing the key.

### Local development

Set the environment variable before starting the API:

```bash
export CARTO_BASEMAPS_API_KEY=your-key-here
cd api && dotnet watch run
```

Or add to `api/appsettings.Development.json` (local only; do not commit real keys):

```json
{
  "CartoBasemaps": {
    "ApiKey": "your-key-here"
  }
}
```

After configuring, hard-refresh the browser — CARTO and your browser may cache watermarked tiles briefly.

## Environment Variables Reference

All configuration can be set via environment variables using the double underscore (`__`) notation for nested keys:

| Configuration Key | Environment Variable | Default |
|------------------|---------------------|---------|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | - |
| `MediaStorage:RootPath` | `MediaStorage__RootPath` | `./media` |
| `MediaStorage:MaxFileSizeBytes` | `MediaStorage__MaxFileSizeBytes` | `52428800` |
| `ElevationCalculation:NoiseThresholdMeters` | `ElevationCalculation__NoiseThresholdMeters` | `2.0` |
| `ElevationCalculation:MinDistanceMeters` | `ElevationCalculation__MinDistanceMeters` | `10.0` |
| `CartoBasemaps:ApiKey` | `CartoBasemaps__ApiKey` or `CARTO_BASEMAPS_API_KEY` (via compose `.env`) | empty (watermarked tiles) |
| `CORS:AllowedOrigins` | `CORS__AllowedOrigins` | - |
| `JWT:SecretKey` | `JWT__SecretKey` | - (REQUIRED in production) |
| `JWT:Issuer` | `JWT__Issuer` | `Tempo` |
| `JWT:Audience` | `JWT__Audience` | `Tempo` |
| `JWT:ExpirationDays` | `JWT__ExpirationDays` | `7` |
| `JWT:RememberMeExpirationDays` | `JWT__RememberMeExpirationDays` | `30` |

## Next Steps

- [Import your first workout](../user-guide/importing-workouts.md)
- Learn about [production deployment](../deployment/production.md)

