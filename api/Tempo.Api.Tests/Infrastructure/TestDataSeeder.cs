using System.Text.Json;
using Microsoft.Data.Sqlite;
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
            AvgPaceS = durationS / (distanceM / 1000.0), // seconds per km
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
    /// Seeds a workout with route data (GeoJSON LineString)
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="workout">Existing workout entity (must be saved first)</param>
    /// <param name="coordinates">List of [longitude, latitude] coordinate pairs (default: simple route)</param>
    /// <returns>Created WorkoutRoute entity</returns>
    public static async Task<WorkoutRoute> SeedWorkoutWithRouteAsync(
        TempoDbContext db,
        Workout workout,
        List<double[]>? coordinates = null)
    {
        // Default coordinates: simple route from (0, 0) to (0.01, 0.01) with a few points
        if (coordinates == null || coordinates.Count == 0)
        {
            coordinates = new List<double[]>
            {
                new[] { 0.0, 0.0 },      // Start
                new[] { 0.002, 0.002 },  // Intermediate
                new[] { 0.005, 0.005 },  // Intermediate
                new[] { 0.008, 0.008 },  // Intermediate
                new[] { 0.01, 0.01 }      // End
            };
        }

        var routeGeoJson = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = coordinates
        });

        var route = new WorkoutRoute
        {
            WorkoutId = workout.Id,
            RouteGeoJson = routeGeoJson
        };

        db.WorkoutRoutes.Add(route);
        await db.SaveChangesAsync();
        return route;
    }

    /// <summary>
    /// Seeds a workout with splits
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="workout">Existing workout entity (must be saved first)</param>
    /// <param name="splitDistanceM">Distance per split in meters (default: 1000m / 1km)</param>
    /// <param name="totalDistanceM">Total distance in meters (default: uses workout.DistanceM)</param>
    /// <param name="totalDurationS">Total duration in seconds (default: uses workout.DurationS)</param>
    /// <returns>List of created WorkoutSplit entities</returns>
    public static async Task<List<WorkoutSplit>> SeedWorkoutWithSplitsAsync(
        TempoDbContext db,
        Workout workout,
        double splitDistanceM = 1000.0,
        double? totalDistanceM = null,
        int? totalDurationS = null)
    {
        var distance = totalDistanceM ?? workout.DistanceM;
        var duration = totalDurationS ?? workout.DurationS;
        var numSplits = (int)Math.Floor(distance / splitDistanceM);
        var splits = new List<WorkoutSplit>();

        for (int i = 0; i < numSplits; i++)
        {
            var splitDistance = splitDistanceM;
            var splitDuration = (int)(duration * (splitDistanceM / distance));
            var splitPace = splitDuration / (splitDistance / 1000.0); // seconds per km

            splits.Add(new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Idx = i,
                DistanceM = splitDistance,
                DurationS = splitDuration,
                PaceS = splitPace
            });
        }

        // Add final partial split if there's remaining distance
        var remainingDistance = distance - (numSplits * splitDistanceM);
        if (remainingDistance > 10) // Only add if significant (>10m)
        {
            var remainingDuration = (int)(duration * (remainingDistance / distance));
            var remainingPace = remainingDistance > 0 ? remainingDuration / (remainingDistance / 1000.0) : 0;

            splits.Add(new WorkoutSplit
            {
                WorkoutId = workout.Id,
                Idx = numSplits,
                DistanceM = remainingDistance,
                DurationS = remainingDuration,
                PaceS = remainingPace
            });
        }

        db.WorkoutSplits.AddRange(splits);
        await db.SaveChangesAsync();
        return splits;
    }

    /// <summary>
    /// Seeds a workout with time-series data
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="workout">Existing workout entity (must be saved first)</param>
    /// <param name="intervalSeconds">Interval between time-series points in seconds (default: 10)</param>
    /// <param name="totalDurationS">Total duration in seconds (default: uses workout.DurationS)</param>
    /// <param name="includeHeartRate">Whether to include heart rate data (default: true)</param>
    /// <param name="includeCadence">Whether to include cadence data (default: true)</param>
    /// <param name="includePower">Whether to include power data (default: false)</param>
    /// <returns>List of created WorkoutTimeSeries entities</returns>
    public static async Task<List<WorkoutTimeSeries>> SeedWorkoutWithTimeSeriesAsync(
        TempoDbContext db,
        Workout workout,
        int intervalSeconds = 10,
        int? totalDurationS = null,
        bool includeHeartRate = true,
        bool includeCadence = true,
        bool includePower = false)
    {
        var duration = totalDurationS ?? workout.DurationS;
        var timeSeries = new List<WorkoutTimeSeries>();
        var numPoints = duration / intervalSeconds;

        for (int i = 0; i <= numPoints; i++)
        {
            var elapsedSeconds = i * intervalSeconds;
            if (elapsedSeconds > duration) break;

            var point = new WorkoutTimeSeries
            {
                WorkoutId = workout.Id,
                ElapsedSeconds = elapsedSeconds,
                DistanceM = workout.DistanceM * (elapsedSeconds / (double)duration),
                ElevationM = 100.0 + (i * 0.5) // Simple elevation profile
            };

            if (includeHeartRate)
            {
                // Simulate heart rate: starts at 120, increases to 160, then decreases
                var hrBase = 120;
                var hrVariation = (int)(40 * Math.Sin(elapsedSeconds / (double)duration * Math.PI));
                point.HeartRateBpm = (byte)Math.Clamp(hrBase + hrVariation, 60, 200);
            }

            if (includeCadence)
            {
                // Simulate cadence: varies between 160-180
                point.CadenceRpm = (byte)(160 + (i % 20));
            }

            if (includePower)
            {
                // Simulate power: varies between 200-300 watts
                point.PowerWatts = (ushort)(200 + (i % 100));
            }

            timeSeries.Add(point);
        }

        db.WorkoutTimeSeries.AddRange(timeSeries);
        await db.SaveChangesAsync();
        return timeSeries;
    }

    /// <summary>
    /// Seeds a workout with media files
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="workout">Existing workout entity (must be saved first)</param>
    /// <param name="mediaDirectory">Directory path for media files (must exist)</param>
    /// <param name="count">Number of media files to create (default: 1)</param>
    /// <returns>List of created WorkoutMedia entities</returns>
    public static async Task<List<WorkoutMedia>> SeedWorkoutWithMediaAsync(
        TempoDbContext db,
        Workout workout,
        string mediaDirectory,
        int count = 1)
    {
        var mediaFiles = new List<WorkoutMedia>();

        for (int i = 0; i < count; i++)
        {
            var filename = $"test-photo-{workout.Id}-{i}.jpg";
            var filePath = Path.Combine(mediaDirectory, workout.Id.ToString(), filename);

            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

            // Create a dummy file (empty file for testing)
            File.WriteAllText(filePath, "dummy image data");

            mediaFiles.Add(new WorkoutMedia
            {
                WorkoutId = workout.Id,
                Filename = filename,
                FilePath = filePath,
                MimeType = "image/jpeg",
                FileSizeBytes = 1024, // 1KB dummy file
                Caption = i == 0 ? "Test photo" : null,
                CreatedAt = DateTime.UtcNow
            });
        }

        db.WorkoutMedia.AddRange(mediaFiles);
        await db.SaveChangesAsync();
        return mediaFiles;
    }

    /// <summary>
    /// Seeds a complete workout with all related data (route, splits, time series)
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="userId">User ID (optional, for future use)</param>
    /// <param name="shoeId">Shoe ID (optional)</param>
    /// <param name="startedAt">Workout start time (default: 1 hour ago)</param>
    /// <param name="distanceM">Distance in meters (default: 5000m / 5km)</param>
    /// <param name="durationS">Duration in seconds (default: 1800s / 30 minutes)</param>
    /// <param name="name">Workout name (optional)</param>
    /// <param name="includeRoute">Whether to include route data (default: true)</param>
    /// <param name="includeSplits">Whether to include splits (default: true)</param>
    /// <param name="includeTimeSeries">Whether to include time-series data (default: true)</param>
    /// <param name="splitDistanceM">Distance per split in meters (default: 1000m)</param>
    /// <returns>Created Workout entity with all related data</returns>
    public static async Task<Workout> SeedWorkoutCompleteAsync(
        TempoDbContext db,
        Guid? userId = null,
        Guid? shoeId = null,
        DateTime? startedAt = null,
        double distanceM = 5000.0,
        int durationS = 1800,
        string? name = null,
        bool includeRoute = true,
        bool includeSplits = true,
        bool includeTimeSeries = true,
        double splitDistanceM = 1000.0)
    {
        // Create the base workout
        var workout = await SeedWorkoutAsync(db, userId, shoeId, startedAt, distanceM, durationS, name);

        // Add route if requested
        if (includeRoute)
        {
            await SeedWorkoutWithRouteAsync(db, workout);
        }

        // Add splits if requested
        if (includeSplits)
        {
            await SeedWorkoutWithSplitsAsync(db, workout, splitDistanceM, distanceM, durationS);
        }

        // Add time-series if requested
        if (includeTimeSeries)
        {
            await SeedWorkoutWithTimeSeriesAsync(db, workout, intervalSeconds: 10, totalDurationS: durationS);
        }

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

    /// <summary>
    /// Safely clears all test data from the database using raw SQL, handling missing tables gracefully
    /// This is useful for cleanup in integration tests where tables might not exist yet
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="preserveUsers">Whether to preserve users (default: true, needed for authentication)</param>
    public static async Task SafeClearAllDataAsync(TempoDbContext db, bool preserveUsers = true)
    {
        // Use a transaction to ensure atomic cleanup
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            // Delete in order to respect foreign key constraints
            // Catch SqliteException for "no such table" errors and ignore them
            await SafeDeleteFromTableAsync(db, "WorkoutTimeSeries");
            await SafeDeleteFromTableAsync(db, "WorkoutSplits");
            await SafeDeleteFromTableAsync(db, "WorkoutMedia");
            await SafeDeleteFromTableAsync(db, "BestEfforts");
            await SafeDeleteFromTableAsync(db, "WorkoutRoutes");
            await SafeDeleteFromTableAsync(db, "Workouts");
            await SafeDeleteFromTableAsync(db, "UserSettings");
            await SafeDeleteFromTableAsync(db, "Shoes");
            
            if (!preserveUsers)
            {
                await SafeDeleteFromTableAsync(db, "Users");
            }
            
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Safely deletes all rows from a table, ignoring "no such table" errors
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="tableName">Name of the table to delete from</param>
    private static async Task SafeDeleteFromTableAsync(TempoDbContext db, string tableName)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName}");
        }
        catch (SqliteException ex) when (ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
        {
            // Table doesn't exist yet, nothing to clean up - this is fine
            // This can happen when the database schema hasn't been created yet
        }
    }
}
