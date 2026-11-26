# Frequently Asked Questions

## General

### What is Tempo?

A self-hosted running tracker and privacy-first alternative to Strava. The WebUI (command center) handles import, settings, analytics, and the Workout overview; the [iOS app](https://apps.apple.com/us/app/tempo-self-hosted-running/id6763753229) is the daily driver.

### Is Tempo free?

Yes. Open source under the MIT License. You pay only for whatever hardware or hosting you run it on.

### Is it multi-user?

No. Registration is open only until the first account exists.

## Installation

### What are the system requirements?

- **Minimum:** 1 CPU core, 2 GB RAM, 10 GB storage
- **Recommended:** 2+ cores, 4 GB+ RAM, 50 GB+ storage (raw files, time series, and media grow with your library)

### Do I need Docker?

Docker Compose is the supported path. You can run the API (.NET 10), frontend (Next.js), and PostgreSQL 16 separately; see [Developer setup](../developers/setup.md).

### How do I update Tempo?

Follow the [upgrade steps](../deployment/docker.md#updating): back up, pin the **same** release tag for api and frontend, don't restart during a Strava import or Tempo restore (it is marked `interrupted` and won't resume), `pull` + `up -d`, then check the API log for `Startup backfills: all 8 complete; none run`. If it warns that backfills haven't completed, run `docker compose exec api dotnet Tempo.Api.dll backfill` once. Migrations run automatically.

## Importing

### What file formats are supported?

- **Import page:** `.gpx`, `.fit`, `.fit.gz`
- **Strava archive ZIP:** `activities.csv` plus GPX/FIT/FIT.GZ files (Settings → Migrate / restore, or onboarding)
- **Tempo export ZIP:** restore under Settings → Migrate / restore

CSV and TCX files are not accepted on their own.

### Does Tempo sync with my watch?

Not directly. Options:

- **intervals.icu** — connect Garmin (or others) to intervals.icu, then [connect intervals.icu in Settings](../user-guide/settings.md#intervalsicu). New runs import about every 15 minutes.
- **iOS app** — imports Apple Health workouts.
- **Manual** — upload FIT/GPX on the Import page.

### Can I import from Strava?

Yes: single GPX files on the Import page, or your whole history from a Strava data export ZIP ([Bulk Import](../user-guide/bulk-import.md)). Only `Run` activities are imported.

### I skipped Strava during onboarding. Can I import later?

Yes. **Settings → Data Management → Migrate / restore → Import Strava archive**.

### What happens if I import the same run twice?

It is matched on start time, distance (within 1 m), and elapsed time (within 1 s). If the stored copy lacks raw file data it is updated; otherwise it is skipped.

### Why is my import slow?

Each Workout gets geometry, weather (an Open-Meteo call), zones, Relative Effort, and Best efforts. Archives with hundreds of runs take minutes; restores also run a repair pass.

## Onboarding

### What is first-run onboarding?

After registration, a wizard offers an optional Tempo restore, essential settings (units and heart-rate zones, optional default shoe), and an optional Strava archive. App routes are gated until it finishes. See [First-run onboarding](../getting-started/onboarding.md).

### Will upgrading force me through onboarding again?

No. Existing accounts were marked complete when onboarding shipped. There is no "run setup again" in Settings.

### Why am I stuck on the onboarding screen?

Every app route redirects there until onboarding completes. Finish restore or essentials (and answer the Strava question), or use **Skip for now** after a failed Strava job. Logout is available from the onboarding shell.

## Data and Privacy

### Where is my data stored?

- **Database:** PostgreSQL (Docker volume or your own instance) — Workouts, raw files, time series, settings
- **Media and import staging:** `MediaStorage:RootPath` (`./media` mounted at `/app/media` in Compose)

### Does Tempo send data to external services?

Only:

- **Open-Meteo** — workout start location and hour, for weather at import
- **CARTO** — map tiles, requested by your browser
- **intervals.icu** — only if you connect it; Tempo calls it with your API key

### Can I export my data?

Yes. **Settings → Export → Export All Data** downloads a ZIP with Workouts (including raw files), routes, splits and device laps, time series, media, shoes, and settings. Best efforts, the intervals.icu connection, API keys, and the password are not included. Restore under **Settings → Migrate / restore**. See [Export](../user-guide/settings.md#export-import) and [Backup and Restore](../deployment/backup-restore.md).

### How do I move to a new server?

Either export and restore through Settings, or copy the database and media directory (see [Backup and Restore](../deployment/backup-restore.md)). Keep the same `JWT_SECRET_KEY`, or you will need to reconnect intervals.icu and regenerate the API key.

### Does Tempo work offline?

Yes, except weather lookup, map tiles, and intervals.icu sync.

## Workouts

### Why don't I see a route?

The file has no GPS (treadmill or indoor). Such Workouts still import if they have distance.

### Why is weather missing?

Weather needs a GPS point and Open-Meteo access at import time. See [No weather on a Workout](common-issues.md#no-weather-on-a-workout).

### Why does the Duration differ from Strava or Garmin?

Tempo stores elapsed, timer, and moving time separately. The Workout overview shows timer time when it differs from elapsed; analytics sums use elapsed. See [Metrics](../user-guide/viewing-workouts.md#metrics).

### What is the difference between RPE and Relative Effort?

**RPE** is your 1–10 rating (or the FIT `workout_rpe`). **Relative Effort** is calculated from heart rate and zones. See [Relative Effort](../user-guide/analytics.md#relative-effort).

## Account

### How do I reset my password?

There is no email or login-page reset. An operator with host access sets a new passphrase via the API command (same 16–64 character rules as registration). Outstanding sessions are invalidated; log in again. Registration stays locked — this is not how you add a second person.

```bash
docker compose exec -it api dotnet Tempo.Api.dll reset-password
```

Scripts (no TTY):

```bash
docker compose exec -T api dotnet Tempo.Api.dll reset-password --password-stdin
```

Production Compose file: add `-f docker-compose.prod.yml`. If the API container is not running: `docker compose run --no-deps --rm api reset-password` (the image entrypoint already prefixes `dotnet Tempo.Api.dll`). On a bare API host, run `dotnet Tempo.Api.dll reset-password` from the API working directory.

Forgot the username: omit `--username` when there is exactly one account (the command prints the name it reset). Last resort: `SELECT "Username" FROM "Users";` — do not hand-write a BCrypt hash.

If the session expires immediately after a successful login, that is a cookie/HTTPS issue, not a forgotten passphrase. See [Login succeeds but you are immediately logged out](common-issues.md#login-succeeds-but-you-are-immediately-logged-out).

### How do tools like tempo-cli authenticate?

With an API key from **Settings → Account → API key**. See [API key](../user-guide/settings.md#api-key).

## Support

- [Discord](https://discord.gg/9Svd99npyj)
- [GitHub Issues](https://github.com/trevordavies095/tempo/issues) — for bugs, include steps to reproduce, expected vs actual, and the **Settings → System Information** block. Feature requests go here too.
- [Documentation](../index.md)
