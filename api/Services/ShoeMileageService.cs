using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;

namespace Tempo.Api.Services;

/// <summary>
/// Service for calculating shoe mileage from assigned workouts.
/// </summary>
public class ShoeMileageService
{
    /// <summary>
    /// Calculates total mileage for a shoe in the specified unit preference.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="shoeId">Shoe ID</param>
    /// <param name="unitPreference">Unit preference: "metric" or "imperial"</param>
    /// <returns>Total mileage in the requested units</returns>
    public async Task<double> GetTotalMileageAsync(
        TempoDbContext db,
        Guid shoeId,
        string unitPreference)
    {
        // Get the shoe with initial mileage
        var shoe = await db.Shoes.FindAsync(shoeId);
        if (shoe == null)
        {
            return 0.0;
        }

        // Sum all workout distances for this shoe
        var totalDistanceM = await db.Workouts
            .Where(w => w.ShoeId == shoeId)
            .SumAsync(w => (double?)w.DistanceM) ?? 0.0;

        // Add initial mileage
        var totalMeters = totalDistanceM + (shoe.InitialMileageM ?? 0.0);

        // Convert to requested units
        if (unitPreference?.Equals("imperial", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Convert meters to miles (1 mile = 1609.344 meters)
            return totalMeters / 1609.344;
        }
        else
        {
            // Convert meters to kilometers (1 km = 1000 meters)
            return totalMeters / 1000.0;
        }
    }

    /// <summary>
    /// Gets total mileage for a shoe using the user's unit preference from settings.
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="shoeId">Shoe ID</param>
    /// <returns>Total mileage in user's preferred units</returns>
    public async Task<double> GetTotalMileageWithUserPreferenceAsync(
        TempoDbContext db,
        Guid shoeId)
    {
        var settings = await db.UserSettings.FirstOrDefaultAsync();
        var unitPreference = settings?.UnitPreference ?? "metric";
        return await GetTotalMileageAsync(db, shoeId, unitPreference);
    }
}

