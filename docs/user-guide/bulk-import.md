# Bulk Import from Strava

Import your Strava history from a Strava data export ZIP.

Strava import is offered during [first-run onboarding](../getting-started/onboarding.md). After setup it lives under **Settings → Data Management → Migrate / restore → Import Strava archive**. Day-to-day GPX/FIT files stay on the [Import](importing-workouts.md) page.

## Requesting Your Strava Export

1. In Strava, go to **Settings → My Account → Download or Delete Your Account**.
2. Click **Request Archive**.
3. Download the ZIP from the email Strava sends (this can take hours).

Upload the ZIP as downloaded; it already has the right layout.

## ZIP Layout {#zip-file-structure-requirements}

```
export.zip
├── activities.csv         # Required, at the root
├── activities/
│   ├── 1234567890.gpx
│   ├── 1234567891.fit.gz
│   └── ...
└── media/                 # Optional; photos referenced by the CSV Media column
```

- `activities.csv` must be at the root, or the job fails with `ZIP file must contain activities.csv in the root`.
- Each row's `Filename` column points at its file inside the ZIP. `.gpx`, `.fit`, and `.fit.gz` are imported; other formats (e.g. `.tcx`) error with `Unsupported file format. Only .gpx and .fit/.fit.gz files are supported.`
- Only rows whose `Activity Type` is exactly `Run` are processed. Other types (Ride, Walk, Trail Run, Virtual Run, …) are dropped before counting and do not appear in the summary.

From each CSV row Tempo also takes:

- **Activity Name** → Workout name (on an existing Workout, only if its name is empty)
- **Activity Description** and **Activity Private Note** → notes
- **Media** → photos/videos copied into the Workout's media (same type and 50 MB limits as [Media](media.md))
- Strava's weather columns, used before falling back to Open-Meteo

Each file then goes through the same [Workout intake](importing-workouts.md#import-process) as a single-file import, with source `strava_import`.

## Running the Import

1. Expand **Migrate / restore** and find **Import Strava archive** (or reach the Strava step in onboarding).
2. Click or drop the ZIP (max 500 MB) and click **Import Strava Export**.
3. The button shows `Uploading N%...`, then `Importing processed/total...`.
4. **Cancel import** stops the job. Workouts already imported stay.

The ZIP uploads as an [Import job](#how-it-works) in 512 KiB chunks, so there is no single 500 MB request through your proxy. The current unit preference is sent with the job and saved to UserSettings when it starts.

Refreshing Settings reattaches to an in-progress Strava job.

### Summary

When the job completes:

- **Total processed** — number of `Run` rows
- **Successfully imported** — new Workouts
- **Updated with new data** — existing Workouts that gained raw file data (see [Duplicates](#duplicates))
- **Skipped (already complete)** — duplicates with complete raw data
- **Errors** — per-file messages under **View error details** (e.g. `File not found in ZIP archive`)

If you set a default shoe, new Workouts are assigned to it (unless it is retired).

## One Import at a Time

Only one import job runs at a time, across Strava bulk import and [Tempo export restore](settings.md#migrate--restore). If a restore is active, the Strava card shows `A Tempo export restore is already in progress. Finish or cancel it before starting a Strava import.` and will not start.

## Duplicates

Same rule as single-file import: start time (exact), distance (within 1 m), and elapsed time (within 1 s).

- **Updated** when the stored Workout is missing raw file bytes or its FIT/GPX JSON is incomplete. Tempo backfills bytes/JSON, rebuilds splits, and fills an empty route or time series. Distance, duration, and elevation are not rewritten.
- **Skipped** when raw bytes and JSON are already complete.

Media from the CSV still attaches after created, updated, or skipped (files already attached by name are not duplicated). Re-uploading the same ZIP after a cancel or failure is safe.

## How It Works

1. `POST /workouts/import/jobs` with `kind: strava_bulk`, filename, byte size. Returns `409` with the active job if one is running.
2. `PUT /workouts/import/jobs/{id}/chunks/{index}?total=N` for each 512 KiB chunk, in order.
3. `POST /workouts/import/jobs/{id}/complete` assembles the archive under `MediaStorage:RootPath/imports/<job id>/` and queues it.
4. The background worker extracts the ZIP, parses the CSV, and runs each `Run` row through Workout intake, updating `processed`/`total`.
5. The command center polls `GET /workouts/import/jobs/{id}` every second until `completed` or `failed`.

A job still receiving chunks with no chunk for 15 minutes is replaced by the next upload. Restarting the API marks a queued or running job `failed` with `interrupted`; it does not resume. Re-upload the ZIP.

`POST /workouts/import/bulk` accepts a whole ZIP in one request (for scripts) and creates the same job.

**Implementation:** `frontend/components/BulkImport.tsx`, `frontend/hooks/useImportJobSession.ts`, `frontend/lib/api.ts` (`importStravaExportChunked`), `api/Services/ImportJobService.cs`, `api/Services/ImportJobWorker.cs`, `api/Services/StravaBulkImportOrchestrator.cs`, `api/Services/BulkImportService.cs`, `api/Services/StravaCsvParserService.cs`, `api/Models/ImportJob.cs` (`ImportJobLimits`).

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `File must be a ZIP file` | Filename does not end in `.zip` | Upload the ZIP Strava sent. |
| `File exceeds the 500MB limit` | Archive over 500,000,000 bytes | Remove `media/` or split the archive: keep `activities.csv` at the root of each part with only the rows whose files it contains. |
| `ZIP file must contain activities.csv in the root` | Re-zipped with an extra top-level folder | Zip the contents, not the folder. |
| Fewer Workouts than in Strava | Non-`Run` types are dropped; rows with missing files error | Check **View error details**. |
| `File not found in ZIP archive` | CSV `Filename` points at a file not in the ZIP | Use the unmodified Strava export. |
| Error `interrupted` | API restarted during the job | Re-upload; duplicates skip. |
| Error `cancelled` | You clicked **Cancel import** | Re-upload to continue. |
| Strava card says a Tempo restore is in progress | One-job rule | Finish or cancel the restore first. |

Large archives take minutes to tens of minutes; progress moves one activity at a time. API logs show `Found N run activities to process` at the start and `Import job {JobId} failed` on failure.

## Next Steps

- [View your imported workouts](viewing-workouts.md)
- [Settings](settings.md), including Tempo backup and restore
