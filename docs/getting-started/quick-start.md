# Quick Start

Get Tempo running in minutes with Docker Compose. This is the recommended method for most users.

## Prerequisites

- Docker and Docker Compose installed
  - [Install Docker](https://docs.docker.com/get-docker/)
  - [Install Docker Compose](https://docs.docker.com/compose/install/)

## Installation Steps

### 1. Clone the Repository

```bash
git clone https://github.com/trevordavies095/tempo.git
cd tempo
```

### 2. Start All Services

```bash
docker-compose up -d
```

This command will:
- Start PostgreSQL database
- Start the API server
- Start the frontend application
- Run database migrations automatically

### 3. Access the Application

Once all services are running, access Tempo at:

- **Frontend**: http://localhost:3000
- **API**: http://localhost:5001
- **API Swagger UI** (development): http://localhost:5001/swagger

### 4. Register Your Account

1. Navigate to `http://localhost:3000`
2. You'll be redirected to the login page
3. Register your account (only available when no users exist)
   - Choose a username and password
   - Confirm your password
   - After registration, you'll be automatically logged in

**Note:** Registration is automatically locked after the first user is created. This is a security feature for single-user deployments.

## Verification

To verify everything is working:

1. Check that all containers are running:
   ```bash
   docker-compose ps
   ```

2. Check container logs:
   ```bash
   docker-compose logs -f
   ```

3. Access the health endpoint:
   ```bash
   curl http://localhost:5001/health
   ```

## Data Persistence

Your data is persisted in Docker volumes:
- **Database**: Stored in the `postgres_data` volume
- **Media files**: Stored in the `./media` directory (mounted as a volume)

Data will survive container restarts. To completely remove data, use:
```bash
docker-compose down -v
```

## Next Steps

- [Configure Tempo](configuration.md) for your environment
- [Import your first workout](../user-guide/importing-workouts.md)
- Learn about [production deployment](../deployment/production.md)

## Troubleshooting

If you encounter issues, see the [Troubleshooting](../troubleshooting/index.md) section for common problems and solutions.

