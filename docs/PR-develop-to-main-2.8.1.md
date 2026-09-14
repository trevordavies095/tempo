# PR: develop → main — Tempo 2.8.1

## Title

**Release 2.8.1: Split avg HR, ordered splits, Next.js security patches**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.8.1**. Builds on **2.8.0** (HealthKit import, lighter list payloads, Dependabot floor bumps) already on `main`.

Note: [PR #189](https://github.com/trevordavies095/tempo/pull/189) (Next.js 16.3.4) landed on **`develop`**, not `main`; this release PR carries it plus the split HR work.

### Added

- **Per-split average heart rate** — `WorkoutSplit.AvgHeartRateBpm`; time-weighted from time-series HR on import/crop/recalc.
- **`avgHeartRateBpm` on workout split payloads** — `GET /workouts/{id}` and crop (`null` when no HR in that window).
- **Startup backfill** — `SplitHeartRateBackfillWorker` fills historical splits with HR series (idempotent, per-workout).
- **Command center** — Workout overview splits table shows **Avg HR (bpm)** when any split has a value.

### Fixed

- **Split ordering** — detail/crop/query paths order splits by `idx`.

### Security

- **Next.js 16.3.4** — `next` and `eslint-config-next` **>=16.3.4** ([GHSA-p293-qw3h-jr36](https://github.com/vercel/next.js/security/advisories/GHSA-p293-qw3h-jr36), [GHSA-2xp9-vwfh-vxw4](https://github.com/vercel/next.js/security/advisories/GHSA-2xp9-vwfh-vxw4)).
- **`@humanfs/node` override** — **>= 0.16.8** ([GHSA-p498-v437-472g](https://github.com/advisories/GHSA-p498-v437-472g)).

### Changed

- **Frontend:** `agentRules: false` in `next.config.ts` (no auto-written `AGENTS.md` / `CLAUDE.md` from Next 16.3+).

### Migration

- **Database:** `AddAvgHeartRateToWorkoutSplit` (nullable `AvgHeartRateBpm` on `WorkoutSplits`). Automatic on API startup; backfill stamps historical rows.

### Version & artifacts

- App version: **2.8.1** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`).
- Production Compose example pins **`ghcr.io/.../api:v2.8.1`** and **`frontend:v2.8.1`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.8.1]**
- PRs on develop: [#184](https://github.com/trevordavies095/tempo/pull/184) (split order), [#186](https://github.com/trevordavies095/tempo/pull/186) / [#187](https://github.com/trevordavies095/tempo/pull/187) (split avg HR + backfill), [#189](https://github.com/trevordavies095/tempo/pull/189) (security)

### Post-merge checklist

- [ ] Tag **`v2.8.1`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.8.1`** exist for production pulls.
- [ ] Self-hosted upgrades: migration for `AvgHeartRateBpm` (automatic on API startup; split HR backfill runs in background).
- [ ] Confirm Dependabot alerts **104–108** auto-closed after the Next.js / `@humanfs/node` bump is on `main`.
