# Tempo

Self-hosted running tracker. The WebUI is the **command center**; the iOS app is the **daily driver** after setup.

## Language

**Command center**:
The WebUI. Full product surface: import, settings, shoes, analytics, and Workout overview. Not a phone clone and not a thin admin utility.
_Avoid_: companion site, admin console, desk companion

**Daily driver**:
The iOS app, used for day-to-day logging and glancing once Tempo is set up. Functionality that exists only on the command center stays on the command center.
_Avoid_: mobile client as the source of truth for settings

**Control plane**:
Command-center screens for operating Tempo: settings, import, activities list, shoes. Dense, scannable.
_Avoid_: admin pages, back office

**Workout overview**:
The command-center screen for one Workout: map, splits, time series, weather, media, comparison. Expansive, not dense.
_Avoid_: workout detail, activity page (when meaning this screen)

**Workout**:
A recorded run with stats, optional route, splits, time series, media, shoe, and weather.
_Avoid_: activity (except the existing Activities list name), session

**TrackPoint**:
An in-memory sample on a Workout path. Latitude and longitude are optional (indoor samples). Optional elevation, time, sensors (HR, cadence, power, temperature), and motion (speed, distance, grade, vertical speed). Not a table.
_Avoid_: GpxPoint, GPS track, polyline (as this type)

**Track geometry**:
In-process derive of elevation gain, `WorkoutRoute`, `WorkoutSplit`s, and `WorkoutTimeSeries` from `TrackPoint`s. Crop and split recalc call it too. Not a table.
_Avoid_: GpxParser splits, GPS smoothing (as the module name)

**Workout intake**:
The persist pipeline behind `POST /workouts/import`, `POST /workouts/import/healthkit`, and Strava bulk per-file processing: decode adapter (GPX/FIT/HealthKit) then `PersistAsync` (geometry, duplicate policy, weather, relative effort, best efforts). Persist is the single pipeline; new formats enter via decoded input, not a second pipeline. Not the HTTP module and not Settings ZIP restore.
_Avoid_: import endpoint (when meaning this module), bulk persist

**Import job**:
A Postgres-backed background import (`kind`: `strava_bulk` or `tempo_export`) with chunked upload (or whole-ZIP adapter), worker processing, poll, cancel, and one-active-job rules. Not Workout intake and not single-file GPX/FIT import.
_Avoid_: Hangfire job, sync bulk POST (as the product model)

**WorkoutRoute**:
GeoJSON LineString for one Workout.
_Avoid_: GPS track, polyline (as the domain name)

**WorkoutSplit**:
Distance-based split for a Workout (km or mile per UserSettings).
_Avoid_: lap, mile split (as the type name)

**WorkoutTimeSeries**:
Per-elapsed-second (or per-point) samples for a Workout: heart rate, pace/speed, elevation, and related sensors.
_Avoid_: stream, chart data, records

**WorkoutMedia**:
Photo or video attached to a Workout.

**Shoe**:
A pair of running shoes with mileage and Workout assignments.

**UserSettings**:
Single-row preferences: units, heart-rate zones, default shoe. Appearance (dark/light) is a command-center preference, not UserSettings.
_Avoid_: config, profile

**Onboarding**:
Hard-gated first-run wizard on the command center after registration (optional Tempo export restore, essentials, optional Strava bulk). Driven by `User.OnboardingCompleted` (account flag, not UserSettings). Day-to-day Import stays GPX/FIT; late ZIPs use Settings → Migrate / restore.
_Avoid_: setup wizard as UserSettings, re-run setup from Settings

**Run type**:
Classification of a Workout (Easy, Workout, Long Run, Race, and the same set the product already uses).
_Avoid_: tag, category (when meaning run type)

**Highlight**:
Shared focus on Workout overview: a split index and/or elapsed seconds that map, splits, and time series follow together.
_Avoid_: hover state, cursor (as the domain name)
