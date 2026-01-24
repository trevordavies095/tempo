using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for calculating and managing running insights including data coverage,
/// weather extremes, performance highlights, and habit patterns.
/// </summary>
public class InsightsService
{
    private readonly ILogger<InsightsService> _logger;

    public InsightsService(ILogger<InsightsService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive insights about a user's running data.
    /// Returns data coverage metadata and (in future) specific insights.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <returns>Insights response with metadata and statistics</returns>
    public async Task<InsightsResponse> GetInsightsAsync(TempoDbContext db)
    {
        try
        {
            // Get total workout count
            var totalWorkouts = await db.Workouts
                .AsNoTracking()
                .CountAsync();

            // Check if user has sufficient workouts for insights
            if (totalWorkouts < InsightsThresholds.MINIMUM_WORKOUTS)
            {
                return new InsightsResponse
                {
                    Message = $"Complete at least {InsightsThresholds.MINIMUM_WORKOUTS} runs to see insights!",
                    SufficientData = false,
                    CurrentWorkouts = totalWorkouts,
                    RequiredWorkouts = InsightsThresholds.MINIMUM_WORKOUTS,
                    Metadata = new DataCoverageMetadata
                    {
                        TotalWorkouts = totalWorkouts,
                        MinimumWorkoutsRequired = InsightsThresholds.MINIMUM_WORKOUTS,
                        DataAvailability = new Dictionary<string, DataAvailabilityCategory>()
                    }
                };
            }

            // Calculate data coverage metadata
            var metadata = await CalculateDataCoverageAsync(db, totalWorkouts);

            return new InsightsResponse
            {
                SufficientData = true,
                Metadata = metadata
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating insights");
            throw;
        }
    }

    /// <summary>
    /// Calculate comprehensive data coverage metadata including availability by category.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="totalWorkouts">Total number of workouts</param>
    /// <returns>Data coverage metadata</returns>
    private async Task<DataCoverageMetadata> CalculateDataCoverageAsync(TempoDbContext db, int totalWorkouts)
    {
        // Execute all data availability queries in parallel for performance
        var weatherCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.Weather != null)
            .CountAsync();

        var heartRateCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxHeartRateBpm.HasValue)
            .CountAsync();

        var elevationCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.ElevGainM.HasValue)
            .CountAsync();

        var caloriesCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.Calories.HasValue)
            .CountAsync();

        var cadenceCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxCadenceRpm.HasValue)
            .CountAsync();

        var powerCountTask = db.Workouts
            .AsNoTracking()
            .Where(w => w.MaxPowerWatts.HasValue)
            .CountAsync();

        var firstWorkoutTask = db.Workouts
            .AsNoTracking()
            .OrderBy(w => w.StartedAt)
            .FirstOrDefaultAsync();

        var latestWorkoutTask = db.Workouts
            .AsNoTracking()
            .OrderByDescending(w => w.StartedAt)
            .FirstOrDefaultAsync();

        // Await all queries
        await Task.WhenAll(
            weatherCountTask,
            heartRateCountTask,
            elevationCountTask,
            caloriesCountTask,
            cadenceCountTask,
            powerCountTask,
            firstWorkoutTask,
            latestWorkoutTask
        );

        var weatherCount = await weatherCountTask;
        var heartRateCount = await heartRateCountTask;
        var elevationCount = await elevationCountTask;
        var caloriesCount = await caloriesCountTask;
        var cadenceCount = await cadenceCountTask;
        var powerCount = await powerCountTask;
        var firstWorkout = await firstWorkoutTask;
        var latestWorkout = await latestWorkoutTask;

        // Calculate date range
        DateTime? firstWorkoutDate = firstWorkout?.StartedAt;
        DateTime? latestWorkoutDate = latestWorkout?.StartedAt;
        int? daysSinceFirstWorkout = null;

        if (firstWorkoutDate.HasValue && latestWorkoutDate.HasValue)
        {
            daysSinceFirstWorkout = (int)(latestWorkoutDate.Value - firstWorkoutDate.Value).TotalDays;
        }

        // Build data availability dictionary
        var dataAvailability = new Dictionary<string, DataAvailabilityCategory>
        {
            ["weather"] = CreateAvailabilityCategory(weatherCount, totalWorkouts, InsightsThresholds.MINIMUM_WEATHER_COVERAGE),
            ["heartRate"] = CreateAvailabilityCategory(heartRateCount, totalWorkouts, InsightsThresholds.MINIMUM_HR_COVERAGE),
            ["elevation"] = CreateAvailabilityCategory(elevationCount, totalWorkouts, 0.0), // Always show if any data exists
            ["calories"] = CreateAvailabilityCategory(caloriesCount, totalWorkouts, 0.0),
            ["cadence"] = CreateAvailabilityCategory(cadenceCount, totalWorkouts, 0.0),
            ["power"] = CreateAvailabilityCategory(powerCount, totalWorkouts, 0.0)
        };

        return new DataCoverageMetadata
        {
            TotalWorkouts = totalWorkouts,
            FirstWorkoutDate = firstWorkoutDate,
            LatestWorkoutDate = latestWorkoutDate,
            DaysSinceFirstWorkout = daysSinceFirstWorkout,
            DataAvailability = dataAvailability,
            MinimumWorkoutsRequired = InsightsThresholds.MINIMUM_WORKOUTS
        };
    }

    /// <summary>
    /// Create a data availability category with count, percentage, and availability flag.
    /// </summary>
    /// <param name="count">Number of workouts with this data</param>
    /// <param name="totalWorkouts">Total number of workouts</param>
    /// <param name="minimumCoverage">Minimum coverage percentage (0.0-1.0) required for availability</param>
    /// <returns>Data availability category</returns>
    private DataAvailabilityCategory CreateAvailabilityCategory(int count, int totalWorkouts, double minimumCoverage)
    {
        var percentage = totalWorkouts > 0 ? (count * 100.0 / totalWorkouts) : 0.0;
        var coverageRatio = totalWorkouts > 0 ? (count / (double)totalWorkouts) : 0.0;
        var available = count > 0 && coverageRatio >= minimumCoverage;

        return new DataAvailabilityCategory
        {
            Count = count,
            Percentage = Math.Round(percentage, 1),
            Available = available
        };
    }
}
