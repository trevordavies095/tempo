using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Helper class for seeding test data
/// </summary>
public static class TestDataSeeder
{
    /// <summary>
    /// Seeds a test user in the database
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="username">Username (default: "testuser")</param>
    /// <param name="password">Plain text password (default: "Test123!")</param>
    /// <returns>Created User entity</returns>
    public static async Task<User> SeedUserAsync(
        TempoDbContext db,
        string username = "testuser",
        string password = "Test123!")
    {
        var passwordService = new PasswordService();
        var user = new User
        {
            Username = username,
            PasswordHash = passwordService.HashPassword(password),
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Seeds a test shoe in the database
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="brand">Shoe brand (default: "Nike")</param>
    /// <param name="model">Shoe model (default: "Pegasus")</param>
    /// <param name="initialMileage">Initial mileage in meters (optional)</param>
    /// <returns>Created Shoe entity</returns>
    public static async Task<Shoe> SeedShoeAsync(
        TempoDbContext db,
        string brand = "Nike",
        string model = "Pegasus",
        double? initialMileage = null)
    {
        var shoe = new Shoe
        {
            Brand = brand,
            Model = model,
            InitialMileageM = initialMileage,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Shoes.Add(shoe);
        await db.SaveChangesAsync();
        return shoe;
    }

    /// <summary>
    /// Seeds user settings in the database
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="userId">User ID (optional, for future use)</param>
    /// <param name="defaultShoeId">Default shoe ID (optional)</param>
    /// <param name="unitPreference">Unit preference: "metric" or "imperial" (default: "metric")</param>
    /// <param name="age">Age for heart rate calculation (default: 30)</param>
    /// <returns>Created UserSettings entity</returns>
    public static async Task<UserSettings> SeedUserSettingsAsync(
        TempoDbContext db,
        Guid? userId = null,
        Guid? defaultShoeId = null,
        string unitPreference = "metric",
        int age = 30)
    {
        // Calculate heart rate zones based on age (220 - age method)
        var maxHeartRate = 220 - age;
        var zone1Min = (int)(maxHeartRate * 0.50);
        var zone1Max = (int)(maxHeartRate * 0.60);
        var zone2Min = (int)(maxHeartRate * 0.60);
        var zone2Max = (int)(maxHeartRate * 0.70);
        var zone3Min = (int)(maxHeartRate * 0.70);
        var zone3Max = (int)(maxHeartRate * 0.80);
        var zone4Min = (int)(maxHeartRate * 0.80);
        var zone4Max = (int)(maxHeartRate * 0.90);
        var zone5Min = (int)(maxHeartRate * 0.90);
        var zone5Max = maxHeartRate;

        var settings = new UserSettings
        {
            CalculationMethod = HeartRateCalculationMethod.AgeBased,
            Age = age,
            Zone1MinBpm = zone1Min,
            Zone1MaxBpm = zone1Max,
            Zone2MinBpm = zone2Min,
            Zone2MaxBpm = zone2Max,
            Zone3MinBpm = zone3Min,
            Zone3MaxBpm = zone3Max,
            Zone4MinBpm = zone4Min,
            Zone4MaxBpm = zone4Max,
            Zone5MinBpm = zone5Min,
            Zone5MaxBpm = zone5Max,
            UnitPreference = unitPreference,
            DefaultShoeId = defaultShoeId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.UserSettings.Add(settings);
        await db.SaveChangesAsync();
        return settings;
    }

    /// <summary>
    /// Seeds a test workout in the database
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="userId">User ID (optional, for future use)</param>
    /// <param name="shoeId">Shoe ID (optional)</param>
    /// <param name="startedAt">Workout start time (default: 1 hour ago)</param>
    /// <param name="distanceM">Distance in meters (default: 5000m / 5km)</param>
    /// <param name="durationS">Duration in seconds (default: 1800s / 30 minutes)</param>
    /// <param name="name">Workout name (optional)</param>
    /// <returns>Created Workout entity</returns>
    public static async Task<Workout> SeedWorkoutAsync(
        TempoDbContext db,
        Guid? userId = null,
        Guid? shoeId = null,
        DateTime? startedAt = null,
        double distanceM = 5000.0,
        int durationS = 1800,
        string? name = null)
    {
        var workout = new Workout
        {
            StartedAt = startedAt ?? DateTime.UtcNow.AddHours(-1),
            DurationS = durationS,
            DistanceM = distanceM,
            AvgPaceS = (int)(durationS / (distanceM / 1000.0)), // seconds per km
            ShoeId = shoeId,
            Name = name ?? "Test Run",
            Source = "test",
            CreatedAt = DateTime.UtcNow
        };

        db.Workouts.Add(workout);
        await db.SaveChangesAsync();
        return workout;
    }

    /// <summary>
    /// Clears all test data from the database
    /// </summary>
    /// <param name="db">Database context</param>
    public static async Task ClearAllDataAsync(TempoDbContext db)
    {
        // Delete in order to respect foreign key constraints
        db.WorkoutTimeSeries.RemoveRange(db.WorkoutTimeSeries);
        db.WorkoutSplits.RemoveRange(db.WorkoutSplits);
        db.WorkoutMedia.RemoveRange(db.WorkoutMedia);
        db.WorkoutRoutes.RemoveRange(db.WorkoutRoutes);
        db.BestEfforts.RemoveRange(db.BestEfforts);
        db.Workouts.RemoveRange(db.Workouts);
        db.UserSettings.RemoveRange(db.UserSettings);
        db.Shoes.RemoveRange(db.Shoes);
        db.Users.RemoveRange(db.Users);
        
        await db.SaveChangesAsync();
    }
}
