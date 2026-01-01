using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Helper class for validating import results and comparing data integrity
/// </summary>
public static class ImportTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Validates ImportResult structure and statistics
    /// </summary>
    public static void ValidateImportResult(
        ImportService.ImportResult result,
        bool expectedSuccess,
        int? expectedShoesImported = null,
        int? expectedWorkoutsImported = null,
        int? expectedRoutesImported = null,
        int? expectedSplitsImported = null,
        int? expectedTimeSeriesImported = null,
        int? expectedMediaImported = null,
        int? expectedBestEffortsImported = null,
        int? expectedRawFilesImported = null,
        int? expectedShoesSkipped = null,
        int? expectedWorkoutsSkipped = null,
        int? expectedBestEffortsSkipped = null,
        int? expectedErrors = null)
    {
        result.Should().NotBeNull();
        result.Success.Should().Be(expectedSuccess);
        // Only validate ImportedAt if it's not default (was deserialized correctly)
        if (result.ImportedAt != default)
        {
            result.ImportedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }
        // Manifest is only available when testing ImportService directly, not via HTTP endpoint
        // So we only validate it if it's not null
        if (result.Manifest != null)
        {
            result.Manifest.Should().NotBeNull();
        }
        result.Statistics.Should().NotBeNull();
        result.Warnings.Should().NotBeNull();
        result.Errors.Should().NotBeNull();

        if (expectedShoesImported.HasValue)
        {
            result.Statistics.Shoes.Imported.Should().Be(expectedShoesImported.Value);
        }

        if (expectedWorkoutsImported.HasValue)
        {
            result.Statistics.Workouts.Imported.Should().Be(expectedWorkoutsImported.Value);
        }

        if (expectedRoutesImported.HasValue)
        {
            result.Statistics.Routes.Imported.Should().Be(expectedRoutesImported.Value);
        }

        if (expectedSplitsImported.HasValue)
        {
            result.Statistics.Splits.Imported.Should().Be(expectedSplitsImported.Value);
        }

        if (expectedTimeSeriesImported.HasValue)
        {
            result.Statistics.TimeSeries.Imported.Should().Be(expectedTimeSeriesImported.Value);
        }

        if (expectedMediaImported.HasValue)
        {
            result.Statistics.Media.Imported.Should().Be(expectedMediaImported.Value);
        }

        if (expectedBestEffortsImported.HasValue)
        {
            result.Statistics.BestEfforts.Imported.Should().Be(expectedBestEffortsImported.Value);
        }

        if (expectedRawFilesImported.HasValue)
        {
            result.Statistics.RawFiles.Imported.Should().Be(expectedRawFilesImported.Value);
        }

        if (expectedShoesSkipped.HasValue)
        {
            result.Statistics.Shoes.Skipped.Should().Be(expectedShoesSkipped.Value);
        }

        if (expectedWorkoutsSkipped.HasValue)
        {
            result.Statistics.Workouts.Skipped.Should().Be(expectedWorkoutsSkipped.Value);
        }

        if (expectedBestEffortsSkipped.HasValue)
        {
            result.Statistics.BestEfforts.Skipped.Should().Be(expectedBestEffortsSkipped.Value);
        }

        if (expectedErrors.HasValue)
        {
            result.Errors.Count.Should().Be(expectedErrors.Value);
        }
    }

    /// <summary>
    /// Compares data integrity between two database states
    /// </summary>
    public static async Task<DataIntegrityComparison> CompareDataIntegrityAsync(
        TempoDbContext originalDb,
        TempoDbContext importedDb)
    {
        var comparison = new DataIntegrityComparison();

        // Compare counts
        comparison.ShoesCount = await importedDb.Shoes.CountAsync();
        comparison.WorkoutsCount = await importedDb.Workouts.CountAsync();
        comparison.RoutesCount = await importedDb.WorkoutRoutes.CountAsync();
        comparison.SplitsCount = await importedDb.WorkoutSplits.CountAsync();
        comparison.TimeSeriesCount = await importedDb.WorkoutTimeSeries.CountAsync();
        comparison.MediaCount = await importedDb.WorkoutMedia.CountAsync();
        comparison.BestEffortsCount = await importedDb.BestEfforts.CountAsync();
        comparison.SettingsCount = await importedDb.UserSettings.CountAsync();

        // Compare totals
        comparison.TotalDistanceM = await importedDb.Workouts.SumAsync(w => w.DistanceM);
        comparison.TotalDurationS = await importedDb.Workouts.SumAsync(w => w.DurationS);

        // Get original counts for comparison
        var originalShoesCount = await originalDb.Shoes.CountAsync();
        var originalWorkoutsCount = await originalDb.Workouts.CountAsync();
        var originalRoutesCount = await originalDb.WorkoutRoutes.CountAsync();
        var originalSplitsCount = await originalDb.WorkoutSplits.CountAsync();
        var originalTimeSeriesCount = await originalDb.WorkoutTimeSeries.CountAsync();
        var originalMediaCount = await originalDb.WorkoutMedia.CountAsync();
        var originalBestEffortsCount = await originalDb.BestEfforts.CountAsync();
        var originalSettingsCount = await originalDb.UserSettings.CountAsync();
        var originalTotalDistanceM = await originalDb.Workouts.SumAsync(w => w.DistanceM);
        var originalTotalDurationS = await originalDb.Workouts.SumAsync(w => w.DurationS);

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

        // Compare GUIDs
        var originalShoeIds = await originalDb.Shoes.Select(s => s.Id).ToListAsync();
        var importedShoeIds = await importedDb.Shoes.Select(s => s.Id).ToListAsync();
        comparison.ShoeGuidsPreserved = originalShoeIds.All(id => importedShoeIds.Contains(id));

        var originalWorkoutIds = await originalDb.Workouts.Select(w => w.Id).ToListAsync();
        var importedWorkoutIds = await importedDb.Workouts.Select(w => w.Id).ToListAsync();
        comparison.WorkoutGuidsPreserved = originalWorkoutIds.All(id => importedWorkoutIds.Contains(id));

        var originalBestEffortIds = await originalDb.BestEfforts.Select(be => be.Id).ToListAsync();
        var importedBestEffortIds = await importedDb.BestEfforts.Select(be => be.Id).ToListAsync();
        comparison.BestEffortGuidsPreserved = originalBestEffortIds.All(id => importedBestEffortIds.Contains(id));

        // Compare timestamps (with tolerance for database precision)
        var originalWorkouts = await originalDb.Workouts
            .Select(w => new { w.Id, w.StartedAt, w.CreatedAt })
            .ToListAsync();
        var importedWorkouts = await importedDb.Workouts
            .Select(w => new { w.Id, w.StartedAt, w.CreatedAt })
            .ToListAsync();

        comparison.TimestampsPreserved = originalWorkouts.All(original =>
        {
            var imported = importedWorkouts.FirstOrDefault(i => i.Id == original.Id);
            if (imported == null) return false;
            return Math.Abs((original.StartedAt - imported.StartedAt).TotalSeconds) < 1 &&
                   Math.Abs((original.CreatedAt - imported.CreatedAt).TotalSeconds) < 1;
        });

        // Verify relationships
        var originalWorkoutShoePairs = await originalDb.Workouts
            .Where(w => w.ShoeId.HasValue)
            .Select(w => new { w.Id, w.ShoeId })
            .ToListAsync();
        var importedWorkoutShoePairs = await importedDb.Workouts
            .Where(w => w.ShoeId.HasValue)
            .Select(w => new { w.Id, w.ShoeId })
            .ToListAsync();

        comparison.RelationshipsPreserved = originalWorkoutShoePairs.All(original =>
        {
            var imported = importedWorkoutShoePairs.FirstOrDefault(i => i.Id == original.Id);
            return imported != null && imported.ShoeId == original.ShoeId;
        });

        // Verify workout-route relationships
        var originalWorkoutRoutePairs = await originalDb.WorkoutRoutes
            .Select(r => new { r.WorkoutId, r.Id })
            .ToListAsync();
        var importedWorkoutRoutePairs = await importedDb.WorkoutRoutes
            .Select(r => new { r.WorkoutId, r.Id })
            .ToListAsync();

        var routeRelationshipsPreserved = originalWorkoutRoutePairs.All(original =>
        {
            var imported = importedWorkoutRoutePairs.FirstOrDefault(i => i.WorkoutId == original.WorkoutId);
            return imported != null;
        });

        comparison.RelationshipsPreserved = comparison.RelationshipsPreserved && routeRelationshipsPreserved;

        return comparison;
    }

    /// <summary>
    /// Creates an export ZIP by calling the export endpoint
    /// </summary>
    public static async Task<MemoryStream> CreateExportZipWithDataAsync(
        HttpClient authenticatedClient)
    {
        var response = await authenticatedClient.PostAsync("/workouts/export", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        var zipStream = new MemoryStream();
        await response.Content.CopyToAsync(zipStream);
        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Gets the current count of temp directories matching the GUID pattern
    /// </summary>
    public static int GetTempDirectoryCount()
    {
        var tempPath = Path.GetTempPath();
        return Directory.GetDirectories(tempPath)
            .Count(d => Guid.TryParse(Path.GetFileName(d), out _));
    }

    /// <summary>
    /// Verifies that temp directories are cleaned up after import
    /// Note: This is difficult to test directly since temp directories are created with random GUIDs.
    /// Instead, we verify that no new temp directories were left behind by checking the count
    /// of directories matching the pattern before and after import.
    /// </summary>
    /// <param name="baselineCount">Optional baseline count of temp directories before the operation.
    /// If provided, verifies that the current count hasn't increased beyond a reasonable threshold.</param>
    /// <param name="maxAllowedIncrease">Maximum allowed increase in temp directory count (default: 5).
    /// This accounts for other processes that may create temp directories during the test.</param>
    public static void VerifyTempDirectoryCleanup(int? baselineCount = null, int maxAllowedIncrease = 5)
    {
        var currentCount = GetTempDirectoryCount();

        if (baselineCount.HasValue)
        {
            // Verify that the count hasn't increased beyond the threshold
            // This indicates that temp directories created during import were cleaned up
            currentCount.Should().BeLessThanOrEqualTo(
                baselineCount.Value + maxAllowedIncrease,
                "temp directories should be cleaned up after import, allowing for other processes");
        }
        else
        {
            // Without a baseline, verify that the count is reasonable (not excessive)
            // An excessive count would indicate cleanup failures over time
            // Set a reasonable threshold (e.g., 100) to catch significant cleanup failures
            const int maxReasonableCount = 100;
            currentCount.Should().BeLessThanOrEqualTo(
                maxReasonableCount,
                $"temp directory count ({currentCount}) should be reasonable, indicating proper cleanup");
        }
    }

    /// <summary>
    /// Verifies that orphaned media files are removed when database save fails
    /// This is tested by checking that media files don't exist in the media directory
    /// when they're not referenced in the database
    /// </summary>
    public static async Task VerifyOrphanedMediaCleanupAsync(
        TempoDbContext db,
        string mediaRootPath)
    {
        // Get all media files in the database
        var mediaInDb = await db.WorkoutMedia
            .Select(m => new { m.WorkoutId, m.Id, m.FilePath })
            .ToListAsync();

        // Check that all media files in the database actually exist
        var missingFiles = new List<string>();
        foreach (var media in mediaInDb)
        {
            if (!string.IsNullOrEmpty(media.FilePath) && !File.Exists(media.FilePath))
            {
                missingFiles.Add(media.FilePath);
            }
        }

        missingFiles.Should().BeEmpty(
            $"all media files referenced in the database should exist on disk. Missing files: {string.Join(", ", missingFiles)}");

        // Check for orphaned files: files in the media directory that aren't in the database
        var orphanedDirectories = new List<string>();
        if (Directory.Exists(mediaRootPath))
        {
            var workoutDirs = Directory.GetDirectories(mediaRootPath);
            foreach (var workoutDir in workoutDirs)
            {
                if (Guid.TryParse(Path.GetFileName(workoutDir), out var workoutId))
                {
                    var mediaDir = Path.Combine(workoutDir, "media");
                    if (Directory.Exists(mediaDir))
                    {
                        var mediaSubDirs = Directory.GetDirectories(mediaDir);
                        foreach (var mediaSubDir in mediaSubDirs)
                        {
                            if (Guid.TryParse(Path.GetFileName(mediaSubDir), out var mediaId))
                            {
                                var mediaInDbForWorkout = mediaInDb.FirstOrDefault(m => m.WorkoutId == workoutId && m.Id == mediaId);
                                if (mediaInDbForWorkout == null)
                                {
                                    orphanedDirectories.Add(mediaSubDir);
                                }
                            }
                        }
                    }
                }
            }
        }

        orphanedDirectories.Should().BeEmpty(
            $"orphaned media directories should be cleaned up. Found orphaned directories: {string.Join(", ", orphanedDirectories)}");
    }

    /// <summary>
    /// Helper class for data integrity comparison results
    /// </summary>
    public class DataIntegrityComparison
    {
        public int ShoesCount { get; set; }
        public int WorkoutsCount { get; set; }
        public int RoutesCount { get; set; }
        public int SplitsCount { get; set; }
        public int TimeSeriesCount { get; set; }
        public int MediaCount { get; set; }
        public int BestEffortsCount { get; set; }
        public int SettingsCount { get; set; }
        public double TotalDistanceM { get; set; }
        public int TotalDurationS { get; set; }

        public int OriginalShoesCount { get; set; }
        public int OriginalWorkoutsCount { get; set; }
        public int OriginalRoutesCount { get; set; }
        public int OriginalSplitsCount { get; set; }
        public int OriginalTimeSeriesCount { get; set; }
        public int OriginalMediaCount { get; set; }
        public int OriginalBestEffortsCount { get; set; }
        public int OriginalSettingsCount { get; set; }
        public double OriginalTotalDistanceM { get; set; }
        public int OriginalTotalDurationS { get; set; }

        public bool ShoeGuidsPreserved { get; set; }
        public bool WorkoutGuidsPreserved { get; set; }
        public bool BestEffortGuidsPreserved { get; set; }
        public bool TimestampsPreserved { get; set; }
        public bool RelationshipsPreserved { get; set; }
    }
}

