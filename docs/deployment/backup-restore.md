# Backup and Restore

Procedures for backing up and restoring Tempo data.

## Overview

Tempo stores data in two locations:
1. **Database** - PostgreSQL database with workout data, settings, and metadata
2. **Media Files** - Filesystem storage for photos and videos

Both must be backed up for a complete backup.

## Backup Procedures

### Database Backup

#### Using Docker

```bash
# Backup database
docker exec tempo-postgres pg_dump -U postgres tempo > backup-$(date +%Y%m%d-%H%M%S).sql

# Or with compression
docker exec tempo-postgres pg_dump -U postgres tempo | gzip > backup-$(date +%Y%m%d-%H%M%S).sql.gz
```

#### Using PostgreSQL Client

```bash
# Backup database
pg_dump -h localhost -U postgres tempo > backup-$(date +%Y%m%d-%H%M%S).sql

# Or with compression
pg_dump -h localhost -U postgres tempo | gzip > backup-$(date +%Y%m%d-%H%M%S).sql.gz
```

### Media Files Backup

#### Using tar

```bash
# Backup media directory
tar -czf media-backup-$(date +%Y%m%d-%H%M%S).tar.gz ./media
```

#### Using rsync

```bash
# Backup media directory
rsync -av ./media/ /backup/location/media/
```

### Complete Backup Script

Create a backup script that backs up both:

```bash
#!/bin/bash
BACKUP_DIR="/backup/tempo"
DATE=$(date +%Y%m%d-%H%M%S)

# Create backup directory
mkdir -p "$BACKUP_DIR"

# Backup database
docker exec tempo-postgres pg_dump -U postgres tempo | gzip > "$BACKUP_DIR/db-$DATE.sql.gz"

# Backup media
tar -czf "$BACKUP_DIR/media-$DATE.tar.gz" ./media

# Keep only last 30 days of backups
find "$BACKUP_DIR" -name "*.gz" -mtime +30 -delete

echo "Backup completed: $DATE"
```

## Automated Backups

### Cron Job

Add to crontab for daily backups:

```bash
# Edit crontab
crontab -e

# Add daily backup at 2 AM
0 2 * * * /path/to/backup-script.sh
```

### Systemd Timer

Create a systemd service and timer for automated backups.

## Restore Procedures

### Database Restore

#### Using Docker

```bash
# Restore database
docker exec -i tempo-postgres psql -U postgres tempo < backup.sql

# Or from compressed backup
gunzip -c backup.sql.gz | docker exec -i tempo-postgres psql -U postgres tempo
```

#### Using PostgreSQL Client

```bash
# Restore database
psql -h localhost -U postgres tempo < backup.sql

# Or from compressed backup
gunzip -c backup.sql.gz | psql -h localhost -U postgres tempo
```

**Important**: Restore to an empty database or drop existing database first.

### Media Files Restore

#### Using tar

```bash
# Extract media backup
tar -xzf media-backup.tar.gz -C ./
```

#### Using rsync

```bash
# Restore media directory
rsync -av /backup/location/media/ ./media/
```

### Complete Restore

1. Stop Tempo services
2. Restore database
3. Restore media files
4. Verify file permissions
5. Start Tempo services
6. Verify data integrity

## Backup Verification

### Verify Database Backup

```bash
# Check backup file
pg_restore --list backup.sql | head -20

# Test restore to temporary database
createdb tempo_test
pg_restore -d tempo_test backup.sql
```

### Verify Media Backup

```bash
# List contents
tar -tzf media-backup.tar.gz | head -20

# Check file count
tar -tzf media-backup.tar.gz | wc -l
```

## Backup Storage

### Local Storage

- Store backups on separate disk
- Use different physical location if possible
- Encrypt sensitive backups

### Remote Storage

- Cloud storage (S3, Google Cloud Storage, etc.)
- Remote server via rsync/SSH
- Network-attached storage (NAS)

### Encryption

Encrypt backups before storing:

```bash
# Encrypt backup
gpg --symmetric --cipher-algo AES256 backup.sql.gz

# Decrypt backup
gpg --decrypt backup.sql.gz.gpg > backup.sql.gz
```

## Retention Policy

### Recommended Retention

- **Daily backups**: Keep 7 days
- **Weekly backups**: Keep 4 weeks
- **Monthly backups**: Keep 12 months
- **Yearly backups**: Keep indefinitely

### Cleanup Script

```bash
#!/bin/bash
BACKUP_DIR="/backup/tempo"

# Remove backups older than 30 days
find "$BACKUP_DIR" -name "*.sql.gz" -mtime +30 -delete
find "$BACKUP_DIR" -name "*.tar.gz" -mtime +30 -delete
```

## Disaster Recovery

### Recovery Plan

1. **Assess damage**: Determine what data is lost
2. **Stop services**: Prevent further data loss
3. **Restore from backup**: Use most recent backup
4. **Verify integrity**: Check data completeness
5. **Resume services**: Start Tempo services
6. **Monitor**: Watch for issues

### Testing Restores

Regularly test restore procedures:

- Test database restore to temporary database
- Verify media files restore correctly
- Test complete restore procedure
- Document any issues

## Best Practices

### Backup Frequency

- **Active use**: Daily backups recommended
- **Light use**: Weekly backups may suffice
- **Critical data**: Consider multiple daily backups

### Backup Location

- Store backups off-site
- Use multiple backup locations
- Test backup accessibility regularly

### Documentation

- Document backup procedures
- Keep restore procedures accessible
- Update procedures as needed
- Train team members

## Troubleshooting

### Backup Fails

- Check disk space
- Verify database is accessible
- Check file permissions
- Review error messages

### Restore Fails

- Verify backup file integrity
- Check database connection
- Ensure sufficient disk space
- Review error messages

### Data Mismatch

- Verify backup date matches restore date
- Check for missing media files
- Verify database schema matches
- Review migration history

## Next Steps

- [Set up automated backups](backup-restore.md#automated-backups)
- [Test restore procedures](backup-restore.md#testing-restores)
- [Configure backup storage](backup-restore.md#backup-storage)

