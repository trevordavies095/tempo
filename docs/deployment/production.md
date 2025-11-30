# Production Setup

Configure Tempo for production deployment with best practices.

## Overview

This guide covers production configuration, environment variables, and deployment considerations.

## Environment Variables

Configure the following in `docker-compose.prod.yml` or via environment files:

### Required Configuration

#### JWT Secret Key

**CRITICAL**: Must be set to a secure random value.

```bash
# Generate a secure key
openssl rand -base64 32
```

Set in `docker-compose.prod.yml`:
```yaml
JWT__SecretKey: "your-generated-secret-key-here"
```

Or use an environment variable:
```bash
export JWT_SECRET_KEY="your-generated-secret-key-here"
```

### Database Configuration

```yaml
ConnectionStrings__DefaultConnection: "Host=postgres;Port=5432;Database=tempo;Username=postgres;Password=YOUR_SECURE_PASSWORD"
```

**Important**: Change the default database password in production.

### CORS Configuration

Configure allowed origins:

```yaml
CORS__AllowedOrigins: "https://yourdomain.com,https://www.yourdomain.com"
```

For multiple origins, use comma-separated list.

### Media Storage

```yaml
MediaStorage__RootPath: "/app/media"
MediaStorage__MaxFileSizeBytes: "52428800"  # 50MB default
```

### Elevation Calculation

```yaml
ElevationCalculation__NoiseThresholdMeters: "2.0"
ElevationCalculation__MinDistanceMeters: "10.0"
```

### JWT Configuration

```yaml
JWT__SecretKey: "YOUR_SECRET_KEY"  # REQUIRED
JWT__Issuer: "Tempo"
JWT__Audience: "Tempo"
JWT__ExpirationDays: "7"
```

## Production Checklist

### Security

- [ ] JWT secret key is set and secure (minimum 32 characters)
- [ ] Database password changed from default
- [ ] HTTPS configured (required for secure cookies)
- [ ] CORS origins configured correctly
- [ ] Firewall rules configured
- [ ] Regular security updates applied

### Configuration

- [ ] Environment variables set correctly
- [ ] Database connection string configured
- [ ] Media storage path configured and writable
- [ ] Ports configured appropriately
- [ ] Health checks enabled

### Data Management

- [ ] Backup strategy in place
- [ ] Media directory included in backups
- [ ] Database backup automated
- [ ] Sufficient disk space allocated

### Monitoring

- [ ] Health check endpoint accessible
- [ ] Logs configured and monitored
- [ ] Resource usage monitored
- [ ] Error tracking in place

## Reverse Proxy Setup

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
    
    # Frontend
    location / {
        proxy_pass http://localhost:3004;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
    
    # API
    location /api/ {
        proxy_pass http://localhost:5001/;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

### Traefik Example

```yaml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.tempo.rule=Host(`yourdomain.com`)"
  - "traefik.http.routers.tempo.entrypoints=websecure"
  - "traefik.http.routers.tempo.tls.certresolver=letsencrypt"
```

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

Monitor the health endpoint:

```bash
curl http://localhost:5001/health
```

### Logging

View container logs:

```bash
docker-compose -f docker-compose.prod.yml logs -f api
docker-compose -f docker-compose.prod.yml logs -f frontend
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

