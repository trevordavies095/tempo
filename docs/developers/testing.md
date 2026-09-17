# Testing Guide

This guide covers how to run tests, generate coverage reports, and add new tests to the Tempo project.

## Overview

The Tempo API uses **xUnit** for testing with **coverlet** for code coverage collection. EF-backed tests run against **PostgreSQL 16** (`postgres:16-alpine`, same image as compose) via Testcontainers. Docker must be running for those tests; they fail loudly if the container cannot start. Parser, track-geometry, and HealthKit decoder tests do **not** start Postgres and do not need Docker.

Tests are organized into:

- **Unit Tests**: Test individual services in isolation (located in `api/Tempo.Api.Tests/Services/`)
  - Parser tests (`GpxParserServiceTests`, `FitParserServiceTests`) assert decode: fixture file → `TrackPoint`s and optional device summary, not split counts or elevation-gain algorithm details. No Docker.
  - `TrackGeometryTests` assert elevation, split boundaries, GeoJSON, and series elapsed-seconds from fixture `TrackPoint` lists (no `DbContext`). No Docker.
  - `HealthKitWorkoutDecoderTests` decode HealthKit JSON without a database. No Docker.
  - `WorkoutIntakeTests` cover `created` / `updated` / `skipped` / `error` with faked weather, relative effort, and best efforts; persist and duplicate stay real on a Postgres clone. Includes a `PersistAsync` seam test that builds `DecodedWorkout` without the file adapter, plus HealthKit outdoor and indoor persist/skip cases (UUID identity, DistM splits without route, summary-only). `ImportHealthKitWorkoutTests` cover the JSON import path (outdoor GPS, indoor DistM stream / summary-only, empty-distance reject), including parallel same-UUID posts. `TrackGeometryTests` cover DistM-based splits when GPS is absent.
- **Integration Tests**: Test endpoints and full request/response cycles (located in `api/Tempo.Api.Tests/IntegrationTests/`)
  - Import-job and Tempo export-import tests (`ImportJobTests`, `ImportExportTests`) poll `GET /workouts/import/jobs/{id}` until `completed` or `failed`. Do not expect a blocking **200** summary from `POST /workouts/import/bulk` or `POST /workouts/import/export` (those adapters return **202**).

## Running Tests

EF-backed tests start one process-wide Testcontainers Postgres (`postgres:16-alpine`), migrate a template database once, and clone a fresh database per service-test method or integration factory. Tests run **sequentially** (`DisableTestParallelization`). There is no in-memory SQLite mode and no `TEST_CONNECTION_STRING` override.

### Run All Tests

```bash
cd api
dotnet test
```

Start Docker first if you will run EF-backed tests. Parser-only filters (below) do not need it.

### Run Tests with Coverage

```bash
dotnet test --collect:"XPlat Code Coverage" --settings:"Tempo.Api.Tests/coverlet.runsettings"
```

This generates a Cobertura XML coverage report in the `TestResults/` directory.

### Run Specific Tests

```bash
# Run tests in a specific class
dotnet test --filter "FullyQualifiedName~AuthEndpointsTests"

# Run tests matching a pattern
dotnet test --filter "FullyQualifiedName~ServiceTests"
```

### Verbose Output

```bash
dotnet test --verbosity normal
```

## Code Coverage

### Generating Coverage Reports

1. **Run tests with coverage collection**:
   ```bash
   dotnet test --collect:"XPlat Code Coverage" --settings:"Tempo.Api.Tests/coverlet.runsettings" --results-directory:"TestResults"
   ```

2. **Install ReportGenerator** (optional, for HTML reports):
   ```bash
   dotnet tool install -g dotnet-reportgenerator-globaltool
   ```

3. **Generate HTML report**:
   ```bash
   reportgenerator \
     -reports:"**/coverage.cobertura.xml" \
     -targetdir:"coverage-report" \
     -reporttypes:"Html;Badges" \
     -classfilters:"-*Dynastream*" \
     -assemblyfilters:"-*Garmin.FIT.Sdk*"
   ```

   The HTML report will be in the `coverage-report/` directory. Open `index.html` in a browser.

### Coverage Configuration

Coverage is configured in `api/Tempo.Api.Tests/coverlet.runsettings`:

- **Includes**: `[Tempo.Api]*` - Only API code is measured
- **Excludes**: 
  - `[Tempo.Api.Tests]*` - Test code is excluded
  - `**/Program.cs` - Startup code is excluded
  - `**/Migrations/**` - Database migrations are excluded
  - Generated code (via attributes)

### Coverage Threshold

The CI pipeline enforces a **minimum 45% code coverage** threshold. If coverage drops below 45%, the CI build will fail. This threshold will be gradually increased as test coverage improves.

### Viewing Coverage Locally

1. **Cobertura XML**: Generated in `TestResults/*/coverage.cobertura.xml`
2. **HTML Report**: Generate using ReportGenerator (see above)
3. **IDE Integration**: Many IDEs (Visual Studio, Rider) can display coverage inline

### Interpreting Coverage

- **Line Coverage**: Percentage of code lines executed during tests
- **Branch Coverage**: Percentage of conditional branches (if/else, switch) executed
- **Overall Coverage**: Weighted average of line and branch coverage

**What to test:**
- ✅ Happy paths (normal operation)
- ✅ Error paths (validation failures, exceptions)
- ✅ Edge cases (boundary conditions, null values)
- ✅ Configuration variations
- ✅ Security-sensitive code (authentication, authorization)

## Adding New Tests

### Test Structure

Follow the existing patterns in the codebase:

#### Unit Test Example

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

#### Integration Test Example

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

### Test Conventions

