using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class RelativeEffortService : IRelativeEffortService
{
    // Zone weights: Zone 1 = 1 point/min, Zone 2 = 2 points/min, etc.
    private static readonly int[] ZoneWeights = { 1, 2, 3, 4, 5 };

    /// <summary>
    /// Calculate Relative Effort for a workout using heart rate zones.
    /// Attempts to use WorkoutTimeSeries data first, falls back to raw data or average heart rate approximation.
    /// </summary>
    public int? CalculateRelativeEffort(Workout workout, List<HeartRateZone> zones, TempoDbContext db)
    {
        if (zones == null || zones.Count != 5)
        {
            return null; // Invalid zones
        }

        // Try to use time series data first (most accurate)
        var timeSeries = db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id && ts.HeartRateBpm.HasValue)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToList();

        if (timeSeries.Count > 0)
        {
            return CalculateFromTimeSeries(timeSeries, zones);
        }

        // Fall back to extracting from raw data
        var result = CalculateFromRawData(workout, zones);
        if (result.HasValue)
        {
            return result;
        }

        // If no detailed data available, return null (can't calculate accurately)
        return null;
    }

    /// <summary>
    /// Bucket time-series heart rate into five zone durations (seconds).
    /// Returns null when series/zones are invalid or no sample lands in a zone.
    /// </summary>
    public double[]? TryGetTimeInZones(List<WorkoutTimeSeries>? timeSeries, List<HeartRateZone>? zones)
    {
        if (timeSeries == null || timeSeries.Count == 0 || zones == null || zones.Count != 5)
        {
            return null;
        }

        var timeInZones = new double[5];

        for (int i = 0; i < timeSeries.Count; i++)
        {
            var currentPoint = timeSeries[i];
            if (!currentPoint.HeartRateBpm.HasValue)
            {
                continue;
            }

            int heartRate = currentPoint.HeartRateBpm.Value;

            int zoneIndex = GetZoneIndex(heartRate, zones);
            if (zoneIndex >= 0)
            {
                double timeSeconds = 1.0;
                if (i < timeSeries.Count - 1)
                {
                    var nextPoint = timeSeries[i + 1];
                    timeSeconds = nextPoint.ElapsedSeconds - currentPoint.ElapsedSeconds;
                    // Clamp to reasonable values (avoid gaps from pauses)
                    if (timeSeconds > 10 || timeSeconds < 0)
                    {
                        timeSeconds = 1.0;
                    }
                }

                timeInZones[zoneIndex] += timeSeconds;
            }
        }

        double sum = 0.0;
        for (int i = 0; i < 5; i++)
        {
            sum += timeInZones[i];
        }

        return sum > 0 ? timeInZones : null;
    }

    /// <summary>
    /// Calculate Relative Effort from time series data.
    /// </summary>
    public int CalculateFromTimeSeries(List<WorkoutTimeSeries> timeSeries, List<HeartRateZone> zones)
    {
        var timeInZones = TryGetTimeInZones(timeSeries, zones);
        if (timeInZones == null)
        {
            return 0;
        }

        double totalEffort = 0.0;
        for (int i = 0; i < 5; i++)
        {
            double timeInMinutes = timeInZones[i] / 60.0;
            totalEffort += timeInMinutes * ZoneWeights[i];
        }

        return (int)Math.Round(totalEffort);
    }

    /// <summary>
    /// Calculate Relative Effort from raw FIT/GPX JSONB data.
    /// This is a fallback when time series data is not available.
    /// </summary>
    public int? CalculateFromRawData(Workout workout, List<HeartRateZone> zones)
    {
        if (zones == null || zones.Count != 5)
        {
            return null;
        }

        // Try FIT data first
        if (!string.IsNullOrEmpty(workout.RawFitData))
        {
            try
            {
                var fitData = JsonSerializer.Deserialize<JsonElement>(workout.RawFitData);
                if (fitData.TryGetProperty("session", out var session))
                {
                    // FIT files store session-level summaries, not per-record data
                    // We can use average heart rate as an approximation
                    if (session.TryGetProperty("avgHeartRate", out var avgHr) && avgHr.ValueKind == JsonValueKind.Number)
                    {
                        int avgHeartRate = avgHr.GetInt32();
                        return CalculateFromAverageHeartRate(avgHeartRate, workout.DurationS, zones);
                    }
                }
            }
            catch
            {
                // Ignore JSON parsing errors
            }
        }

        // Try GPX data (though GPX typically doesn't have heart rate)
        if (!string.IsNullOrEmpty(workout.RawGpxData))
        {
            try
            {
                var gpxData = JsonSerializer.Deserialize<JsonElement>(workout.RawGpxData);
                // GPX files might have heart rate in extensions, but we don't currently extract it
                // Could be enhanced in the future
            }
            catch
            {
                // Ignore JSON parsing errors
            }
        }

        // If workout has average heart rate stored directly, use that as approximation
        if (workout.AvgHeartRateBpm.HasValue)
        {
            return CalculateFromAverageHeartRate(workout.AvgHeartRateBpm.Value, workout.DurationS, zones);
        }

        return null;
    }

    /// <summary>
    /// Calculate Relative Effort using average heart rate as an approximation.
    /// This assumes the entire workout was spent in the zone corresponding to the average heart rate.
    /// </summary>
    private int? CalculateFromAverageHeartRate(int avgHeartRate, int durationSeconds, List<HeartRateZone> zones)
    {
        int zoneIndex = GetZoneIndex(avgHeartRate, zones);
        if (zoneIndex < 0)
        {
            return null; // Heart rate outside all zones
        }

        double timeInMinutes = durationSeconds / 60.0;
        double effort = timeInMinutes * ZoneWeights[zoneIndex];
        return (int)Math.Round(effort);
    }

    /// <summary>
    /// Determine which zone a heart rate falls into.
    /// Returns 0-4 for zones 1-5, or -1 if outside all zones.
    /// Zone boundaries: inclusive min, exclusive max (except Zone 5 which is inclusive max).
    /// </summary>
    private int GetZoneIndex(int heartRate, List<HeartRateZone> zones)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (i == zones.Count - 1)
            {
                // Zone 5: inclusive max
                if (heartRate >= zone.MinBpm && heartRate <= zone.MaxBpm)
                {
                    return i;
                }
            }
            else
            {
                // Zones 1-4: inclusive min, exclusive max
                if (heartRate >= zone.MinBpm && heartRate < zone.MaxBpm)
                {
                    return i;
                }
            }
        }

        return -1; // Outside all zones
    }

    /// <summary>
    /// Get IDs of workouts that are eligible for relative effort calculation.
    /// A workout qualifies if it has:
    /// 1. Time series data with heart rate
    /// 2. RawFitData (may contain avgHeartRate)
    /// 3. AvgHeartRateBpm field populated
    /// </summary>
    public async Task<List<Guid>> GetQualifyingWorkoutIdsAsync(TempoDbContext db)
    {
        // Get workouts that can have relative effort calculated:
        // 1. Workouts with time series data with heart rate
        var workoutIdsWithTimeSeries = await db.WorkoutTimeSeries
            .Where(ts => ts.HeartRateBpm.HasValue)
            .Select(ts => ts.WorkoutId)
            .Distinct()
            .ToListAsync();

        // 2. Workouts with RawFitData (may contain avgHeartRate)
        var workoutIdsWithRawFit = await db.Workouts
            .Where(w => w.RawFitData != null)
            .Select(w => w.Id)
            .ToListAsync();

        // 3. Workouts with AvgHeartRateBpm
        var workoutIdsWithAvgHr = await db.Workouts
            .Where(w => w.AvgHeartRateBpm.HasValue)
            .Select(w => w.Id)
            .ToListAsync();

        // Combine all qualifying workout IDs
        var allQualifyingIds = workoutIdsWithTimeSeries
            .Union(workoutIdsWithRawFit)
            .Union(workoutIdsWithAvgHr)
            .Distinct()
            .ToList();

        return allQualifyingIds;
    }
}

