# Deployment

Running Tempo on a server you control with production Compose (`docker-compose.prod.yml`): pinned GHCR images, secrets from `.env`, one HTTPS origin for the command center and the daily driver.

| Page | Use it for |
|---|---|
| [Docker Deployment](docker.md) | Install, update, stop, password recovery. Start here. |
| [Reverse Proxy and Login Cookies](reverse-proxy.md) | Caddy / nginx / Traefik configs; login that doesn't stick. |
| [Production Setup](production.md) | Go-live checklist, large uploads, health checks, logging, resources. |
| [Security](security.md) | What Tempo enforces and what is left to you. |
| [Backup and Restore](backup-restore.md) | Database dumps, media, `.env`, Tempo export ZIP. |

Settings and env vars: [Configuration](../getting-started/configuration.md). Local trial: [Quick Start](../getting-started/quick-start.md).

## Shape

```
internet ──443──▶ reverse proxy ──▶ 127.0.0.1:3004 frontend ──/api──▶ api:5001 ──▶ postgres:5432
                                    (command center)                  (Compose network only)
```

- **Required in `.env`:** `JWT_SECRET_KEY`, `POSTGRES_PASSWORD`. Compose will not start without them.
- **HTTPS required:** the session cookie is `Secure`.
- **Ready check:** `https://<origin>/api/ready`. `/api/health` is liveness only; `/health` is the command center process.
- **Version:** `https://<origin>/api/version`.
- **Logs:** `docker compose -f docker-compose.prod.yml logs -f api`.