1. **Naming**: `MethodName_Scenario_ExpectedBehavior`
   - Example: `Login_WithInvalidPassword_ReturnsUnauthorized`

2. **Organization**: Use `#region` blocks to group related tests
   ```csharp
   #region Login Tests
   
   [Fact]
   public async Task Login_WithValidCredentials_ReturnsSuccess() { }
   
   #endregion
   ```

3. **Assertions**: Use FluentAssertions for readable assertions
   ```csharp
   result.Should().NotBeNull();
   result.Value.Should().Be(42);
   list.Should().HaveCount(3);
   ```

4. **Test Data**: Use `TestDataSeeder` for consistent test data. Passwords must satisfy `PasswordPolicy` (16+ characters, etc.); use `TestPasswords.Default` or `TestPasswords.Alternate` from `Tempo.Api.Tests.Infrastructure`.
   ```csharp
   var user = await TestDataSeeder.SeedUserAsync(_db, "testuser", TestPasswords.Default);
   ```

5. **Database**: EF-backed service tests clone from the migrated template (`UseNpgsql`). Parser/geometry/decoder tests skip this.
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

6. **Cleanup**: Drop the clone in `DisposeAsync` (see above). Do not `EnsureCreated`.

### Test Collections

Integration tests use test collections to ensure proper isolation:

```csharp
[Collection("Integration Tests")]
public class MyIntegrationTests : IClassFixture<TempoWebApplicationFactory>
{
    // ...
}
```

This ensures tests run sequentially and don't interfere with each other. Assembly-level `CollectionBehavior(DisableTestParallelization = true)` keeps one container and shared factory state sane. Do not re-enable parallelization.

## Test Infrastructure

### PostgresTestFixture

Process-wide lazy Postgres 16 container. First EF-backed use starts `postgres:16-alpine`, migrates `tempo_test_template` with `DatabaseMigrationHelper.ApplyMigrations`, then `CREATE DATABASE … TEMPLATE` for each clone. Parser tests must not call this type.

```csharp
var connectionString = await PostgresTestFixture.CreateCloneAsync();
var db = PostgresTestFixture.CreateContext(connectionString);
// UseNpgsql — not in-memory SQLite
await PostgresTestFixture.DropCloneAsync(connectionString);
```

### TestHttpClientFactory

Helper for creating authenticated HTTP clients:

```csharp
// Create authenticated client
var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

// Create authenticated client with custom user
var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "username", TestPasswords.Default);

// Create unauthenticated client
var client = TestHttpClientFactory.CreateUnauthenticatedClient(_factory);
```

### TestDataSeeder

Helper for seeding test data:

```csharp
// Seed user
var user = await TestDataSeeder.SeedUserAsync(_db, "username", TestPasswords.Default);

// Seed workout
var workout = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000);

// Seed workout with time series
var workout = await TestDataSeeder.SeedWorkoutCompleteAsync(_db, distanceM: 10000, includeTimeSeries: true);
```

### TempoWebApplicationFactory

Factory for creating test web applications. Automatically handles:
- One Postgres clone per factory instance (connection string set before the host builds; host uses `UseNpgsql`)
- Service configuration (JWT, media, elevation). Does not replace DbContext registration
- Authentication setup
- Schema comes from the migrated template; Testing skips host migrations

## CI Coverage Gate

The CI pipeline automatically:

1. Runs all tests with coverage collection
2. Generates coverage reports
3. Checks if coverage meets the 45% threshold
4. Fails the build if coverage is below 45%

### Fixing Coverage Failures

If coverage fails:

1. **Identify uncovered code**: Check the coverage report to see which files/methods are uncovered
2. **Add tests**: Write tests for uncovered code paths
3. **Focus on critical code**: Prioritize endpoints, services, and security-sensitive code
4. **Test edge cases**: Don't just test happy paths - test error conditions, validation, and edge cases

### Coverage Exclusions

Some code is intentionally excluded from coverage:

- `Program.cs` - Application startup code
- `Migrations/**` - Database migrations
- Generated code (via attributes)
- Third-party libraries (`Garmin.FIT.Sdk`)

If you need to exclude additional code, update `coverlet.runsettings`.

## Best Practices

1. **Test Independence**: Each test should be independent and not rely on other tests
2. **Clean State**: Ensure tests start with a clean database state
3. **Meaningful Names**: Test names should clearly describe what is being tested
4. **Arrange-Act-Assert**: Structure tests with clear Arrange, Act, and Assert sections
5. **Test One Thing**: Each test should verify one specific behavior
6. **Avoid Test Interdependence**: Don't rely on test execution order
7. **Mock External Dependencies**: Use Moq for external services (HTTP clients, file system, etc.)
8. **Keep parser tests Docker-free**: GPX/FIT decode, track geometry, and HealthKit JSON decode must not construct `TempoDbContext` or call `PostgresTestFixture`

## Troubleshooting

### Tests Fail Locally But Pass in CI

- Check database state - ensure clean database between tests
- Verify test isolation - tests shouldn't depend on each other
- Check for timing issues - add delays if needed for async operations

### Coverage Not Generating

- Ensure `coverlet.collector` package is installed
- Check that `coverlet.runsettings` is correctly referenced
- Verify test execution completed successfully

### Coverage Too Low

- Review coverage report to identify gaps
- Add tests for uncovered methods
- Focus on critical paths first (endpoints, services)
- Test error conditions and edge cases

## Resources

- [xUnit Documentation](https://xunit.net/)
- [FluentAssertions Documentation](https://fluentassertions.com/)
- [Coverlet Documentation](https://github.com/coverlet-coverage/coverlet)
- [ReportGenerator Documentation](https://github.com/danielpalme/ReportGenerator)

