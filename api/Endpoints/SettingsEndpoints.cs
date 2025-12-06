using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class SettingsEndpoints
{
    /// <summary>
    /// Calculates heart rate zones based on the request method and validates the input.
    /// Returns the zones and any validation error message.
    /// </summary>
    private static (List<HeartRateZone>? Zones, string? ErrorMessage) CalculateZonesFromRequest(
        UpdateHeartRateZonesRequest request,
        HeartRateZoneService zoneService)
    {
        if (!Enum.TryParse<HeartRateCalculationMethod>(request.CalculationMethod, true, out var method))
        {
            return (null, "Invalid calculation method");
        }

        List<HeartRateZone> zones;

        // Calculate zones based on method
        switch (method)
        {
            case HeartRateCalculationMethod.AgeBased:
                if (!request.Age.HasValue)
                {
                    return (null, "Age is required for AgeBased calculation method");
                }
                zones = zoneService.CalculateZonesFromAge(request.Age.Value);
                break;

            case HeartRateCalculationMethod.Karvonen:
                if (!request.MaxHeartRateBpm.HasValue || !request.RestingHeartRateBpm.HasValue)
                {
                    return (null, "Max heart rate and resting heart rate are required for Karvonen calculation method");
                }
                zones = zoneService.CalculateZonesFromKarvonen(
                    request.MaxHeartRateBpm.Value,
                    request.RestingHeartRateBpm.Value);
                break;

            case HeartRateCalculationMethod.Custom:
                if (request.Zones == null || request.Zones.Count != 5)
                {
                    return (null, "Exactly 5 zones are required for Custom method");
                }
                var validation = zoneService.ValidateCustomZones(request.Zones);
                if (!validation.IsValid)
                {
                    return (null, validation.ErrorMessage);
                }
                zones = request.Zones;
                break;

            default:
                return (null, "Invalid calculation method");
        }

        return (zones, null);
    }

    /// <summary>
    /// Get heart rate zones configuration
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Heart rate zones configuration with calculation method and zone boundaries</returns>
    /// <remarks>
    /// Returns the current heart rate zones configuration. If no settings exist, returns default zones
    /// calculated using age-based method with age 30 (max HR = 190).
    /// </remarks>
    private static async Task<IResult> GetHeartRateZones(
        TempoDbContext db,
        HeartRateZoneService zoneService,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// Update heart rate zones configuration
    /// </summary>
    /// <param name="request">Heart rate zones update request</param>
    /// <param name="db">Database context</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated heart rate zones configuration</returns>
    /// <remarks>
    /// Updates heart rate zones using one of three calculation methods:
    /// - AgeBased: Requires age, calculates max HR as 220 - age
    /// - Karvonen: Requires max HR and resting HR
    /// - Custom: Requires exactly 5 zones with valid boundaries
    /// </remarks>
    private static async Task<IResult> UpdateHeartRateZones(
        [FromBody] UpdateHeartRateZonesRequest request,
        TempoDbContext db,
        HeartRateZoneService zoneService,
        ILogger<Program> logger)
    {
        try
        {
            // Calculate zones based on method
            var (zones, errorMessage) = CalculateZonesFromRequest(request, zoneService);
            if (zones == null)
            {
                return Results.BadRequest(new { error = errorMessage ?? "Invalid calculation method" });
            }

            var method = Enum.Parse<HeartRateCalculationMethod>(request.CalculationMethod, true);

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
    }


    /// <summary>
    /// Update heart rate zones and optionally recalculate relative effort
    /// </summary>
    /// <param name="request">Heart rate zones update request with optional recalculation flag</param>
    /// <param name="db">Database context</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated heart rate zones and optional recalculation results</returns>
    /// <remarks>
    /// Updates heart rate zones and optionally recalculates relative effort for all qualifying workouts
    /// in one atomic operation. If RecalculateExisting is true, all workouts with heart rate data will
    /// have their relative effort recalculated using the new zones.
    /// </remarks>
    private static async Task<IResult> UpdateHeartRateZonesWithRecalc(
        [FromBody] UpdateHeartRateZonesWithRecalcRequest request,
        TempoDbContext db,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        ILogger<Program> logger)
    {
        try
        {
            // Calculate zones based on method
            // Convert UpdateHeartRateZonesWithRecalcRequest to UpdateHeartRateZonesRequest for the helper
            var baseRequest = new UpdateHeartRateZonesRequest
            {
                CalculationMethod = request.CalculationMethod,
                Age = request.Age,
                RestingHeartRateBpm = request.RestingHeartRateBpm,
                MaxHeartRateBpm = request.MaxHeartRateBpm,
                Zones = request.Zones
            };
            var (zones, errorMessage) = CalculateZonesFromRequest(baseRequest, zoneService);
            if (zones == null)
            {
                return Results.BadRequest(new { error = errorMessage ?? "Invalid calculation method" });
            }

            var method = Enum.Parse<HeartRateCalculationMethod>(request.CalculationMethod, true);

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
                    var allQualifyingIds = await relativeEffortService.GetQualifyingWorkoutIdsAsync(db);

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
    }

    /// <summary>
    /// Get unit preference
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Current unit preference (metric or imperial)</returns>
    /// <remarks>
    /// Returns the stored unit preference. Defaults to "metric" if no preference has been set.
    /// </remarks>
    private static async Task<IResult> GetUnitPreference(
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";
            
            return Results.Ok(new { unitPreference });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting unit preference");
            return Results.Problem("Failed to retrieve unit preference");
        }
    }

    /// <summary>
    /// Update unit preference
    /// </summary>
    /// <param name="request">Unit preference update request</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated unit preference</returns>
    /// <remarks>
    /// Updates the unit preference to either "metric" or "imperial". This affects how distances
    /// and splits are calculated and displayed throughout the application.
    /// </remarks>
    private static async Task<IResult> UpdateUnitPreference(
        [FromBody] UpdateUnitPreferenceRequest request,
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.UnitPreference) ||
                (request.UnitPreference != "metric" && request.UnitPreference != "imperial"))
            {
                return Results.BadRequest(new { error = "Unit preference must be 'metric' or 'imperial'" });
            }

            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings();
                db.UserSettings.Add(settings);
            }

            settings.UnitPreference = request.UnitPreference;
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            return Results.Ok(new { unitPreference = settings.UnitPreference });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating unit preference");
            return Results.Problem("Failed to update unit preference");
        }
    }


    public static void MapSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/settings")
            .WithTags("Settings")
            .RequireAuthorization();

        group.MapGet("/heart-rate-zones", GetHeartRateZones)
            .WithName("GetHeartRateZones")
            .Produces(200)
            .Produces(500)
            .WithSummary("Get heart rate zones")
            .WithDescription("Returns the current heart rate zones configuration. If no settings exist, returns default zones calculated using age-based method with age 30.");

        group.MapPut("/heart-rate-zones", UpdateHeartRateZones)
            .WithName("UpdateHeartRateZones")
            .Produces(200)
            .Produces(400)
            .Produces(500)
            .WithSummary("Update heart rate zones")
            .WithDescription("Updates heart rate zones using one of three calculation methods: AgeBased, Karvonen, or Custom.");

        group.MapPost("/heart-rate-zones/update-with-recalc", UpdateHeartRateZonesWithRecalc)
            .WithName("UpdateHeartRateZonesWithRecalc")
            .Produces(200)
            .Produces(400)
            .WithSummary("Update heart rate zones and optionally recalculate relative effort")
            .WithDescription("Updates heart rate zones and optionally recalculates relative effort for all qualifying workouts in one atomic operation");

        group.MapGet("/unit-preference", GetUnitPreference)
            .WithName("GetUnitPreference")
            .Produces(200)
            .WithSummary("Get unit preference")
            .WithDescription("Returns the stored unit preference (metric or imperial), defaults to metric");

        group.MapPut("/unit-preference", UpdateUnitPreference)
            .WithName("UpdateUnitPreference")
            .Produces(200)
            .Produces(400)
            .WithSummary("Update unit preference")
            .WithDescription("Updates the unit preference (metric or imperial)");

        group.MapGet("/default-shoe", GetDefaultShoe)
            .WithName("GetDefaultShoe")
            .Produces(200)
            .WithSummary("Get default shoe")
            .WithDescription("Returns the current default shoe, or null if none is set");

        group.MapPut("/default-shoe", SetDefaultShoe)
            .WithName("SetDefaultShoe")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Set default shoe")
            .WithDescription("Sets the default shoe for automatic assignment to new workouts. Pass null to clear the default.");
    }

    /// <summary>
    /// Request model for updating heart rate zones
    /// </summary>
    public class UpdateHeartRateZonesRequest
    {
        /// <summary>
        /// Calculation method: "AgeBased", "Karvonen", or "Custom"
        /// </summary>
        public string CalculationMethod { get; set; } = string.Empty;

        /// <summary>
        /// Age (required for AgeBased method)
        /// </summary>
        public int? Age { get; set; }

        /// <summary>
        /// Resting heart rate in BPM (required for Karvonen method)
        /// </summary>
        public int? RestingHeartRateBpm { get; set; }

        /// <summary>
        /// Maximum heart rate in BPM (required for Karvonen method)
        /// </summary>
        public int? MaxHeartRateBpm { get; set; }

        /// <summary>
        /// Custom zones (required for Custom method, must contain exactly 5 zones)
        /// </summary>
        public List<HeartRateZone>? Zones { get; set; }
    }

    /// <summary>
    /// Request model for updating heart rate zones with optional recalculation
    /// </summary>
    public class UpdateHeartRateZonesWithRecalcRequest
    {
        /// <summary>
        /// Calculation method: "AgeBased", "Karvonen", or "Custom"
        /// </summary>
        public string CalculationMethod { get; set; } = string.Empty;

        /// <summary>
        /// Age (required for AgeBased method)
        /// </summary>
        public int? Age { get; set; }

        /// <summary>
        /// Resting heart rate in BPM (required for Karvonen method)
        /// </summary>
        public int? RestingHeartRateBpm { get; set; }

        /// <summary>
        /// Maximum heart rate in BPM (required for Karvonen method)
        /// </summary>
        public int? MaxHeartRateBpm { get; set; }

        /// <summary>
        /// Custom zones (required for Custom method, must contain exactly 5 zones)
        /// </summary>
        public List<HeartRateZone>? Zones { get; set; }

        /// <summary>
        /// If true, recalculates relative effort for all qualifying workouts after updating zones
        /// </summary>
        public bool? RecalculateExisting { get; set; }
    }

    /// <summary>
    /// Request model for updating unit preference
    /// </summary>
    public class UpdateUnitPreferenceRequest
    {
        /// <summary>
        /// Unit preference: "metric" or "imperial"
        /// </summary>
        public string UnitPreference { get; set; } = string.Empty;
    }

    /// <summary>
    /// Get default shoe
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Default shoe information or null</returns>
    private static async Task<IResult> GetDefaultShoe(
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var settings = await db.UserSettings
                .Include(s => s.DefaultShoe)
                .FirstOrDefaultAsync();

            if (settings == null || settings.DefaultShoeId == null)
            {
                return Results.Ok(new { defaultShoeId = (Guid?)null });
            }

            return Results.Ok(new
            {
                defaultShoeId = settings.DefaultShoeId,
                brand = settings.DefaultShoe?.Brand,
                model = settings.DefaultShoe?.Model
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting default shoe");
            return Results.Problem("Failed to retrieve default shoe");
        }
    }

    /// <summary>
    /// Set default shoe
    /// </summary>
    /// <param name="request">Set default shoe request</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated default shoe information</returns>
    private static async Task<IResult> SetDefaultShoe(
        [FromBody] SetDefaultShoeRequest request,
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings();
                db.UserSettings.Add(settings);
            }

            if (request.DefaultShoeId.HasValue)
            {
                // Validate that the shoe exists
                var shoe = await db.Shoes.FindAsync(request.DefaultShoeId.Value);
                if (shoe == null)
                {
                    return Results.NotFound(new { error = "Shoe not found" });
                }

                settings.DefaultShoeId = request.DefaultShoeId.Value;
            }
            else
            {
                // Clear default shoe
                settings.DefaultShoeId = null;
            }

            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            // Reload with navigation property
            await db.Entry(settings).Reference(s => s.DefaultShoe).LoadAsync();

            return Results.Ok(new
            {
                defaultShoeId = settings.DefaultShoeId,
                brand = settings.DefaultShoe?.Brand,
                model = settings.DefaultShoe?.Model
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error setting default shoe");
            return Results.Problem("Failed to set default shoe");
        }
    }

    /// <summary>
    /// Request model for setting default shoe
    /// </summary>
    public class SetDefaultShoeRequest
    {
        /// <summary>
        /// Shoe ID to set as default, or null to clear the default
        /// </summary>
        public Guid? DefaultShoeId { get; set; }
    }
}
