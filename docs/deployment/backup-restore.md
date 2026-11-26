# Backup and Restore

How to back up and restore a production Compose install, either at the host level (database dump + media + `.env`) or with the in-app Tempo export ZIP. Commands assume you are in the directory holding `docker-compose.prod.yml`; for the development stack, drop `-f docker-compose.prod.yml`.

## What to back up

| Item | Where | Notes |
|---|---|---|
| Database | Volume `postgres_data`, database `tempo`, user `postgres`, service `postgres` | Workouts, settings, shoes, account, API keys, Intervals.icu connection, best efforts. |
| Media | `./media` next to the Compose file (`/app/media` in the API container) | WorkoutMedia files, one directory per Workout id. `./media/imports/` is transient import job staging; skip it. |
| `.env` | Compose directory | `JWT_SECRET_KEY` must match the database: stored Intervals.icu and API key secrets are encrypted with keys derived from it. `POSTGRES_PASSWORD` must match the volume. |

## Choosing a method

| | Host backup (`pg_dump` + media + `.env`) | Tempo export ZIP |
|---|---|---|
| Made from | Shell on the host | Settings → Data Management → **Export All Data** |
| Contains | Everything | Settings, shoes, Workouts (routes, splits, time series, raw GPX/FIT/CSV files), media |
| Not included | Nothing | Account and password, Tempo API keys, Intervals.icu connection, best efforts (recomputed on restore) |
| Restore | Replaces the database | Merges into an existing install, skipping duplicates |
| Automate | Yes (cron) | No (manual download) |
| Use for | Disaster recovery, host moves | Portable copy, moving between installs, first-run restore |

Do both: scheduled host backups, plus an occasional export ZIP.

## Host backup

### Database

`pg_dump` inside the `postgres` container (local socket; no password needed):

```bash
docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U postgres -d tempo | gzip > "tempo-db-$(date +%Y%m%d-%H%M%S).sql.gz"
```

The dump is plain SQL. It is consistent while Tempo is running.

### Media

```bash
tar -czf "tempo-media-$(date +%Y%m%d-%H%M%S).tar.gz" --exclude='media/imports' media
```

Or incrementally: `rsync -a --delete --exclude=imports/ ./media/ /backup/tempo/media/`.

### Script and schedule

```bash
#!/usr/bin/env bash
# /opt/tempo/backup.sh — run from cron; keeps 30 days
set -euo pipefail
cd /opt/tempo                       # directory with docker-compose.prod.yml, .env, media/
BACKUP_DIR=/backup/tempo
DATE=$(date +%Y%m%d-%H%M%S)
mkdir -p "$BACKUP_DIR"

docker compose -f docker-compose.prod.yml exec -T postgres \
  pg_dump -U postgres -d tempo | gzip > "$BACKUP_DIR/db-$DATE.sql.gz"
tar -czf "$BACKUP_DIR/media-$DATE.tar.gz" --exclude='media/imports' media
cp .env "$BACKUP_DIR/env-$DATE"
chmod 600 "$BACKUP_DIR/env-$DATE"

find "$BACKUP_DIR" -type f -mtime +30 -delete
```

```bash
# crontab -e — daily at 02:00
0 2 * * * /opt/tempo/backup.sh >> /var/log/tempo-backup.log 2>&1
```

The backups contain secrets and personal data. Encrypt before moving off-host, e.g. `gpg --symmetric --cipher-algo AES256 db-….sql.gz`.

## Host restore

Restores replace the current database. Use the `.env` (at least `JWT_SECRET_KEY`) from the same backup.

1. Stop the app, leave Postgres running:
   ```bash
   docker compose -f docker-compose.prod.yml stop api frontend
   ```
   On a new host, start only the database first: `docker compose -f docker-compose.prod.yml up -d postgres`.
2. Recreate an empty database and load the dump:
   ```bash
   docker compose -f docker-compose.prod.yml exec -T postgres dropdb -U postgres --if-exists tempo
   docker compose -f docker-compose.prod.yml exec -T postgres createdb -U postgres tempo
   gunzip -c tempo-db-YYYYMMDD-HHMMSS.sql.gz | docker compose -f docker-compose.prod.yml exec -T postgres \
     psql -U postgres -d tempo -v ON_ERROR_STOP=1 -q
   ```
   Load into an empty database: the API creates the schema on start, so restoring over a migrated database fails on existing objects.
3. Restore media:
   ```bash
   tar -xzf tempo-media-YYYYMMDD-HHMMSS.tar.gz     # recreates ./media
   ```
4. Start:
   ```bash
   docker compose -f docker-compose.prod.yml up -d
   ```
   Migrations bring an older dump up to the running version. Check `/api/ready` and the `Startup backfills` log line ([Updating](docker.md#updating)).

## Tempo export and restore

### Export

Settings → Data Management → **Export** → **Export All Data** downloads a ZIP:

```
manifest.json            format version 1.0.0, Tempo version, counts
README.txt
data/                    settings, shoes, workouts, routes, splits, time-series, media-metadata (JSON)
workouts/<id>/raw/       original GPX / FIT / CSV, when stored
workouts/<id>/media/     photos and videos
```

### Restore

- **New install:** register, then use the **Restore from Tempo export** step of [onboarding](../getting-started/onboarding.md).
- **Existing install:** Settings → Data Management → **Migrate / restore** → **Restore Tempo export**, then drop the ZIP.

The ZIP (up to 500 MB) uploads in chunks and is processed as a background import job: progress shows upload, then `processed` / `total`. You can cancel; data already restored stays. Only one import job (Tempo restore or Strava bulk import) runs at a time.

Behavior:

- Keeps the export's ids and timestamps.
- Skips Workouts and shoes that already exist (same id, or same start time, distance, and elapsed time for Workouts).
- Overwrites UserSettings when the export has them.
- Recomputes derived data (best efforts and other backfills) before the job reports **Completed**.
- Accepts export format version `1.0.0` only.

Details: [Settings → Export / Import](../user-guide/settings.md#export-import).

## Testing restores

Restore the latest dump into a scratch database next to the live one:

```bash
docker compose -f docker-compose.prod.yml exec -T postgres createdb -U postgres tempo_restore_test
gunzip -c tempo-db-YYYYMMDD-HHMMSS.sql.gz | docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U postgres -d tempo_restore_test -v ON_ERROR_STOP=1 -q
docker compose -f docker-compose.prod.yml exec -T postgres \
  psql -U postgres -d tempo_restore_test -c 'SELECT count(*) FROM "Workouts";'
docker compose -f docker-compose.prod.yml exec -T postgres dropdb -U postgres tempo_restore_test
```

Check the media archive: `tar -tzf tempo-media-….tar.gz | wc -l`. Periodically do a full restore onto a spare host.

## Troubleshooting

| Symptom | Cause |
|---|---|
| `psql` errors like `relation … already exists` | Database was not empty. Drop and recreate it (step 2), with `api` stopped. |
| `dropdb` fails: database is being accessed | `api` is still running; `stop api frontend` first. |
| After restore: Intervals.icu sync fails, API keys cannot be revealed | `JWT_SECRET_KEY` differs from the backup's. Restore the old `.env`, or reconnect Intervals.icu and create new API keys. |
| Photos missing | `./media` not restored, or restored to a directory other than the one next to the Compose file. |
| Export restore rejected | Not a Tempo export, a format version other than `1.0.0`, or over 500 MB. |

## Next Steps

- [Docker Deployment](docker.md)
- [Security](security.md)
