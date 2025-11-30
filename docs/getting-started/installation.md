# Installation

This guide covers different installation methods for Tempo.

## Installation Methods

### Docker Compose (Recommended)

The easiest way to run Tempo is with Docker Compose. See the [Quick Start Guide](quick-start.md) for detailed instructions.

### Local Development Setup

For development or if you prefer to run services individually:

#### Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- PostgreSQL 16 (or use Docker Compose for database only)

#### Step 1: Start PostgreSQL

If you don't have PostgreSQL installed locally, you can use Docker Compose to run only the database:

```bash
docker-compose up -d postgres
```

Or install PostgreSQL locally and create a database:

```bash
createdb tempo
```

#### Step 2: Configure Backend

Update `api/appsettings.json` with your database connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres"
  }
}
```

#### Step 3: Run Database Migrations

```bash
cd api
dotnet ef database update
```

#### Step 4: Start Backend

```bash
cd api
dotnet watch run
```

The API will run at `http://localhost:5001`. Swagger UI is available at `/swagger` in Development mode.

#### Step 5: Start Frontend

In a new terminal:

```bash
cd frontend
npm install
npm run dev
```

The frontend will run at `http://localhost:3000`.

## Production Deployment

For production deployment, see the [Production Deployment Guide](../deployment/production.md).

## System Requirements

### Minimum Requirements

- **CPU**: 1 core
- **RAM**: 2GB
- **Storage**: 10GB (varies based on workout data)
- **OS**: Linux, macOS, or Windows

### Recommended Requirements

- **CPU**: 2+ cores
- **RAM**: 4GB+
- **Storage**: 50GB+ (for media files and database)
- **OS**: Linux (for production)

## Port Requirements

Default ports used by Tempo:

- **Frontend**: 3000
- **API**: 5001
- **PostgreSQL**: 5432

You can change these ports in `docker-compose.yml` if needed.

## Next Steps

- [Configure Tempo](configuration.md) for your environment
- [Import your first workout](../user-guide/importing-workouts.md)

