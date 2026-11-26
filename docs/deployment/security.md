# Security

What Tempo enforces, what production Compose already does, and what is left to you. Setting names and defaults: [Configuration](../getting-started/configuration.md).

## Secrets

### JWT secret (`JWT_SECRET_KEY`)

- Required by production Compose. The API refuses to start without `JWT:SecretKey` or with the shipped placeholder.
- Generate with `openssl rand -base64 32` (at least 32 bytes). Keep it in the gitignored `.env` or a secrets manager.
- It signs sessions **and** derives the keys that encrypt the stored intervals.icu API key and Tempo API key plaintext. Rotate it only on purpose (suspected leak): rotation signs everyone out and requires reconnecting Intervals.icu ([details](../getting-started/configuration.md#rotating-the-jwt-secret)). Back it up with your data.

### Database password (`POSTGRES_PASSWORD`)

- Required by production Compose; no default in the file. Use a long random value without `;`.
- Applied only when the volume is first created; existing volumes keep their password ([Docker Deployment](docker.md#2-fill-in-env)).
- Postgres is not published on the host. The development `docker-compose.yml` publishes it on `5432` with password `postgres`; never use that file on a network.

## HTTPS and the session cookie

The session is an `authToken` cookie: `HttpOnly`, `SameSite=Lax`, and in Production always `Secure`, so it only works over HTTPS (or `http://localhost`). Terminate TLS at a reverse proxy pointed at `127.0.0.1:3004` ([Reverse Proxy](reverse-proxy.md)).

`TEMPO_COOKIE_SECURE=false` exists only for plain HTTP on a trusted LAN. Never on an internet-facing install.

Forwarded headers (`X-Forwarded-For`, `X-Forwarded-Proto`) are honored one hop deep and only from `Tempo:Proxy:TrustedNetworks` (loopback and private ranges by default). Narrow it if untrusted hosts can reach the API port.

## Accounts

### Password requirements

New passwords (registration and password change):

- **Length**: 16–64 characters
- **Encoding**: at most 72 bytes UTF-8 (bcrypt limit)
- **Complexity**: no mandatory mixed case, digits, or symbols; passphrases encouraged
- **Rejected**: common passwords; passwords containing the username (when it is at least 3 characters); any character repeated five or more times in a row

Existing passwords stay valid until changed. Passwords are stored as BCrypt hashes.

### Single user

- Registration works only while no user exists; after the first account it is locked.
- Sessions last 7 days, or 30 with "Remember me" (`JWT:ExpirationDays`, `JWT:RememberMeExpirationDays`).
- Tempo API keys (Settings) authenticate as `Authorization: Bearer …` and cannot reach session-only routes.
- A forgotten passphrase is reset on the host only with `reset-password` ([Password recovery](docker.md#password-recovery)). There is no HTTP or email reset, and it cannot add a second account.

## Network

- Expose only the reverse proxy (80/443). Production Compose publishes only `127.0.0.1:3004`.
- If you uncomment the loopback API publish (`127.0.0.1:5001:5001`), keep it on loopback.
- CORS is not needed on the default path (same-origin `/api`, native daily driver), and production Compose does not set `CORS__AllowedOrigins`. If you set it, list exact origins only.

## Uploads

- Media: 50 MB per file by default (`MediaStorage:MaxFileSizeBytes`); extensions `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.mp4`, `.mov`, `.avi`.
- ZIP archives (import jobs): 500 MB.
- Files are stored under the media directory, outside any web root, and served only through authenticated API routes.

## Outbound connections

Tempo keeps your data on your host but makes these outbound requests:

- **Open-Meteo** (`archive-api.open-meteo.com`), from the API: a Workout's start location and time, to fetch weather on import.
- **CARTO** (`basemaps.cartocdn.com`), from the browser: map tiles, with your CARTO key if set.
- **intervals.icu**, from the API: only when you connect Intervals.icu in Settings.

## Security headers

Add at the proxy, for example nginx:

```nginx
add_header X-Frame-Options "SAMEORIGIN" always;
add_header X-Content-Type-Options "nosniff" always;
add_header Referrer-Policy "strict-origin-when-cross-origin" always;
```

A strict `Content-Security-Policy: default-src 'self'` breaks the command center: Next.js uses inline scripts and maps load tiles from `basemaps.cartocdn.com`. Test any CSP before enforcing it (start with `Content-Security-Policy-Report-Only`).

## Containers

- Pin release tags (`vX.Y.Z`), not `latest`; update with [Updating](docker.md#updating).
- The API and frontend images run as root and write `./media` as root.
- Back up and protect `.env`, database dumps, and export ZIPs: they contain everything ([Backup and Restore](backup-restore.md)). Encrypt them off-host.

## Reporting security issues

Open a private security advisory on [GitHub](https://github.com/trevordavies095/tempo/security/advisories/new). Do not disclose publicly until fixed.

## Next Steps

- [Production Setup](production.md)
- [Backup and Restore](backup-restore.md)
