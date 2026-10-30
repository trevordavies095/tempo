using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// DropBestEffortsTable must copy old all-time records into WorkoutBestEfforts for workouts the backfill
/// has not reached yet, so all-time bests stay correct until it does.
/// </summary>
public class DropBestEffortsTableMigrationTests : IAsyncLifetime
{
    private const string PreviousMigration = "20261030220307_AddWorkoutBestEfforts";
    private const string TargetMigration = "20261030221208_DropBestEffortsTable";

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

    [Fact]
    public async Task Up_SeedsRecordsForUnmarkedWorkouts_ThenDropsTable()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);

        var unmarked = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 10000);
        var marked = await TestDataSeeder.SeedWorkoutAsync(_db, distanceM: 5000);
        marked.BestEffortsComputedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Unmarked workout holds the 5K and 10K records, and already has a (seeded) 10K row
        await InsertRecordAsync("5K", 5000, 1200, unmarked);
        await InsertRecordAsync("10K", 10000, 2500, unmarked);
        await InsertRowAsync(unmarked.Id, "10K", 10000, 2600);
        // Marked workout was already backfilled; its stale record must not be copied
        await InsertRecordAsync("1K", 1000, 200, marked);

        await migrator.MigrateAsync(TargetMigration);

        var rows = await _db.WorkoutBestEfforts.AsNoTracking().ToListAsync();
        rows.Select(r => (r.WorkoutId, r.Distance, r.TimeS)).Should().BeEquivalentTo(new[]
        {
            (unmarked.Id, "5K", 1200),
            (unmarked.Id, "10K", 2600)
        });
        (await _db.Workouts.AsNoTracking().SingleAsync(w => w.Id == unmarked.Id))
            .BestEffortsComputedAt.Should().BeNull();

        var tableCount = await _db.Database
            .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_name = 'BestEfforts'""")
            .SingleAsync();
        tableCount.Should().Be(0);
    }

    private async Task InsertRecordAsync(string distance, double distanceM, int timeS, Workout workout)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "BestEfforts" ("Id", "Distance", "DistanceM", "TimeS", "WorkoutId", "WorkoutDate", "CalculatedAt")
            VALUES ({Guid.NewGuid()}, {distance}, {distanceM}, {timeS}, {workout.Id}, {workout.StartedAt}, now())
            """);
    }

    private async Task InsertRowAsync(Guid workoutId, string distance, double distanceM, int timeS)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "WorkoutBestEfforts" ("Id", "WorkoutId", "Distance", "DistanceM", "TimeS")
            VALUES ({Guid.NewGuid()}, {workoutId}, {distance}, {distanceM}, {timeS})
            """);
    }
}
