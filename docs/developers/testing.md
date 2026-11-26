# Testing Guide

How to run the API tests and the other CI checks locally, and how to add tests. Read this before changing the API: a pull request must pass every check in [What CI Runs](#what-ci-runs).

## Overview

The API tests (`api/Tempo.Api.Tests/`) use **xUnit**, **FluentAssertions**, **Moq**, and **coverlet** for coverage. EF-backed tests run against **PostgreSQL 16** (`postgres:16-alpine`, same image as compose) via Testcontainers, so **Docker must be running** for them; they fail loudly if the container cannot start. Tests that never touch `PostgresTestFixture` or `TempoWebApplicationFactory` (GPX/FIT parsers, track geometry, HealthKit decoder, password/JWT/secret helpers, and similar) do not need Docker.

There are no frontend tests; the frontend gate is ESLint.

Tests are organized into:

- **Service tests** (`api/Tempo.Api.Tests/Services/`): one class per service, `<Service>Tests`.
  - Parser tests (`GpxParserServiceTests`, `FitParserServiceTests`) assert decode: fixture file → `TrackPoint`s and optional device summary, not split counts or elevation-gain algorithm details. No Docker.
  - `TrackGeometryTests` assert elevation, split boundaries (including DistM-based splits when GPS is absent), GeoJSON, and series elapsed-seconds from fixture `TrackPoint` lists (no `DbContext`). No Docker.
  - `HealthKitWorkoutDecoderTests` decode HealthKit JSON without a database. No Docker.
  - `WorkoutIntakeTests` cover `created` / `updated` / `skipped` / `error` with faked weather, relative effort, and best efforts; persist and duplicate stay real on a Postgres clone. Includes a `PersistAsync` seam test that builds `DecodedWorkout` without the file adapter, plus HealthKit outdoor and indoor persist/skip cases (UUID identity, DistM splits without route, summary-only).
  - Backfills: `BackfillRunnerTests` (shared `BackfillPass`) plus one `<Name>BackfillServiceTests` per backfill.
- **Integration tests** (`api/Tempo.Api.Tests/IntegrationTests/`): full HTTP request/response through `TempoWebApplicationFactory`.
  - `ImportHealthKitWorkoutTests` cover the JSON import path (outdoor GPS, indoor DistM stream / summary-only, empty-distance reject), including parallel same-UUID posts.
  - Import-job and Tempo export-import tests (`ImportJobTests`, `ImportExportTests`) poll `GET /workouts/import/jobs/{id}` until `completed` or `failed`. Do not expect a blocking **200** summary from `POST /workouts/import/bulk` or `POST /workouts/import/export` (those adapters return **202**).
- **Other**: `Authentication/`, `Logging/`, and startup/version tests at the project root.

## Running Tests

Run from the **repo root**. `dotnet test` inside `api/` only finds `Tempo.Api.csproj` (not a test project) and runs nothing.

EF-backed tests start one process-wide Testcontainers Postgres, migrate a template database once, and clone a fresh database per service-test method or integration factory. Tests run **sequentially** (`DisableTestParallelization`). There is no in-memory SQLite mode and no `TEST_CONNECTION_STRING` override.

### Run All Tests

Start Docker first, then:

```bash
dotnet test Tempo.sln
```

### Run Specific Tests

```bash
# One class
dotnet test Tempo.sln --filter "FullyQualifiedName~AuthEndpointsTests"

# Docker-free decode and geometry tests
dotnet test Tempo.sln --filter "FullyQualifiedName~GpxParserServiceTests|FullyQualifiedName~FitParserServiceTests|FullyQualifiedName~TrackGeometryTests|FullyQualifiedName~HealthKitWorkoutDecoderTests"

# Verbose output
dotnet test Tempo.sln --verbosity normal
```

### Run Tests with Coverage

Same flags as CI:

```bash
dotnet test Tempo.sln \
  --collect:"XPlat Code Coverage" \
  --settings:"api/Tempo.Api.Tests/coverlet.runsettings" \
  --results-directory:"TestResults"
```

This writes Cobertura XML to `TestResults/<guid>/coverage.cobertura.xml`. For an HTML report:

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool   # once
reportgenerator \
  -reports:"**/coverage.cobertura.xml" \
  -targetdir:"coverage-report" \
  -reporttypes:"Html;Badges" \
  -classfilters:"-*Dynastream*" \
  -assemblyfilters:"-*Garmin.FIT.Sdk*"
```

Open `coverage-report/index.html`. Delete `TestResults/` between runs if you want the gate's "first file found" to be the latest run.

### Manual API Testing

A Bruno collection lives in `api/bruno/Tempo.Api/`. Install [Bruno](https://www.usebruno.com/), open that folder, and select the `Local` environment (`baseUrl` = `http://localhost:5001`).

## What CI Runs

`.github/workflows/tests.yml` runs on pushes and pull requests to `main` and `develop`. Three jobs:

| Job | What it checks | Run locally |
|-----|----------------|-------------|
| `test` | `dotnet tool restore`, `dotnet restore Tempo.sln`, `dotnet build Tempo.sln --no-restore`, the [OpenAPI drift check](#openapi-drift-check), tests with coverage, then the [45% coverage gate](#coverage-gate) | The commands on this page |
| `frontend-lint` | `npm ci` and `npm run lint` in `frontend/` on Node 20 | `cd frontend && npm ci && npm run lint` |
| `prod-compose-secrets` | `docker compose -f docker-compose.prod.yml config` fails without `JWT_SECRET_KEY` and `POSTGRES_PASSWORD`, and with them renders no placeholder JWT, no `192.168` address, no published 5432/5001, and binds 3004 on `127.0.0.1` | Only relevant when editing `docker-compose.prod.yml` |

### OpenAPI Drift Check

CI boots the real host with `dotnet swagger tofile` against `bin/Debug/net10.0/Tempo.Api.dll` and fails on any `diff` against the committed `docs/openapi.json`. After changing routes or route/Swagger metadata, regenerate and commit it. With the Compose Postgres up (`docker compose up -d postgres`), from `api/`:

```bash
dotnet build
ASPNETCORE_ENVIRONMENT=Development \
  JWT__SecretKey='local-openapi-only-not-for-production-min-32-chars!' \
  ConnectionStrings__DefaultConnection='Host=localhost;Port=5432;Database=tempo;Username=postgres;Password=postgres' \
  dotnet swagger tofile --output ../docs/openapi.json bin/Debug/net10.0/Tempo.Api.dll v1
```

- Use the **Debug** build, as CI does.
- Use **Development**, not `Testing`: with `Testing` the generic host looks for a `StartupTesting` class that does not exist, and Swashbuckle fails.
- The host applies migrations to the target database. The [README](https://github.com/trevordavies095/tempo#documentation) has a variant with a throwaway `postgres:16-alpine` container instead.

### Coverage Gate

The gate reads the `line-rate` of the first `coverage.cobertura.xml` under `TestResults/`, rounds it to a whole percent, and fails below **45%**. It is line coverage only; branch coverage is reported but not gated. The threshold will be raised as coverage improves.

Coverage settings live in `api/Tempo.Api.Tests/coverlet.runsettings`:

- **Include**: `[Tempo.Api]*`
- **Exclude**: `[Tempo.Api.Tests]*`
- **Exclude by file**: `**/Program.cs`, `**/Migrations/**`
- **Exclude by attribute**: `Obsolete`, `GeneratedCodeAttribute`, `CompilerGeneratedAttribute`

The FIT SDK is a NuGet package (`Garmin.FIT.Sdk`), outside the `[Tempo.Api]` include; ReportGenerator also filters `Dynastream` classes. To exclude more code, edit `coverlet.runsettings`.

If the gate fails, open the HTML report, find uncovered endpoints and services, and add tests for their error and edge paths as well as the happy path.

## Adding New Tests

### Unit Test Example

```csharp
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class MyServiceTests
{
    private readonly MyService _service;

    public MyServiceTests()
    {
        _service = new MyService();
    }

    [Fact]
    public void MyMethod_WithValidInput_ReturnsExpectedResult()
    {
        // Arrange
        var input = "test";

        // Act
        var result = _service.MyMethod(input);

        // Assert
        result.Should().Be("expected");
    }
}
```

### Integration Test Example

```csharp
using System.Net;
using FluentAssertions;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class MyEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public MyEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetEndpoint_ReturnsSuccess()
    {
        // Arrange
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.GetAsync("/my-endpoint");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
```

`[Collection("Integration Tests")]` binds to `IntegrationTestCollection`. Together with the assembly-level `CollectionBehavior(DisableTestParallelization = true)` in `AssemblyInfo.cs`, it keeps one container and shared factory state sane. Do not re-enable parallelization.

### Test Conventions

1. **Naming**: `MethodName_Scenario_ExpectedBehavior`, e.g. `Login_WithInvalidPassword_ReturnsUnauthorized`
2. **Organization**: group related tests in `#region` blocks
3. **Assertions**: FluentAssertions (`result.Should().NotBeNull()`, `list.Should().HaveCount(3)`)
4. **Test data**: use `TestDataSeeder`. Passwords must satisfy `PasswordPolicy` (16+ characters); use `TestPasswords.Default` or `TestPasswords.Alternate` from `Tempo.Api.Tests.Infrastructure`.
   ```csharp
   var user = await TestDataSeeder.SeedUserAsync(_db, "testuser", TestPasswords.Default);
   ```
5. **Database**: EF-backed service tests clone from the migrated template (`UseNpgsql`) and drop the clone in `DisposeAsync`. Do not `EnsureCreated`. Parser/geometry/decoder tests skip this.
   ```csharp
   public class MyServiceTests : IAsyncLifetime
   {
       private string _cloneConnectionString = null!;
       private TempoDbContext _db = null!;

       public async Task InitializeAsync()
       {
           _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
           _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
       }

       public async Task DisposeAsync()
       {
           if (_db is not null)
           {
               await _db.DisposeAsync();
           }

           if (_cloneConnectionString is not null)
           {
               await PostgresTestFixture.DropCloneAsync(_cloneConnectionString);
           }
       }
   }
   ```
6. **External dependencies**: fake them with Moq or the helpers in `Infrastructure/` (`MockWeatherServiceHelper`, `FakeIntervalsIcuClient`).
7. **Keep parser tests Docker-free**: GPX/FIT decode, track geometry, and HealthKit JSON decode must not construct `TempoDbContext` or call `PostgresTestFixture`.
8. **Fixtures**: binary fixtures go in `api/Tempo.Api.Tests/Fixtures/`.

## Test Infrastructure

All in `api/Tempo.Api.Tests/Infrastructure/`.

### PostgresTestFixture

Process-wide lazy Postgres 16 container. First EF-backed use starts `postgres:16-alpine`, migrates `tempo_test_template` with `DatabaseMigrationHelper.ApplyMigrations`, then `CREATE DATABASE … TEMPLATE` for each clone. Parser tests must not call this type.

```csharp
var connectionString = await PostgresTestFixture.CreateCloneAsync();
var db = PostgresTestFixture.CreateContext(connectionString);
// UseNpgsql — not in-memory SQLite
await PostgresTestFixture.DropCloneAsync(connectionString);
```

### TempoWebApplicationFactory

Factory for integration-test hosts. It:

- Runs the host in the `Testing` environment (host skips migrations; schema comes from the migrated template; `IntervalsIcuSyncWorker` is not registered)
- Creates one Postgres clone per factory instance and sets the connection string before the host builds (host uses `UseNpgsql`; DbContext registration is not replaced)
- Supplies test configuration (JWT, media, elevation) and authentication setup

In `Testing`, EF throws on an unordered, unfiltered `First` (`FirstWithoutOrderByAndFilterWarning`), so such queries fail tests instead of warning in production.

### TestHttpClientFactory

```csharp
// Authenticated client (default user "testuser" / TestPasswords.Default)
var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

// Authenticated client with custom user
var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "username", TestPasswords.Default);

// Unauthenticated client
var client = TestHttpClientFactory.CreateUnauthenticatedClient(_factory);
```

### TestDataSeeder

```csharp
// Seed user
var user = await TestDataSeeder.SeedUserAsync(_db, "username", TestPasswords.Default);

// Seed workout
var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000);

// Seed workout with route, splits, and time series
var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 10000, includeTimeSeries: true);
```

Other seeders cover shoes, user settings, routes, splits, device laps, time series, and media.

## Troubleshooting

### EF-backed tests fail at startup

- Docker is not running, or Testcontainers cannot reach the Docker socket. Start Docker and rerun.

### "You must install or update .NET to run this application"

- The test host needs the .NET 10 runtime (`Microsoft.NETCore.App` 10.0). A machine with only a newer SDK/runtime builds the tests but cannot run them; install the .NET 10 SDK.

### Tests pass locally but fail in CI (or the reverse)

- Check isolation: each test should start from its own clone and not depend on execution order
- For background work (import jobs), poll the job document until a terminal status instead of sleeping

### Coverage not generating

- Pass `--collect:"XPlat Code Coverage"` and the `--settings` path relative to where you run `dotnet test`
- `coverlet.collector` is referenced by `Tempo.Api.Tests.csproj`; a failed test run may not write a report

## Resources

- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Testcontainers for .NET](https://dotnet.testcontainers.org/)
- [Coverlet Documentation](https://github.com/coverlet-coverage/coverlet)
- [ReportGenerator Documentation](https://github.com/danielpalme/ReportGenerator)
