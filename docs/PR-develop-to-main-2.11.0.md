# PR: develop → main — Tempo 2.11.0

## Title

**Release 2.11.0: Next.js RCE patches, best-effort history, thresholds, run types, lap intensity, API key settings**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.11.0**. Builds on **2.10.1** already on `main`. Ships two critical Next.js RCE fixes alongside the feature work queued on `develop`.

### Security

- **Next.js 16.4.1** — `next` and `eslint-config-next` **>=16.4.1** ([GHSA-673q-mjm6-r5xr](https://github.com/advisories/GHSA-673q-mjm6-r5xr), [GHSA-2464-fhr4-w9xw](https://github.com/advisories/GHSA-2464-fhr4-w9xw), [GHSA-p3m5-59mp-jv5r](https://github.com/advisories/GHSA-p3m5-59mp-jv5r)).
- **`sharp` 0.35.5** ([GHSA-wq5f-xc86-pv6w](https://github.com/advisories/GHSA-wq5f-xc86-pv6w)).
- **`source-map-js` 1.2.2** ([GHSA-68fv-2mgg-jv7q](https://github.com/advisories/GHSA-68fv-2mgg-jv7q)).

### Added

- **Best efforts by date range and PR history** — `GET /stats/best-efforts` `from` / `to` / `since`, with `previousTimeS`, `previousWorkoutId`, `previousWorkoutDate`.
- **Threshold pace and HR** — `GET` / `PUT /settings/thresholds`, with a suggested threshold pace from recent 5K/10K efforts.
- **Observed max HR** on the heart-rate-zones endpoints.
- **New run types** — `Recovery`, `Walk-Run`, `Threshold`, `Intervals`, `Hills`, `Fartlek`.
- **Device lap intensity, trigger, and step index** on FIT splits (startup backfill).
- **Weekly recap** — HR zone time, moving time, longest run, runs by type.
- **Settings → Account → API key** — generate, reveal, copy, revoke.
- **Similar-routes HR fields**, **relative-effort `referenceDate`**, **OpenAPI response schemas**.

### Changed

- **Best efforts stored per workout** — `BestEfforts` replaced by `WorkoutBestEfforts`, with a startup backfill; ZIP export drops `best-efforts.json`.
- **`maxHeartRateBpm` always resolved** (never null).
- **Time in HR zones stored per workout** (startup backfill).
- **Weekly recap easy-run HR includes Recovery.**
- **One active API key per user** (409 on a second key).

### Removed

- **Leftover in-Tempo coach schema** dropped on startup; `:feat-ai-coach` images retired.

### Upgrade notes

- **API keys:** the migration revokes all but each user's newest active key. Clients on an older key get **401**; copy the current key from Settings.
- **Startup backfills:** best efforts, HR zone times, and FIT lap intensity fill in after the first start. Date-range and `since` best-effort results only cover workouts the backfill has reached.
- **`:feat-ai-coach`:** switching off that image discards in-Tempo coach data.

### Version & artifacts

- App version: **2.11.0** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`, bug report template).
- Production Compose example pins **`ghcr.io/.../api:v2.11.0`** and **`frontend:v2.11.0`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.11.0]**
- PRs on develop: [#244](https://github.com/trevordavies095/tempo/pull/244), [#249](https://github.com/trevordavies095/tempo/pull/249), [#250](https://github.com/trevordavies095/tempo/pull/250), [#251](https://github.com/trevordavies095/tempo/pull/251), [#252](https://github.com/trevordavies095/tempo/pull/252), [#254](https://github.com/trevordavies095/tempo/pull/254), [#260](https://github.com/trevordavies095/tempo/pull/260), [#261](https://github.com/trevordavies095/tempo/pull/261), [#262](https://github.com/trevordavies095/tempo/pull/262), [#263](https://github.com/trevordavies095/tempo/pull/263), [#264](https://github.com/trevordavies095/tempo/pull/264), [#265](https://github.com/trevordavies095/tempo/pull/265), [#266](https://github.com/trevordavies095/tempo/pull/266), [#267](https://github.com/trevordavies095/tempo/pull/267), [#269](https://github.com/trevordavies095/tempo/pull/269), [#271](https://github.com/trevordavies095/tempo/pull/271)

### Post-merge checklist

- [ ] Tag **`v2.11.0`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.11.0`** exist for production pulls.
- [ ] Confirm Dependabot alerts **116, 118–123** auto-closed once the Next.js bump is on `main`.
- [ ] After deploying, watch the startup logs for the best-effort backfill to finish.
