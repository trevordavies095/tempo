# Tempo - Self-Hostable Running Tracker

> A privacy-first, self-hosted Strava alternative. Import GPX, FIT, and CSV files from Garmin, Apple Watch, Strava, and more. Keep all your data local—no subscriptions, no cloud required.

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![Version](https://img.shields.io/badge/version-1.4.0-green.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)
![Next.js](https://img.shields.io/badge/Next.js-16-black.svg)
[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2?logo=discord)](https://discord.gg/9Svd99npyj)

**[View Full Documentation](https://trevordavies095.github.io/tempo/)** - Complete guides for installation, configuration, usage, deployment, and more.

## Screenshots

![Dashboard](https://i.imgur.com/pURdx2e.png)
*Dashboard view*

![My Activities](https://i.imgur.com/nZEt9mN.png)
*Activities list*

![Activity Details](https://i.imgur.com/aj671gl.png)
*Activity details view*

## Quick Start

Get Tempo running in minutes with Docker Compose:

```bash
# Clone the repository
git clone https://github.com/trevordavies095/tempo.git
cd tempo

# Start all services
docker-compose up -d
```

Access the application at:
- **Frontend**: http://localhost:3000
- **API**: http://localhost:5001

That's it! The database migrations run automatically on first startup. For detailed setup instructions, authentication, and configuration, see the [full documentation](https://trevordavies095.github.io/tempo/).

## Features

- **Multi-Format Support** - Import GPX, FIT (.fit, .fit.gz), and Strava CSV files from Garmin, Apple Watch, and other devices
- **Workout Analytics** - Track distance, pace, elevation, splits, and time series data
- **Interactive Maps** - Visualize routes with elevation profiles
- **Media Support** - Attach photos and videos to workouts
- **Weather Data** - Automatic weather conditions for each workout
- **Bulk Import** - Import multiple workouts at once via ZIP file (up to 500MB)
- **Heart Rate Zones** - Calculate zones using Age-based, Karvonen, or Custom methods
- **Relative Effort** - Automatic calculation of workout intensity based on heart rate zones
- **Best Efforts** - Track your fastest times for standard distances (400m to Marathon) from any segment within workouts
- **Shoe Tracking** - Track mileage on your running shoes and know when to replace them
- **Workout Editing** - Crop/trim workouts and edit activity names
- **Statistics Dashboards** - Weekly and yearly statistics with relative effort tracking
- **Unit Preferences** - Switch between metric and imperial units
- **100% Local** - All data stays on your machine, no cloud sync required

## Tech Stack

- **Frontend**: Next.js 16, React 19, TypeScript, Tailwind CSS
- **Backend**: ASP.NET Core (.NET 9) Minimal APIs
- **Database**: PostgreSQL 16
- **State Management**: TanStack Query

## Documentation

Comprehensive documentation is available at **[https://trevordavies095.github.io/tempo/](https://trevordavies095.github.io/tempo/)**:

- **[Getting Started](https://trevordavies095.github.io/tempo/getting-started/)** - Installation, quick start, and configuration guides
- **[User Guide](https://trevordavies095.github.io/tempo/user-guide/)** - Importing workouts, viewing analytics, managing media, and settings
- **[Developer Documentation](https://trevordavies095.github.io/tempo/developers/)** - Architecture, local development setup, API reference, and database schema
- **[Deployment](https://trevordavies095.github.io/tempo/deployment/)** - Production deployment, security best practices, and backup/restore procedures
- **[Troubleshooting](https://trevordavies095.github.io/tempo/troubleshooting/)** - Common issues, solutions, and frequently asked questions

## Support

- **Discord**: Join our community on [Discord](https://discord.gg/9Svd99npyj) for support and discussions
- **Issues**: Report bugs or request features on [GitHub Issues](https://github.com/trevordavies095/tempo/issues)
- **Changelog**: See [CHANGELOG.md](CHANGELOG.md) for version history and updates

## License

MIT License - see LICENSE file for details
