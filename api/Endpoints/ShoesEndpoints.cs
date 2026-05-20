using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class ShoesEndpoints
{
    /// <summary>
    /// List shoes with calculated mileage (active by default).
    /// </summary>
    /// <param name="status">Filter: active (default), retired, or all</param>
    /// <param name="db">Database context</param>
    /// <param name="mileageService">Shoe mileage service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of shoes with total mileage</returns>
    private static async Task<IResult> GetShoes(
        string? status,
        TempoDbContext db,
        ShoeMileageService mileageService,
        ILogger<Program> logger)
    {
        try
        {
            var filter = (status ?? "active").Trim().ToLowerInvariant();
            if (filter is not ("active" or "retired" or "all"))
            {
                return Results.BadRequest(new { error = "status must be active, retired, or all" });
            }

            var query = db.Shoes.AsQueryable();
            query = filter switch
            {
                "active" => query.Where(s => !s.IsRetired),
                "retired" => query.Where(s => s.IsRetired),
                _ => query
            };

            var shoes = await query
                .OrderBy(s => s.Brand)
                .ThenBy(s => s.Model)
                .ToListAsync();

            // Get unit preference from settings
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";

            var shoesWithMileage = new List<object>();
            foreach (var shoe in shoes)
            {
                var totalMileage = await mileageService.GetTotalMileageAsync(db, shoe.Id, unitPreference);
                shoesWithMileage.Add(new
                {
                    id = shoe.Id,
                    brand = shoe.Brand,
                    model = shoe.Model,
                    initialMileageM = shoe.InitialMileageM,
                    isRetired = shoe.IsRetired,
                    totalMileage = totalMileage,
                    unit = unitPreference == "imperial" ? "miles" : "km",
                    createdAt = shoe.CreatedAt,
                    updatedAt = shoe.UpdatedAt
                });
            }

            return Results.Ok(shoesWithMileage);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting shoes");
            return Results.Problem("Failed to retrieve shoes");
        }
    }

    /// <summary>
    /// Create a new shoe
    /// </summary>
    /// <param name="request">Create shoe request</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Created shoe</returns>
    private static async Task<IResult> CreateShoe(
        [FromBody] CreateShoeRequest request,
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Brand))
            {
                return Results.BadRequest(new { error = "Brand is required" });
            }

            if (string.IsNullOrWhiteSpace(request.Model))
            {
                return Results.BadRequest(new { error = "Model is required" });
            }

            if (request.Brand.Length > 100)
            {
                return Results.BadRequest(new { error = "Brand must be 100 characters or less" });
            }

            if (request.Model.Length > 100)
            {
                return Results.BadRequest(new { error = "Model must be 100 characters or less" });
            }

            if (request.InitialMileageM.HasValue && request.InitialMileageM.Value < 0)
            {
                return Results.BadRequest(new { error = "Initial mileage cannot be negative" });
            }

            var shoe = new Shoe
            {
                Brand = request.Brand.Trim(),
                Model = request.Model.Trim(),
                InitialMileageM = request.InitialMileageM,
                IsRetired = request.IsRetired ?? false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Shoes.Add(shoe);
            await db.SaveChangesAsync();

            logger.LogInformation("Created shoe {ShoeId}: {Brand} {Model}", shoe.Id, LogSanitizer.Sanitize(shoe.Brand), LogSanitizer.Sanitize(shoe.Model));

            return Results.Ok(new
            {
                id = shoe.Id,
                brand = shoe.Brand,
                model = shoe.Model,
                initialMileageM = shoe.InitialMileageM,
                isRetired = shoe.IsRetired,
                createdAt = shoe.CreatedAt,
                updatedAt = shoe.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating shoe");
            return Results.Problem("Failed to create shoe");
        }
    }

    /// <summary>
    /// Update a shoe
    /// </summary>
    /// <param name="id">Shoe ID</param>
    /// <param name="request">Update shoe request</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated shoe</returns>
    private static async Task<IResult> UpdateShoe(
        Guid id,
        HttpRequest request,
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            // Parse JSON body to check which properties are provided
            JsonDocument? jsonDoc;
            try
            {
                jsonDoc = await JsonDocument.ParseAsync(request.Body);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to parse update request body");
                return Results.BadRequest(new { error = "Invalid request body" });
            }

            if (jsonDoc == null)
            {
                return Results.BadRequest(new { error = "Request body is required" });
            }

            using (jsonDoc)
            {
                var root = jsonDoc.RootElement;

            var shoe = await db.Shoes.FindAsync(id);
            if (shoe == null)
            {
                return Results.NotFound(new { error = "Shoe not found" });
            }

            // Update Brand if provided
            if (root.TryGetProperty("brand", out var brandElement))
            {
                string? brandValue = null;
                if (brandElement.ValueKind == JsonValueKind.String)
                {
                    brandValue = brandElement.GetString();
                }
                else if (brandElement.ValueKind == JsonValueKind.Null)
                {
                    return Results.BadRequest(new { error = "Brand cannot be null" });
                }
                else
                {
                    return Results.BadRequest(new { error = "Brand must be a string" });
                }

                if (string.IsNullOrWhiteSpace(brandValue))
                {
                    return Results.BadRequest(new { error = "Brand cannot be empty" });
                }
                if (brandValue.Length > 100)
                {
                    return Results.BadRequest(new { error = "Brand must be 100 characters or less" });
                }
                shoe.Brand = brandValue.Trim();
            }

            // Update Model if provided
            if (root.TryGetProperty("model", out var modelElement))
            {
                string? modelValue = null;
                if (modelElement.ValueKind == JsonValueKind.String)
                {
                    modelValue = modelElement.GetString();
                }
                else if (modelElement.ValueKind == JsonValueKind.Null)
                {
                    return Results.BadRequest(new { error = "Model cannot be null" });
                }
                else
                {
                    return Results.BadRequest(new { error = "Model must be a string" });
                }

                if (string.IsNullOrWhiteSpace(modelValue))
                {
                    return Results.BadRequest(new { error = "Model cannot be empty" });
                }
                if (modelValue.Length > 100)
                {
                    return Results.BadRequest(new { error = "Model must be 100 characters or less" });
                }
                shoe.Model = modelValue.Trim();
            }

            // Update InitialMileageM if provided (including explicit null to clear the value)
            if (root.TryGetProperty("initialMileageM", out var initialMileageElement))
            {
                double? initialMileageValue = null;
                if (initialMileageElement.ValueKind == JsonValueKind.Number)
                {
                    if (initialMileageElement.TryGetDouble(out var doubleValue))
                    {
                        if (doubleValue < 0)
                        {
                            return Results.BadRequest(new { error = "Initial mileage cannot be negative" });
                        }
                        initialMileageValue = doubleValue;
                    }
                    else
                    {
                        return Results.BadRequest(new { error = "Initial mileage must be a valid number" });
                    }
                }
                else if (initialMileageElement.ValueKind == JsonValueKind.Null)
                {
                    initialMileageValue = null; // Explicitly clear the value
                }
                else
                {
                    return Results.BadRequest(new { error = "Initial mileage must be a number or null" });
                }

                shoe.InitialMileageM = initialMileageValue;
            }

            // Update IsRetired if provided
            if (root.TryGetProperty("isRetired", out var isRetiredElement))
            {
                if (isRetiredElement.ValueKind != JsonValueKind.True && isRetiredElement.ValueKind != JsonValueKind.False)
                {
                    return Results.BadRequest(new { error = "isRetired must be a boolean" });
                }

                var newRetired = isRetiredElement.GetBoolean();
                if (newRetired && !shoe.IsRetired)
                {
                    var settingsForRetire = await db.UserSettings.FirstOrDefaultAsync();
                    if (settingsForRetire != null && settingsForRetire.DefaultShoeId == id)
                    {
                        settingsForRetire.DefaultShoeId = null;
                        settingsForRetire.UpdatedAt = DateTime.UtcNow;
                    }
                }

                shoe.IsRetired = newRetired;
            }

            shoe.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            logger.LogInformation("Updated shoe {ShoeId}: {Brand} {Model}", shoe.Id, LogSanitizer.Sanitize(shoe.Brand), LogSanitizer.Sanitize(shoe.Model));

            return Results.Ok(new
            {
                id = shoe.Id,
                brand = shoe.Brand,
                model = shoe.Model,
                initialMileageM = shoe.InitialMileageM,
                isRetired = shoe.IsRetired,
                createdAt = shoe.CreatedAt,
                updatedAt = shoe.UpdatedAt
            });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating shoe");
            return Results.Problem("Failed to update shoe");
        }
    }

    /// <summary>
    /// Delete a shoe
    /// </summary>
    /// <param name="id">Shoe ID</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>No content</returns>
    /// <remarks>
    /// When a shoe is deleted, all assigned workouts' ShoeId is set to null (handled by database cascade).
    /// If the shoe is set as the default shoe, the default shoe is also cleared.
    /// </remarks>
    private static async Task<IResult> DeleteShoe(
        Guid id,
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var shoe = await db.Shoes.FindAsync(id);
            if (shoe == null)
            {
                return Results.NotFound(new { error = "Shoe not found" });
            }

            // Clear default shoe if this shoe is set as default
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings != null && settings.DefaultShoeId == id)
            {
                settings.DefaultShoeId = null;
                settings.UpdatedAt = DateTime.UtcNow;
            }

            // Delete the shoe (workouts will have ShoeId set to null via cascade)
            db.Shoes.Remove(shoe);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted shoe {ShoeId}: {Brand} {Model}", shoe.Id, LogSanitizer.Sanitize(shoe.Brand), LogSanitizer.Sanitize(shoe.Model));

            return Results.NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting shoe");
            return Results.Problem("Failed to delete shoe");
        }
    }

    /// <summary>
    /// Get calculated total mileage for a shoe
    /// </summary>
    /// <param name="id">Shoe ID</param>
    /// <param name="db">Database context</param>
    /// <param name="mileageService">Shoe mileage service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Total mileage in user's preferred units</returns>
    private static async Task<IResult> GetShoeMileage(
        Guid id,
        TempoDbContext db,
        ShoeMileageService mileageService,
        ILogger<Program> logger)
    {
        try
        {
            var shoe = await db.Shoes.FindAsync(id);
            if (shoe == null)
            {
                return Results.NotFound(new { error = "Shoe not found" });
            }

            var totalMileage = await mileageService.GetTotalMileageWithUserPreferenceAsync(db, id);
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";

            return Results.Ok(new
            {
                shoeId = id,
                totalMileage = totalMileage,
                unit = unitPreference == "imperial" ? "miles" : "km"
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting shoe mileage");
            return Results.Problem("Failed to get shoe mileage");
        }
    }

    public static void MapShoesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/shoes")
            .WithTags("Shoes")
            .RequireAuthorization();

        group.MapGet("", GetShoes)
            .WithName("GetShoes")
            .Produces(200)
            .Produces(400)
            .Produces(500)
            .WithSummary("List shoes")
            .WithDescription("Returns shoes with calculated total mileage. Query status=active (default), retired, or all.");

        group.MapPost("", CreateShoe)
            .WithName("CreateShoe")
            .Produces(200)
            .Produces(400)
            .Produces(500)
            .WithSummary("Create a new shoe")
            .WithDescription("Creates a new shoe with brand, model, optional initial mileage, and optional isRetired (defaults to false)");

        group.MapPatch("/{id:guid}", UpdateShoe)
            .WithName("UpdateShoe")
            .Produces(200)
            .Produces(400)
            .Produces(404)
            .Produces(500)
            .WithSummary("Update a shoe")
            .WithDescription("Updates shoe brand, model, initial mileage, and/or isRetired");

        group.MapDelete("/{id:guid}", DeleteShoe)
            .WithName("DeleteShoe")
            .Produces(204)
            .Produces(404)
            .Produces(500)
            .WithSummary("Delete a shoe")
            .WithDescription("Deletes a shoe and sets all assigned workouts' ShoeId to null");

        group.MapGet("/{id:guid}/mileage", GetShoeMileage)
            .WithName("GetShoeMileage")
            .Produces(200)
            .Produces(404)
            .Produces(500)
            .WithSummary("Get shoe mileage")
            .WithDescription("Returns the total calculated mileage for a shoe in user's preferred units");
    }

    /// <summary>
    /// Request model for creating a shoe
    /// </summary>
    public class CreateShoeRequest
    {
        /// <summary>
        /// Shoe brand (required, max 100 characters)
        /// </summary>
        public string Brand { get; set; } = string.Empty;

        /// <summary>
        /// Shoe model (required, max 100 characters)
        /// </summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>
        /// Initial mileage in meters (optional)
        /// </summary>
        public double? InitialMileageM { get; set; }

        /// <summary>
        /// Whether the shoe is retired (optional, defaults to false)
        /// </summary>
        public bool? IsRetired { get; set; }
    }

    /// <summary>
    /// Request model for updating a shoe
    /// </summary>
    public class UpdateShoeRequest
    {
        /// <summary>
        /// Shoe brand (optional, max 100 characters)
        /// </summary>
        public string? Brand { get; set; }

        /// <summary>
        /// Shoe model (optional, max 100 characters)
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Initial mileage in meters (optional)
        /// </summary>
        public double? InitialMileageM { get; set; }
    }
}

