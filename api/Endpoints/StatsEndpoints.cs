using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class StatsEndpoints
{
    /// <summary>
    /// Get weekly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Daily miles for the current week (Monday-Sunday)</returns>
    /// <remarks>
    /// Returns daily miles for the current week (Monday-Sunday), grouped by day of week.
    /// Distances are converted from meters to miles. Week boundaries are calculated in the specified timezone.
    /// </remarks>
    private static async Task<IResult> GetWeeklyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current week boundaries (Monday-Sunday) in the specified timezone
        // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
        // To convert UTC to local: UTC + offset (since offset is already negative)
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes.HasValue)
        {
            now = now.AddMinutes(timezoneOffsetMinutes.Value);
        }

        // Calculate start of current week (Monday)
        var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = now.Date.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(7).AddTicks(-1);

        // Convert back to UTC for database query
        var weekStartUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekStart, DateTimeKind.Utc);
        var weekEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekEnd, DateTimeKind.Utc);

        // Query workouts for the current week
        var workouts = await db.Workouts
            .Where(w => w.StartedAt >= weekStartUtc && w.StartedAt <= weekEndUtc)
            .AsNoTracking()
            .ToListAsync();

        // Group by day of week and sum distances
        // DayOfWeek enum: Sunday=0, Monday=1, ..., Saturday=6
        // We want: Monday=0, Tuesday=1, ..., Sunday=6
        var dailyTotals = new double[7]; // M T W T F S S

        foreach (var workout in workouts)
        {
            // Convert UTC to local timezone
            // timezoneOffsetMinutes is already negative (from -getTimezoneOffset()), so add it directly
            var localTime = timezoneOffsetMinutes.HasValue
                ? workout.StartedAt.AddMinutes(timezoneOffsetMinutes.Value)
                : workout.StartedAt;

            // Get day of week (0=Monday, 6=Sunday)
            var dayOfWeek = ((int)localTime.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            
            // Convert meters to miles and add to daily total
            var miles = workout.DistanceM / 1609.344;
            dailyTotals[dayOfWeek] += miles;
        }

        return Results.Ok(new
        {
            weekStart = weekStart.ToString("yyyy-MM-dd"),
            weekEnd = weekEnd.ToString("yyyy-MM-dd"),
            dailyMiles = dailyTotals
        });
    }

    /// <summary>
    /// Get relative effort stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Cumulative relative effort for the current week and 3-week average range</returns>
    /// <remarks>
    /// Returns cumulative relative effort for the current week (Monday-Sunday) and calculates
    /// the 3-week average and range from the previous 3 complete weeks.
    /// </remarks>
    private static async Task<IResult> GetRelativeEffortStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current week boundaries (Monday-Sunday) in the specified timezone
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes.HasValue)
        {
            now = now.AddMinutes(timezoneOffsetMinutes.Value);
        }

        // Calculate start of current week (Monday)
        var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = now.Date.AddDays(-daysSinceMonday);
        var weekEnd = weekStart.AddDays(7).AddTicks(-1);

        // Convert back to UTC for database query
        var weekStartUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekStart, DateTimeKind.Utc);
        var weekEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekEnd, DateTimeKind.Utc);

        // Query workouts for the current week with relative effort
        var currentWeekWorkouts = await db.Workouts
            .Where(w => w.StartedAt >= weekStartUtc && w.StartedAt <= weekEndUtc)
            .AsNoTracking()
            .ToListAsync();

        // Calculate daily relative effort totals (Monday-Sunday)
        var dailyEffort = new int[7]; // M T W T F S S
        foreach (var workout in currentWeekWorkouts)
        {
            if (!workout.RelativeEffort.HasValue)
            {
                continue;
            }

            // Convert UTC to local timezone
            var localTime = timezoneOffsetMinutes.HasValue
                ? workout.StartedAt.AddMinutes(timezoneOffsetMinutes.Value)
                : workout.StartedAt;

            // Get day of week (0=Monday, 6=Sunday)
            var dayOfWeek = ((int)localTime.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            dailyEffort[dayOfWeek] += workout.RelativeEffort.Value;
        }

        // Calculate cumulative values (Monday = day 1, Tuesday = day 1 + day 2, etc.)
        var cumulativeEffort = new int[7];
        int runningTotal = 0;
        for (int i = 0; i < 7; i++)
        {
            runningTotal += dailyEffort[i];
            cumulativeEffort[i] = runningTotal;
        }

        // Calculate previous 3 complete weeks
        var previousWeeks = new List<int>();
        for (int weekOffset = 1; weekOffset <= 3; weekOffset++)
        {
            var prevWeekStart = weekStart.AddDays(-7 * weekOffset);
            var prevWeekEnd = prevWeekStart.AddDays(7).AddTicks(-1);

            var prevWeekStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(prevWeekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(prevWeekStart, DateTimeKind.Utc);
            var prevWeekEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(prevWeekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(prevWeekEnd, DateTimeKind.Utc);

            var prevWeekTotal = await db.Workouts
                .Where(w => w.StartedAt >= prevWeekStartUtc && w.StartedAt <= prevWeekEndUtc)
                .AsNoTracking()
                .SumAsync(w => (int?)w.RelativeEffort) ?? 0;

            previousWeeks.Add(prevWeekTotal);
        }

        // Calculate 3-week average and range
        var threeWeekAverage = previousWeeks.Count > 0 ? previousWeeks.Average() : 0.0;
        var rangeMin = previousWeeks.Count > 0 ? previousWeeks.Min() : 0;
        var rangeMax = previousWeeks.Count > 0 ? previousWeeks.Max() : 0;

        // Calculate current week total (last cumulative value)
        var currentWeekTotal = cumulativeEffort[6];

        return Results.Ok(new
        {
            weekStart = weekStart.ToString("yyyy-MM-dd"),
            weekEnd = weekEnd.ToString("yyyy-MM-dd"),
            currentWeek = cumulativeEffort,
            previousWeeks = previousWeeks,
            threeWeekAverage = Math.Round(threeWeekAverage, 1),
            rangeMin = rangeMin,
            rangeMax = rangeMax,
            currentWeekTotal = currentWeekTotal
        });
    }

    /// <summary>
    /// Get best efforts for all standard distances
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="bestEffortService">Best effort service</param>
    /// <returns>List of best effort times for each standard distance</returns>
    /// <remarks>
    /// Returns the fastest time achieved for each standard running distance (400m through Marathon).
    /// Best efforts are calculated from any segment within any workout, not just workouts of that exact distance.
    /// </remarks>
    private static async Task<IResult> GetBestEfforts(
        TempoDbContext db,
        BestEffortService bestEffortService)
    {
        try
        {
            var bestEfforts = await bestEffortService.GetBestEffortsAsync(db);
            return Results.Ok(new { distances = bestEfforts });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve best efforts: {ex.Message}");
        }
    }

    /// <summary>
    /// Recalculate all best efforts
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="bestEffortService">Best effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Success message with count of best efforts calculated</returns>
    /// <remarks>
    /// Performs a full recalculation of all best efforts across all workouts.
    /// This may take some time depending on the number of workouts and time series data.
    /// </remarks>
    private static async Task<IResult> RecalculateBestEfforts(
        TempoDbContext db,
        BestEffortService bestEffortService,
        ILogger<Program> logger)
    {
        try
        {
            logger.LogInformation("Starting manual recalculation of best efforts");
            var bestEfforts = await bestEffortService.CalculateAllBestEffortsAsync(db);
            logger.LogInformation("Completed manual recalculation of best efforts. Found {Count} best efforts", bestEfforts.Count);
            
            return Results.Ok(new
            {
                message = "Best efforts recalculated successfully",
                count = bestEfforts.Count
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating best efforts");
            return Results.Problem($"Failed to recalculate best efforts: {ex.Message}");
        }
    }

    /// <summary>
    /// Get yearly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Total miles for the current year and previous year</returns>
    /// <remarks>
    /// Returns total miles for the current year and previous year. Year boundaries are calculated
    /// in the specified timezone. Distances are converted from meters to miles.
    /// </remarks>
    private static async Task<IResult> GetYearlyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current date in the specified timezone
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes.HasValue)
        {
            now = now.AddMinutes(timezoneOffsetMinutes.Value);
        }

        var currentYear = now.Year;
        var previousYear = currentYear - 1;

        // Calculate year boundaries in local timezone
        var currentYearStart = new DateTime(currentYear, 1, 1, 0, 0, 0);
        var currentYearEnd = new DateTime(currentYear, 12, 31, 23, 59, 59, 999);
        var previousYearStart = new DateTime(previousYear, 1, 1, 0, 0, 0);
        var previousYearEnd = new DateTime(previousYear, 12, 31, 23, 59, 59, 999);

        // Convert to UTC for database queries
        var currentYearStartUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(currentYearStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(currentYearStart, DateTimeKind.Utc);
        var currentYearEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(currentYearEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(currentYearEnd, DateTimeKind.Utc);
        var previousYearStartUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(previousYearStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(previousYearStart, DateTimeKind.Utc);
        var previousYearEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(previousYearEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(previousYearEnd, DateTimeKind.Utc);

        // Query and sum distances for current year
        var currentYearDistanceM = await db.Workouts
            .Where(w => w.StartedAt >= currentYearStartUtc && w.StartedAt <= currentYearEndUtc)
            .AsNoTracking()
            .SumAsync(w => (double?)w.DistanceM) ?? 0.0;

        // Query and sum distances for previous year
        var previousYearDistanceM = await db.Workouts
            .Where(w => w.StartedAt >= previousYearStartUtc && w.StartedAt <= previousYearEndUtc)
            .AsNoTracking()
            .SumAsync(w => (double?)w.DistanceM) ?? 0.0;

        // Convert to miles
        var currentYearMiles = currentYearDistanceM / 1609.344;
        var previousYearMiles = previousYearDistanceM / 1609.344;

        return Results.Ok(new
        {
            currentYear = currentYearMiles,
            previousYear = previousYearMiles,
            currentYearLabel = currentYear.ToString(),
            previousYearLabel = previousYear.ToString()
        });
    }

    /// <summary>
    /// Get yearly weekly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="periodEndDate">End date of the period (YYYY-MM-DD format). If not provided, defaults to today.</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>52 equal week buckets within a 1-year period</returns>
    /// <remarks>
    /// Returns 52 equal week buckets within a 1-year period, covering all dates with no gaps or overlaps.
    /// If periodEndDate not provided, defaults to today (last 12 months ending today).
    /// Each bucket represents approximately 1/52 of the total period.
    /// </remarks>
    private static async Task<IResult> GetYearlyWeeklyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] string? periodEndDate = null,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current date in the specified timezone
        // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
        // To convert UTC to local: UTC + offset (since offset is already negative)
        var now = DateTime.UtcNow;
        if (timezoneOffsetMinutes.HasValue)
        {
            now = now.AddMinutes(timezoneOffsetMinutes.Value);
        }

        // Compute period bounds
        // If periodEndDate not provided, default to today (last 12 months ending today)
        // This ensures alignment with /stats/available-periods
        DateTime periodEnd;
        var today = now.Date;
        if (!string.IsNullOrEmpty(periodEndDate) && DateTime.TryParse(periodEndDate, out var parsedEndDate))
        {
            periodEnd = parsedEndDate.Date;
        }
        else
        {
            // Default to today (so the period is the last 12 months ending today)
            periodEnd = today;
        }

        // Period start = periodEnd.AddYears(-1).AddDays(1) (inclusive)
        // This gives us the last 12 months ending on periodEnd
        var periodStart = periodEnd.AddYears(-1).AddDays(1);
        
        // Calculate total days in period (365 or 366)
        var totalDays = (periodEnd - periodStart).Days + 1;

        // Convert period bounds to UTC for database queries
        var periodStartUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(periodStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
        var periodEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(periodEnd.AddMinutes(-timezoneOffsetMinutes.Value).AddDays(1).AddTicks(-1), DateTimeKind.Utc)
            : DateTime.SpecifyKind(periodEnd.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

        // Get all workouts in the period
        var workouts = await db.Workouts
            .Where(w => w.StartedAt >= periodStartUtc && w.StartedAt <= periodEndUtc)
            .AsNoTracking()
            .ToListAsync();

        // Initialize 52 week buckets (indexed 0-51)
        var weekBuckets = new Dictionary<int, double>();
        for (int i = 0; i < 52; i++)
        {
            weekBuckets[i] = 0.0;
        }

        // Group workouts by week index
        foreach (var workout in workouts)
        {
            // Convert workout date to local timezone for calculation
            var workoutDate = workout.StartedAt;
            if (timezoneOffsetMinutes.HasValue)
            {
                workoutDate = workoutDate.AddMinutes(timezoneOffsetMinutes.Value);
            }
            var workoutDateOnly = workoutDate.Date;

            // Calculate dayIndex (0-based integer number of days from periodStart)
            var dayIndex = (workoutDateOnly - periodStart).Days;

            // Calculate weekIndex (0-51)
            var weekIndex = (int)Math.Floor(dayIndex * 52.0 / totalDays);
            
            // Clamp to valid range (shouldn't be necessary, but safety check)
            if (weekIndex < 0) weekIndex = 0;
            if (weekIndex >= 52) weekIndex = 51;

            // Update bucket distance
            weekBuckets[weekIndex] += workout.DistanceM;
        }

        // Build response with 52 weeks
        // Calculate theoretical date range for each bucket to ensure complete coverage
        var weeks = new List<object>();
        for (int weekIndex = 0; weekIndex < 52; weekIndex++)
        {
            var distanceM = weekBuckets[weekIndex];
            
            // Calculate theoretical date range for this bucket
            // This ensures every date in the period is covered exactly once
            var dayIndexStart = (int)Math.Floor(weekIndex * totalDays / 52.0);
            var dayIndexEnd = (int)Math.Floor((weekIndex + 1) * totalDays / 52.0) - 1;
            
            // Ensure dayIndexEnd doesn't exceed totalDays - 1
            if (dayIndexEnd >= totalDays)
            {
                dayIndexEnd = totalDays - 1;
            }
            
            var weekStartDate = periodStart.AddDays(dayIndexStart);
            var weekEndDate = periodStart.AddDays(dayIndexEnd);

            weeks.Add(new
            {
                weekNumber = weekIndex + 1, // 1-52
                weekStart = weekStartDate.ToString("yyyy-MM-dd"),
                weekEnd = weekEndDate.ToString("yyyy-MM-dd"),
                distanceM = distanceM
            });
        }

        return Results.Ok(new
        {
            weeks,
            dateRangeStart = periodStart.ToString("yyyy-MM-dd"),
            dateRangeEnd = periodEnd.ToString("yyyy-MM-dd")
        });
    }

    /// <summary>
    /// Get available periods
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Consecutive 1-year periods going backwards from today</returns>
    /// <remarks>
    /// Returns consecutive 1-year periods (365/366 days) going backwards from today.
    /// Current period is the last 12 months ending today. Stops when reaching the first workout date
    /// or after 20 periods (safety limit).
    /// </remarks>
    private static async Task<IResult> GetAvailablePeriods(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current date in the specified timezone
        // We need to get today's date in the local timezone, not UTC
        // The date should be the calendar date in the user's timezone, not UTC
        // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
        // For example, EST (UTC-5) sends -300, PST (UTC-8) sends -480
        // To convert UTC to local: UTC + offset (since offset is already negative)
        var nowUtc = DateTime.UtcNow;
        DateTime today;
        if (timezoneOffsetMinutes.HasValue)
        {
            // Convert UTC to local time by adding the offset
            // timezoneOffsetMinutes is already negative for timezones behind UTC (e.g., EST = -300)
            // So we add the negative number, which subtracts time (correct conversion)
            var localDateTime = nowUtc.AddMinutes(timezoneOffsetMinutes.Value);
            // Get just the date part (midnight of that day in local time)
            today = localDateTime.Date;
        }
        else
        {
            // No timezone offset provided, use UTC date
            today = nowUtc.Date;
        }
        
        // Ensure today is a date at midnight (no time component)
        today = today.Date;
        
        // Log the calculated today date for debugging
        logger.LogDebug("Calculated today's date: {Today} (UTC now: {UtcNow}, timezone offset: {Offset})", 
            today.ToString("yyyy-MM-dd"), nowUtc.ToString("yyyy-MM-dd HH:mm:ss"), 
            timezoneOffsetMinutes?.ToString() ?? "none");

        // Get first workout date to know when to stop
        var firstWorkout = await db.Workouts
            .AsNoTracking()
            .OrderBy(w => w.StartedAt)
            .FirstOrDefaultAsync();

        DateTime? firstWorkoutDate = null;
        if (firstWorkout != null)
        {
            firstWorkoutDate = firstWorkout.StartedAt;
            if (timezoneOffsetMinutes.HasValue)
            {
                firstWorkoutDate = firstWorkoutDate.Value.AddMinutes(timezoneOffsetMinutes.Value);
            }
        }

        // Generate consecutive 1-year periods going backwards from today
        // Current period: End = today (inclusive), Start = today.AddYears(-1).AddDays(1) (last 12 months ending today)
        // Previous periods: For older period N with newer period N+1:
        //   End(N) = Start(N+1).AddDays(-1)
        //   Start(N) = End(N).AddYears(-1).AddDays(1)
        // Example: If today = Nov 18, 2025:
        //   Period 1: Nov 19, 2024 - Nov 18, 2025
        //   Period 2: Nov 19, 2023 - Nov 18, 2024
        //   Period 3: Nov 19, 2022 - Nov 18, 2023
        var periods = new List<object>();
        var currentPeriodEnd = today;
        var currentPeriodStart = today.AddYears(-1).AddDays(1);
        
        // Log first period for debugging
        logger.LogDebug("First period (last 12 months ending today): {Start} - {End}", currentPeriodStart.ToString("yyyy-MM-dd"), currentPeriodEnd.ToString("yyyy-MM-dd"));
        
        while (true)
        {
            periods.Add(new
            {
                periodEndDate = currentPeriodEnd.ToString("yyyy-MM-dd"),
                dateRangeStart = currentPeriodStart.ToString("yyyy-MM-dd"),
                dateRangeEnd = currentPeriodEnd.ToString("yyyy-MM-dd"),
                dateRangeLabel = $"{currentPeriodStart:MMM d, yyyy} - {currentPeriodEnd:MMM d, yyyy}"
            });
            
            // Calculate previous period
            // Previous period ends 1 day before current period starts
            var previousPeriodEnd = currentPeriodStart.AddDays(-1);
            // Previous period starts 1 year before its end date (so it's exactly 1 year)
            var previousPeriodStart = previousPeriodEnd.AddYears(-1).AddDays(1);
            
            // Stop if we've generated more than 20 periods (safety limit)
            if (periods.Count > 20)
            {
                break;
            }
            
            // Stop if we've gone past the first workout date
            if (firstWorkoutDate.HasValue && previousPeriodEnd.Date < firstWorkoutDate.Value.Date)
            {
                break;
            }
            
            currentPeriodStart = previousPeriodStart;
            currentPeriodEnd = previousPeriodEnd;
        }

        return Results.Ok(periods);
    }

    /// <summary>
    /// Get available years
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of years that have workouts</returns>
    /// <remarks>
    /// Returns a list of distinct years (in descending order) that have workouts in the database.
    /// </remarks>
    private static async Task<IResult> GetAvailableYears(
        TempoDbContext db,
        ILogger<Program> logger)
    {
        // Get distinct years from workouts
        var years = await db.Workouts
            .AsNoTracking()
            .Select(w => w.StartedAt.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return Results.Ok(years);
    }

    public static void MapStatsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/stats")
            .WithTags("Stats")
            .RequireAuthorization();

        group.MapGet("/weekly", GetWeeklyStats)
            .WithName("GetWeeklyStats")
            .Produces(200)
            .WithSummary("Get weekly stats")
            .WithDescription("Returns daily miles for the current week (Monday-Sunday), grouped by day of week");

        group.MapGet("/relative-effort", GetRelativeEffortStats)
            .WithName("GetRelativeEffortStats")
            .Produces(200)
            .WithSummary("Get relative effort stats")
            .WithDescription("Returns cumulative relative effort for the current week and 3-week average range");

        group.MapGet("/best-efforts", GetBestEfforts)
            .WithName("GetBestEfforts")
            .Produces(200)
            .WithSummary("Get best efforts")
            .WithDescription("Returns the fastest time achieved for each standard running distance (400m through Marathon). Best efforts are calculated from any segment within any workout.");

        group.MapPost("/best-efforts/recalculate", RecalculateBestEfforts)
            .WithName("RecalculateBestEfforts")
            .Produces(200)
            .Produces(500)
            .WithSummary("Recalculate best efforts")
            .WithDescription("Performs a full recalculation of all best efforts across all workouts. This may take some time depending on the number of workouts.");

        group.MapGet("/yearly", GetYearlyStats)
            .WithName("GetYearlyStats")
            .Produces(200)
            .WithSummary("Get yearly stats")
            .WithDescription("Returns total miles for the current year and previous year");

        group.MapGet("/yearly-weekly", GetYearlyWeeklyStats)
            .WithName("GetYearlyWeeklyStats")
            .Produces(200)
            .WithSummary("Get yearly weekly stats")
            .WithDescription("Returns 52 equal week buckets within a 1-year period, covering all dates with no gaps or overlaps. If periodEndDate not provided, defaults to today (last 12 months ending today).");

        group.MapGet("/available-periods", GetAvailablePeriods)
            .WithName("GetAvailablePeriods")
            .Produces(200)
            .WithSummary("Get available periods")
            .WithDescription("Returns consecutive 1-year periods (365/366 days) going backwards from today. Current period is the last 12 months ending today.");

        group.MapGet("/available-years", GetAvailableYears)
            .WithName("GetAvailableYears")
            .Produces(200)
            .WithSummary("Get available years")
            .WithDescription("Returns list of years that have workouts");
    }
}
