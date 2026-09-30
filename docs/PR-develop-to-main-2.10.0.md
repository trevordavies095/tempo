# PR: develop → main — Tempo 2.10.0

## Title

**Release 2.10.0: intervals.icu, external identities, ops hardening**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.10.0**. Builds on **2.9.0** (timer clock, device laps, cadence/power charts, HR zone times) already on `main`.

### Added

- **intervals.icu integration (Settings → Integrations)** — connect/replace key/re-enable/disconnect/sync now; encrypted API key; background poll + Sync now; skips non-runs and existing `intervals_icu` identities.
- **Workout external identities** — `WorkoutExternalIdentities` for upstream `(source, externalId)` idempotency; first token `intervals_icu`.
- **Generic workout intake (`DecodedWorkout` + `PersistAsync`)** — shared persist pipeline for file import, HealthKit, Strava bulk, and intervals.icu sync.
- **Support snapshot (`GET /version`)** — public identity dump + frozen `snapshot` text; Settings → System Information Copy.
- **FIT session RPE → Workout** — fills `Workout.Rpe` from FIT session `workout_rpe` when null.
- **Named logging profiles (`standard` | `debug`)** — `Tempo:Logging:Profile` / `TEMPO_LOGGING_PROFILE`.
- **Liveness `/health` and readiness `/ready`** — API process pulse + Postgres readiness; command-center `GET /health`.
- **Host-only `reset-password` command** — break-glass passphrase reset; bumps `SessionVersion`.

### Fixed

- **Startup backfill idle on Postgres** — timer/device-lap jsonb path scans; split-HR `no_overlap` stamp; avoids rewriting stamped leftovers every boot.

### Security

- **Production Compose requires secrets from `.env`** — `JWT_SECRET_KEY` and `POSTGRES_PASSWORD` (copy `.env.example`).
- **Production Compose publish surface** — command center `127.0.0.1:3004`; Postgres/API unpublished by default.
- **Break-glass recovery is host-only** — no public reset form.

### Changed

- **API:** rejects SQLite connection strings and a missing/empty `ConnectionStrings:DefaultConnection` at startup. Tests and OpenAPI generation require PostgreSQL 16.

### Migration

- **Database:** `AddWorkoutExternalIdentities`, `AddIntervalsIcuConnection`, and `AddWorkoutSplitHeartRateBackfill`. Automatic on API startup.
- Reconnect intervals.icu under Settings → Integrations after restore or after changing `JWT__SecretKey`.

### Version & artifacts

- App version: **2.10.0** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`).
- Production Compose example pins **`ghcr.io/.../api:v2.10.0`** and **`frontend:v2.10.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.10.0]**
- PRs on develop: [#210](https://github.com/trevordavies095/tempo/pull/210) (generic workout / external identities), [#211](https://github.com/trevordavies095/tempo/pull/211) (repo layout), [#212](https://github.com/trevordavies095/tempo/pull/212) (intervals.icu), [#226](https://github.com/trevordavies095/tempo/pull/226) (cadence backfill), [#228](https://github.com/trevordavies095/tempo/pull/228) (postgres-only), [#229](https://github.com/trevordavies095/tempo/pull/229) (backfills), [#230](https://github.com/trevordavies095/tempo/pull/230) (break-glass), [#231](https://github.com/trevordavies095/tempo/pull/231) (docker-compose), [#232](https://github.com/trevordavies095/tempo/pull/232) (health/ready), [#233](https://github.com/trevordavies095/tempo/pull/233) (openapi version), [#234](https://github.com/trevordavies095/tempo/pull/234) (log profiles), [#235](https://github.com/trevordavies095/tempo/pull/235) (support snapshot), [#237](https://github.com/trevordavies095/tempo/pull/237) (FIT RPE)

### Post-merge checklist

- [ ] Tag **`v2.10.0`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.10.0`** exist for production pulls.
- [ ] Self-hosted upgrades: migrations for external identities, intervals.icu connection, and split-HR backfill cursor (automatic on API startup); reconnect intervals.icu after JWT secret change or Tempo ZIP restore.
