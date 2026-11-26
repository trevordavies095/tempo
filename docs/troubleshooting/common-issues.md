# Common Issues

Each entry: **symptom → cause → fix**. Quoted strings are the exact UI or log text. Read API logs with `docker compose logs -f api` (add `-f docker-compose.prod.yml` for production).

## Installation and Startup

### Containers exit or never become healthy

- **Cause:** missing required env, database not ready, or a port in use.
- **Fix:**
  1. `docker compose logs api postgres`.
  2. Production Compose refuses to start with `Set JWT_SECRET_KEY in .env (copy .env.example)` or `Set POSTGRES_PASSWORD in .env …`. Fill in `.env`.
  3. API exits with `JWT:SecretKey must be changed from the default placeholder value.` Set a real random key.
  4. `curl http://localhost:5001/ready` returns `503` until Postgres answers within 2 s; `/health` only checks the process.

### Port is already allocated

- **Cause:** something else holds 3000/5001/5432 (dev Compose) or 3004 (prod Compose, bound to `127.0.0.1`).
- **Fix:** `lsof -i :3000` to find it, stop it, or change the host side of the `ports:` mapping (`"3001:3000"`).

### Database connection or migration errors

- **Cause:** wrong `ConnectionStrings__DefaultConnection`, Postgres down, or a schema the migrator cannot reconcile.
- **Fix:** `docker compose ps postgres`; check the password in `.env` matches the one the volume was initialized with (changing `POSTGRES_PASSWORD` later does not change an existing database's password). Migrations run on startup; manually: `dotnet tool restore` at the repo root, then `cd api && dotnet ef database update`. PostgreSQL 16.

### Startup warns that backfills have not completed

- **Log:** `Startup backfills: N of 8 have not completed on this library (...)`.
- **Cause:** an upgrade skipped versions, or a restore was cut short.
- **Fix:** `docker compose exec api dotnet Tempo.Api.dll backfill` once. See [Startup backfills](../getting-started/configuration.md#startup-backfills).

## Login and Sessions

### "Invalid username or password"

- **Cause:** wrong credentials (the API returned 401).
- **Fix:** if the password is lost, use the host-only reset: [How do I reset my password?](faq.md#how-do-i-reset-my-password). Do not `docker compose down -v` or hand-hash BCrypt.

### Login succeeds but you are immediately logged out

- **Log:** `Auth cookie is Secure but this request arrived over plain HTTP; the browser will drop it and the session will not stick.`
- **Cause:** serving over plain HTTP, or a proxy not sending `X-Forwarded-Proto`.
- **Fix:** serve over HTTPS behind a proxy that forwards the scheme, or on a trusted LAN set `TEMPO_COOKIE_SECURE=false`. Leave `Cookie__Domain` unset unless you share sessions across subdomains. See [Reverse Proxy and Login Cookies](../deployment/reverse-proxy.md#troubleshooting-login).

### Everyone is signed out after a restart

- **Cause:** `JWT__SecretKey` changed. This also makes the stored intervals.icu key and API-key reveal unreadable.
- **Fix:** keep the same `JWT_SECRET_KEY` across moves and restores. If it already changed, log in again, replace the intervals.icu key, and regenerate the API key if you need to see it.

### "Registration is disabled. An account already exists."

- **Cause:** Tempo is single-user; registration closes after the first account.
- **Fix:** log in with that account, or reset its password from the host.

### Every page redirects to onboarding

- **Cause:** `User.OnboardingCompleted` is false; app routes are gated until setup finishes.
- **Fix:** finish restore or essentials and answer the Strava question, or **Skip for now** after a failed Strava job. See [First-run onboarding](../getting-started/onboarding.md). Late ZIPs go under Settings → Migrate / restore.

### CORS errors in the browser console

- **Cause:** a browser on another origin is calling the API directly. The command center uses same-origin `/api` and the iOS app is native, so neither needs CORS.
- **Fix:** add that exact origin (scheme, host, port) to `CORS__AllowedOrigins` (comma-separated) and restart the API.

## Single-File Import

| Symptom (UI `Error: …`) | Cause | Fix |
|---|---|---|
| `File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)` | Other extension (CSV, TCX, ZIP) | Export GPX/FIT. ZIPs go under Settings → Migrate / restore. |
| `No track points found in GPX file`, `GPX file must contain at least 2 track points`, `GPX file must contain timestamps` | GPX route/course, not a recorded activity | Export the recorded activity. |
| `Not a valid FIT file`, `Error parsing FIT file: …` | Corrupt or non-activity FIT | Re-copy from the device. |
| `FIT file contains no GPS data and no distance information` | Manual or strength entry | Nothing to import. |
| Import "succeeds" but shows the old Workout | Duplicate (start time, distance within 1 m, elapsed within 1 s) | Expected: it was skipped or updated. |

API log lines: `Error parsing workout file`, `Error persisting workout`, `Skipped duplicate workout (already has complete raw data): …`. See [Importing Workouts](../user-guide/importing-workouts.md#troubleshooting).

## Strava Bulk Import and Tempo Restore

| Symptom | Cause | Fix |
|---|---|---|
| `File must be a ZIP file` | Not a `.zip` name | Upload the ZIP itself. |
| `File exceeds the 500MB limit` | Over 500,000,000 bytes | Strava: split the archive. Tempo: restore the database and media directly ([Backup and Restore](../deployment/backup-restore.md)). |
| `ZIP file must contain activities.csv in the root` | Strava ZIP re-zipped with a parent folder | Zip the contents. |
| `Import failed: manifest.json not found in export ZIP` | Not a Tempo export | Use the file from Export All Data. |
| `A Tempo export restore is already in progress…` / `A Strava import is already in progress…` | One import job at a time | Finish or cancel the other job. |
| Error `interrupted` | API restarted during the job; jobs do not resume | Re-upload; duplicates skip. |
| Error `cancelled` | **Cancel import** | Re-upload to continue. |
| Fewer Workouts than in Strava | Only `Activity Type` = `Run` rows are imported; missing files error | Check **View error details**. |
| Progress looks stuck | Large archives process one activity at a time; server polled every second | Watch `processed/total`; refresh Settings to reattach. Check `docker stats`. |
| Chunk errors such as `Chunks must be uploaded sequentially` | Two tabs uploading, or a hand-rolled client | Upload from one tab. Scripts can use `POST /workouts/import/bulk` with the whole ZIP. |

Details: [Bulk Import](../user-guide/bulk-import.md), [Migrate / restore](../user-guide/settings.md#migrate--restore).

## Workout Data

### No map on a Workout

- **Cause:** the file has no GPS (treadmill, indoor). Crop and similar runs also need a route.
- **Fix:** none; expected.

### Map tiles show "API key required"

- **Cause:** CARTO basemaps need a key.
- **Fix:** set `CARTO_BASEMAPS_API_KEY` in `.env`, restart the API, hard-refresh. See [CARTO Basemaps](../getting-started/configuration.md#carto-basemaps-maps).

### No weather on a Workout

- **Cause:** no GPS point (weather uses the first one), or Open-Meteo had no data for that hour or was unreachable. Weather is fetched once, at import.
- **Log:** `Failed to fetch weather from Open-Meteo API`, `Could not find matching hour in Open-Meteo response`, `Open-Meteo response missing hourly data`.
- **Fix:** check the API container can reach `archive-api.open-meteo.com`.

### No Relative Effort or zone bar

- **Cause:** no heart-rate data, or zones changed and you chose **Keep Existing Unchanged**.
- **Fix:** Settings → Data Recalculation → **Recalculate Relative Effort**. Per-Workout API: `POST /workouts/{id}/recalculate-effort` returns `Heart rate zones not configured. Please configure heart rate zones in settings first.` if no zones exist.

### Splits are in the wrong unit

- **Cause:** units changed after import; existing splits are not rebuilt automatically.
- **Fix:** Settings → Data Recalculation → **Recalculate Splits**. Device laps are not affected.

### Best efforts missing or stale

- **Cause:** a restore's repair pass is still running, or an upgrade backfill has not run.
- **Fix:** wait for the restore job to finish, or click the refresh button on the Dashboard's Best Efforts card.

## intervals.icu Sync

| Symptom (card **Last error**) | Cause | Fix |
|---|---|---|
| `intervals.icu rejected the API key` (sync disabled) | Key revoked or wrong | Fix in intervals.icu, then **Re-enable**, or **Replace key**. |
| `Could not reach intervals.icu. Try again.` (on connect) | Network or intervals.icu down | Retry; check outbound HTTPS from the API container. |
| `Could not list intervals.icu activities.` | Transient API error or rate limit | Next tick retries (every 15 min). |
| `Could not decrypt the stored API key.` | `JWT__SecretKey` changed | **Replace key**. |
| `N activities failed to persist` | Intake errors for some files | API log: `intervals.icu activity {ActivityId} persist failed: {Error}`. |
| A run never appears | Not a `Run`-type activity, no FIT/GPX original, or older than the sync window (2 days before the last success, max 14 days) | Upload the file manually. |

See [intervals.icu](../user-guide/settings.md#intervalsicu).

## Media

| Symptom | Cause | Fix |
|---|---|---|
| `No files were successfully uploaded` | Over 50 MB, unsupported type (e.g. HEIC), or the media directory is not writable | API log names the reason (`Media file exceeds size limit`, `Unsupported media file type`, `Error uploading media file`). Compress/convert, raise `MediaStorage__MaxFileSizeBytes`, or fix the volume. |
| Broken thumbnail | File missing on disk | Restore the media directory, or delete the item (logs `Media file not found on filesystem (orphaned record)`). |

See [Media](../user-guide/media.md).

## Performance

### Slow pages

- **Cause:** undersized host, or Postgres starved of memory.
- **Fix:** `docker stats`; give the database more RAM; run `VACUUM ANALYZE`. Long Workouts load up to 20,000 time-series samples in pages.

### High CPU during import

- **Cause:** imports compute geometry, weather, zones, and Best efforts per Workout; restores also run every backfill.
- **Fix:** expected while a job runs. Avoid restarting the API mid-job (it marks the job `interrupted`).

## Next Steps

- [FAQ](faq.md)
- [Discord](https://discord.gg/9Svd99npyj)
- [Report an issue](https://github.com/trevordavies095/tempo/issues) (include the Settings → System Information block)
