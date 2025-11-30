# Frequently Asked Questions

Common questions about Tempo.

## General

### What is Tempo?

Tempo is a self-hostable running tracker that serves as a privacy-first alternative to Strava. It allows you to track your workouts while keeping all data on your own infrastructure.

### Is Tempo free?

Yes, Tempo is open source and free to use. It's licensed under the MIT License.

### Do I need to pay for hosting?

No, you can host Tempo on your own infrastructure. You only need to pay for your own server/hosting if you choose to use cloud services.

### Can I use Tempo without technical knowledge?

Yes! The Docker Compose quick start makes it easy to get started. However, some technical knowledge helps for production deployment and troubleshooting.

## Installation

### What are the system requirements?

- **Minimum**: 1 CPU core, 2GB RAM, 10GB storage
- **Recommended**: 2+ CPU cores, 4GB+ RAM, 50GB+ storage

### Do I need Docker?

Docker Compose is the easiest way to run Tempo, but you can also run services individually if you prefer.

### Can I run Tempo on Windows/Mac/Linux?

Yes, Tempo runs on all platforms that support Docker. The backend is .NET (cross-platform), and the frontend is Next.js (Node.js).

### How long does installation take?

With Docker Compose, installation takes just a few minutes. Most of the time is spent downloading images.

## Features

### What file formats does Tempo support?

Tempo supports:
- GPX (`.gpx`)
- FIT (`.fit`, `.fit.gz`)
- Strava CSV (`.csv`)

### Can I import from Strava?

Yes! You can:
- Import individual GPX files from Strava
- Bulk import your entire Strava history using a Strava data export

### Does Tempo sync with my devices?

Tempo doesn't automatically sync with devices. You need to export files from your device and import them into Tempo.

### Can I export my data from Tempo?

Export functionality is planned but not yet implemented. Your data is stored in PostgreSQL and can be accessed directly.

### Does Tempo work offline?

Yes! Once installed, Tempo runs entirely on your infrastructure and doesn't require internet connectivity (except for weather data, which is optional).

## Data and Privacy

### Where is my data stored?

All data is stored locally on your infrastructure:
- Database: PostgreSQL (in Docker volume or your PostgreSQL instance)
- Media files: Filesystem in `media/` directory

### Does Tempo send data to external services?

Tempo only contacts external services for:
- Weather data (Open-Meteo API) - based on workout location and time
- No other data is sent externally

### Can I backup my data?

Yes! See the [Backup and Restore](../deployment/backup-restore.md) guide for instructions.

### How do I migrate to a new server?

1. Backup database and media files
2. Install Tempo on new server
3. Restore database and media files
4. Update configuration if needed

## Technical

### What database does Tempo use?

PostgreSQL 16. The database runs in Docker or can be a separate PostgreSQL instance.

### Can I use a different database?

No, Tempo is designed for PostgreSQL and uses PostgreSQL-specific features (JSONB, etc.).

### How do I update Tempo?

1. Pull new Docker images
2. Update `docker-compose.prod.yml` with new image tags
3. Restart services
4. Database migrations run automatically

### Can I customize Tempo?

Yes! Tempo is open source. You can modify the code to suit your needs.

### How do I contribute?

See the [Contributing Guide](../developers/contributing.md) for details.

## Troubleshooting

### Why can't I register?

Registration is only available when no users exist. After the first user is created, registration is locked for security.

### Why is my import slow?

Large files take longer to process. Very large imports (hundreds of workouts) may take several minutes.

### Why don't I see my route on the map?

Routes require GPS data in the workout file. Some files may not contain GPS coordinates.

### Why is weather data missing?

Weather data requires:
- Valid location (latitude/longitude) in workout
- Valid timestamp
- Internet connectivity (for API call)

### How do I reset my password?

Password reset functionality is not yet implemented. You'll need to reset it directly in the database or reinstall.

## Support

### Where can I get help?

- [Discord Community](https://discord.gg/9Svd99npyj)
- [GitHub Issues](https://github.com/trevordavies095/tempo/issues)
- [Documentation](../index.md)

### How do I report a bug?

Create an issue on [GitHub](https://github.com/trevordavies095/tempo/issues) with:
- Description of the bug
- Steps to reproduce
- Expected vs actual behavior
- Environment details

### Can I request a feature?

Yes! Create a feature request on [GitHub Issues](https://github.com/trevordavies095/tempo/issues).

## Next Steps

- [Check common issues](common-issues.md)
- [Get help from the community](https://discord.gg/9Svd99npyj)
- [Review the documentation](../index.md)

