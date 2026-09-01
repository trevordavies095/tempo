# PR: develop → main — Tempo 2.8.0

## Title

**Release 2.8.0: HealthKit import, lighter workout payloads, Dependabot patches**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.8.0**: **HealthKit import** for tempo-ios (outdoor GPS and indoor distance/summary, idempotent UUID), **smaller workout list/detail payloads** (route previews, list `media` / `splitsCount`, `includeRaw` on detail), optional **CARTO basemaps API key**, and **Dependabot security bumps** (Next.js 16.2.11, transitive npm pins, Microsoft.OpenApi 2.7.5). This release builds on **2.7.0** (onboarding, import jobs, UI identity) already on `main`.

### Highlights

- **HealthKit** — `POST /workouts/import/healthkit` through the same intake pipeline as file import; unique `healthKitUuid`; `GET /workouts/healthkit-uuids` for iOS already-imported badges.
- **Lighter payloads** — List `route` is a ≤ 100-point preview; list items include `media` and `splitsCount`; `GET /workouts/{id}` omits raw GPX/FIT/Strava/HealthKit JSON unless `includeRaw=true`.
- **CARTO** — Optional `CartoBasemaps__ApiKey` / `CARTO_BASEMAPS_API_KEY`; command center maps append `?key=` when configured.
- **Security** — Next.js / eslint-config-next **>=16.2.11**; npm overrides for postcss, nanoid, js-yaml, brace-expansion, sharp, @babel/core; Microsoft.OpenApi 2.7.5 + Swashbuckle 10.2.3.

### Version & artifacts

- App version: **2.8.0** (`VERSION`, changelog, README / docs badges).
- Production Compose example pins **`ghcr.io/.../api:v2.8.0`** and **`frontend:v2.8.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.8.0]**
- [docs/openapi.json](openapi.json) — OpenAPI document (HealthKit import, healthkit-uuids, includeRaw, list media / splitsCount / route preview)

### Post-merge checklist

- [ ] Tag **`v2.8.0`** and publish the GitHub Release (notes from changelog).
- [ ] Confirm container images for **`v2.8.0`** exist for production pulls.
- [ ] Self-hosted upgrades: DB migrations for HealthKit raw JSON, unique `HealthKitUuid`, and `PreviewGeoJson` (automatic on API startup; route previews backfill in batches).
- [ ] Close Dependabot [PR #181](https://github.com/trevordavies095/tempo/pull/181) as superseded by the security bump already on `develop`.
