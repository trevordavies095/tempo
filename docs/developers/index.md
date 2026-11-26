# Developer Documentation

Technical docs for changing Tempo: architecture, local setup, the HTTP API, the schema, tests, and the contribution process. Start here if you are a developer or a coding agent orienting in the repo.

## Pages

- **[Architecture](architecture.md)** - Components, services, data flow, and a [Where to change X](architecture.md#where-to-change-x) task map
- **[Development Setup](setup.md)** - Run Postgres, the API, and the command center locally
- **[API Reference](api-reference.md)** - Endpoint documentation with examples
- **[Database Schema](database.md)** - Entities, relationships, indexes, and migrations
- **[Testing](testing.md)** - Running API tests (Docker required), coverage gate, OpenAPI drift check, frontend lint
- **[Contributing](contributing.md)** - Branches, commits, and the pull request checklist

## Sources of Truth

These drift faster than prose. When a doc and one of these disagree, the file wins:

| Topic | Source |
|-------|--------|
| Domain vocabulary (Workout intake, Workout derivations, Import job, Command center, …) | [`CONTEXT.md`](https://github.com/trevordavies095/tempo/blob/develop/CONTEXT.md) |
| Coding-agent instructions (commands, pre-finish checks, gotchas) | [`AGENTS.md`](https://github.com/trevordavies095/tempo/blob/develop/AGENTS.md) |
| HTTP contract | [`docs/openapi.json`](https://github.com/trevordavies095/tempo/blob/develop/docs/openapi.json), checked against the code in CI |
| Schema | `api/Data/TempoDbContext.cs` and `api/Migrations/` |
| Services, DI lifetimes, middleware, config reads | `api/Program.cs` |
| CI checks | `.github/workflows/tests.yml` |
| Pinned .NET tools (`dotnet-ef`, `dotnet swagger`) | `.config/dotnet-tools.json` |

## Technology Stack

- **Frontend**: Next.js 16, React 19, TypeScript, Tailwind CSS 4 (semantic tokens, Geist, hand-rolled UI kit)
- **Icons**: Tabler Icons (`@tabler/icons-react`)
- **Backend**: ASP.NET Core Minimal APIs on .NET 10 (C#), Entity Framework Core 10
- **Database**: PostgreSQL 16 with JSONB fields
- **State Management**: TanStack Query
- **Maps**: Leaflet/React-Leaflet
- **Charts**: Recharts
- **Docs site**: MkDocs (`mkdocs.yml`, deployed from `main`)

## Getting Started

1. Read [Architecture](architecture.md) and skim `CONTEXT.md`
2. Set up your [development environment](setup.md)
3. Read [Testing](testing.md) before changing the API
4. Follow [Contributing](contributing.md) when you open a pull request

## Quick Links

- [GitHub Repository](https://github.com/trevordavies095/tempo)
- [Issue Tracker](https://github.com/trevordavies095/tempo/issues)
- [Discord Community](https://discord.gg/9Svd99npyj)
