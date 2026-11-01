using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Leftover :feat-ai-coach schema cleanup.
/// Catalog/safety checks run without Docker.
/// </summary>
public class LeftoverCoachSchemaCleanupTests
{
    // Full feat/ai-coach migration set (2026-10-13 .. 2026-10-19), kept here so drift is obvious.
    private static readonly string[] ExpectedCoachMigrationIds =
    [
        "20261013233508_AddCoachPreferences",
        "20261013234603_AddCoachProviderConnection",
        "20261014003607_AddCoachGoalAndMemoryLayers",
        "20261014011934_AddCoachConstraints",
        "20261014014510_AddCoachCheckIns",
        "20261014015448_AddCoachPrivacyPreflights",
        "20261014020240_AddCoachJobsAndWeeks",
        "20261014021250_AddCoachWeekApplyRecord",
        "20261014104847_AddCoachAdjustmentsAndJobKind",
        "20261018134037_AddCoachEpisodesAndRules",
        "20261018191631_AddCoachSeedMemoryNudgeDismissed",
        "20261018222335_AddCoachCheckInFeelAndQuestion",
        "20261019214216_AddCoachExchanges",
        "20261019221519_AddCoachAdjustmentSourceExchangeId",
    ];

    private static readonly string[] ExpectedCoachTables =
    [
        "CoachPreferences",
        "CoachProviderConnections",
        "CoachGoals",
        "CoachMemoryLayers",
        "CoachConstraints",
        "CoachCheckIns",
        "CoachPrivacyPreflights",
        "CoachJobs",
        "CoachWeeks",
        "CoachAdjustments",
        "CoachEpisodes",
        "CoachRules",
        "CoachExchanges",
    ];

    [Fact]
    public void CoachCleanupCatalog_MatchesFeatAiCoachSchema()
    {
        DatabaseMigrationHelper.LeftoverCoachTables.Should().Equal(ExpectedCoachTables);
        DatabaseMigrationHelper.LeftoverCoachMigrationIds.Should().Equal(ExpectedCoachMigrationIds);

        DatabaseMigrationHelper.LeftoverCoachTables.Should()
            .OnlyContain(t => t.StartsWith("Coach", StringComparison.Ordinal));
        DatabaseMigrationHelper.LeftoverCoachTables.Should()
            .NotContain(["Workouts", "UserSettings", "Shoes"]);

        DatabaseMigrationHelper.LeftoverCoachMigrationIds.Should()
            .OnlyContain(id => id.Contains("Coach", StringComparison.Ordinal));
        DatabaseMigrationHelper.LeftoverCoachMigrationIds.Should()
            .NotContain(id => id.Contains("InitialCreate", StringComparison.Ordinal));
    }

    [Fact]
    public void IsSafeCoachTableName_AcceptsCatalog_RejectsCoreAndInjection()
    {
        foreach (var table in DatabaseMigrationHelper.LeftoverCoachTables)
        {
            DatabaseMigrationHelper.IsSafeCoachTableName(table).Should().BeTrue(table);
        }

        DatabaseMigrationHelper.IsSafeCoachTableName("Workouts").Should().BeFalse();
        DatabaseMigrationHelper.IsSafeCoachTableName("UserSettings").Should().BeFalse();
        DatabaseMigrationHelper.IsSafeCoachTableName("Shoes").Should().BeFalse();
        DatabaseMigrationHelper.IsSafeCoachTableName("Coach\"; DROP TABLE \"Workouts").Should().BeFalse();
        DatabaseMigrationHelper.IsSafeCoachTableName("").Should().BeFalse();
        DatabaseMigrationHelper.IsSafeCoachTableName("coachpreferences").Should().BeFalse();
    }
}

/// <summary>
/// End-to-end cleanup against PostgreSQL via Testcontainers.
/// Hard-skipped until a Docker daemon is available (local agent had none).
/// To verify: start Docker, remove Skip below, then
/// <c>dotnet test --filter FullyQualifiedName~LeftoverCoachSchemaCleanupPostgresTests</c>.
/// </summary>
public class LeftoverCoachSchemaCleanupPostgresTests : IAsyncLifetime
{
    private const string DockerSkip =
        "Requires Docker daemon for Testcontainers PostgreSQL — skipped without a live daemon. " +
        "Remove Skip and re-run when Docker is available.";

