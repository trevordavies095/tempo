# Deployment

This section covers deploying Tempo to production environments.

## Overview

Tempo can be deployed in various ways depending on your needs and infrastructure. This guide covers the most common deployment scenarios.

## Deployment Methods

- **[Docker Deployment](docker.md)** - Deploy using Docker Compose (recommended)
- **[Production Setup](production.md)** - Production configuration and best practices
- **[Security](security.md)** - Security considerations and hardening
- **[Backup and Restore](backup-restore.md)** - Data backup and recovery procedures

## Quick Start

For a quick production deployment:

1. Copy `docker-compose.prod.yml` and `.env.example` (as `.env`)
2. Set `JWT_SECRET_KEY` and `POSTGRES_PASSWORD` in `.env`
3. Start services with `docker compose -f docker-compose.prod.yml up -d`

See the [Docker Deployment Guide](docker.md) for detailed instructions.

## Key Considerations

### Security

- **JWT Secret Key**: Must be set via `JWT_SECRET_KEY` in `.env` (production Compose fails without it)
- **HTTPS**: Required for secure cookie transmission
- **Database Password**: Must be set via `POSTGRES_PASSWORD` in `.env` (same value for Postgres and the API)
- **Public origin**: One reverse-proxy upstream to `127.0.0.1:3004` for command center and daily driver (CORS not required on the default path)

### Performance

- **Resource Requirements**: Minimum 2GB RAM, 2 CPU cores recommended
- **Storage**: Plan for growth (database + media files)
- **Backup Strategy**: Regular backups of database and media

### Monitoring

- Health check endpoint: `/health` (via public origin `/api/health` or `docker compose exec`)
- Version endpoint: `/version`
- Container logs: `docker compose -f docker-compose.prod.yml logs -f`

## Next Steps

- [Set up Docker deployment](docker.md)
- [Configure for production](production.md)
- [Review security best practices](security.md)

