# Production Setup

What to check and tune after a production Compose install is running: a go-live checklist, large uploads, health checks, logging, resources. Install and upgrade steps are in [Docker Deployment](docker.md); every setting is in [Configuration](../getting-started/configuration.md).

## Checklist

Already handled by `docker-compose.prod.yml`:

- [x] `JWT_SECRET_KEY` and `POSTGRES_PASSWORD` required from `.env`
- [x] Postgres and API not published on the host; command center on `127.0.0.1:3004` only
- [x] Image tags pinned to a release
- [x] Health checks and `restart: unless-stopped`

Yours:

- [ ] HTTPS reverse proxy with one upstream to `127.0.0.1:3004` ([Reverse Proxy](reverse-proxy.md))
- [ ] Firewall exposes only the proxy (80/443)
- [ ] `.env` backed up (losing `JWT_SECRET_KEY` breaks stored integration secrets; see [Rotating the JWT secret](../getting-started/configuration.md#rotating-the-jwt-secret))
- [ ] `CARTO_BASEMAPS_API_KEY` set, if maps should not be watermarked
- [ ] `./media` on a disk with room for media plus a 500 MB import staging file
- [ ] Database and media backups scheduled and a restore tested ([Backup and Restore](backup-restore.md))
- [ ] `/api/ready` monitored
- [ ] Host and image updates applied ([Updating](docker.md#updating))

CORS is not needed on this path: the command center calls same-origin `/api`, and the daily driver is a native app. Set `CORS__AllowedOrigins` only if a browser on another origin calls the API directly.

## Large uploads

ZIP archives (Strava exports, Tempo exports) are limited to 500 MB. The command center uploads them in 512 KiB chunks over `/api`, then the import job processes them in the background (poll and cancel), so a default proxy body limit works for the UI.

Whole-ZIP adapters (`POST /workouts/import/bulk`, `POST /workouts/import/export`) and other direct posts send the full body in one request. For those, your proxy needs:

- Body size limit of at least 500 MB
- Read/send timeouts of 10–30 minutes, depending on upload speed

The API side is already set (Kestrel and multipart limits 500 MB, no minimum data rate, 10-minute keep-alive) and not configurable.

Smoke test: upload a large Strava or Tempo export at Settings → Data Management → **Migrate / restore** and watch upload progress, then processing progress.

## Monitoring and Logging

### Health checks

```bash
curl -f https://tempo.example.com/api/ready
docker compose -f docker-compose.prod.yml exec api curl -f http://localhost:5001/ready
```

Use `/ready` (`200` when Postgres answers within 2 s, else `503`). `/api/health` is liveness only. Public `/health` (no `/api`) is the command center process. Compose already health-checks the API on `/ready` and the frontend on its `/health`.

### Logging

Production Compose uses logging profile `standard` (Tempo Information; no EF SQL or per-request framework noise). The API logs `Logging profile: standard` at startup. Profiles: [Logging profiles](../getting-started/configuration.md#logging-profiles).

```bash
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f frontend
```

Capturing a debug log for an import or query bug:

1. Set `TEMPO_LOGGING_PROFILE=debug` in `.env`; `docker compose -f docker-compose.prod.yml up -d api`.
2. Reproduce once.
3. Save `docker compose -f docker-compose.prod.yml logs api` (include the `Logging profile: debug` line).
4. Remove the setting (or set `standard`) and `up -d api` again.

Do not tune `Logging:LogLevel` or Serilog overrides; the profile is the interface.

#### Postgres checkpoints

Official Compose runs Postgres with `log_checkpoints=off`; the API `debug` profile does not change that. For WAL debugging, add `-c log_checkpoints=on` to the `postgres` service `command`.

### Resources

```bash
docker stats
```

## Resource limits

Compose does not set limits. To add them, per service:

```yaml
deploy:
  resources:
    limits:
      cpus: '2'
      memory: 4G
```

Backfills after an upgrade and large imports are the CPU, disk, and WAL peaks.

## Next Steps

- [Security](security.md)
- [Backup and Restore](backup-restore.md)
- [Reverse Proxy](reverse-proxy.md)
