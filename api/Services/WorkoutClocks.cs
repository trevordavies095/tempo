using System.Text.Json;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Workout duration clocks: elapsed (<see cref="Workout.DurationS"/>), timer
/// (<see cref="Workout.TimerTimeS"/>), and moving (<see cref="Workout.MovingTimeS"/>).
/// </summary>
public static class WorkoutClocks
{
    /// <summary>
    /// Reads a FIT/session JSON number into nullable seconds. Missing, non-numeric, or
    /// &lt;= 0 → null. Does not clamp to elapsed.
    /// </summary>
    public static int? SecondsFromJsonNumber(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var seconds = (int)Math.Round(element.GetDouble());
        return seconds > 0 ? seconds : null;
    }

    /// <summary>
    /// Sets <see cref="Workout.AvgPaceS"/> from timer → moving → elapsed (seconds/km).
    /// </summary>
    public static void ApplyAvgPace(Workout workout)
    {
        var clock = workout.TimerTimeS ?? workout.MovingTimeS ?? workout.DurationS;
        workout.AvgPaceS = clock > 0 && workout.DistanceM > 0
            ? clock / (workout.DistanceM / 1000.0)
            : 0;
    }
}
