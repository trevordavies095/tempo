# Tempo Deployment Guide

This guide will help you deploy Tempo on your home lab using pre-built Docker images from GitHub Container Registry.

## Prerequisites

- Docker and Docker Compose installed on your server
- Basic familiarity with Docker commands
- A GitHub account (for pulling images from GitHub Container Registry)

## Quick Start

1. **Create a directory for Tempo:**
   ```bash
   mkdir tempo
   cd tempo
   ```

2. **Download the production compose file:**
   ```bash
   curl -o docker-compose.yml https://raw.githubusercontent.com/{owner}/{repo}/main/docker-compose.prod.yml
   ```
   
   Or create `docker-compose.yml` manually (see Configuration section below).

3. **Edit the compose file:**
   - Replace `{owner}` and `{repo}` with the actual GitHub username and repository name
   - Example: `ghcr.io/username/tempo/api:latest`

4. **Start the services:**
   ```bash
   docker-compose up -d
   ```

5. **Access Tempo:**
   - Frontend: http://localhost:3000
   - API: http://localhost:5001

That's it! Tempo should now be running.

## Configuration

### Image Tags

The production compose file uses pre-built images with the following tag options:

- **`latest`** - Stable releases (recommended for most users)
- **`edge`** - Bleeding edge builds from the develop branch (may be unstable)
- **`v1.0.0`** - Specific version tags (for pinning to a version)

To use a different tag, edit `docker-compose.yml` and change the image tags:
```yaml
api:
  image: ghcr.io/{owner}/{repo}/api:latest  # Change 'latest' to 'edge' or 'v1.0.0'
```

### Environment Variables

All configuration is done via environment variables in `docker-compose.yml`. Key settings:

**Database Connection:**
```yaml
ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=tempo;Username=postgres;Password=postgres"
```
Change the password for production use!

**CORS Origins:**
```yaml
CORS__AllowedOrigins: "http://localhost:3000"
```
If accessing from a different host, update this to match your frontend URL (e.g., `http://192.168.1.100:3000` or `https://tempo.example.com`).

**Media Storage:**
```yaml
MediaStorage__RootPath: "/app/media"
```
Media files are stored in the `./media` directory (mounted as a volume).

### Ports

Default ports:
- **Frontend:** 3000
- **API:** 5001
- **PostgreSQL:** 5432 (internal only, not exposed by default)

To change ports, modify the port mappings in `docker-compose.yml`:
```yaml
ports:
  - "8080:3000"  # Change 3000 to 8080 for frontend
```

### Volumes

Two volumes are used for data persistence:

1. **PostgreSQL data:** `postgres_data` (Docker named volume)
   - Persists database data across container restarts
   - Located in Docker's volume directory

2. **Media files:** `./media` (bind mount)
   - Stores uploaded photos and videos
   - Located in the same directory as `docker-compose.yml`
   - **Important:** Back up this directory along with your database!

## Updating Tempo

### Update to Latest Version

1. **Pull the latest images:**
   ```bash
   docker-compose pull
   ```

2. **Restart services:**
   ```bash
   docker-compose up -d
   ```

### Update to Specific Version

1. **Edit `docker-compose.yml`** and change image tags to the desired version:
   ```yaml
   api:
     image: ghcr.io/{owner}/{repo}/api:v1.0.0
   frontend:
     image: ghcr.io/{owner}/{repo}/frontend:v1.0.0
   ```

2. **Pull and restart:**
   ```bash
   docker-compose pull
   docker-compose up -d
   ```

### Switch Between Tags

To switch from `latest` to `edge` (or vice versa):

1. Edit `docker-compose.yml` and change the tag
2. Run `docker-compose pull`
3. Run `docker-compose up -d`

## Troubleshooting

### Services Won't Start

1. **Check logs:**
   ```bash
   docker-compose logs
   ```

2. **Check if ports are in use:**
   ```bash
   # Check if port 3000 is in use
   lsof -i :3000
   # Check if port 5001 is in use
   lsof -i :5001
   ```

3. **Verify Docker is running:**
   ```bash
   docker ps
   ```

### Database Connection Issues

1. **Check PostgreSQL is healthy:**
   ```bash
   docker-compose ps
   ```
   The postgres service should show as "healthy"

2. **Check database logs:**
   ```bash
   docker-compose logs postgres
   ```

### Frontend Can't Connect to API

1. **Check CORS settings:**
   - Ensure `CORS__AllowedOrigins` matches your frontend URL
   - If accessing from a different host, include the full URL

2. **Check API is running:**
   ```bash
   curl http://localhost:5001/health
   ```
   Should return `{"status":"healthy"}`

3. **Check API logs:**
   ```bash
   docker-compose logs api
   ```

### Media Files Not Appearing

1. **Check media directory exists:**
   ```bash
   ls -la ./media
   ```

2. **Check permissions:**
   The media directory should be writable by the Docker container

3. **Check API logs for errors:**
   ```bash
   docker-compose logs api | grep -i media
   ```

## Backup and Restore

### Backup Database

```bash
# Create backup
docker-compose exec postgres pg_dump -U postgres tempo > tempo_backup.sql

# Or use docker exec directly
docker exec tempo-postgres pg_dump -U postgres tempo > tempo_backup.sql
```

### Restore Database

```bash
# Restore from backup
cat tempo_backup.sql | docker-compose exec -T postgres psql -U postgres tempo
```

### Backup Media Files

Simply copy the `./media` directory:
```bash
tar -czf tempo_media_backup.tar.gz ./media
```

### Full Backup

1. Backup database (see above)
2. Backup media directory (see above)
3. Save your `docker-compose.yml` configuration

## Advanced Configuration

### Using a Reverse Proxy

If you're using a reverse proxy (nginx, Traefik, etc.):

1. Update `CORS__AllowedOrigins` to match your frontend URL
2. Configure the reverse proxy to forward:
   - `/` → Frontend (port 3000)
   - `/api` or `/workouts` → API (port 5001)

### Custom Domain

1. Set up DNS to point to your server
2. Update `CORS__AllowedOrigins` to use your domain
3. Configure SSL/TLS (Let's Encrypt recommended)

### Environment-Specific Settings

Create a `.env` file for environment variables:
```bash
POSTGRES_PASSWORD=your_secure_password
CORS_ORIGINS=http://your-domain.com:3000
```

Then reference in `docker-compose.yml`:
```yaml
environment:
  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD}
  CORS__AllowedOrigins: ${CORS_ORIGINS}
```

## Building from Source

If you prefer to build images locally instead of using pre-built ones:

1. Use the development `docker-compose.yml` (the one with `build:` instead of `image:`)
2. Clone the repository
3. Run `docker-compose up -d`

This will build the images locally, which takes longer but gives you full control.

## Getting Help

- Check the logs: `docker-compose logs`
- Review GitHub issues
- Check the main README.md for more information

## Security Notes

- **Change the default PostgreSQL password** for production use
- **Use HTTPS** if exposing Tempo to the internet
- **Keep images updated** by regularly pulling the latest versions
- **Back up your data** regularly (database + media files)

