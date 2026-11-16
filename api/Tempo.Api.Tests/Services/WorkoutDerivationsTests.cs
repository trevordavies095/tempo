using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WorkoutDerivationsTests : IAsyncLifetime
{
    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private FakeBestEffortService _bestEfforts = null!;
    private WorkoutDerivations _derivations = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _bestEfforts = new FakeBestEffortService();
        _derivations = new WorkoutDerivations(
            _db,
            new HeartRateZoneService(),
            new RelativeEffortService(),
            _bestEfforts,
            NullLogger<WorkoutDerivations>.Instance);
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
    public async Task RecomputeAsync_WithoutSettings_KeepsRelativeEffort()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout);
        workout.RelativeEffort = 42;
        await _db.SaveChangesAsync();

        var result = await _derivations.RecomputeAsync(workout, WorkoutChange.HeartRateZones);

        result.ZonesConfigured.Should().BeFalse();
        result.RelativeEffort.Should().Be(42);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync();
        stored.RelativeEffort.Should().Be(42);
        stored.ZoneSecondsComputedAt.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_WithZones_SetsRelativeEffortAndZoneSeconds()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db, age: 30);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout);

        var result = await _derivations.RecomputeAsync(workout, WorkoutChange.HeartRateZones);

        result.ZonesConfigured.Should().BeTrue();
        result.RelativeEffort.Should().BeGreaterThan(0);
        var stored = await _db.Workouts.AsNoTracking().SingleAsync();
        stored.RelativeEffort.Should().Be(result.RelativeEffort);
        stored.ZoneSecondsComputedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task RecomputeAsync_WithZones_ClearsRelativeEffort_WhenNoHeartRateData()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db, age: 30);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout, includeHeartRate: false);
        workout.RelativeEffort = 42;
        await _db.SaveChangesAsync();

        var result = await _derivations.RecomputeAsync(workout, WorkoutChange.Samples);

        result.RelativeEffort.Should().BeNull();
        (await _db.Workouts.AsNoTracking().SingleAsync()).RelativeEffort.Should().BeNull();
    }

    [Fact]
    public async Task RecomputeAsync_Samples_UpdatesBestEfforts()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);

        await _derivations.RecomputeAsync(workout, WorkoutChange.Samples);

        _bestEfforts.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task RecomputeAsync_HeartRateZones_LeavesBestEffortsAlone()
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);

        await _derivations.RecomputeAsync(workout, WorkoutChange.HeartRateZones);

        _bestEfforts.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task RecomputeAsync_SwallowsBestEffortFailure()
    {
        await TestDataSeeder.SeedUserSettingsAsync(_db, age: 30);
        var workout = await TestDataSeeder.SeedWorkoutAsync(_db);
        await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(_db, workout);
        _bestEfforts.Throw = true;

        var result = await _derivations.RecomputeAsync(workout, WorkoutChange.Samples);

        result.RelativeEffort.Should().BeGreaterThan(0);
        (await _db.Workouts.AsNoTracking().SingleAsync()).RelativeEffort.Should().Be(result.RelativeEffort);
    }

    private sealed class FakeBestEffortService : IBestEffortService
    {
        public int CallCount { get; private set; }
        public bool Throw { get; set; }

        public Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout)
        {
            CallCount++;
            if (Throw)
            {
                throw new InvalidOperationException("best efforts failed");
            }

            return Task.CompletedTask;
        }
    }
}
