# Production Setup

Configure Tempo for production deployment with best practices.

## Overview

This guide covers production configuration, environment variables, and deployment considerations.

## Environment Variables

Set secrets in `.env` (copy from `.env.example`). Production Compose fails closed if they are missing or empty — see [Docker Deployment](docker.md).

### Required Configuration

#### JWT Secret Key

**CRITICAL**: Must be set to a secure random value in `.env` as `JWT_SECRET_KEY`.

```bash
# Generate a secure key
openssl rand -base64 32
```

```bash
# In .env
JWT_SECRET_KEY=your-generated-secret-key-here
```

#### Database password

**CRITICAL**: Set `POSTGRES_PASSWORD` in `.env` (same value for Postgres and the API connection string). Do not use `;`. Existing volumes must use the password already in the cluster; editing `.env` alone does not rotate it.

```bash
# In .env
POSTGRES_PASSWORD=your-secure-password
```

### CORS (usually not needed)

The command center talks same-origin `/api` through the Next rewrite. The daily driver is native. Production Compose does **not** set `CORS__AllowedOrigins`. Only configure CORS if you intentionally expose the API to a browser on another origin.

### Media Storage

Defaults live in the API image / `appsettings.json`. Compose mounts media at `/app/media`.

### CARTO Basemaps (Maps)

Workout maps require a free CARTO API key. Set in `.env` (gitignored):

```bash
CARTO_BASEMAPS_API_KEY=your-key-here
```

