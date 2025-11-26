using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/settings")
            .WithTags("Settings");

        group.MapGet("/heart-rate-zones", async (
            TempoDbContext db,
            HeartRateZoneService zoneService,
            ILogger<Program> logger) =>
        {
            try
            {
                // Get the single settings record (or create default if none exists)
                var settings = await db.UserSettings.FirstOrDefaultAsync();

                if (settings == null)
                {
                    // Return default settings (220-age with age 30 as example)
                    var defaultAge = 30;
                    var defaultZones = zoneService.CalculateZonesFromAge(defaultAge);
                    var defaultMaxHr = 220 - defaultAge;

                    return Results.Ok(new
                    {
                        calculationMethod = HeartRateCalculationMethod.AgeBased.ToString(),
                        age = defaultAge,
                        restingHeartRateBpm = (int?)null,
                        maxHeartRateBpm = defaultMaxHr,
                        zones = defaultZones.Select((z, i) => new
                        {
                            zoneNumber = i + 1,
                            minBpm = z.MinBpm,
                            maxBpm = z.MaxBpm
                        }).ToArray()
                    });
                }

                var zones = zoneService.GetZonesFromUserSettings(settings);

                return Results.Ok(new
                {
                    calculationMethod = settings.CalculationMethod.ToString(),
                    age = settings.Age,
                    restingHeartRateBpm = settings.RestingHeartRateBpm,
                    maxHeartRateBpm = settings.MaxHeartRateBpm,
                    zones = zones.Select((z, i) => new
                    {
                        zoneNumber = i + 1,
                        minBpm = z.MinBpm,
                        maxBpm = z.MaxBpm
                    }).ToArray()
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting heart rate zones");
                return Results.Problem("Failed to retrieve heart rate zones");
            }
        });

        group.MapPut("/heart-rate-zones", async (
            [FromBody] UpdateHeartRateZonesRequest request,
            TempoDbContext db,
            HeartRateZoneService zoneService,
            ILogger<Program> logger) =>
        {
            try
            {
                // Validate request
                if (!Enum.TryParse<HeartRateCalculationMethod>(request.CalculationMethod, true, out var method))
                {
                    return Results.BadRequest(new { error = "Invalid calculation method" });
                }

                List<HeartRateZone> zones;

                // Calculate zones based on method
                switch (method)
                {
                    case HeartRateCalculationMethod.AgeBased:
                        if (!request.Age.HasValue)
                        {
                            return Results.BadRequest(new { error = "Age is required for AgeBased calculation method" });
                        }
                        zones = zoneService.CalculateZonesFromAge(request.Age.Value);
                        break;

                    case HeartRateCalculationMethod.Karvonen:
                        if (!request.MaxHeartRateBpm.HasValue || !request.RestingHeartRateBpm.HasValue)
                        {
                            return Results.BadRequest(new { error = "Max heart rate and resting heart rate are required for Karvonen calculation method" });
                        }
                        zones = zoneService.CalculateZonesFromKarvonen(
                            request.MaxHeartRateBpm.Value,
                            request.RestingHeartRateBpm.Value);
                        break;

                    case HeartRateCalculationMethod.Custom:
                        if (request.Zones == null || request.Zones.Count != 5)
                        {
                            return Results.BadRequest(new { error = "Exactly 5 zones are required for Custom method" });
                        }
                        var validation = zoneService.ValidateCustomZones(request.Zones);
                        if (!validation.IsValid)
                        {
                            return Results.BadRequest(new { error = validation.ErrorMessage });
                        }
                        zones = request.Zones;
                        break;

                    default:
                        return Results.BadRequest(new { error = "Invalid calculation method" });
                }

                // Get or create settings record
                var settings = await db.UserSettings.FirstOrDefaultAsync();
                var isFirstTimeSetup = settings == null;

                if (settings == null)
                {
                    settings = new UserSettings();
                    db.UserSettings.Add(settings);
                }

                // Update settings
                settings.CalculationMethod = method;
                settings.Age = request.Age;
                settings.RestingHeartRateBpm = request.RestingHeartRateBpm;
                settings.MaxHeartRateBpm = request.MaxHeartRateBpm;
                zoneService.ApplyZonesToUserSettings(settings, zones);
                settings.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    calculationMethod = settings.CalculationMethod.ToString(),
                    age = settings.Age,
                    restingHeartRateBpm = settings.RestingHeartRateBpm,
                    maxHeartRateBpm = settings.MaxHeartRateBpm,
                    zones = zones.Select((z, i) => new
                    {
                        zoneNumber = i + 1,
                        minBpm = z.MinBpm,
                        maxBpm = z.MaxBpm
                    }).ToArray(),
                    isFirstTimeSetup = isFirstTimeSetup
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid argument in heart rate zone calculation");
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating heart rate zones");
                return Results.Problem("Failed to update heart rate zones");
            }
        });

        group.MapGet("/recalculate-relative-effort/count", async (
            TempoDbContext db,
            ILogger<Program> logger) =>
        {
            try
            {
                // Get workouts that can have relative effort calculated:
                // 1. Workouts with time series data with heart rate
                // 2. Workouts with RawFitData (may contain avgHeartRate)
                // 3. Workouts with AvgHeartRateBpm
                var workoutIdsWithTimeSeries = await db.WorkoutTimeSeries
                    .Where(ts => ts.HeartRateBpm.HasValue)
                    .Select(ts => ts.WorkoutId)
                    .Distinct()
                    .ToListAsync();

                var workoutIdsWithRawFit = await db.Workouts
                    .Where(w => w.RawFitData != null)
                    .Select(w => w.Id)
                    .ToListAsync();

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

                return Results.Ok(new { count = allQualifyingIds.Count });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting qualifying workout count");
                return Results.Problem("Failed to get qualifying workout count");
            }
        })
        .Produces(200)
        .WithSummary("Get count of workouts eligible for relative effort recalculation")
        .WithDescription("Returns the number of workouts that have heart rate data (time series, raw FIT data, or average HR)");

        group.MapPost("/recalculate-relative-effort", async (
            TempoDbContext db,
            HeartRateZoneService zoneService,
            RelativeEffortService relativeEffortService,
            ILogger<Program> logger) =>
        {
            try
            {
                // Check if heart rate zones are configured
                var settings = await db.UserSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return Results.BadRequest(new { error = "Heart rate zones not configured. Please configure heart rate zones in settings first." });
                }

                var zones = zoneService.GetZonesFromUserSettings(settings);

                // Get workouts that can have relative effort calculated:
                // 1. Workouts with time series data with heart rate
                // 2. Workouts with RawFitData (may contain avgHeartRate)
                // 3. Workouts with AvgHeartRateBpm
                var workoutIdsWithTimeSeries = await db.WorkoutTimeSeries
                    .Where(ts => ts.HeartRateBpm.HasValue)
                    .Select(ts => ts.WorkoutId)
                    .Distinct()
                    .ToListAsync();

                var workoutIdsWithRawFit = await db.Workouts
                    .Where(w => w.RawFitData != null)
                    .Select(w => w.Id)
                    .ToListAsync();

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

                if (allQualifyingIds.Count == 0)
                {
                    return Results.Ok(new
                    {
                        updatedCount = 0,
                        totalQualifyingWorkouts = 0,
                        message = "No workouts with heart rate data found"
                    });
                }

                // Get all qualifying workouts
                var qualifyingWorkouts = await db.Workouts
                    .Where(w => allQualifyingIds.Contains(w.Id))
                    .ToListAsync();

                if (qualifyingWorkouts.Count == 0)
                {
                    return Results.Ok(new
                    {
                        updatedCount = 0,
                        message = "No workouts with time series heart rate data found"
                    });
                }

                int updatedCount = 0;
                int errorCount = 0;
                var errors = new List<string>();

                // Recalculate relative effort for each qualifying workout
                foreach (var workout in qualifyingWorkouts)
                {
                    try
                    {
                        var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                        if (relativeEffort.HasValue)
                        {
                            workout.RelativeEffort = relativeEffort.Value;
                            updatedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                        errors.Add($"Workout {workout.Id}: {ex.Message}");
                    }
                }

                // Save all changes
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    updatedCount = updatedCount,
                    totalQualifyingWorkouts = qualifyingWorkouts.Count,
                    errorCount = errorCount,
                    errors = errors.Count > 0 ? errors : null
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error recalculating Relative Effort for all workouts");
                return Results.Problem("Failed to recalculate Relative Effort for all workouts");
            }
        })
        .Produces(200)
        .Produces(400)
        .WithSummary("Recalculate relative effort for all qualifying workouts")
        .WithDescription("Recalculates relative effort for all workouts that have time series heart rate data using the current heart rate zone configuration");

        group.MapPost("/heart-rate-zones/update-with-recalc", async (
            [FromBody] UpdateHeartRateZonesWithRecalcRequest request,
            TempoDbContext db,
            HeartRateZoneService zoneService,
            RelativeEffortService relativeEffortService,
            ILogger<Program> logger) =>
        {
            try
            {
                // Validate request
                if (!Enum.TryParse<HeartRateCalculationMethod>(request.CalculationMethod, true, out var method))
                {
                    return Results.BadRequest(new { error = "Invalid calculation method" });
                }

                List<HeartRateZone> zones;

                // Calculate zones based on method
                switch (method)
                {
                    case HeartRateCalculationMethod.AgeBased:
                        if (!request.Age.HasValue)
                        {
                            return Results.BadRequest(new { error = "Age is required for AgeBased calculation method" });
                        }
                        zones = zoneService.CalculateZonesFromAge(request.Age.Value);
                        break;

                    case HeartRateCalculationMethod.Karvonen:
                        if (!request.MaxHeartRateBpm.HasValue || !request.RestingHeartRateBpm.HasValue)
                        {
                            return Results.BadRequest(new { error = "Max heart rate and resting heart rate are required for Karvonen calculation method" });
                        }
                        zones = zoneService.CalculateZonesFromKarvonen(
                            request.MaxHeartRateBpm.Value,
                            request.RestingHeartRateBpm.Value);
                        break;

                    case HeartRateCalculationMethod.Custom:
                        if (request.Zones == null || request.Zones.Count != 5)
                        {
                            return Results.BadRequest(new { error = "Exactly 5 zones are required for Custom method" });
                        }
                        var validation = zoneService.ValidateCustomZones(request.Zones);
                        if (!validation.IsValid)
                        {
                            return Results.BadRequest(new { error = validation.ErrorMessage });
                        }
                        zones = request.Zones;
                        break;

                    default:
                        return Results.BadRequest(new { error = "Invalid calculation method" });
                }

                // Get or create settings record
                var settings = await db.UserSettings.FirstOrDefaultAsync();
                var isFirstTimeSetup = settings == null;

                if (settings == null)
                {
                    settings = new UserSettings();
                    db.UserSettings.Add(settings);
                }

                // Update settings
                settings.CalculationMethod = method;
                settings.Age = request.Age;
                settings.RestingHeartRateBpm = request.RestingHeartRateBpm;
                settings.MaxHeartRateBpm = request.MaxHeartRateBpm;
                zoneService.ApplyZonesToUserSettings(settings, zones);
                settings.UpdatedAt = DateTime.UtcNow;

                await db.SaveChangesAsync();

                // If recalculateExisting is true, recalculate all workouts
                int? recalculatedCount = null;
                int? recalculatedErrorCount = null;
                if (request.RecalculateExisting == true)
                {
                    try
                    {
                        // Get workouts that can have relative effort calculated
                        var workoutIdsWithTimeSeries = await db.WorkoutTimeSeries
                            .Where(ts => ts.HeartRateBpm.HasValue)
                            .Select(ts => ts.WorkoutId)
                            .Distinct()
                            .ToListAsync();

                        var workoutIdsWithRawFit = await db.Workouts
                            .Where(w => w.RawFitData != null)
                            .Select(w => w.Id)
                            .ToListAsync();

                        var workoutIdsWithAvgHr = await db.Workouts
                            .Where(w => w.AvgHeartRateBpm.HasValue)
                            .Select(w => w.Id)
                            .ToListAsync();

                        var allQualifyingIds = workoutIdsWithTimeSeries
                            .Union(workoutIdsWithRawFit)
                            .Union(workoutIdsWithAvgHr)
                            .Distinct()
                            .ToList();

                        if (allQualifyingIds.Count > 0)
                        {
                            var qualifyingWorkouts = await db.Workouts
                                .Where(w => allQualifyingIds.Contains(w.Id))
                                .ToListAsync();

                            int updatedCount = 0;
                            int errorCount = 0;

                            foreach (var workout in qualifyingWorkouts)
                            {
                                try
                                {
                                    var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                                    if (relativeEffort.HasValue)
                                    {
                                        workout.RelativeEffort = relativeEffort.Value;
                                        updatedCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    errorCount++;
                                    logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                                }
                            }

                            await db.SaveChangesAsync();
                            recalculatedCount = updatedCount;
                            recalculatedErrorCount = errorCount;
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to recalculate Relative Effort during zone update");
                        // Continue - zones are saved, recalculation is optional
                    }
                }

                return Results.Ok(new
                {
                    calculationMethod = settings.CalculationMethod.ToString(),
                    age = settings.Age,
                    restingHeartRateBpm = settings.RestingHeartRateBpm,
                    maxHeartRateBpm = settings.MaxHeartRateBpm,
                    zones = zones.Select((z, i) => new
                    {
                        zoneNumber = i + 1,
                        minBpm = z.MinBpm,
                        maxBpm = z.MaxBpm
                    }).ToArray(),
                    isFirstTimeSetup = isFirstTimeSetup,
                    recalculatedCount = recalculatedCount,
                    recalculatedErrorCount = recalculatedErrorCount
                });
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Invalid argument in heart rate zone calculation");
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error updating heart rate zones with recalculation");
                return Results.Problem("Failed to update heart rate zones");
            }
        })
        .Produces(200)
        .Produces(400)
        .WithSummary("Update heart rate zones and optionally recalculate relative effort")
        .WithDescription("Updates heart rate zones and optionally recalculates relative effort for all qualifying workouts in one atomic operation");
    }

    public class UpdateHeartRateZonesRequest
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<HeartRateZone>? Zones { get; set; }
    }

    public class UpdateHeartRateZonesWithRecalcRequest
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<HeartRateZone>? Zones { get; set; }
        public bool? RecalculateExisting { get; set; }
    }
}

