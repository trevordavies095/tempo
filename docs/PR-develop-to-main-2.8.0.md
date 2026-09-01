# PR: develop → main — Tempo 2.8.0

## Title

**Release 2.8.0: HealthKit import, lighter workout payloads, Dependabot patches**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.8.0**. Builds on **2.7.0** (onboarding, import jobs, UI identity) already on `main`.

### Added

- **HealthKit import (tempo-ios)** — `POST /workouts/import/healthkit` accepts a versioned HealthKit JSON document (outdoor GPS or indoor distance/summary) and persists through the same workout intake pipeline as file import. Indoor runs without GPS still get splits/time series from the distance stream.
- **HealthKit UUID** — Optional unique `healthKitUuid` on workouts for idempotent iOS imports. `GET /workouts/healthkit-uuids` returns stored UUIDs so tempo-ios can badge already-imported runs without paging the feed.
- **CARTO basemaps API key** — Optional `CartoBasemaps:ApiKey` / `CartoBasemaps__ApiKey` on the API (Docker: `CARTO_BASEMAPS_API_KEY` in `.env`). Authenticated `GET /settings/carto-basemaps` exposes the key to the command center; workout maps append `?key=` to CARTO raster tile URLs.
- **`includeRaw` on workout detail** — `GET /workouts/{id}` omits raw GPX/FIT/Strava/HealthKit JSON by default. Pass `includeRaw=true` to include the blobs.
- **Route previews on the workout list** — `GET /workouts` item `route` is a ≤ 100-point GeoJSON LineString (Douglas-Peucker). Startup backfill fills `WorkoutRoutes.PreviewGeoJson`.
- **List `media` field** — `media: [{ id, mimeType }, ...]` ordered by `createdAt` (empty array when none).
- **List `splitsCount`** — SQL `COUNT` of split rows; split rows themselves are not loaded.

### Changed

- Crop, duplicate-update, and split/route recalculation that rewrite `RouteGeoJson` also recompute `PreviewGeoJson` in the same save.

### Security

- **Next.js 16.2.11** — `next` and `eslint-config-next` **>=16.2.11**.
- **Transitive npm overrides** — postcss, nanoid, js-yaml, brace-expansion, sharp, @babel/core.
- **API OpenAPI stack** — Microsoft.OpenApi 2.7.5 and Swashbuckle.AspNetCore 10.2.3.

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