    private string _cloneConnectionString = null!;
    private Tempo.Api.Data.TempoDbContext _db = null!;

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

    [Fact(Skip = DockerSkip)]
    public async Task ApplyMigrations_WithLeftoverCoachSchema_DropsCoachTablesAndHistory_PreservesCoreData()
    {
        var shoe = await TestDataSeeder.SeedShoeAsync(_db, brand: "Tempo", model: "Cleanup");
        var settings = await TestDataSeeder.SeedUserSettingsAsync(_db, unitPreference: "metric");
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            shoeId: shoe.Id,
            name: "Cleanup-test run");

        var workoutId = workout.Id;
        var shoeId = shoe.Id;
        var settingsId = settings.Id;

        await using (var conn = new NpgsqlConnection(_cloneConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE "CoachPreferences" (
                    "Id" uuid PRIMARY KEY,
                    "OptedIn" boolean NOT NULL,
                    "CreatedAt" timestamptz NOT NULL,
                    "UpdatedAt" timestamptz NOT NULL
                );
                CREATE TABLE "CoachJobs" (
                    "Id" uuid PRIMARY KEY,
                    "Status" varchar(32) NOT NULL
                );
                INSERT INTO "CoachPreferences" ("Id", "OptedIn", "CreatedAt", "UpdatedAt")
                VALUES (gen_random_uuid(), true, NOW(), NOW());
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES
                    ('20261013233508_AddCoachPreferences', '10.0.0'),
                    ('20261014020240_AddCoachJobsAndWeeks', '10.0.0'),
                    ('20261019221519_AddCoachAdjustmentSourceExchangeId', '10.0.0')
                ON CONFLICT ("MigrationId") DO NOTHING;
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        _db.ChangeTracker.Clear();
        DatabaseMigrationHelper.ApplyMigrations(_db);

        await using (var conn = new NpgsqlConnection(_cloneConnectionString))
        {
            await conn.OpenAsync();

            await using (var tablesCmd = conn.CreateCommand())
            {
                tablesCmd.CommandText = """
                    SELECT table_name
                    FROM information_schema.tables
                    WHERE table_schema = 'public'
                      AND table_type = 'BASE TABLE'
                      AND table_name LIKE 'Coach%';
                    """;
                await using var reader = await tablesCmd.ExecuteReaderAsync();
                var leftover = new List<string>();
                while (await reader.ReadAsync())
                {
                    leftover.Add(reader.GetString(0));
                }

                leftover.Should().BeEmpty("all Coach* tables must be dropped");
            }

            await using (var histCmd = conn.CreateCommand())
            {
                histCmd.CommandText = """
                    SELECT "MigrationId"
                    FROM "__EFMigrationsHistory"
                    WHERE "MigrationId" LIKE '%Coach%';
                    """;
                await using var reader = await histCmd.ExecuteReaderAsync();
                var leftover = new List<string>();
                while (await reader.ReadAsync())
                {
                    leftover.Add(reader.GetString(0));
                }

                leftover.Should().BeEmpty("coach migration history rows must be purged");
            }
        }

        _db.ChangeTracker.Clear();
        (await _db.Workouts.SingleAsync(w => w.Id == workoutId)).Name.Should().Be("Cleanup-test run");
        (await _db.Shoes.SingleAsync(s => s.Id == shoeId)).Model.Should().Be("Cleanup");
        (await _db.UserSettings.SingleAsync(s => s.Id == settingsId)).UnitPreference.Should().Be("metric");
    }

    [Fact(Skip = DockerSkip)]
    public void ApplyMigrations_WithoutCoachLeftovers_IsIdempotentNoOp()
    {
        DatabaseMigrationHelper.ApplyMigrations(_db);
        DatabaseMigrationHelper.ApplyMigrations(_db);
    }
}
