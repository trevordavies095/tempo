# Reverse Proxy and Login Cookies

If you copy this page, login works behind TLS. Start here when login "succeeds" but the session doesn't stick.

## How a request reaches the API

```
Browser ──HTTPS──▶ Caddy / nginx / Traefik ──HTTP──▶ frontend :3004 ──/api rewrite──▶ api :5001
                   (sets X-Forwarded-Proto)          (Next passes it through)         (reads it)
```

- **One upstream.** Point the proxy at the command center on `127.0.0.1:3004`. The frontend rewrites `/api` to the API over the Compose network. Do not publish or proxy `:5001` for the web app.
- **Session cookie.** The API sets `authToken` with `HttpOnly`, `SameSite=Lax`, `Path=/`, and, in Production, `Secure`. Browsers only store and send a `Secure` cookie over HTTPS (or `http://localhost`).
- **Forwarded headers.** The API reads `X-Forwarded-Proto` and `X-Forwarded-For` only from trusted peers (loopback and private ranges by default, which covers the Compose network), one hop deep. Anyone else's forwarded headers are ignored.

## Recommended: HTTPS at the edge

Production Compose needs no extra settings for this. Cookies are always `Secure`, and your proxy terminates TLS.

### Caddy

```caddy
tempo.example.com {
    # Caddy sets X-Forwarded-For/-Proto/-Host and drops client-supplied ones by default
    reverse_proxy 127.0.0.1:3004 {
        transport http {
            read_timeout 30m
            write_timeout 30m
        }
    }
}
```

### nginx

```nginx
server {
    listen 80;
    server_name tempo.example.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    http2 on;
    server_name tempo.example.com;

    ssl_certificate     /path/to/fullchain.pem;
    ssl_certificate_key /path/to/privkey.pem;

    location / {
        proxy_pass http://127.0.0.1:3004;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Whole-ZIP adapters only; the command center uploads 512 KiB chunks
        client_max_body_size 500M;
        proxy_read_timeout 600s;
        proxy_send_timeout 600s;
    }
}
```

### Traefik

Traefik sets `X-Forwarded-Proto` by default. Route `Host(`tempo.example.com`)` on your TLS entrypoint to the frontend container's port `3000`, and raise the body limit if you use whole-ZIP adapters:

```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.tempo.rule=Host(`tempo.example.com`)"
  - "traefik.http.routers.tempo.entrypoints=websecure"
  - "traefik.http.routers.tempo.tls.certresolver=letsencrypt"
  - "traefik.http.services.tempo.loadbalancer.server.port=3000"
  - "traefik.http.middlewares.tempo-body.buffering.maxRequestBodyBytes=524288000"
  - "traefik.http.routers.tempo.middlewares=tempo-body"
```

Use the same public `https://` origin for the command center and the daily driver.

## Exception: plain HTTP on a trusted LAN

Without HTTPS, browsers drop the `Secure` cookie: login returns 200, and the next request is signed out. The API logs a warning when this happens:

```
Auth cookie is Secure but this request arrived over plain HTTP; the browser will drop it ...
```

Prefer HTTPS even on a LAN (for example, Caddy with an internal CA or a DNS-challenge certificate). If you accept plain HTTP on a network you trust, opt out explicitly in `.env`:

```bash
TEMPO_COOKIE_SECURE=false
```

Then restart the API. Startup logs a warning while this is set. Never use it on an internet-facing install: the session cookie would travel in clear text.

`http://localhost` works without this. Browsers accept `Secure` cookies on localhost.

## Settings

| Setting | Env var | Default | Notes |
|---|---|---|---|
| `Cookie:Secure` | `Cookie__Secure` or `TEMPO_COOKIE_SECURE` (Compose `.env`) | unset | Unset: always `Secure` in Production; elsewhere `Secure` follows the request scheme. `true` / `false` forces it. Any other value fails startup. |
| `Cookie:Domain` | `Cookie__Domain` | unset (host-only) | Leave unset. Set it only to share the session across subdomains (e.g. `example.com` for both `tempo.example.com` and `www.example.com`). Never set an IP address. |
| `Tempo:Proxy:TrustedNetworks` | `Tempo__Proxy__TrustedNetworks` | `127.0.0.0/8,::1/128,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,fc00::/7` | Comma-separated CIDR list of peers whose forwarded headers are honored. Narrow it (e.g. to your Compose subnet) if untrusted hosts can reach the API port directly. Invalid entries fail startup. |
| `CORS:AllowedOrigins` | `CORS__AllowedOrigins` | `http://localhost:3000` | Not needed for the default path; the command center is same-origin `/api` and the daily driver is native. Set it only when a browser on another origin calls the API directly, e.g. `https://tempo.example.com`. |

## Troubleshooting login

1. **Session expires right after login.** Check the API logs for the `Auth cookie is Secure but this request arrived over plain HTTP` warning. You're on HTTP: switch to HTTPS, or set `TEMPO_COOKIE_SECURE=false` on a trusted LAN.
2. **HTTPS, but still signed out.** Check that the browser is on the same host the cookie was set for. If `Cookie__Domain` is set, unset it, or make sure it matches that host. Then clear cookies for the site.
3. **CORS errors.** These only happen when a browser calls the API on another origin. Add that exact origin (scheme, host, and port) to `CORS__AllowedOrigins`.
4. **Login returns 401.** That's a wrong or forgotten passphrase, not a cookie problem. See [How do I reset my password?](../troubleshooting/faq.md#how-do-i-reset-my-password).
