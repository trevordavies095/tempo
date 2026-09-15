using System.Text.Json;
using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WorkoutClocksTests
{
    [Fact]
    public void SecondsFromJsonNumber_NonNumber_ReturnsNull()
    {
        var element = JsonSerializer.Deserialize<JsonElement>("\"x\"");
        WorkoutClocks.SecondsFromJsonNumber(element).Should().BeNull();
    }

    [Fact]
    public void SecondsFromJsonNumber_ZeroOrNegative_ReturnsNull()
    {
        WorkoutClocks.SecondsFromJsonNumber(JsonSerializer.Deserialize<JsonElement>("0"))
            .Should().BeNull();
        WorkoutClocks.SecondsFromJsonNumber(JsonSerializer.Deserialize<JsonElement>("-5"))
            .Should().BeNull();
    }

    [Fact]
    public void SecondsFromJsonNumber_Positive_Rounds()
    {
        WorkoutClocks.SecondsFromJsonNumber(JsonSerializer.Deserialize<JsonElement>("1000.4"))
            .Should().Be(1000);
        WorkoutClocks.SecondsFromJsonNumber(JsonSerializer.Deserialize<JsonElement>("1000.6"))
            .Should().Be(1001);
    }

    [Fact]
    public void ApplyAvgPace_UsesTimerThenMovingThenElapsed()
    {
        var workout = new Workout { DistanceM = 1000, DurationS = 400, MovingTimeS = 350, TimerTimeS = 300 };
        WorkoutClocks.ApplyAvgPace(workout);
        workout.AvgPaceS.Should().Be(300);

        workout.TimerTimeS = null;
        WorkoutClocks.ApplyAvgPace(workout);
        workout.AvgPaceS.Should().Be(350);

        workout.MovingTimeS = null;
        WorkoutClocks.ApplyAvgPace(workout);
        workout.AvgPaceS.Should().Be(400);
    }

    [Fact]
    public void ApplyAvgPace_ZeroClockOrDistance_IsZero()
    {
        var workout = new Workout { DistanceM = 0, DurationS = 400 };
        WorkoutClocks.ApplyAvgPace(workout);
        workout.AvgPaceS.Should().Be(0);

        workout.DistanceM = 1000;
        workout.DurationS = 0;
        WorkoutClocks.ApplyAvgPace(workout);
        workout.AvgPaceS.Should().Be(0);
    }
}
