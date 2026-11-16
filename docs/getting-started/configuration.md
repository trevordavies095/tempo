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
- Use HTTPS in production (session cookies are `Secure`; see [Reverse Proxy and Login Cookies](../deployment/reverse-proxy.md))
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

## Logging profiles

API console verbosity is controlled by one named profile — host config (environment / appsettings), restart to change. Not a Settings UI toggle. Do not use Microsoft.Extensions.Logging `Logging:LogLevel` or `Serilog__MinimumLevel__Override__…` as the public interface; the named profile owns Serilog levels.

| Profile | Behavior |
|---------|----------|
| `standard` (default) | Tempo Information stays (imports, backfills, auth, media). Microsoft / System at Warning (hosting lifetime still prints). No EF SQL dumps. Request middleware logs 5xx and unhandled exceptions only. |
| `debug` | EF SQL and non-probe request traces at Information. Profile name `debug` is **not** Serilog level Debug. |

Successful `/health` and `/ready` request lines are omitted on both profiles (failed probes still log). Empty or unset profile → `standard`. Unknown values refuse to start (allowed: `standard`, `debug`; case-insensitive).

### Docker

Official Compose passes `Tempo__Logging__Profile` from `.env`:

```bash
TEMPO_LOGGING_PROFILE=standard
```

Restart the API after changing. Official Compose Postgres also runs with `log_checkpoints=off`; setting `debug` does **not** turn checkpoints back on (see [production logging](../deployment/production.md#logging)).

### Local development

```bash
export Tempo__Logging__Profile=debug
cd api && dotnet watch run
```

Or in `api/appsettings.Development.json`:

```json
{
  "Tempo": {
    "Logging": {
      "Profile": "debug"
    }
  }
}
```

Unset still means `standard` even when `ASPNETCORE_ENVIRONMENT=Development`.

## Startup backfills

Some upgrades add data that older workouts don't have yet (route previews, timer time, device laps, zone seconds, best efforts, ...). Tempo fills it in with backfills. Each logs `<name> backfill: N of N` and, once a run finds nothing to do (`0 of 0`), is recorded complete in the `CompletedBackfills` table.

**Normal starts run none.** After migrations, one query checks the records:

| Startup log | Meaning |
|-------------|---------|
| `Startup backfills: all 8 complete; none run` | Nothing to do. |
| `Startup backfills: no workouts yet, nothing to fill; recorded all 8 complete` | New install. |
| `WRN Startup backfills: N of 8 have not completed on this library (...)` | An install that skipped versions, or a restore cut short. Run the command below once. |

### When backfills run

- **Tempo ZIP restore**: repairs the restored workouts before the job reports **Completed**, so a finished restore is fully usable without a restart.
- **The `backfill` command**: runs every backfill not yet recorded, records them, and exits. Safe while the API is running.
  ```bash
  docker compose -f docker-compose.prod.yml exec api dotnet Tempo.Api.dll backfill
  # rerun everything, even recorded ones:
  docker compose -f docker-compose.prod.yml exec api dotnet Tempo.Api.dll backfill --force
  ```
  Exit code 0 when all are recorded complete, 1 otherwise (see the log).
- **Opt-in at startup** (for hosts where you can't run a command in the container), via `Tempo:Backfills:OnStartup`:

| Value | Behavior |
|-------|----------|
| `skip` (default) | Run none; check and warn as above. |
| `run` | Run the pending ones in the background once, without blocking browsing or `/ready`. |
| `force` | Run all of them in the background once, recorded or not. |

Empty or unset → `skip`. Unknown values refuse to start (allowed: `skip`, `run`, `force`; case-insensitive). Host config only; restart to change. Set `run` back to `skip` (or remove it) once the log says `Backfills finished: all 8 recorded complete`.

```bash
# Compose .env, for one start
TEMPO_BACKFILLS_ON_STARTUP=run
```

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
| `Tempo:Logging:Profile` | `Tempo__Logging__Profile` or `TEMPO_LOGGING_PROFILE` (via compose `.env`) | `standard` |
| `Tempo:Backfills:OnStartup` | `Tempo__Backfills__OnStartup` or `TEMPO_BACKFILLS_ON_STARTUP` (via compose `.env`) | `skip` |
| `CORS:AllowedOrigins` | `CORS__AllowedOrigins` | `http://localhost:3000` |
| `Cookie:Secure` | `Cookie__Secure` or `TEMPO_COOKIE_SECURE` (via compose `.env`) | unset (always `Secure` in Production) — see [Reverse Proxy](../deployment/reverse-proxy.md) |
| `Cookie:Domain` | `Cookie__Domain` | unset (host-only cookie) |
| `Tempo:Proxy:TrustedNetworks` | `Tempo__Proxy__TrustedNetworks` | loopback + private ranges |
| `JWT:SecretKey` | `JWT__SecretKey` | - (REQUIRED in production) |
| `JWT:Issuer` | `JWT__Issuer` | `Tempo` |
| `JWT:Audience` | `JWT__Audience` | `Tempo` |
| `JWT:ExpirationDays` | `JWT__ExpirationDays` | `7` |
| `JWT:RememberMeExpirationDays` | `JWT__RememberMeExpirationDays` | `30` |

## Next Steps

- [Import your first workout](../user-guide/importing-workouts.md)
- Learn about [production deployment](../deployment/production.md)

