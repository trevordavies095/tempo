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

**Elapsed time**:
Wall-clock duration of a Workout from start to finish, including pauses. Stored as `Workout.DurationS`. Duplicate detection uses this clock.
_Avoid_: duration (when the clock is unspecified), timer time, moving time

**Timer time**:
Time the recording device was running, pauses excluded. Garmin Connect’s primary duration (`total_timer_time` in FIT). Stored as `Workout.TimerTimeS`. Not the same as elapsed or moving.
_Avoid_: elapsed time, moving time, DurationS (as this clock)

**Moving time**:
Time spent moving by a speed threshold, when the source provides it. Stored as `Workout.MovingTimeS`. Not timer time.
_Avoid_: timer time, elapsed time

**TrackPoint**:
An in-memory sample on a Workout path. Latitude and longitude are optional (indoor samples). Optional elevation, time, sensors (HR, cadence, power, temperature), and motion (speed, distance, grade, vertical speed). Not a table.
_Avoid_: GpxPoint, GPS track, polyline (as this type)

**Track geometry**:
In-process derive of elevation gain, `WorkoutRoute`, `WorkoutSplit`s, and `WorkoutTimeSeries` from `TrackPoint`s. Crop and split recalc call it too. Not a table.
_Avoid_: GpxParser splits, GPS smoothing (as the module name)

**Workout intake**:
The persist pipeline behind `POST /workouts/import`, `POST /workouts/import/healthkit`, and Strava bulk per-file processing: decode adapter (GPX/FIT/HealthKit) then `PersistAsync` (geometry, duplicate policy, weather, relative effort, best efforts). Persist is the single pipeline; new formats enter via decoded input, not a second pipeline. Not the HTTP module and not Settings ZIP restore.
_Avoid_: import endpoint (when meaning this module), bulk persist

**Workout external identity**:
A `(source, externalId)` row linking a Workout to one upstream system. Used for intake idempotency (find / persist / unique-race) before the start/distance/elapsed stats key.
_Avoid_: `Workout.Source` (provenance of how the Workout was ingested: `fit_import`, `healthkit`, `strava_import`, …), `HealthKitUuid` (dedicated column until a later move), activity id as a Workout column

**Import job**:
A Postgres-backed background import (`kind`: `strava_bulk` or `tempo_export`) with chunked upload (or whole-ZIP adapter), worker processing, poll, cancel, and one-active-job rules. Not Workout intake and not single-file GPX/FIT import.
_Avoid_: Hangfire job, sync bulk POST (as the product model)

**WorkoutRoute**:
GeoJSON LineString for one Workout.
_Avoid_: GPS track, polyline (as the domain name)

**WorkoutSplit**:
Segment row for a Workout with `Kind` (`distance` | `device_lap`). `distance` rows are unit-derived (km or mile per UserSettings) and replaced on unit-preference recalc; `device_lap` rows are device ranges when present. One table; Idx is unique per kind.
_Avoid_: lap table, mile split (as the type name)

**Device lap**:
Product term for a `WorkoutSplit` with `Kind = device_lap` — a range the recording device wrote (FIT `lap` message, or HealthKit `laps` summaries from tempo-ios), usually auto-distance plus leftover. Authoritative for overview display when any exist. Survives unit-preference split recalc; crop deletes them. Not a separate table. Library FIT rows without `device_lap` yet are filled on startup by `DeviceLapBackfillWorker` (copies stored/reparsed FIT laps; skips cropped sessions). Not rebuilt from DistM.
_Avoid_: second entity/table, auto-split (when meaning the FIT lap)

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

**Intervals.icu connection**:
Instance-level 0-or-1 row linking Tempo to the authenticated intervals.icu athlete (always id `0`): encrypted personal API key, enabled flag, sync origin, last-success / last-error. Command-center Settings card. Not UserSettings, not Tempo `ApiKey`, not an ImportJob.
_Avoid_: UserSettings, Tempo API key, ImportJob (as the connection)

**Intervals.icu sync**:
Hosted poller plus on-demand wake. Lists recent running activities, fetches original FIT/GPX, feeds Workout intake with overlay identity `source = intervals_icu`. Not backfill, not a webhook, not client-held secrets.
_Avoid_: ImportJob, backfill, webhook (as this product)

**Onboarding**:
Hard-gated first-run wizard on the command center after registration (optional Tempo export restore, essentials, optional Strava bulk). Driven by `User.OnboardingCompleted` (account flag, not UserSettings). Day-to-day Import stays GPX/FIT; late ZIPs use Settings → Migrate / restore.
_Avoid_: setup wizard as UserSettings, re-run setup from Settings

**Run type**:
Classification of a Workout (Easy, Workout, Long Run, Race, and the same set the product already uses).
_Avoid_: tag, category (when meaning run type)

**Highlight**:
Shared focus on Workout overview: a split index and/or elapsed seconds that map, splits, and time series follow together.
_Avoid_: hover state, cursor (as the domain name)

**Cadence**:
Steps per minute (both feet). Stored in `CadenceRpm` / `AvgCadenceRpm` / `MaxCadenceRpm` (historical names; the number is steps/min). FIT decode multiplies record and session avg/max cadence by 2 (strides → steps); GPX TrackPointExtension and HealthKit `cad` are stored as given. API JSON keys stay `cadenceRpm` / `avgCadenceRpm` / `maxCadenceRpm`.
_Avoid_: rpm (for running), strides/min (as the stored unit)
