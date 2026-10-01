# PR: develop → main — Tempo 2.10.1

## Title

**Release 2.10.1: Next.js / Dependabot security patches, intervals.icu sync wake fix**

---

## Description

### Summary

Merges **`develop` → `main`** for **Tempo 2.10.1**. Builds on **2.10.0** (intervals.icu, external identities, ops hardening) already on `main`.

### Fixed

- **intervals.icu sync queue stuck pending** — `BeginTick` clears `_pending` so Sync now can wake again after a completed tick; `BeginTick` runs before any await; worker `EndTick` safety net if the tick fails early.

### Security

- **Next.js 16.3.8** — `next` and `eslint-config-next` **>=16.3.8** ([GHSA-vcvr-r3jv-pc5j](https://github.com/advisories/GHSA-vcvr-r3jv-pc5j)).
- **`brace-expansion` overrides** — **1.1.21** / **2.1.7** ([GHSA-q2hr-2g5m-vwhr](https://github.com/advisories/GHSA-q2hr-2g5m-vwhr)).
- **`ajv` override** — **^6.14.0** ([GHSA-2g4f-4pwh-qvx6](https://github.com/advisories/GHSA-2g4f-4pwh-qvx6)).

### Version & artifacts

- App version: **2.10.1** (`VERSION`, changelog, README / docs badges, OpenAPI `info.version`).
- Production Compose example pins **`ghcr.io/.../api:v2.10.1`** and **`frontend:v2.10.1`** — publish matching images when tagging the release.

### References

- [CHANGELOG.md](../CHANGELOG.md) — section **[2.10.1]**
- PRs on develop: [#239](https://github.com/trevordavies095/tempo/pull/239) (intervals.icu sync queue), [#240](https://github.com/trevordavies095/tempo/pull/240) (Dependabot / Next.js)

### Post-merge checklist

- [ ] Tag **`v2.10.1`** and publish the GitHub Release (notes from changelog) — release workflow builds/pushes images.
- [ ] Confirm container images for **`v2.10.1`** exist for production pulls.
- [ ] Confirm Dependabot alerts **113–115** auto-closed after the Next.js / `brace-expansion` bump is on `main`.
