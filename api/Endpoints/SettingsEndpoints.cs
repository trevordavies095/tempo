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
                    }).ToArray()
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
    }

    public class UpdateHeartRateZonesRequest
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<HeartRateZone>? Zones { get; set; }
    }
}

