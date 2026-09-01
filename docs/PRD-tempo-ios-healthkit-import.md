# PRD: HealthKit Workout Import for tempo-ios

**Status:** Draft
**Date:** 2026-08-30
**Audience:** tempo-ios team (primary), tempo API maintainers (backend requirements in §7)

---

## 1. Background

Tempo currently requires users to manually upload FIT/GPX files for every workout. That is fine for privacy-focused users, but it is a real adoption barrier for users who just want Tempo's benefits (self-hosted analytics, MCP access via tempo-cli, the iOS app) without a manual chore after every run.

Server-side integrations were researched and rejected for now:

- **Strava API** requires each self-hoster to hold a paid Strava subscription for API credentials, cannot return original FIT/GPX files (streams only), and its API Policy (June 2026) explicitly prohibits using Strava-sourced data in AI applications or exposing it via MCP servers — directly at odds with tempo-cli.
- **Garmin's** official developer program is enterprise-only and currently paused for new applications; unofficial access requires storing the user's Garmin password and breaks regularly.

**HealthKit avoids all of this.** It is a free, stable, on-device API with per-type user consent, no third-party terms restricting downstream use, and full-fidelity data for Apple Watch recordings: GPS route (`HKWorkoutRoute`), continuous heart rate, cadence, and running power. The iOS app becomes the sync agent between the user's Health store and their Tempo server.

### Known data-fidelity constraints (verified)

- **Apple Watch / Apple Workout app** recordings: full fidelity (route, HR stream, cadence, power).
- **Garmin Connect** writes only summary-grade workouts to Apple Health: no GPS route, no HR stream (min/max only), and backfills only ~2 weeks. **Strava's** Health writes are similarly summary-grade.
- HealthKit **background delivery is best-effort and batched**: typically 1–60 minutes after a workout, even at "immediate" frequency. It is not real-time and can be skipped by iOS entirely.

This feature primarily serves Apple Watch runners. Garmin/Strava-sourced Health entries are excluded by default (see §5.3) because they would import as degraded shadow copies.

---

## 2. Goals

1. A user can connect Apple Health to tempo-ios and import any or all of their historical runs into their Tempo server, with clear indication of which runs are already in Tempo.
2. With auto-sync enabled, new runs are detected in the background, staged for review, and imported with a single tap — no file handling, no Mac, no cables.
3. Imported runs are first-class Tempo workouts: route, splits, time series, heart rate, weather, default shoe, relative effort, and best efforts all populate exactly as they do for a manually uploaded file.
4. Re-syncing is always safe: no duplicates, ever, regardless of retries, reinstalls, or overlap with workouts already imported via GPX/FIT upload or Strava bulk import.

## 3. Non-goals (v1)

- **Other sports.** Running only (`HKWorkoutActivityType.running`), outdoor and indoor. No walking, hiking, or cycling until Tempo has a sport concept.
- **Writing to HealthKit.** No two-way sync; Tempo never writes workouts back to Health.
- **Silent auto-import.** All imports go through user review (see §5.4). Revisit after v1 telemetry.
- **Server-side fuzzy deduplication.** The source filter plus UUID idempotency covers the realistic cases; time/distance-tolerance matching is deliberately out.
- **Server-side ignore tombstones.** The ignore list is on-device only (see §5.5); it does not survive reinstall.
- **Strava/Garmin server integrations.** Separate decision, previously researched and deferred.
- **Media.** HealthKit has no workout photos; nothing to import.

---

## 4. User stories

1. As a new Tempo user with three years of Apple Watch runs, I connect Health, review my history, select the runs I want, and watch them import with progress — then see them fully populated on my Tempo dashboard.
2. As a daily runner, I finish a run, and within the hour my phone notifies me that the run is ready to review; one tap in tempo-ios approves it into Tempo.
3. As a treadmill runner, my indoor runs import with distance, duration, and heart rate even though they have no GPS route.
4. As a user who also did a one-time Strava ZIP bulk import, I never see duplicates when I later connect HealthKit.
5. As a user who dismissed a junk workout from the review queue, I never see it offered again on this device.

---

## 5. Product requirements — iOS

### 5.1 Connect flow

- New **Settings → Apple Health** section: connect, disconnect, source filter, auto-sync toggle, ignore-list management, sync activity log.
- Connecting requests read authorization for: workouts, workout routes, heart rate, running cadence (step count during workout), running power, distance, and active energy. Handle partial/denied grants gracefully — HealthKit does not reveal read-denial, so the UX copy must explain that missing data usually means denied permissions, with a deep link to Health settings.
- Connection state, sync anchors, and the ignore list persist locally on device.

### 5.2 History picker (manual import + backfill)

- Lists the user's full HealthKit running history (allowlisted sources only), newest first, with per-run: date, distance, duration, source app, and an **already-in-Tempo badge** (matched by stored HealthKit UUID, falling back to the server's start/distance/duration duplicate check).
- **Selection is explicit.** Multi-select is supported, but there is no prominent "Import all" one-tap action; the user reviews and chooses. (Product decision: review-first over bulk convenience.)
- Import runs sequentially with visible progress (n of m), survives interruption, and is resumable — safe because every POST is idempotent by HealthKit UUID.
- Per-run context action: **"Don't ask again"** → adds to the on-device ignore list.

### 5.3 Source filter

- Default allowlist: workouts whose source is Apple's own recorders (Apple Watch / Apple Workout app). Rationale: Garmin Connect and Strava write summary-only shadow copies (no route, no HR stream) of runs that usually exist in Tempo at full fidelity already.
- The user can view all source apps present in their Health store and opt additional sources in. When a non-default source is enabled, show a one-time notice that those workouts may lack route and heart-rate detail.

