using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for common workout database queries.
/// </summary>
public static class WorkoutQueryService
{
    /// <summary>
    /// Finds a duplicate workout based on start time, distance, and duration.
    /// A workout is considered a duplicate if it has the same start time and very similar distance and duration.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="startTime">Start time of the workout</param>
    /// <param name="distanceMeters">Distance in meters</param>
    /// <param name="durationSeconds">Duration in seconds</param>
    /// <returns>The existing workout if found, null otherwise</returns>
    public static async Task<Workout?> FindDuplicateWorkoutAsync(
        TempoDbContext db,
        DateTime startTime,
        double distanceMeters,
        int durationSeconds)
    {
        return await db.Workouts
            .Where(w => w.StartedAt == startTime &&
                        Math.Abs(w.DistanceM - distanceMeters) < 1.0 &&
                        Math.Abs(w.DurationS - durationSeconds) < 1)
            .FirstOrDefaultAsync();
    }
}

