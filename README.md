# Tempo - Self-Hostable Running Tracker

A privacy-first, self-hostable running tracker for runners who want to track their workouts locally without social features or subscriptions.

## Features

- **Manual GPX Import**: Upload GPX files from Apple Watch or other devices
- **Workout Analytics**: View distance, pace, elevation, and splits
- **Self-Hostable**: Run everything on your own server with Docker
- **100% Local**: All data stays on your machine

## Tech Stack

- **Frontend**: Next.js 14+ (React, TypeScript, Tailwind CSS)
- **Backend**: ASP.NET Core (.NET 9) Minimal APIs
- **Database**: PostgreSQL
- **State Management**: TanStack Query

## Prerequisites

- .NET 9 SDK
- Node.js 18+ and npm
- Docker and Docker Compose
- PostgreSQL (or use Docker Compose)

## Setup

### 1. Start PostgreSQL

```bash
docker-compose up -d postgres
```

### 2. Configure Backend

Update `api/appsettings.json` with your database connection string if needed (defaults are already set for Docker Compose).

### 3. Run Database Migrations

```bash
cd api
dotnet ef database update
```

### 4. Start Backend

```bash
cd api
dotnet run
```

The API will be available at `http://localhost:5001`

### 5. Start Frontend

```bash
cd frontend
npm install
npm run dev
```

The frontend will be available at `http://localhost:3000`

## Usage

1. Export a GPX file from your Apple Watch or other device
2. Navigate to the frontend at `http://localhost:3000`
3. Drag and drop or select your GPX file
4. Click "Import Workout" to process and save the workout

## API Endpoints

- `POST /workouts/import` - Upload and import a GPX file
- `GET /health` - Health check endpoint

## Development

### Backend

```bash
cd api
dotnet watch run
```

### Frontend

```bash
cd frontend
npm run dev
```

## License

MIT

