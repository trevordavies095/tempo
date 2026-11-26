# User Guide

How to use the Tempo command center (the WebUI). The iOS app is the daily driver for logging and glancing; everything that recalculates or is one-time setup lives here.

## Topics

- **[Importing Workouts](importing-workouts.md)** — GPX/FIT uploads, what intake does, duplicates
- **[Bulk Import](bulk-import.md)** — Strava archive ZIP via Import jobs
- **[Viewing Workouts](viewing-workouts.md)** — Dashboard, Activities, Workout overview, similar runs, crop, delete
- **[Media](media.md)** — photos and videos on a Workout
- **[Analytics](analytics.md)** — clocks, time in zones, Relative Effort, Best efforts, weekly recap, Insights
- **[Settings](settings.md)** — appearance, units, shoes, heart-rate zones, thresholds, intervals.icu, export/restore, recalculation, password, API key

## Getting Started

1. Complete [first-run onboarding](../getting-started/onboarding.md): restore a Tempo export, or set units and heart-rate zones and optionally import a Strava archive.
2. [Import a workout](importing-workouts.md) on the Import page, or [connect intervals.icu](settings.md#intervalsicu).
3. Open the [Dashboard](viewing-workouts.md#dashboard).

## Common Workflows

### Adding a run

- Upload the FIT/GPX on **Import**, or
- Let [intervals.icu sync](settings.md#intervalsicu) pull it within 15 minutes (or click **Sync now**), or
- Let the iOS app send it from Apple Health.

### Migrating from Strava

1. Request your Strava archive.
2. Choose Strava import during [onboarding](../getting-started/onboarding.md), or later **Settings → Migrate / restore → Import Strava archive**.
3. Upload the ZIP and wait for `Importing processed/total...` to finish. See [Bulk Import](bulk-import.md).

### Backup and restore

1. **Settings → Export → Export All Data** downloads a ZIP.
2. To restore, **Settings → Migrate / restore → Restore Tempo export** and upload it. You can cancel; restored data stays.
3. Reconnect intervals.icu and generate a new API key afterwards; neither is in the export.

See also [Backup and Restore](../deployment/backup-restore.md) for database-level backups.

### Reviewing training

- **Dashboard** — this week's recap, Relative Effort against the last three weeks, Best efforts
- **Workout overview** — splits or device laps, charts, zones, weather, similar runs
- **Insights** — personal records by range, weather extremes, data coverage

## Need Help?

See [Troubleshooting](../troubleshooting/index.md).
