# PR: develop → main — Tempo 2.9.0

## Title

**Release 2.9.0: timer clock, device laps, cadence/power charts, HR zone times**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.9.0**. Builds on **2.8.1** (split avg HR, ordered splits, Next.js 16.3.4) already on `main`.

### Added

- **Time in HR zones on Workout overview** — live `heartRateZoneTimes` on detail GET (and crop’s detail-shaped payload): five `{ zone, timeS }` or JSON null from HR time series + current Settings zones. Command center Additional Details shows a stacked bar (green-to-red) and five Zone rows. Not on list GET; not stored; no avg-HR fallback.
- **Workout timer clock (`TimerTimeS`)** — nullable FIT `total_timer_time` alongside elapsed `DurationS` and moving `MovingTimeS`. GET list/detail include `timerTimeS`. Command center shows timer as Duration when it differs from elapsed. `TimerTimeBackfillWorker` fills existing FIT rows.
- **Cadence chart on Workout overview** — elapsed-time cadence (tooltip **spm**); omitted when no samples.
- **Power chart on Workout overview** — elapsed-time power (tooltip **W**); omitted when no samples.
- **WorkoutSplit kinds and elapsed bounds** — `Kind` (`distance` | `device_lap`), wall `StartElapsedS` / `EndElapsedS`, optional `StartDistanceM`; unique `(WorkoutId, Kind, Idx)`. Display list prefers `device_lap` when present.
- **FIT device laps on import** — FIT `LapMesg` copied into `device_lap` when 2+ kept laps exist. `DeviceLapBackfillWorker` backfills existing FIT workouts.
- **HealthKit optional laps** — schema v1 additive `laps`; repeat UUID POST can attach laps (`updated`) when the workout has none yet.

### Fixed

- **FIT cadence as steps/min** — FIT record and session avg/max cadence stored as steps/min (×2 from FIT strides/min). JSON keys stay `cadenceRpm` / `avgCadenceRpm` / `maxCadenceRpm`. Startup backfill re-parses FIT workouts that still have raw file bytes.
- **FIT cadence startup backfill on Postgres** — candidate selection no longer runs text `LIKE`/`Contains` on `RawFitData` (`jsonb`); Postgres scans the marker via `::text`.

### Security

- **`SQLitePCLRaw.bundle_e_sqlite3` 2.1.12** — test/SQLite host package pin.

### Changed

- **API:** replace vendored FIT SDK under `api/Libraries/FitSDK/` with `Garmin.FIT.Sdk` 21.214.0.
- **Split write paths** — unit-preference recalc replaces `distance` rows only (`device_lap` preserved); crop deletes all kinds then writes new `distance`; intake duplicate update still wipes all kinds before rewriting.
- **Local `dotnet-ef` tool** — pinned to 10.0.0 in `.config/dotnet-tools.json`.

### Migration

- **Database:** `AddWorkoutSplitKindAndElapsedBounds` (`Kind`, elapsed bounds, `StartDistanceM`; unique `(WorkoutId, Kind, Idx)`) and `AddWorkoutTimerTimeS` (nullable `TimerTimeS` on `Workouts`). Automatic on API startup.
- **Startup workers:** `TimerTimeBackfillWorker`, `DeviceLapBackfillWorker`, and FIT cadence steps/min backfill (idempotent). FIT cadence rows with JSON only (no file bytes) may keep the old strides/min scale.

### Version & artifacts

- App version: **2.9.0** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`).
- Production Compose example pins **`ghcr.io/.../api:v2.9.0`** and **`frontend:v2.9.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.9.0]**
- PRs on develop: [#191](https://github.com/trevordavies095/tempo/pull/191) (FIT SDK), [#192](https://github.com/trevordavies095/tempo/pull/192) (build cleanup / SQLitePCLRaw), [#199](https://github.com/trevordavies095/tempo/pull/199) / [#200](https://github.com/trevordavies095/tempo/pull/200) (cadence steps/min + Postgres backfill), [#201](https://github.com/trevordavies095/tempo/pull/201) (cadence/power charts), [#203](https://github.com/trevordavies095/tempo/pull/203) (split kinds), [#204](https://github.com/trevordavies095/tempo/pull/204) (dotnet-ef), [#205](https://github.com/trevordavies095/tempo/pull/205) (timer clock), [#206](https://github.com/trevordavies095/tempo/pull/206) (device laps), [#207](https://github.com/trevordavies095/tempo/pull/207) / [#208](https://github.com/trevordavies095/tempo/pull/208) (HR zone times)

### Post-merge checklist

- [ ] Tag **`v2.9.0`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.9.0`** exist for production pulls.
- [ ] Self-hosted upgrades: migrations for split kinds/bounds and `TimerTimeS` (automatic on API startup; timer, device-lap, and cadence backfills run in background).
