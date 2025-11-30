# Common Issues

Detailed solutions for common Tempo problems.

## Installation Issues

### Docker Containers Not Starting

**Symptoms:**
- Containers exit immediately
- `docker-compose ps` shows exited containers

**Solutions:**
1. Check logs: `docker-compose logs`
2. Verify Docker is running: `docker ps`
3. Check disk space: `df -h`
4. Verify ports are not in use: `netstat -tuln | grep -E '3000|5001|5432'`
5. Check Docker Compose version: `docker-compose --version`

### Port Conflicts

**Symptoms:**
- Error: "port is already allocated"
- Services can't bind to ports

**Solutions:**
1. Find process using port:
   ```bash
   # Linux/macOS
   lsof -i :3000
   # Or
   netstat -tuln | grep 3000
   ```
2. Change ports in `docker-compose.yml`:
   ```yaml
   ports:
     - "3001:3000"  # Change external port
   ```
3. Stop conflicting services

### Database Connection Errors

**Symptoms:**
- API fails to start
- "Connection refused" errors
- Database migration failures

**Solutions:**
1. Verify PostgreSQL is running:
   ```bash
   docker-compose ps postgres
   # Or
   pg_isready -h localhost
   ```
2. Check connection string in `appsettings.json`
3. Verify database exists: `docker exec tempo-postgres psql -U postgres -l`
4. Check network connectivity between services
5. Verify credentials match

## Import Issues

### File Format Not Recognized

**Symptoms:**
- Error: "Unsupported file format"
- Import fails immediately

**Solutions:**
1. Verify file extension is correct (`.gpx`, `.fit`, `.fit.gz`, `.csv`)
2. Check file isn't corrupted
3. Verify file contains valid workout data
4. Try opening file in another application to verify

### Import Fails

**Symptoms:**
- Import starts but fails
- Error messages in logs
- No workout created

**Solutions:**
1. Check API logs: `docker-compose logs api`
2. Verify file size is within limits (50MB single, 500MB bulk)
3. Check disk space: `df -h`
4. Verify file contains GPS data (required for routes)
5. Check database is accessible

### Missing Data in Imported Workouts

**Symptoms:**
- Some metrics missing
- No route displayed
- Missing heart rate data

**Solutions:**
1. Check source file contains the data
2. Some devices don't record all metrics
3. Weather data requires location and time
4. Elevation depends on GPS accuracy
5. Heart rate requires HR monitor

### Bulk Import Problems

**Symptoms:**
- ZIP file rejected
- Some workouts not imported
- Import takes too long

**Solutions:**
1. Verify ZIP structure matches requirements:
   - `activities.csv` in root
   - Workout files in `activities/` folder
2. Check file size (max 500MB)
3. Verify CSV references correct file paths
4. Only "Run" activities are imported
5. Duplicates are automatically skipped

## Performance Issues

### Slow Imports

**Symptoms:**
- Import takes very long
- Timeout errors

**Solutions:**
1. Large files take longer to process
2. Check server resources: `docker stats`
3. Verify sufficient disk space
4. Check database performance
5. Consider splitting very large imports

### Slow Page Loads

**Symptoms:**
- Dashboard loads slowly
- Activities list is slow
- Maps take time to render

**Solutions:**
1. Check database indexes are created
2. Verify sufficient server resources
3. Check network latency
4. Clear browser cache
5. Check for large media files

### High Resource Usage

**Symptoms:**
- High CPU usage
- High memory usage
- Server becomes unresponsive

**Solutions:**
1. Monitor resources: `docker stats`
2. Check for memory leaks
3. Limit container resources in Docker
4. Optimize database queries
5. Consider scaling resources

## Configuration Issues

### Settings Not Saving

**Symptoms:**
- Changes don't persist
- Settings revert after refresh

**Solutions:**
1. Verify you're logged in
2. Check browser console for errors
3. Verify API is accessible
4. Check API logs for errors
5. Clear browser cache and cookies

### Authentication Problems

**Symptoms:**
- Can't log in
- Session expires immediately
- Registration fails

**Solutions:**
1. Verify JWT secret key is configured (production)
2. Check cookie settings (requires HTTPS in production)
3. Clear browser cookies
4. Verify database has user table
5. Check API logs for authentication errors

### CORS Errors

**Symptoms:**
- Browser console shows CORS errors
- API requests fail
- "Origin not allowed" errors

**Solutions:**
1. Verify `CORS__AllowedOrigins` includes your frontend URL
2. In Docker, use double underscores: `CORS__AllowedOrigins`
3. Restart API container after changing CORS
4. Check for typos in origin URLs
5. Verify protocol matches (http vs https)

## Database Issues

### Migration Errors

**Symptoms:**
- API fails to start
- Migration errors in logs
- Database schema mismatches

**Solutions:**
1. Migrations run automatically on startup
2. For manual migration: `cd api && dotnet ef database update`
3. Migrations are idempotent (safe to run multiple times)
4. Check database connection
5. Verify PostgreSQL version (16 required)

### Database Performance

**Symptoms:**
- Slow queries
- High database CPU
- Timeout errors

**Solutions:**
1. Check database indexes are created
2. Run `VACUUM ANALYZE` on database
3. Check for missing indexes
4. Monitor query performance
5. Consider database optimization

## Media Issues

### Media Upload Fails

**Symptoms:**
- Upload fails immediately
- Error messages
- Files not saved

**Solutions:**
1. Check file size (max 50MB)
2. Verify file format is supported
3. Check disk space: `df -h`
4. Verify media directory is writable
5. Check API logs for errors

### Media Not Displaying

**Symptoms:**
- Media files don't appear
- Broken image links
- Videos won't play

**Solutions:**
1. Refresh the page
2. Check file exists in `media/` directory
3. Verify file permissions
4. Check browser console for errors
5. Verify MIME type is correct

## Next Steps

- [Check the FAQ](faq.md) for more answers
- [Get help from the community](https://discord.gg/9Svd99npyj)
- [Report an issue](https://github.com/trevordavies095/tempo/issues)

