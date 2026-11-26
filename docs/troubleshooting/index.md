# Troubleshooting

- **[Common Issues](common-issues.md)** — symptom → cause → fix, with the exact UI and log strings
- **[FAQ](faq.md)** — how Tempo behaves and why

Feature pages carry their own troubleshooting tables: [Importing Workouts](../user-guide/importing-workouts.md#troubleshooting), [Bulk Import](../user-guide/bulk-import.md#troubleshooting), [Migrate / restore](../user-guide/settings.md#migrate--restore), [Media](../user-guide/media.md#troubleshooting).

## First Checks

1. **API logs:** `docker compose logs -f api` (add `-f docker-compose.prod.yml` in production). Errors from intake, import jobs, weather, and intervals.icu are logged here.
2. **Readiness:** `GET /ready` returns 200 when Postgres answers within 2 s, else 503. `GET /health` only checks that the API process is up.
3. **Browser:** the UI shows API errors as `Error: <message>`; the network tab has the JSON `{ "error": "…" }` body.
4. **Version:** Settings → System Information has a copyable block (version, commit, logging profile, image tags).
5. **More logging:** set `TEMPO_LOGGING_PROFILE=debug` (see [Logging profiles](../getting-started/configuration.md#logging-profiles)).

## Where to Look

| Area | Common Issues section |
|---|---|
| Containers, ports, database, backfills | [Installation and Startup](common-issues.md#installation-and-startup) |
| Login, cookies, onboarding, CORS | [Login and Sessions](common-issues.md#login-and-sessions) |
| GPX/FIT uploads | [Single-File Import](common-issues.md#single-file-import) |
| Strava ZIP, Tempo restore | [Strava Bulk Import and Tempo Restore](common-issues.md#strava-bulk-import-and-tempo-restore) |
| Maps, weather, Relative Effort, splits, Best efforts | [Workout Data](common-issues.md#workout-data) |
| intervals.icu | [intervals.icu Sync](common-issues.md#intervalsicu-sync) |
| Photos and videos | [Media](common-issues.md#media) |

## Reporting an Issue

Search [GitHub Issues](https://github.com/trevordavies095/tempo/issues) or ask on [Discord](https://discord.gg/9Svd99npyj). A new issue should include:

- What you did, what you expected, what happened
- The Settings → System Information block
- Relevant API log lines and the UI error text
- Deployment details (Compose file, reverse proxy, HTTPS or plain HTTP)