### 5.4 Auto-sync (staged review)

- Opt-in toggle. Implementation: `HKObserverQuery` registered at app launch + `enableBackgroundDelivery` for the workout type + `HKAnchoredObjectQuery` for deltas; upload via background `URLSession`.
- When new allowlisted runs are detected, they are **staged into a review queue — never imported silently.** A local notification ("1 run ready to import") deep-links to the queue.
- The review queue shows the same card as the picker (date, distance, duration, source) with per-run approve, multi-select approve, and dismiss (dismiss = ignore list).
- On every app foreground, run the same anchored delta check as a fallback — background delivery is best-effort and must not be the only trigger.
- Engineering notes (known HealthKit failure modes): observer queries must be re-registered on every launch before anything else; the observer's completion handler must always be called or iOS permanently stops background wakes; background delivery does not work on Simulator — test on hardware.

### 5.5 Ignore list and deletion semantics

- Dismissing a run (queue) or "Don't ask again" (picker) adds its HealthKit UUID to an **on-device ignore list**: never shown in the picker, never staged. Viewable and clearable in Settings → Apple Health.
- If the user deletes a synced workout in Tempo, the run **reappears in the picker as importable** (deletion may have been a mistake) but is **never auto-staged again** by auto-sync.

### 5.6 Sync activity log

- Reverse-chronological log in Settings → Apple Health: staged, imported, dismissed, and failed events with timestamps and error detail on failures. Local only.

---

## 6. Data mapping

The iOS app is responsible for assembling one JSON document per workout:

| Tempo concept | HealthKit source |
| --- | --- |
| Start time / duration | `HKWorkout.startDate`, `.duration` |
| Distance | `HKWorkout` distance statistic (authoritative, device summary) |
| Route points | `HKWorkoutRoute` → timestamped `CLLocation`s (lat, lon, altitude) |
| Heart rate series | HR quantity samples within the workout interval |
| Cadence / power | Running cadence and power samples where present |
| Energy | Active energy statistic |
| Indoor flag | `HKWorkout.isIndoorWorkout` / metadata |
| External ID | `HKWorkout.uuid` |
| Source app | `HKSourceRevision` (name + bundle ID) |

The app merges HR/cadence/power samples onto route points by timestamp (nearest-sample within a tolerance) to produce a single track-point array. For indoor runs the array may be empty or GPS-free; include cumulative distance samples when available so the server can compute splits and time series without GPS.

---

## 7. Backend requirements (tempo API)

1. **New endpoint** `POST /workouts/import/healthkit` (JWT-protected, same as all workout endpoints). Accepts one workout per request: summary block (start, duration, distance, energy, avg/max HR, indoor flag, source app), track-point array, HealthKit UUID, and a schema version field.
2. Feeds the existing `WorkoutIntake` pipeline: summary block is treated as the authoritative device summary (same precedence as FIT session data); `TrackGeometry` derives route, splits, and time series; weather, default shoe, relative effort, and best efforts run unchanged. Indoor runs follow the existing routeless path already supported for FIT files.
3. **Idempotency:** persist the HealthKit UUID on the workout (new nullable external-ID column). Duplicate check order: UUID match → return the existing workout (200-equivalent "skipped/exists" result, not an error) → existing start/distance/duration rule as backstop.
4. Store the raw request payload in a JSONB column following the `RawStravaData` pattern, so future reprocessing (e.g., improved split derivation) can rehydrate from source.
5. Response mirrors the existing import result shape (`created` / `updated` / `skipped` + workout ID) so the app can badge accurately.
6. **UUID index** `GET /workouts/healthkit-uuids` returns `{ "uuids": [...] }` of all non-null `HealthKitUuid` values so tempo-ios can badge the history picker without paging `GET /workouts`.

---

## 8. Edge cases

- **Runs with no distance and no route** (rare, bad third-party writes): reject client-side with a clear "not enough data" state in the picker; do not send.
- **Watch-only user with poor connectivity:** background `URLSession` retries; queue state must survive app termination.
- **Permission revoked after connect:** anchored queries silently return nothing new; surface "check Health permissions" in the sync log rather than failing silently forever.
- **Very long runs** (ultra-distance, 10k+ route points): cap/downsample client-side to a bounded payload consistent with existing time-series limits (20,000 samples).
- **Duplicate across devices** (user runs tempo-ios on two devices): server-side UUID idempotency makes this safe; second device shows the run as already imported.

---

## 9. Success metrics

- ≥ 80% of runs from connected users arrive via HealthKit (vs manual upload) within 60 days of release.
- Median time from workout end to "staged in queue" under 60 minutes; from staging to import driven by user action (report distribution).
- Zero duplicate workouts reported from HealthKit-sourced imports.
- Backfill completion rate: of users who open the history picker, % who import ≥ 1 run.

## 10. Open questions

1. Does tempo-ios already hold a persistent authenticated session suitable for background `URLSession` uploads (cookie refresh while backgrounded)? If tokens can expire mid-queue, the staging model tolerates it, but the sync log must explain re-auth.
2. Should the review queue eventually offer an "auto-approve runs from Apple Watch" escalation once trust is established? (Deferred; revisit with v1 telemetry.)
3. Minimum iOS version: running power and some metadata require iOS 16+; confirm tempo-ios's current deployment target.

## 11. Phasing

- **M1 — Manual path:** connect flow, source filter, history picker with badges, JSON endpoint + UUID idempotency on the API, sequential import with progress, ignore list.
- **M2 — Auto-sync:** observer/background delivery, staging queue, notifications, foreground fallback, sync log.
- Both milestones ship in one release; M1 is independently testable and gates M2.
