using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Tests for ThresholdSuggestionService: which recent 5K/10K effort drives the suggested threshold pace
/// </summary>
public class ThresholdSuggestionServiceTests : IAsyncLifetime
{
    private static readonly DateTime NowUtc = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = DateOnly.FromDateTime(NowUtc);

    private string _cloneConnectionString = null!;
    private TempoDbContext _db = null!;
    private ThresholdSuggestionService _service = null!;

    public async Task InitializeAsync()
    {
        _cloneConnectionString = await PostgresTestFixture.CreateCloneAsync();
        _db = PostgresTestFixture.CreateContext(_cloneConnectionString);
        _service = new ThresholdSuggestionService(new BestEffortService(NullLogger<BestEffortService>.Instance));
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
    public async Task GetSuggestionAsync_WithNoEfforts_ReturnsNull()
    {
        (await _service.GetSuggestionAsync(_db, NowUtc)).Should().BeNull();
    }

    [Fact]
    public async Task GetSuggestionAsync_With5KOnly_UsesItAndReportsSource()
    {
        var workout = await SeedEffortAsync(Today.AddDays(-10), "5K", 1200);

        var suggestion = await _service.GetSuggestionAsync(_db, NowUtc);

        suggestion.Should().NotBeNull();
        suggestion!.ThresholdPaceS.Should().Be(256); // 20:00 5K → VDOT 49.8 → T 4:16/km
        suggestion.Distance.Should().Be("5K");
        suggestion.TimeS.Should().Be(1200);
        suggestion.WorkoutDate.Should().Be(Today.AddDays(-10).ToString("yyyy-MM-dd"));
        suggestion.WorkoutId.Should().Be(workout.Id.ToString());
    }

    [Fact]
    public async Task GetSuggestionAsync_With10KOnly_UsesIt()
    {
        await SeedEffortAsync(Today.AddDays(-30), "10K", 2481);

        var suggestion = await _service.GetSuggestionAsync(_db, NowUtc);

        suggestion!.Distance.Should().Be("10K");
        suggestion.ThresholdPaceS.Should().Be(255);
    }

    [Theory]
    [InlineData(1448, 2481, "10K", 255)] // 5K at VDOT 40, 10K at VDOT 50 → 10K wins
    [InlineData(1197, 3003, "5K", 255)]  // 5K at VDOT 50, 10K at VDOT 40 → 5K wins
    public async Task GetSuggestionAsync_WithBoth_FasterThresholdPaceWins(
        int fiveKTimeS, int tenKTimeS, string expectedDistance, int expectedPaceS)
    {
        await SeedEffortAsync(Today.AddDays(-5), "5K", fiveKTimeS);
        await SeedEffortAsync(Today.AddDays(-20), "10K", tenKTimeS);

        var suggestion = await _service.GetSuggestionAsync(_db, NowUtc);

        suggestion!.Distance.Should().Be(expectedDistance);
        suggestion.ThresholdPaceS.Should().Be(expectedPaceS);
    }

    [Fact]
    public async Task GetSuggestionAsync_ExcludesEffortsOlderThan90Days()
    {
        await SeedEffortAsync(Today.AddDays(-91), "5K", 960); // much faster, but too old
        await SeedEffortAsync(Today.AddDays(-90), "5K", 1200); // on the window's first day

        var suggestion = await _service.GetSuggestionAsync(_db, NowUtc);

        suggestion!.TimeS.Should().Be(1200);
        suggestion.ThresholdPaceS.Should().Be(256);
    }

    [Fact]
    public async Task GetSuggestionAsync_WithOnlyOtherDistances_ReturnsNull()
    {
        await SeedEffortAsync(Today.AddDays(-3), "Half-Marathon", 5400);
        await SeedEffortAsync(Today.AddDays(-3), "1 mile", 330);

        (await _service.GetSuggestionAsync(_db, NowUtc)).Should().BeNull();
    }

    private async Task<Workout> SeedEffortAsync(DateOnly date, string distance, int timeS)
    {
        var workout = await TestDataSeeder.SeedWorkoutAsync(
            _db,
            startedAt: date.ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc),
            distanceM: 21100);
        _db.WorkoutBestEfforts.Add(new WorkoutBestEffort
        {
            WorkoutId = workout.Id,
            Distance = distance,
            DistanceM = BestEffortService.StandardDistances[distance],
            TimeS = timeS
        });
        await _db.SaveChangesAsync();
        return workout;
    }
}
