using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for import/export flow
/// </summary>
[Collection("Integration Tests")]
public class ImportExportTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ImportExportTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    /// <summary>
    /// Helper method to ensure database is clean before a test (but preserves test user)
    /// </summary>
    private async Task EnsureCleanDatabaseAsync()
    {
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Clear all data except users (we need the test user for authentication)
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                // Delete in order to respect foreign key constraints
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutTimeSeries");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutSplits");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutMedia");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM BestEfforts");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM WorkoutRoutes");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Workouts");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM UserSettings");
                await db.Database.ExecuteSqlRawAsync("DELETE FROM Shoes");
                
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    [Fact]
    public async Task ImportExport_HappyPath_ImportsAllEntitiesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Seed comprehensive data
        Shoe? shoe1 = null;
        Shoe? shoe2 = null;
        Workout? workout1 = null;
        Workout? workout2 = null;
        Workout? workout3 = null;
        BestEffort? bestEffort1 = null;
        BestEffort? bestEffort2 = null;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var mediaConfig = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();

            // Create shoes
            shoe1 = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            shoe2 = await TestDataSeeder.SeedShoeAsync(db, "Adidas", "Ultraboost");

            // Create settings with default shoe
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: shoe1.Id);

            // Create workouts with all related data
            workout1 = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db, shoeId: shoe1.Id, distanceM: 5000, durationS: 1800, name: "Morning Run");
            workout2 = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db, shoeId: shoe2.Id, distanceM: 10000, durationS: 3600, name: "Evening Run");
            workout3 = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db, shoeId: null, distanceM: 3000, durationS: 1200, name: "Quick Run");

            // Add raw file data to workout1
            workout1.RawFileData = Encoding.UTF8.GetBytes("fake gpx data");
            workout1.RawFileName = "workout1.gpx";
            workout1.RawFileType = "gpx";
            await db.SaveChangesAsync();

            // Add media to workout1
            await TestDataSeeder.SeedWorkoutWithMediaAsync(
                db, workout1, mediaConfig.RootPath, count: 2);

            // Create best efforts
            bestEffort1 = new BestEffort
            {
                Distance = "5K",
                DistanceM = 5000,
                TimeS = 1800,
                WorkoutId = workout1.Id,
                WorkoutDate = workout1.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            bestEffort2 = new BestEffort
            {
                Distance = "10K",
                DistanceM = 10000,
                TimeS = 3600,
                WorkoutId = workout2.Id,
                WorkoutDate = workout2.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            db.BestEfforts.AddRange(bestEffort1, bestEffort2);
            await db.SaveChangesAsync();
        }

        // Export data
        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);

        // Clear database (preserve user)
        await EnsureCleanDatabaseAsync();

        // Import exported ZIP
        exportZip.Position = 0;
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(exportZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();

        // Verify ImportResult - success and no errors
        importResult.Success.Should().BeTrue();
        (importResult.Errors?.Count ?? 0).Should().Be(0);
        
        // Verify that entities were imported (exact counts may vary based on test data generation)
        importResult.Statistics.Shoes.Imported.Should().Be(2);
        importResult.Statistics.Workouts.Imported.Should().Be(3);
        importResult.Statistics.Routes.Imported.Should().Be(3);
        importResult.Statistics.Splits.Imported.Should().BeGreaterThan(0);
        importResult.Statistics.TimeSeries.Imported.Should().BeGreaterThan(0);
        importResult.Statistics.Media.Imported.Should().Be(2);
        importResult.Statistics.BestEfforts.Imported.Should().Be(2);
        importResult.Statistics.RawFiles.Imported.Should().Be(1);

        // Verify all entities imported correctly
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            // Verify counts
            (await db.Shoes.CountAsync()).Should().Be(2);
            (await db.Workouts.CountAsync()).Should().Be(3);
            (await db.WorkoutRoutes.CountAsync()).Should().Be(3);
            (await db.WorkoutSplits.CountAsync()).Should().BeGreaterThan(0);
            (await db.WorkoutTimeSeries.CountAsync()).Should().BeGreaterThan(0);
            (await db.WorkoutMedia.CountAsync()).Should().Be(2);
            (await db.BestEfforts.CountAsync()).Should().Be(2);
            (await db.UserSettings.CountAsync()).Should().Be(1);

            // Verify GUIDs preserved
            (await db.Shoes.AnyAsync(s => s.Id == shoe1!.Id)).Should().BeTrue();
            (await db.Shoes.AnyAsync(s => s.Id == shoe2!.Id)).Should().BeTrue();
            (await db.Workouts.AnyAsync(w => w.Id == workout1!.Id)).Should().BeTrue();
            (await db.Workouts.AnyAsync(w => w.Id == workout2!.Id)).Should().BeTrue();
            (await db.Workouts.AnyAsync(w => w.Id == workout3!.Id)).Should().BeTrue();
            (await db.BestEfforts.AnyAsync(be => be.Id == bestEffort1!.Id)).Should().BeTrue();
            (await db.BestEfforts.AnyAsync(be => be.Id == bestEffort2!.Id)).Should().BeTrue();

            // Verify relationships
            var importedWorkout1 = await db.Workouts
                .Include(w => w.Shoe)
                .FirstOrDefaultAsync(w => w.Id == workout1!.Id);
            importedWorkout1.Should().NotBeNull();
            importedWorkout1!.ShoeId.Should().Be(shoe1!.Id);

            var importedWorkout2 = await db.Workouts
                .Include(w => w.Shoe)
                .FirstOrDefaultAsync(w => w.Id == workout2!.Id);
            importedWorkout2.Should().NotBeNull();
            importedWorkout2!.ShoeId.Should().Be(shoe2!.Id);

            // Verify raw file data
            var importedWorkout1WithRaw = await db.Workouts
                .FirstOrDefaultAsync(w => w.Id == workout1!.Id);
            importedWorkout1WithRaw.Should().NotBeNull();
            importedWorkout1WithRaw!.RawFileData.Should().NotBeNull();
            importedWorkout1WithRaw.RawFileName.Should().Be("workout1.gpx");
        }
    }

    [Fact]
    public async Task ImportExport_DuplicateShoes_ByGuid_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Shoe existingShoe = null!;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            existingShoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
        }

        // Create export ZIP with duplicate shoe (same GUID)
        var duplicateShoe = new Shoe
        {
            Id = existingShoe.Id, // Same GUID
            Brand = "Nike",
            Model = "Pegasus",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithShoes(new[] { duplicateShoe });
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.Shoes.Imported.Should().Be(0);
        importResult.Statistics.Shoes.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("GUID"));
    }

    [Fact]
    public async Task ImportExport_DuplicateShoes_ByBrandModel_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
        }

        // Create export ZIP with duplicate shoe (different GUID, same Brand+Model)
        var duplicateShoe = new Shoe
        {
            Id = Guid.NewGuid(), // Different GUID
            Brand = "Nike",
            Model = "Pegasus", // Same Brand+Model
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithShoes(new[] { duplicateShoe });
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.Shoes.Imported.Should().Be(0);
        importResult.Statistics.Shoes.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("Brand") || w.Contains("Model"));
    }

    [Fact]
    public async Task ImportExport_DuplicateWorkouts_ByGuid_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout existingWorkout = null!;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            existingWorkout = await TestDataSeeder.SeedWorkoutAsync(db, distanceM: 5000, durationS: 1800);
        }

        // Create export ZIP with duplicate workout (same GUID)
        var duplicateWorkout = new Workout
        {
            Id = existingWorkout.Id, // Same GUID
            StartedAt = existingWorkout.StartedAt,
            DistanceM = existingWorkout.DistanceM,
            DurationS = existingWorkout.DurationS,
            AvgPaceS = existingWorkout.AvgPaceS,
            CreatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithWorkouts(new[] { duplicateWorkout });
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.Workouts.Imported.Should().Be(0);
        importResult.Statistics.Workouts.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("GUID"));
    }

    [Fact]
    public async Task ImportExport_DuplicateWorkouts_ByKey_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout existingWorkout = null!;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            existingWorkout = await TestDataSeeder.SeedWorkoutAsync(
                db, 
                startedAt: new DateTime(2024, 1, 1, 10, 0, 0, DateTimeKind.Utc),
                distanceM: 5000, 
                durationS: 1800);
        }

        // Create export ZIP with duplicate workout (different GUID, same StartedAt/DistanceM/DurationS)
        var duplicateWorkout = new Workout
        {
            Id = Guid.NewGuid(), // Different GUID
            StartedAt = existingWorkout.StartedAt, // Same StartedAt
            DistanceM = existingWorkout.DistanceM, // Same DistanceM
            DurationS = existingWorkout.DurationS, // Same DurationS
            AvgPaceS = existingWorkout.AvgPaceS,
            CreatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithWorkouts(new[] { duplicateWorkout });
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.Workouts.Imported.Should().Be(0);
        importResult.Statistics.Workouts.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("StartedAt"));
    }

    [Fact]
    public async Task ImportExport_DuplicateBestEfforts_ByGuid_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        BestEffort existingBestEffort = null!;
        Workout workout = null!;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            existingBestEffort = new BestEffort
            {
                Distance = "5K",
                DistanceM = 5000,
                TimeS = 1800,
                WorkoutId = workout.Id,
                WorkoutDate = workout.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            db.BestEfforts.Add(existingBestEffort);
            await db.SaveChangesAsync();
        }

        // Create export ZIP with duplicate best effort (same GUID)
        var duplicateBestEffort = new BestEffort
        {
            Id = existingBestEffort.Id, // Same GUID
            Distance = "5K",
            DistanceM = 5000,
            TimeS = 1800,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithBestEfforts(new[] { duplicateBestEffort }, workout.Id);
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.BestEfforts.Imported.Should().Be(0);
        importResult.Statistics.BestEfforts.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("GUID"));
    }

    [Fact]
    public async Task ImportExport_DuplicateBestEfforts_ByDistance_SkipsDuplicates()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Workout workout = null!;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            var existingBestEffort = new BestEffort
            {
                Distance = "5K",
                DistanceM = 5000,
                TimeS = 1800,
                WorkoutId = workout.Id,
                WorkoutDate = workout.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            db.BestEfforts.Add(existingBestEffort);
            await db.SaveChangesAsync();
        }

        // Create export ZIP with duplicate best effort (different GUID, same Distance)
        var duplicateBestEffort = new BestEffort
        {
            Id = Guid.NewGuid(), // Different GUID
            Distance = "5K", // Same Distance
            DistanceM = 5000,
            TimeS = 1800,
            WorkoutId = workout.Id,
            WorkoutDate = workout.StartedAt,
            CalculatedAt = DateTime.UtcNow
        };

        var zipStream = CreateExportZipWithBestEfforts(new[] { duplicateBestEffort }, workout.Id);
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(zipStream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var importResult = await importResponse.Content.ReadFromJsonAsync<ImportResponse>();
        importResult.Should().NotBeNull();
        importResult!.Success.Should().BeTrue();
        importResult.Statistics.BestEfforts.Imported.Should().Be(0);
        importResult.Statistics.BestEfforts.Skipped.Should().Be(1);
        importResult.Warnings.Should().Contain(w => w.Contains("already exists") || w.Contains("Distance"));
    }

    [Fact]
    public async Task ImportExport_RoundTrip_PreservesDataIntegrity()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Seed comprehensive data
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var mediaConfig = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();

            // Clear any existing settings first to avoid conflicts
            var existingSettings = await db.UserSettings.FirstOrDefaultAsync();
            if (existingSettings != null)
            {
                db.UserSettings.Remove(existingSettings);
                await db.SaveChangesAsync();
            }

            var shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: shoe.Id);

            var workout1 = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db, shoeId: shoe.Id, distanceM: 5000, durationS: 1800);
            var workout2 = await TestDataSeeder.SeedWorkoutCompleteAsync(
                db, shoeId: shoe.Id, distanceM: 10000, durationS: 3600);

            await TestDataSeeder.SeedWorkoutWithMediaAsync(db, workout1, mediaConfig.RootPath, count: 1);

            var bestEffort = new BestEffort
            {
                Distance = "5K",
                DistanceM = 5000,
                TimeS = 1800,
                WorkoutId = workout1.Id,
                WorkoutDate = workout1.StartedAt,
                CalculatedAt = DateTime.UtcNow
            };
            db.BestEfforts.Add(bestEffort);
            await db.SaveChangesAsync();
        }

        // Get original database state (query all data before scope is disposed)
        int originalShoesCount, originalWorkoutsCount, originalRoutesCount, originalSplitsCount;
        int originalTimeSeriesCount, originalMediaCount, originalBestEffortsCount, originalSettingsCount;
        double originalTotalDistanceM;
        int originalTotalDurationS;
        List<Guid> originalShoeIds, originalWorkoutIds, originalBestEffortIds;
        List<WorkoutData> originalWorkoutData;
        List<WorkoutRouteData> originalWorkoutRoutePairs;

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            originalShoesCount = await db.Shoes.CountAsync();
            originalWorkoutsCount = await db.Workouts.CountAsync();
            originalRoutesCount = await db.WorkoutRoutes.CountAsync();
            originalSplitsCount = await db.WorkoutSplits.CountAsync();
            originalTimeSeriesCount = await db.WorkoutTimeSeries.CountAsync();
            originalMediaCount = await db.WorkoutMedia.CountAsync();
            originalBestEffortsCount = await db.BestEfforts.CountAsync();
            originalSettingsCount = await db.UserSettings.CountAsync();
            originalTotalDistanceM = await db.Workouts.SumAsync(w => w.DistanceM);
            originalTotalDurationS = await db.Workouts.SumAsync(w => w.DurationS);
            originalShoeIds = await db.Shoes.Select(s => s.Id).ToListAsync();
            originalWorkoutIds = await db.Workouts.Select(w => w.Id).ToListAsync();
            originalBestEffortIds = await db.BestEfforts.Select(be => be.Id).ToListAsync();
            originalWorkoutData = await db.Workouts
                .Select(w => new WorkoutData { Id = w.Id, StartedAt = w.StartedAt, CreatedAt = w.CreatedAt, ShoeId = w.ShoeId })
                .ToListAsync();
            originalWorkoutRoutePairs = await db.WorkoutRoutes
                .Select(r => new WorkoutRouteData { WorkoutId = r.WorkoutId, Id = r.Id })
                .ToListAsync();
        }

        // Export data
        var exportZip = await ImportTestHelper.CreateExportZipWithDataAsync(client);

        // Clear database (preserve user)
        await EnsureCleanDatabaseAsync();

        // Import exported ZIP
        exportZip.Position = 0;
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(exportZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Compare data integrity (query all data before scope is disposed)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            var comparison = new ImportTestHelper.DataIntegrityComparison
            {
                ShoesCount = await db.Shoes.CountAsync(),
                WorkoutsCount = await db.Workouts.CountAsync(),
                RoutesCount = await db.WorkoutRoutes.CountAsync(),
                SplitsCount = await db.WorkoutSplits.CountAsync(),
                TimeSeriesCount = await db.WorkoutTimeSeries.CountAsync(),
                MediaCount = await db.WorkoutMedia.CountAsync(),
                BestEffortsCount = await db.BestEfforts.CountAsync(),
                SettingsCount = await db.UserSettings.CountAsync(),
                TotalDistanceM = await db.Workouts.SumAsync(w => w.DistanceM),
                TotalDurationS = await db.Workouts.SumAsync(w => w.DurationS)
            };

            comparison.OriginalShoesCount = originalShoesCount;
            comparison.OriginalWorkoutsCount = originalWorkoutsCount;
            comparison.OriginalRoutesCount = originalRoutesCount;
            comparison.OriginalSplitsCount = originalSplitsCount;
            comparison.OriginalTimeSeriesCount = originalTimeSeriesCount;
            comparison.OriginalMediaCount = originalMediaCount;
            comparison.OriginalBestEffortsCount = originalBestEffortsCount;
            comparison.OriginalSettingsCount = originalSettingsCount;
            comparison.OriginalTotalDistanceM = originalTotalDistanceM;
            comparison.OriginalTotalDurationS = originalTotalDurationS;

            var importedShoeIds = await db.Shoes.Select(s => s.Id).ToListAsync();
            comparison.ShoeGuidsPreserved = originalShoeIds.All(id => importedShoeIds.Contains(id));

            var importedWorkoutIds = await db.Workouts.Select(w => w.Id).ToListAsync();
            comparison.WorkoutGuidsPreserved = originalWorkoutIds.All(id => importedWorkoutIds.Contains(id));

            var importedBestEffortIds = await db.BestEfforts.Select(be => be.Id).ToListAsync();
            comparison.BestEffortGuidsPreserved = originalBestEffortIds.All(id => importedBestEffortIds.Contains(id));

            var importedWorkoutData = await db.Workouts
                .Select(w => new WorkoutData { Id = w.Id, StartedAt = w.StartedAt, CreatedAt = w.CreatedAt })
                .ToListAsync();

            comparison.TimestampsPreserved = originalWorkoutData.All(original =>
            {
                var imported = importedWorkoutData.FirstOrDefault(i => i.Id == original.Id);
                if (imported == null) return false;
                return Math.Abs((original.StartedAt - imported.StartedAt).TotalSeconds) < 1 &&
                       Math.Abs((original.CreatedAt - imported.CreatedAt).TotalSeconds) < 1;
            });

            var importedWorkoutShoePairs = await db.Workouts
                .Where(w => w.ShoeId.HasValue)
                .Select(w => new WorkoutData { Id = w.Id, ShoeId = w.ShoeId })
                .ToListAsync();

            comparison.RelationshipsPreserved = originalWorkoutData.All(original =>
            {
                var imported = importedWorkoutShoePairs.FirstOrDefault(i => i.Id == original.Id);
                return imported != null && imported.ShoeId == original.ShoeId;
            });

            var importedWorkoutRoutePairs = await db.WorkoutRoutes
                .Select(r => new WorkoutRouteData { WorkoutId = r.WorkoutId, Id = r.Id })
                .ToListAsync();

            var routeRelationshipsPreserved = originalWorkoutRoutePairs.All(original =>
            {
                var imported = importedWorkoutRoutePairs.FirstOrDefault(i => i.WorkoutId == original.WorkoutId);
                return imported != null;
            });

            comparison.RelationshipsPreserved = comparison.RelationshipsPreserved && routeRelationshipsPreserved;

            // Verify counts match
            comparison.ShoesCount.Should().Be(comparison.OriginalShoesCount);
            comparison.WorkoutsCount.Should().Be(comparison.OriginalWorkoutsCount);
            comparison.RoutesCount.Should().Be(comparison.OriginalRoutesCount);
            comparison.SplitsCount.Should().Be(comparison.OriginalSplitsCount);
            comparison.TimeSeriesCount.Should().Be(comparison.OriginalTimeSeriesCount);
            comparison.MediaCount.Should().Be(comparison.OriginalMediaCount);
            comparison.BestEffortsCount.Should().Be(comparison.OriginalBestEffortsCount);
            comparison.SettingsCount.Should().Be(comparison.OriginalSettingsCount);

            // Verify totals match
            comparison.TotalDistanceM.Should().BeApproximately(comparison.OriginalTotalDistanceM, 0.1);
            comparison.TotalDurationS.Should().Be(comparison.OriginalTotalDurationS);

            // Verify GUIDs preserved
            comparison.ShoeGuidsPreserved.Should().BeTrue();
            comparison.WorkoutGuidsPreserved.Should().BeTrue();
            comparison.BestEffortGuidsPreserved.Should().BeTrue();

            // Verify timestamps preserved
            comparison.TimestampsPreserved.Should().BeTrue();

            // Verify relationships preserved
            comparison.RelationshipsPreserved.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ImportExport_InvalidZip_CleansUpTempDirectory()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Create invalid ZIP (missing manifest)
        var invalidZip = ExportTestHelper.CreateZipWithMissingManifestAsync();
        
        // Import
        var formContent = new MultipartFormDataContent();
        var streamContent = new StreamContent(invalidZip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        formContent.Add(streamContent, "file", "export.zip");

        var importResponse = await client.PostAsync("/workouts/import/export", formContent);
        importResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Verify temp directory cleanup (indirectly - if cleanup failed, we'd see errors)
        // The ImportService cleans up in the finally block, so this test verifies
        // that the exception handling doesn't prevent cleanup
        ImportTestHelper.VerifyTempDirectoryCleanup();
    }

    // Helper methods to create export ZIPs with specific data

    private MemoryStream CreateExportZipWithShoes(Shoe[] shoes)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = shoes.Length,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", shoes);
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    private MemoryStream CreateExportZipWithWorkouts(Workout[] workouts)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = workouts.Length,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", workouts);
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    private MemoryStream CreateExportZipWithBestEfforts(BestEffort[] bestEfforts, Guid workoutId)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = bestEfforts.Length,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create a minimal workout so best effort has a valid reference
            var workout = new Workout
            {
                Id = workoutId,
                StartedAt = DateTime.UtcNow.AddHours(-1),
                DistanceM = 5000,
                DurationS = 1800,
                AvgPaceS = 360,
                CreatedAt = DateTime.UtcNow
            };

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", new[] { workout });
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", bestEfforts);
        }

        zipStream.Position = 0;
        return zipStream;
    }

    private static void CreateJsonFile(ZipArchive archive, string entryName, object data)
    {
        var entry = archive.CreateEntry(entryName);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            writer.Write(json);
        }
    }

    // Response DTOs for import endpoint
    private class ImportResponse
    {
        public bool Success { get; set; }
        public DateTime ImportedAt { get; set; }
        public ImportStatisticsDto Statistics { get; set; } = new();
        public List<string>? Warnings { get; set; }
        public List<string>? Errors { get; set; }
    }

    private class ImportStatisticsDto
    {
        public ItemStatisticsDto Settings { get; set; } = new();
        public ItemStatisticsDto Shoes { get; set; } = new();
        public ItemStatisticsDto Workouts { get; set; } = new();
        public ItemStatisticsDto Routes { get; set; } = new();
        public ItemStatisticsDto Splits { get; set; } = new();
        public ItemStatisticsDto TimeSeries { get; set; } = new();
        public ItemStatisticsDto Media { get; set; } = new();
        public ItemStatisticsDto BestEfforts { get; set; } = new();
        public ItemStatisticsDto RawFiles { get; set; } = new();
    }

    private class ItemStatisticsDto
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }

    private class WorkoutData
    {
        public Guid Id { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public Guid? ShoeId { get; set; }
    }

    private class WorkoutRouteData
    {
        public Guid WorkoutId { get; set; }
        public Guid Id { get; set; }
    }
}

