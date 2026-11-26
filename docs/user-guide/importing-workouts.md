# Importing Workouts

Import individual GPX and FIT files on the **Import** page. The page is file-only: it does not accept Strava or Tempo ZIP archives. For a Strava archive, see [Bulk Import](bulk-import.md) (Settings → Migrate / restore, or [first-run onboarding](../getting-started/onboarding.md)).

Other ways Workouts arrive:

- **intervals.icu sync** — new running activities are pulled automatically once connected. See [Settings → Integrations](settings.md#intervalsicu).
- **Tempo iOS app** (daily driver) — sends Apple Health workouts through `POST /workouts/import/healthkit`.

## Supported File Formats

| Extension | Notes |
|---|---|
| `.gpx` | Needs at least 2 track points with timestamps. Garmin `TrackPointExtension` HR, cadence, power, and temperature are read. |
| `.fit` | Needs timestamps and either GPS or distance (indoor FIT files import without a route). |
| `.fit.gz` | Gzipped FIT, as found in Strava exports. |

Any other extension is rejected with `File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)`. The file picker and drop zone also filter to these extensions; dropped files of other types are skipped with an alert.

## Getting Files Off Your Device

### Garmin

1. Connect the device to your computer.
2. Copy `.fit` files from `GARMIN/ACTIVITY/`.

Or connect Garmin to intervals.icu and let [intervals.icu sync](settings.md#intervalsicu) import new runs.

### Apple Watch

Use the Tempo iOS app, which imports Apple Health workouts directly. Alternatively, export GPX with a third-party app.

### Strava

On an activity page, open the ⋮ menu and choose **Export GPX** (or **Export Original** for the device's FIT file). For your full history as a ZIP, use [Bulk Import](bulk-import.md).

## Import Process

1. Open **Import** from the navigation bar.
2. Click the drop zone or drag files onto it. Multiple files are supported; remove any from the list with ×.
3. Click **Import N file(s)**.

All selected files go up in one request and are processed one after another. The browser's current unit preference is sent with the upload and saved to UserSettings.

For each file, Tempo:

- Detects the format from the extension and parses it
- Checks for a duplicate (see below)
- Builds the route, distance splits, device laps (FIT), and time series from the track points, and calculates elevation gain
- Sets average/max heart rate, cadence, and power from the samples when the file has them (max speed only when the summary lacks it)
- Reads RPE from the FIT session `workout_rpe` (Borg CR10 × 10 → 1–10) when present
- Attaches weather (Strava or FIT weather if present, otherwise Open-Meteo for the first GPS point and start hour); skipped when there is no GPS
- Assigns your default shoe, unless it is retired
- Sets run type to **Easy Run**
- Calculates time in heart-rate zones, Relative Effort, and Best efforts

### Results

- **One file:** "Workout Imported Successfully!" with distance, duration, average pace, elevation gain, and a **View workout details →** link to the Workout overview. A failed file shows `Error: <message>`.
- **Several files:** "Import Complete!" with Total processed, Successfully imported, Updated, Skipped (duplicates), and Errors (expand **View error details** for per-file messages).

The page does not redirect; follow the link or open the Dashboard.

## What Gets Stored

- **Route** — GeoJSON LineString (when the file has GPS)
- **Stats** — distance, elapsed time, timer time (FIT `total_timer_time`), moving time when the source provides it, average pace, elevation gain, calories, HR, cadence, power
- **Time series** — heart rate, pace/speed, elevation, cadence, power, temperature; FIT also adds grade and vertical speed
- **Splits** — 1 km or 1 mile, per your unit preference
- **Device laps** — FIT `lap` messages when the file has 2 or more kept laps, with intensity (warm-up, active, rest, …)
- **Weather**, **device name** (Garmin products are named from the FIT SDK product tables), and the **original file bytes** plus parsed raw JSON for later reprocessing

Cadence is stored as steps per minute. FIT stride cadence is doubled on import; GPX cadence is stored as given.

## Duplicates

Before the stats check, Tempo matches on identity: HealthKit UUID (iOS app) and Workout external identity (e.g. intervals.icu activity id). Then it matches on start time (exact), distance (within 1 m), and elapsed time (within 1 s). Strava bulk import uses the same rule.

- **Updated** when the existing Workout is missing raw file bytes or its FIT/GPX JSON is incomplete. Tempo backfills the bytes and JSON, rebuilds splits, and fills an empty route or time series. Distance, duration, and elevation are not rewritten.
- **Skipped** when raw bytes and JSON are already complete.

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)` | Wrong extension | Rename or re-export as GPX/FIT. CSV is only read inside a Strava ZIP. |
| `No track points found in GPX file` / `GPX file must contain at least 2 track points` / `GPX file must contain timestamps` | GPX is a route or planned course, not a recorded activity | Export the recorded activity instead. |
| `Not a valid FIT file` / `Error parsing FIT file: …` | Corrupt or non-activity FIT | Re-copy from the device; check the file opens elsewhere. |
| `FIT file contains no GPS data and no distance information` | Manual or strength activity | Nothing to import as a run. |
| `File is empty` | Zero-byte upload | Re-copy the file. |
| No map | File has no GPS (treadmill, indoor) | Expected. Weather is also skipped. |
| No heart rate, cadence, or power | The device did not record it | Expected. |

API logs (`docker compose logs api`) carry `Error parsing workout file` or `Error persisting workout` with the exception.

**Implementation:** `frontend/app/import/page.tsx`, `frontend/components/FileUpload.tsx`, `api/Endpoints/WorkoutsEndpoints.cs` (`ImportWorkout`), `api/Services/WorkoutIntake.cs` (`ProcessAsync` / `PersistAsync` / `HandleDuplicateAsync`), `api/Services/GpxParserService.cs`, `api/Services/FitParserService.cs`, `api/Services/WorkoutQueryService.cs` (`FindDuplicateWorkoutAsync`).

## Next Steps

- [Bulk import](bulk-import.md) a Strava archive
- [View your workouts](viewing-workouts.md)
- [Add media](media.md)
