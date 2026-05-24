# PR: develop → main — Tempo 2.6.0

## Title

**Release 2.6.0: weekly recap API, RPE on workouts, Next.js 16.2.x**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.6.0**: adds a **weekly recap** stats endpoint for week-over-week aggregates, **RPE (1–10)** on workouts (API + workout detail UI), and bumps the **Next.js / ESLint** stack to **>=16.2.6**. This release builds on **2.5.0** (password policy, session versioning, PostCSS advisory) already on `main`.

### Highlights

- **`GET /stats/weekly-recap`** — Compact metrics for the reference week vs the previous week (`timezoneOffsetMinutes`, optional `referenceDate`).
- **RPE** — Stored per workout; `PATCH /workouts/{id}` accepts `rpe` (1–10 or null); surfaced on the workout detail page.
- **Next.js** — Dependency and ESLint config updates on the frontend.

### Version & artifacts

- App version: **2.6.0** (`VERSION`, changelog, README badge).
- Production Compose example pins **`ghcr.io/.../api:v2.6.0`** and **`frontend:v2.6.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.6.0]**
- [docs/openapi.json](openapi.json) — OpenAPI document (includes weekly recap and RPE on workout update)

### Post-merge checklist

- [ ] Tag **`v2.6.0`** and publish the GitHub Release (notes from changelog).
- [ ] Confirm container images for **`v2.6.0`** exist for production pulls.
- [ ] Self-hosted upgrades: ensure **DB migration** for `AddRpeToWorkout` (`Rpe` on `Workouts`).
