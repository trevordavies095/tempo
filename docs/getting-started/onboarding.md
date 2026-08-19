# First-run onboarding

After you register (or log in with an incomplete account), Tempo runs a hard-gated setup wizard. You cannot open Dashboard, Activities, Import, or Settings until onboarding finishes.

## What to expect

- A minimal shell (brand + steps). Logout remains available; the main navbar does not.
- Completion is stored on your account (`onboardingCompleted`), so another browser or device does not reopen setup.
- There is no “run setup again” entry after you finish. Ongoing configuration lives in [Settings](../user-guide/settings.md).

## Setup flow

### 1. Restore from a Tempo export?

**Yes** — upload a Tempo export ZIP (same chunked import job as Settings restore).

- Job **completed** and the export included settings → onboarding completes and you land on the dashboard.
- Job **completed** without settings → continue to essentials (do not skip units and heart rate zones).
- Job **failed** or **cancelled** — stay on the restore step: **Retry**, or **Set up fresh instead** (goes to essentials without marking onboarding complete).

**No** — continue to essentials.

### 2. Essential settings

Required before you can continue:

- Unit preference (metric or imperial) — affects split distances on import
- Heart rate zones (Age-based, Karvonen, or Custom) — so relative effort can be calculated on intake

Optional: collapsed **Add a default shoe** (create a shoe and set it as default). Appearance is not part of onboarding; change it later in Settings on this browser.

### 3. Import a Strava archive?

**No** — finish setup and go to the dashboard (empty but correctly configured).

**Yes** — upload a Strava export ZIP (`activities.csv` + `activities/`). Progress, cancel, and one-active-job rules match [Bulk Import](../user-guide/bulk-import.md).

- Job **completed** → onboarding completes → dashboard.
- Job **failed** or **cancelled** — **Retry**, or **Skip for now** (completes onboarding so you can use the app).

## After onboarding

| Task | Where |
|------|--------|
| Day-to-day GPX / FIT / `.fit.gz` files | [Import](../user-guide/importing-workouts.md) |
| Late Strava archive or Tempo export restore | Settings → Data Management → **Migrate / restore** |
| Routine Tempo backup ZIP | Settings → **Export** |
| Units, heart rate zones, shoes | [Settings](../user-guide/settings.md) |

## Upgrades

When Tempo adds onboarding, existing accounts are backfilled as already completed. Upgrading does not force you through the wizard.

## Next steps

- [Import workouts](../user-guide/importing-workouts.md)
- [Bulk import from Strava](../user-guide/bulk-import.md)
- [Backup and restore](../deployment/backup-restore.md)
