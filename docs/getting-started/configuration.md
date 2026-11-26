# Configuration

The authoritative reference for every setting the Tempo API reads. Other pages link here instead of re-listing keys. Install steps live in [Docker Deployment](../deployment/docker.md) (production) and [Quick Start](quick-start.md) (local trial).

All settings are **host config** for the API process: read once at startup, so restart the API after changing one. With Compose, "restart" means `docker compose -f <file> up -d api`, which recreates the container with the new `.env`; `docker compose restart` keeps the old environment. None of them are in the Settings UI. The frontend container has no runtime settings (see [Frontend](#frontend)).

## How configuration is read

The API uses standard ASP.NET Core configuration. Later sources override earlier ones:

1. `api/appsettings.json` (shipped defaults)
2. `api/appsettings.{Environment}.json` (`appsettings.Development.json` sets a local-only JWT key)
3. Environment variables

Nested keys use `:` in JSON and `__` (double underscore) in environment variables: `JWT:SecretKey` ↔ `JWT__SecretKey`.

`ASPNETCORE_ENVIRONMENT` picks the environment. The API image does not set it, so containers run as `Production`. `dotnet run` / `dotnet watch run` use `Development` (from `api/Properties/launchSettings.json`). `Development` enables Swagger (`/swagger`) and loads `appsettings.Development.json`; `Production` makes the session cookie always `Secure`.

### Production Compose `.env`

`docker-compose.prod.yml` reads these from `.env` (copy `.env.example`) and maps them to API keys. Compose refuses to start if a required one is missing or empty.

| `.env` variable | Sets | Required | Default |
|---|---|---|---|
| `JWT_SECRET_KEY` | `JWT__SecretKey` | Yes | none |
| `POSTGRES_PASSWORD` | Postgres `POSTGRES_PASSWORD` and the `Password=` in `ConnectionStrings__DefaultConnection` | Yes | none |
| `CARTO_BASEMAPS_API_KEY` | `CartoBasemaps__ApiKey` | No | empty |
| `TEMPO_LOGGING_PROFILE` | `Tempo__Logging__Profile` | No | `standard` |
| `TEMPO_BACKFILLS_ON_STARTUP` | `Tempo__Backfills__OnStartup` | No | `skip` |
| `TEMPO_COOKIE_SECURE` | `Cookie__Secure` | No | unset |

Any other key: add it under `services.api.environment` in your Compose file using the `__` form, then `docker compose -f docker-compose.prod.yml up -d`.

The development `docker-compose.yml` passes only `TEMPO_LOGGING_PROFILE` and `CARTO_BASEMAPS_API_KEY` from `.env`; everything else is hard-coded in the file.

## Reference

Defaults are the values in code (`api/Program.cs` and the files named below), which match `appsettings.json` unless noted.

### Core

| Key (`appsettings`) | Environment variable | Default | Required | What it does |
|---|---|---|---|---|
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | `Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres` (appsettings.json) | Yes | Npgsql connection string. PostgreSQL only: empty or `Data Source=` (SQLite) strings fail startup. Production Compose builds it from `POSTGRES_PASSWORD`; do not use `;` in the password. |
| `JWT:SecretKey` | `JWT__SecretKey` | Placeholder `CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE` (rejected at startup) | Yes | HS256 signing key for session tokens. Also the root of the keys that encrypt the stored intervals.icu API key and Tempo API key plaintext (`api/Services/SecretProtector.cs`). Use a random value of at least 32 bytes: `openssl rand -base64 32`. See [Rotating the JWT secret](#rotating-the-jwt-secret). |
| `JWT:Issuer` | `JWT__Issuer` | `Tempo` | No | Token issuer, validated on every request. |
| `JWT:Audience` | `JWT__Audience` | `Tempo` | No | Token audience, validated on every request. |
| `JWT:ExpirationDays` | `JWT__ExpirationDays` | `7` | No | Session lifetime in days. |
| `JWT:RememberMeExpirationDays` | `JWT__RememberMeExpirationDays` | `30` | No | Session lifetime when "Remember me" is checked. |
| `ASPNETCORE_URLS` | `ASPNETCORE_URLS` | `http://+:5001` (API image) | No | Listen address. The frontend image's `/api` rewrite expects `api:5001`; leave it. |
| `ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | `Production` (unset) | No | See [How configuration is read](#how-configuration-is-read). |

### Storage and processing

| Key | Environment variable | Default | Required | What it does |
|---|---|---|---|---|
| `MediaStorage:RootPath` | `MediaStorage__RootPath` | `./media` (relative to the working directory; `/app/media` in Compose) | No | Directory for WorkoutMedia files (one subdirectory per Workout) and import job staging (`imports/<job id>/`). Created at startup. |
| `MediaStorage:MaxFileSizeBytes` | `MediaStorage__MaxFileSizeBytes` | `52428800` (50 MB) | No | Maximum size of one uploaded photo or video. Does not affect workout files or ZIPs. |
| `ElevationCalculation:NoiseThresholdMeters` | `ElevationCalculation__NoiseThresholdMeters` | `2.0` | No | A continuous climb counts toward elevation gain only if it rises at least this many meters. Smaller rises are treated as noise. |
| `ElevationCalculation:MinDistanceMeters` | `ElevationCalculation__MinDistanceMeters` | `10.0` | No | Minimum horizontal distance a climb must cover to count toward elevation gain (together with the noise threshold). |
| `Tempo:Backfills:OnStartup` | `Tempo__Backfills__OnStartup` | `skip` | No | `skip`, `run`, or `force` (case-insensitive; other values fail startup). See [Startup backfills](#startup-backfills). |

### Integrations

| Key | Environment variable | Default | Required | What it does |
|---|---|---|---|---|
| `CartoBasemaps:ApiKey` | `CartoBasemaps__ApiKey` | empty | No | CARTO basemap key handed to the logged-in command center for map tiles. Empty → watermarked tiles. See [CARTO Basemaps](#carto-basemaps-maps). |
| `IntervalsIcu:PollIntervalMinutes` | `IntervalsIcu__PollIntervalMinutes` | `15` (minimum `1`) | No | Intervals.icu sync poll interval. The Intervals.icu connection itself is set in Settings. Not in `appsettings.json`. |

### Session cookie, proxy, CORS

Context and troubleshooting: [Reverse Proxy and Login Cookies](../deployment/reverse-proxy.md).

| Key | Environment variable | Default | Required | What it does |
|---|---|---|---|---|
| `Cookie:Secure` | `Cookie__Secure` | unset | No | Unset: the `authToken` cookie is always `Secure` in `Production`; in other environments it follows the request scheme. `true` / `false` forces it (`false` logs a warning at startup). Other values fail startup. Only set `false` for plain HTTP on a trusted LAN. |
| `Cookie:Domain` | `Cookie__Domain` | unset (host-only cookie) | No | Leave unset. Set only to share the session across subdomains. Never an IP address. |
| `Tempo:Proxy:TrustedNetworks` | `Tempo__Proxy__TrustedNetworks` | `127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7` | No | Comma-separated CIDRs whose `X-Forwarded-For` / `X-Forwarded-Proto` are honored (one hop). Invalid entries fail startup. |
| `CORS:AllowedOrigins` | `CORS__AllowedOrigins` | `http://localhost:3000` | No | Comma-separated browser origins allowed to call the API cross-origin (credentials allowed). Not needed when the command center uses its same-origin `/api` rewrite; production Compose does not set it. |

### Logging

| Key | Environment variable | Default | Required | What it does |
|---|---|---|---|---|
| `Tempo:Logging:Profile` | `Tempo__Logging__Profile` | `standard` | No | `standard` or `debug` (case-insensitive; other values fail startup). See [Logging profiles](#logging-profiles). |

### Informational (set by the image)

| Environment variable | Default | What it does |
|---|---|---|
| `TEMPO_VERSION`, `TEMPO_BUILD_DATE`, `TEMPO_GIT_COMMIT` | Baked in at image build | Reported by `GET /version`; `TEMPO_VERSION` is also written into Tempo export manifests. Falls back to the `VERSION` file. |
| `TEMPO_API_IMAGE`, `TEMPO_FRONTEND_IMAGE` | unset (`unknown`) | Optional image references reported by `GET /version`. |

### Fixed limits (not configurable)

Hard-coded in `api/Program.cs` and `api/Models/ImportJob.cs`:

- Kestrel max request body and multipart body: 500,000,000 bytes. No minimum data rate; keep-alive 10 minutes.
- Import job archive (Strava ZIP or Tempo export): 500,000,000 bytes, uploaded in 512 KiB chunks.

## Rotating the JWT secret

Changing `JWT:SecretKey`:

- Signs everyone out (existing tokens no longer validate).
- Makes the stored intervals.icu API key unreadable. Reconnect Intervals.icu in Settings afterwards.
- Makes stored Tempo API key plaintext unreadable for reveal in Settings. Existing keys still authenticate (that uses a hash); create a new key if you need to see it again.

Keep the same `JWT_SECRET_KEY` when you move or restore an install (see [Backup and Restore](../deployment/backup-restore.md)).

## CARTO Basemaps (Maps)

Workout maps use CARTO raster tiles (Dark Matter / Voyager). Without a key, tiles show an "API key required" watermark.

1. Request a free key at [carto.com/basemaps/apikey](https://carto.com/basemaps/apikey).
2. Set it on the **API** (the command center fetches it from `GET /settings/carto-basemaps` after login). Do not commit it.

Compose (either file): add to `.env`, then restart the API:

```bash
CARTO_BASEMAPS_API_KEY=your-key-here
```

Local API:

```bash
export CartoBasemaps__ApiKey=your-key-here
cd api && dotnet watch run
```

Hard-refresh the browser afterwards; watermarked tiles may be cached briefly.

## Logging profiles

One named profile controls API console verbosity. Do not use `Logging:LogLevel` or `Serilog__MinimumLevel__Override__…`; the profile owns Serilog levels.

| Profile | Behavior |
|---------|----------|
| `standard` (default) | Tempo Information stays (imports, backfills, auth, media). Microsoft / System at Warning (hosting lifetime still prints). No EF SQL. Request logging: 5xx and unhandled exceptions only. |
| `debug` | Adds EF SQL and non-probe request traces at Information. The name `debug` is **not** Serilog level Debug. |

Successful `/health` and `/ready` requests are never logged (failed probes are). Empty or unset → `standard`, including under `Development`. The API logs `Logging profile: <name>` once at startup.

Compose: `TEMPO_LOGGING_PROFILE=debug` in `.env`. Local: `export Tempo__Logging__Profile=debug`.

Official Compose runs Postgres with `log_checkpoints=off`; the `debug` profile does not change that (see [production logging](../deployment/production.md#logging)).

## Startup backfills

Some upgrades add derived data that older Workouts lack (route previews, split heart rate, FIT cadence, timer time, device laps, zone seconds, best efforts, device names). Backfills fill it in. Each logs `<name> backfill: N of N`; once a run finds nothing to do, it is recorded complete in the `CompletedBackfills` table.

**Normal starts run none.** After migrations, one query checks the records:

| Startup log | Meaning |
|-------------|---------|
| `Startup backfills: all 8 complete; none run` | Nothing to do. |
| `Startup backfills: no workouts yet, nothing to fill; recorded all 8 complete` | New install. |
| `WRN Startup backfills: N of 8 have not completed on this library (...)` | An install that skipped versions, or a restore cut short. Run the `backfill` command once. |

Backfills run from:

- **Tempo ZIP restore.** Repairs restored Workouts before the import job reports **Completed**; no restart needed.
- **The `backfill` command.** Runs every backfill not yet recorded, records them, exits. Safe while the API is running. Exit code 0 when all are recorded, 1 otherwise.
  ```bash
  docker compose -f docker-compose.prod.yml exec api dotnet Tempo.Api.dll backfill
  # rerun all, even recorded ones:
  docker compose -f docker-compose.prod.yml exec api dotnet Tempo.Api.dll backfill --force
  ```
- **`Tempo:Backfills:OnStartup`**, for hosts where you cannot exec into the container:

| Value | Behavior |
|-------|----------|
| `skip` (default) | Run none; check and warn as above. |
| `run` | Run the pending ones once in the background. Does not block browsing or `/ready`. |
| `force` | Run all of them once in the background, recorded or not. |

Set `TEMPO_BACKFILLS_ON_STARTUP=run` in `.env` for one start, then remove it once the log says `Backfills finished: all 8 recorded complete`.

## Frontend

The frontend image has no runtime settings. Its `/api/*` rewrite (`frontend/next.config.ts`) targets `API_SERVICE_URL` if set when the image is **built**, else `http://api:5001` in production builds and `http://localhost:5001` under `npm run dev`. With the published images, the API service must be reachable as `api:5001` on the Compose network.

## Next Steps

- [Production deployment](../deployment/docker.md)
- [Reverse proxy and login cookies](../deployment/reverse-proxy.md)
