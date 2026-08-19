# PR: develop → main — Tempo 2.7.0

## Title

**Release 2.7.0: onboarding, import jobs, UI identity, workout charts**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.7.0**: first-run **onboarding**, background **import jobs** for Strava bulk and Tempo export restore (chunked upload + poll), a **command-center visual identity** (appearance, maps, overview charts with shared Highlight), **shoe retirement**, and a shared **Workout intake / track geometry** pipeline. This release builds on **2.6.0** (weekly recap, RPE, Next.js 16.2.x) already on `main`.

### Highlights

- **Onboarding** — Hard-gated wizard after registration; `OnboardingCompleted` on `GET /auth/me` and `POST /auth/onboarding/complete` (existing users backfilled completed).
- **Import jobs** — Strava bulk and Tempo restore via chunked ZIP upload; poll `GET /workouts/import/jobs/{id}`. **Breaking (API clients):** `POST /workouts/import/bulk` and `POST /workouts/import/export` return **202** with a job document instead of a blocking **200** summary.
- **Import UX** — Day-to-day Import is GPX/FIT only; Strava/Tempo ZIPs live under Settings → Migrate / restore (and onboarding).
- **Appearance + identity** — System / Dark / Light (browser-only); dark-first command center; Carto tiles by appearance; Workout overview time-series charts with shared Highlight.
- **Shoe retirement** — `IsRetired` on shoes; retire/restore from Settings.
- **Intake / geometry** — Shared `TrackPoint` → track geometry → Workout intake for single-file and Strava bulk; crop rebuilds from remaining points.

### Version & artifacts

- App version: **2.7.0** (`VERSION`, changelog, README / docs badges).
- Production Compose example pins **`ghcr.io/.../api:v2.7.0`** and **`frontend:v2.7.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.7.0]**
- [docs/openapi.json](openapi.json) — OpenAPI document (onboarding, import jobs, shoe retirement)

### Post-merge checklist

- [ ] Tag **`v2.7.0`** and publish the GitHub Release (notes from changelog).
- [ ] Confirm container images for **`v2.7.0`** exist for production pulls.
- [ ] Self-hosted upgrades: DB migrations for shoe retirement, import jobs, and `OnboardingCompleted` (automatic on API startup; existing users backfilled onboarding-complete).
- [ ] API clients: switch bulk/export import callers to **202** + poll `GET /workouts/import/jobs/{id}`.