Compose passes this to the API as `CartoBasemaps__ApiKey`. Request a key at [carto.com/basemaps/apikey](https://carto.com/basemaps/apikey). Restart the API and hard-refresh the browser after updating.

### JWT issuer / expiry

Issuer, audience, and expiration defaults are in the API image. Override with `JWT__*` env vars only if you need non-defaults.

## Production Checklist

### Security

- [x] JWT secret required from `.env` (`JWT_SECRET_KEY`) — done by production Compose
- [x] Database password required from `.env` (`POSTGRES_PASSWORD`) — done by production Compose (choose a strong value; existing volumes must match the cluster)
- [ ] HTTPS configured (required for secure cookies)
- [x] Postgres and API not published on the host — done by production Compose (command center is `127.0.0.1:3004` only)
- [x] Image tags pinned in Compose (not `latest` as the contract) — done by production Compose
- [ ] Firewall rules configured (expose the reverse proxy, not Compose ports)
- [ ] Regular security updates applied

### Configuration

- [ ] `.env` filled from `.env.example`
- [ ] Media directory (`./media`) writable
- [ ] CARTO basemaps API key set (`CARTO_BASEMAPS_API_KEY` in `.env`) if maps should not show the watermark
- [ ] Reverse proxy pointed at `127.0.0.1:3004`
- [x] Health checks enabled — done by production Compose

### Data Management

- [ ] Backup strategy in place
- [ ] Media directory included in backups
- [ ] Database backup automated
- [ ] Sufficient disk space allocated

### Monitoring

- [ ] Ready check reachable via the public origin (`/api/ready`) or `docker compose exec`
- [ ] Logs configured and monitored
- [ ] Resource usage monitored

## Reverse Proxy Setup

Point **one** upstream at the command center on loopback. `/api` is rewritten inside the frontend container to the API on the Compose network — do not split `/api` to a host `:5001` unless you uncomment the opt-in loopback API publish.

### Nginx Example

```nginx
server {
    listen 80;
    server_name yourdomain.com;
    
    # Redirect to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name yourdomain.com;
    
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    
    # Command center (Next rewrites /api to the API on the Compose network)
    location / {
        proxy_pass http://127.0.0.1:3004;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Optional: whole-ZIP adapters through this host (command center uses 512 KiB chunks)
        client_max_body_size 500M;
        proxy_read_timeout 600s;
        proxy_connect_timeout 600s;
        proxy_send_timeout 600s;
    }
}
```

### Caddy Example

```caddy
tempo.yourdomain.com {
    tls {
        protocols tls1.2 tls1.3
    }

    # One upstream: command center. /api is rewritten to the API inside Docker.
    reverse_proxy 127.0.0.1:3004 {
        header_up Host {host}
        header_up X-Real-IP {remote}
        header_up X-Forwarded-For {remote_host}
        header_up X-Forwarded-Proto {scheme}
        transport http {
            read_timeout 30m
            write_timeout 30m
        }
    }
}
```

**Note**:
- Use the same public HTTPS origin for the command center and the daily driver
- If you still need Bruno/curl on the host API port, uncomment `127.0.0.1:5001:5001` in `docker-compose.prod.yml` (loopback only)
- Upgrading from a split `/` + `/api`→`:5001` proxy: merge to this one-upstream shape or temporarily keep the loopback API publish

### Traefik Example

```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.tempo.rule=Host(`yourdomain.com`)"
  - "traefik.http.routers.tempo.entrypoints=websecure"
  - "traefik.http.routers.tempo.tls.certresolver=letsencrypt"
  # Large file upload support
  - "traefik.http.middlewares.tempo-buffering.buffering.maxRequestBodyBytes=524288000"  # 500MB
  - "traefik.http.middlewares.tempo-timeout.buffering.retryExpression=IsNetworkError() && Attempts() < 2"
  - "traefik.http.routers.tempo.middlewares=tempo-buffering,tempo-timeout"
```

## Large File Upload Requirements

Tempo supports ZIP archives up to 500MB (Strava exports, Tempo exports). The **command center** uploads those archives in **512 KiB** chunks over `/api`, so a default reverse-proxy body limit is usually enough for the UI path. Processing runs as a background import job (poll + cancel), not as one long multipart request.

**Whole-ZIP adapters** (`POST /workouts/import/bulk`, `POST /workouts/import/export`) and direct posts to the API still send the full body in one request. Prefer the public origin’s `/api` path, or uncomment loopback `:5001` for host tooling.

### Settings for whole-ZIP / direct API uploads

- **Maximum body size**: 500MB minimum
- **Read timeout**: 10-30 minutes (depending on expected upload speed)
- **Write timeout**: 10-30 minutes
- **Connect timeout**: 10 minutes

These limits are already configured in the API (`Program.cs`) for Kestrel and form options.

### Testing Large Uploads

After configuration, smoke-test through the command center:
1. Export a large archive from Strava (typically 50–200MB), or use a Tempo Settings export with media
2. Strava or Tempo restore: **Settings** → Data Management → **Migrate / restore** (or first-run onboarding)
3. Confirm upload progress then import/restore progress; optional: retry with Bruno/curl against the public origin `/api` (or loopback `:5001` if enabled)

## SSL/TLS Configuration

### Let's Encrypt (Certbot)

```bash
# Install Certbot
sudo apt-get install certbot python3-certbot-nginx

# Obtain certificate
sudo certbot --nginx -d yourdomain.com

# Auto-renewal
sudo certbot renew --dry-run
```

### Self-Signed Certificate (Development Only)

```bash
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout key.pem -out cert.pem
```

**Note**: Self-signed certificates are not recommended for production.

## Performance Optimization

### Resource Limits

Configure Docker resource limits:

```yaml
deploy:
  resources:
    limits:
      cpus: '2'
      memory: 4G
    reservations:
      cpus: '1'
      memory: 2G
```

### Database Optimization

- Configure PostgreSQL shared_buffers
- Set appropriate max_connections
- Enable query logging for optimization
- Regular VACUUM and ANALYZE

### Media Storage

- Use fast storage for media directory
- Consider object storage for large deployments
- Implement cleanup for old media files

## Monitoring and Logging

### Health Checks

Prefer readiness (`/ready`) so a green check means Postgres is reachable. Via the public origin:

```bash
curl -f https://your.domain/api/ready
```

Or inside the Compose network:

```bash
docker compose -f docker-compose.prod.yml exec api curl -f http://localhost:5001/ready
```

`GET /api/health` is API liveness only — it can return `200` while Postgres is down. Public `https://your.domain/health` (no `/api`) is the **command center** process pulse; do not use it as the API check.
### Logging

View container logs:

```bash
docker compose -f docker-compose.prod.yml logs -f api
docker compose -f docker-compose.prod.yml logs -f frontend
```

### Resource Monitoring

Monitor resource usage:

```bash
docker stats
```

## Next Steps

- [Review security best practices](security.md)
- [Set up backup and restore procedures](backup-restore.md)
- [Configure reverse proxy](production.md#reverse-proxy-setup)

