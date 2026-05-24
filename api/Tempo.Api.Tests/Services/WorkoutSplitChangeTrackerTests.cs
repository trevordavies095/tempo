using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Tempo.Api.Utils;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Regression tests for EF change-tracker leaks when split recalculation fails mid-request.
/// </summary>
public class WorkoutSplitChangeTrackerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TempoDbContext _db;

    public WorkoutSplitChangeTrackerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new TempoDbContext(new DbContextOptionsBuilder<TempoDbContext>()
            .UseSqlite(_connection)
            .Options);
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task PendingSplitChanges_WithoutRevert_LeakIntoSubsequentSaveChanges()
    {
        var workoutA = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workoutA);
        var originalCount = await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == workoutA.Id);
        originalCount.Should().BeGreaterThan(0);

        SimulateFailedSplitRecalc(workoutA.Id);

        _db.Workouts.Add(new Workout
        {
            StartedAt = workoutA.StartedAt.AddDays(1),
            DurationS = 1800,
            DistanceM = 5000,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var splitsAfter = await _db.WorkoutSplits.Where(s => s.WorkoutId == workoutA.Id).ToListAsync();
        splitsAfter.Should().HaveCount(1, "unsaved RemoveRange/AddRange leak into the next SaveChanges");
        splitsAfter[0].Idx.Should().Be(0);
        splitsAfter.Count.Should().NotBe(originalCount);
    }

    [Fact]
    public async Task PendingSplitChanges_AfterRevert_DoNotLeakIntoSubsequentSaveChanges()
    {
        var workoutA = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithSplitsAsync(_db, workoutA);
        var originalCount = await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == workoutA.Id);
        originalCount.Should().BeGreaterThan(0);

        SimulateFailedSplitRecalc(workoutA.Id);
        _db.RevertPendingWorkoutSplitChanges(workoutA.Id);

        _db.Workouts.Add(new Workout
        {
            StartedAt = workoutA.StartedAt.AddDays(1),
            DurationS = 1800,
            DistanceM = 5000,
            AvgPaceS = 360,
            Source = "test",
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var splitsAfter = await _db.WorkoutSplits.CountAsync(s => s.WorkoutId == workoutA.Id);
        splitsAfter.Should().Be(originalCount);
    }

    private void SimulateFailedSplitRecalc(Guid workoutId)
    {
        var oldSplits = _db.WorkoutSplits.Where(s => s.WorkoutId == workoutId).ToList();
        _db.WorkoutSplits.RemoveRange(oldSplits);
        _db.WorkoutSplits.AddRange(new[]
        {
            new WorkoutSplit
            {
                WorkoutId = workoutId,
                Idx = 0,
                DistanceM = 1000,
                DurationS = 300,
                PaceS = 300
            }
        });
    }

}
