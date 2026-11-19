# PR: develop → main — Tempo 2.12.0

## Title

**Release 2.12.0: backfills off the start path, login behind a reverse proxy, Insights, Weekly Recap card, Garmin device names**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.12.0**. Builds on **2.11.0** already on `main`. Startup backfills now run once and are recorded, then leave the default start path; login works behind a TLS-terminating proxy; and the command center catches up with the iOS app (Insights, Weekly Recap card, Activities date filter, route comparison).

### Added

- **Backfills off the default start path (#217, #218)** — backfills are recorded in `CompletedBackfills` and normal starts run none; new `dotnet Tempo.Api.dll backfill [--force]` host command; `Tempo:Backfills:OnStartup` = `skip` (default) | `run` | `force`.
- **Insights page** — summary row, Personal Records with range picker and previous bests, Weather Extremes, Data Coverage.
- **Weekly Recap card** replaces This Week on the dashboard (arrows hidden until the week has a run).
- **Activities date filter**, **Laps or Splits** labels with lap intensity tags.
- **`GET /stats/weekly` `referenceDate`.**

### Changed

- **Login behind a reverse proxy (#220)** — trusted `X-Forwarded-*` only; `Cookie:Secure` / `TEMPO_COOKIE_SECURE` explicit opt-out.
- **Route comparison matches iOS Similar Runs** (delta wording, trend rule, pace axis).
- **Frontend lint in CI.**

### Fixed

- **Device names for every Garmin** from the FIT SDK product tables.
- **Crop** keeps the cropped route and splits, and no longer reports failure after cropping.
- **Re-importing a summary-only workout** fills heart-rate metrics; one Relative Effort rule for every recalculation; best efforts no longer depend on Relative Effort.
- **One-row settings tables (#236)** and the last EF `First` without `OrderBy` warnings.
- **Faster similar routes**; the 52-week chart no longer flashes km.

### Upgrade notes

- **Backfills:** installs upgrading straight from 2.11.0 or earlier log a warning on first start naming the unrecorded backfills. Run `docker compose exec api dotnet Tempo.Api.dll backfill` once, or set `TEMPO_BACKFILLS_ON_STARTUP=run` for one start. Production Compose now defaults `TEMPO_BACKFILLS_ON_STARTUP` to `skip`.
- **Settings tables:** the migration keeps the most recently updated `UserSettings` / `IntervalsIcuConnections` row and deletes any extras.
- **Reverse proxy:** behind a proxy outside loopback/private ranges, set `Tempo:Proxy:TrustedNetworks`. `TEMPO_COOKIE_SECURE=false` is now the explicit way to log in over plain HTTP on a LAN.

### Version & artifacts

- App version: **2.12.0** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`, bug report template).
- Production Compose example pins **`ghcr.io/.../api:v2.12.0`** and **`frontend:v2.12.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.12.0]**
- PRs on develop: [#274](https://github.com/trevordavies095/tempo/pull/274), [#275](https://github.com/trevordavies095/tempo/pull/275), [#276](https://github.com/trevordavies095/tempo/pull/276), [#277](https://github.com/trevordavies095/tempo/pull/277), [#278](https://github.com/trevordavies095/tempo/pull/278), [#279](https://github.com/trevordavies095/tempo/pull/279), [#280](https://github.com/trevordavies095/tempo/pull/280), [#281](https://github.com/trevordavies095/tempo/pull/281), [#282](https://github.com/trevordavies095/tempo/pull/282), [#283](https://github.com/trevordavies095/tempo/pull/283), [#284](https://github.com/trevordavies095/tempo/pull/284), [#285](https://github.com/trevordavies095/tempo/pull/285), [#286](https://github.com/trevordavies095/tempo/pull/286), [#287](https://github.com/trevordavies095/tempo/pull/287), [#288](https://github.com/trevordavies095/tempo/pull/288), [#289](https://github.com/trevordavies095/tempo/pull/289), [#290](https://github.com/trevordavies095/tempo/pull/290), [#291](https://github.com/trevordavies095/tempo/pull/291), [#292](https://github.com/trevordavies095/tempo/pull/292), [#293](https://github.com/trevordavies095/tempo/pull/293), [#294](https://github.com/trevordavies095/tempo/pull/294)

### Post-merge checklist

- [ ] Tag **`v2.12.0`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.12.0`** exist for production pulls.
- [ ] After deploying, run the `backfill` command once if the startup log warns about unrecorded backfills.
