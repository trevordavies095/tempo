# Docker Deployment

Deploy Tempo using Docker Compose for easy production deployment.

## Overview

Docker Compose provides the simplest way to deploy Tempo in production. The `docker-compose.prod.yml` file is configured for production use with pre-built images.

## Prerequisites

- Docker and Docker Compose installed
- Sufficient disk space for database and media
- Network access (if using pre-built images from GitHub Container Registry)

## Production Docker Compose

The production configuration (`docker-compose.prod.yml`) includes:

- Pre-built images from GitHub Container Registry
- Version-tagged images for stability
- Dedicated Docker network for service isolation
- Health checks for all services
- Automatic restarts

## Deployment Steps

### 1. Download Configuration

Ensure you have the `docker-compose.prod.yml` file in your deployment directory.

### 2. Configure Environment Variables

Edit `docker-compose.prod.yml` or use environment variables:

**Required:**
- `JWT__SecretKey` - Generate with: `openssl rand -base64 32`

**Recommended:**
- `ConnectionStrings__DefaultConnection` - Database connection string
- `CORS__AllowedOrigins` - Comma-separated list of allowed origins
- Database password (change from default)

### 3. Start Services

```bash
docker-compose -f docker-compose.prod.yml up -d
```

### 4. Verify Deployment

Check that all services are running:

```bash
docker-compose -f docker-compose.prod.yml ps
```

Check logs:

```bash
docker-compose -f docker-compose.prod.yml logs -f
```

### 5. Access Application

- Frontend: `http://your-server:3004` (or configured port)
- API: `http://your-server:5001`
- Health check: `http://your-server:5001/health`

## Image Versions

Production images are available from GitHub Container Registry:

- **API**: `ghcr.io/trevordavies095/tempo/api:v1.2.0`
- **Frontend**: `ghcr.io/trevordavies095/tempo/frontend:v1.2.0`

Update version tags in `docker-compose.prod.yml` when deploying new releases.

## Network Configuration

The production configuration uses a dedicated Docker network (`tempo-network`) for service isolation. Services communicate internally using service names:
- `postgres` - Database service
- `api` - API service
- `frontend` - Frontend service

## Data Persistence

### Database

Database data is stored in the `postgres_data` Docker volume. This persists across container restarts.

### Media Files

Media files are stored in the `./media` directory, mounted as a volume. Ensure this directory:
- Exists and is writable
- Has sufficient disk space
- Is included in backups

## Updating

To update to a new version:

1. Update image tags in `docker-compose.prod.yml`
2. Pull new images: `docker-compose -f docker-compose.prod.yml pull`
3. Restart services: `docker-compose -f docker-compose.prod.yml up -d`

Database migrations run automatically on API startup.

## Stopping Services

```bash
docker-compose -f docker-compose.prod.yml down
```

To remove volumes (clears database):

```bash
docker-compose -f docker-compose.prod.yml down -v
```

## Troubleshooting

### Services Not Starting

- Check logs: `docker-compose -f docker-compose.prod.yml logs`
- Verify environment variables are set correctly
- Ensure ports are not in use
- Check disk space

### Database Connection Issues

- Verify PostgreSQL container is healthy
- Check connection string configuration
- Ensure network connectivity between services

### Image Pull Failures

- Verify network connectivity
- Check image tags are correct
- Ensure authentication if using private registry

## Next Steps

- [Configure for production](production.md)
- [Review security settings](security.md)
- [Set up backups](backup-restore.md)

