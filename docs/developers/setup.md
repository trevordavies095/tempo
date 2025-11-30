# Development Setup

Set up your local development environment for Tempo.

## Prerequisites

- **.NET 9 SDK** - [Download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **Node.js 18+** and npm - [Download](https://nodejs.org/)
- **PostgreSQL 16** - [Download](https://www.postgresql.org/download/) or use Docker
- **Git** - [Download](https://git-scm.com/downloads)

## Quick Setup

### 1. Clone the Repository

```bash
git clone https://github.com/trevordavies095/tempo.git
cd tempo
```

### 2. Start PostgreSQL

You can use Docker Compose to run only the database:

```bash
docker-compose up -d postgres
```

Or install PostgreSQL locally and create a database:

```bash
createdb tempo
```

### 3. Configure Backend

Update `api/appsettings.json` with your database connection string:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres"
  }
}
```

### 4. Run Database Migrations

```bash
cd api
dotnet ef database update
```

Migrations also run automatically on API startup.

### 5. Start Backend

```bash
cd api
dotnet watch run
```

The API runs at `http://localhost:5001`. Swagger UI is available at `/swagger` in Development mode.

### 6. Start Frontend

In a new terminal:

```bash
cd frontend
npm install
npm run dev
```

The frontend runs at `http://localhost:3000`. API requests are proxied via Next.js rewrites from `/api/*` to `http://localhost:5001/*`.

## Development Commands

### Backend (API)

```bash
cd api

# Run development server with hot reload
dotnet watch run

# Create a new migration
dotnet ef migrations add MigrationName

# Apply migrations
dotnet ef database update

# Build for production
dotnet build -c Release
```

### Frontend

```bash
cd frontend

# Install dependencies
npm install

# Run development server
npm run dev

# Build for production
npm run build

# Start production server
npm start

# Run linter (ESLint)
npm run lint
```

### Docker Compose

```bash
# Start all services (postgres, api, frontend)
docker-comose up -d

# View logs
docker-compose logs -f

# Stop all services
docker-compose down

# Stop and remove volumes (clears database)
docker-compose down -v
```

## Configuration

### API Configuration (`appsettings.json`)

Key configuration options:

- `ConnectionStrings:DefaultConnection` - PostgreSQL connection string
- `MediaStorage:RootPath` - Filesystem path for media storage (default: `./media`)
- `MediaStorage:MaxFileSizeBytes` - Maximum upload size (default: 50MB)
- `ElevationCalculation:NoiseThresholdMeters` - Elevation smoothing threshold
- `CORS:AllowedOrigins` - Comma-separated list of allowed origins

**Note**: Large file uploads (bulk import) are configured in `Program.cs` with 500MB limits.

### Frontend Configuration (`next.config.ts`)

- API rewrites: `/api/*` → `http://localhost:5001/*` (dev) or `http://api:5001/*` (Docker)
- `NEXT_PUBLIC_API_URL`: Used for direct API calls (bypasses Next.js for large uploads)
- `API_SERVICE_URL`: Environment variable to override API URL for rewrites

## Testing

### API Testing

A Bruno API testing collection is available in `api/bruno/Tempo.Api/`:

1. Install [Bruno](https://www.usebruno.com/)
2. Open the collection from `api/bruno/Tempo.Api/`
3. Configure the environment (default: `http://localhost:5001`)
4. Test endpoints interactively

### Test Data

Test data files are available in `test_data/` directory for development and testing import functionality.

**Note**: The `.gitignore` excludes `test_data/` from version control.

## Development Workflow

1. Create a feature branch from `main`
2. Make your changes
3. Test locally
4. Run linters and formatters
5. Commit changes
6. Push and create a pull request

## Troubleshooting

### Database Connection Issues

- Verify PostgreSQL is running: `docker ps` or `pg_isready`
- Check connection string matches your setup
- Ensure database exists: `createdb tempo`

### Port Conflicts

- Change ports in `docker-compose.yml` if conflicts occur
- Default ports: Frontend (3000), API (5001), PostgreSQL (5432)

### Migration Errors

- Migrations run automatically on startup
- For manual migration: `cd api && dotnet ef database update`
- Migrations are idempotent and handle existing tables gracefully

## Next Steps

- [Read the architecture documentation](architecture.md)
- [Explore the API reference](api-reference.md)
- [Review the contributing guide](contributing.md)

