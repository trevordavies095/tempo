using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Service for exporting all user data to a portable ZIP format.
/// </summary>
public class ExportService
{
    private readonly TempoDbContext _db;
    private readonly MediaStorageConfig _mediaConfig;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        TempoDbContext db,
        MediaStorageConfig mediaConfig,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ExportService> logger)
    {
        _db = db;
        _mediaConfig = mediaConfig;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    private Guid GetUserId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            throw new UnauthorizedAccessException("User not authenticated");
        }

        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user authentication");
        }

        return userId;
    }

    private string GetUsername()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null)
        {
            return "unknown";
        }

        return user.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
    }

    /// <summary>
    /// Exports all user data to a ZIP file stream.
    /// </summary>
    /// <remarks>
    /// Note: Authentication should be validated before calling this method.
    /// This check is a defensive measure and will throw UnauthorizedAccessException
    /// if the user is not authenticated, which will result in a corrupted response
    /// if called from within a streaming context.
    /// </remarks>
    public async Task ExportAllDataAsync(Stream outputStream)
    {
        // Validate user is authenticated (throws if not)
        // This is a defensive check - authentication should be validated
        // before calling this method to avoid corrupted streaming responses
        _ = GetUserId();

        var exportDate = DateTime.UtcNow;
        var statistics = new ExportStatistics();

        // JSON serializer options
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
        };

        // Build ZIP in MemoryStream first to avoid synchronous write issues with Kestrel
        using (var zipStream = new MemoryStream())
        {
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // 1. Export user settings
            var settings = await GetUserSettingsAsync();
            if (settings != null)
            {
                var settingsEntry = archive.CreateEntry("data/settings.json");
                await WriteJsonEntryAsync(settingsEntry, settings, jsonOptions);
                statistics.Settings = 1;
            }

            // 2. Export shoes
            var shoes = await GetShoesAsync();
            var shoesEntry = archive.CreateEntry("data/shoes.json");
            await WriteJsonEntryAsync(shoesEntry, shoes, jsonOptions);
            statistics.Shoes = shoes.Count;

            // 3. Export workouts (without navigation properties for now)
            var workouts = await GetWorkoutsAsync();
            var workoutsEntry = archive.CreateEntry("data/workouts.json");
            await WriteJsonEntryAsync(workoutsEntry, workouts, jsonOptions);
            statistics.Workouts = workouts.Count;

            // 4. Export routes (parse GeoJSON strings to objects)
            var routes = await GetRoutesAsync();
            var routesData = routes.Select(r => new
            {
                r.Id,
                r.WorkoutId,
                RouteGeoJson = string.IsNullOrEmpty(r.RouteGeoJson) 
                    ? null 
                    : JsonSerializer.Deserialize<object>(r.RouteGeoJson, jsonOptions)
            }).ToList();
            var routesEntry = archive.CreateEntry("data/routes.json");
            await WriteJsonEntryAsync(routesEntry, routesData, jsonOptions);
            statistics.Routes = routes.Count;

            // 5. Export splits
            var splits = await GetSplitsAsync();
            var splitsEntry = archive.CreateEntry("data/splits.json");
            await WriteJsonEntryAsync(splitsEntry, splits, jsonOptions);
            statistics.Splits = splits.Count;

            // 6. Export time series
            var timeSeries = await GetTimeSeriesAsync();
            var timeSeriesEntry = archive.CreateEntry("data/time-series.json");
            await WriteJsonEntryAsync(timeSeriesEntry, timeSeries, jsonOptions);
            statistics.TimeSeries = timeSeries.Count;

            // 7. Export media metadata
            var mediaMetadata = await GetMediaMetadataAsync();
            var mediaMetadataEntry = archive.CreateEntry("data/media-metadata.json");
            await WriteJsonEntryAsync(mediaMetadataEntry, mediaMetadata, jsonOptions);
            statistics.MediaFiles = mediaMetadata.Count;

            // 8. Export best efforts
            var bestEfforts = await GetBestEffortsAsync();
            var bestEffortsEntry = archive.CreateEntry("data/best-efforts.json");
            await WriteJsonEntryAsync(bestEffortsEntry, bestEfforts, jsonOptions);
            statistics.BestEfforts = bestEfforts.Count;

            // 9. Export binary files (raw workout files and media files)
            long totalSizeBytes = 0;
            foreach (var workout in workouts)
            {
                // Export raw workout file if exists
                if (workout.RawFileData != null && workout.RawFileData.Length > 0 && !string.IsNullOrEmpty(workout.RawFileName))
                {
                    var rawPath = $"workouts/{workout.Id}/raw/{workout.RawFileName}";
                    var rawEntry = archive.CreateEntry(rawPath);
                    using (var entryStream = rawEntry.Open())
                    {
                        await entryStream.WriteAsync(workout.RawFileData);
                        totalSizeBytes += workout.RawFileData.Length;
                    }
                }

                // Export media files for this workout
                var workoutMedia = mediaMetadata.Where(m => m.WorkoutId == workout.Id).ToList();
                foreach (var media in workoutMedia)
                {
                    try
                    {
                        var mediaPath = media.FilePath;
                        if (string.IsNullOrEmpty(mediaPath) || !File.Exists(mediaPath))
                        {
                            _logger.LogWarning("Media file not found: {FilePath} for workout {WorkoutId}", mediaPath, workout.Id);
                            continue;
                        }

                        var zipMediaPath = $"workouts/{workout.Id}/media/{media.Id}/{media.Filename}";
                        var mediaEntry = archive.CreateEntry(zipMediaPath);
                        
                        using (var fileStream = new FileStream(mediaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var entryStream = mediaEntry.Open())
                        {
                            await fileStream.CopyToAsync(entryStream);
                            totalSizeBytes += fileStream.Length;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to export media file {MediaId} for workout {WorkoutId}", media.Id, workout.Id);
                        // Continue with other files
                    }
                }
            }

            statistics.TotalSizeBytes = totalSizeBytes;

            // 10. Create manifest
            var manifest = CreateManifest(exportDate, statistics);
            var manifestEntry = archive.CreateEntry("manifest.json");
            await WriteJsonEntryAsync(manifestEntry, manifest, jsonOptions);

            // 11. Create README
            var readmeEntry = archive.CreateEntry("README.txt");
            using (var entryStream = new StreamWriter(readmeEntry.Open(), Encoding.UTF8))
            {
                await entryStream.WriteAsync(CreateReadmeContent());
            }
            } // Dispose ZipArchive here (writes central directory synchronously to MemoryStream)

            // Now copy the complete ZIP from MemoryStream to output stream asynchronously
            zipStream.Position = 0;
            await zipStream.CopyToAsync(outputStream);
        }
    }

    private async Task<UserSettings?> GetUserSettingsAsync()
    {
        return await _db.UserSettings
            .Include(s => s.DefaultShoe)
            .AsNoTracking()
            .FirstOrDefaultAsync();
    }

    private async Task<List<Shoe>> GetShoesAsync()
    {
        return await _db.Shoes
            .AsNoTracking()
            .OrderBy(s => s.CreatedAt)
            .ToListAsync();
    }

    private async Task<List<Workout>> GetWorkoutsAsync()
    {
        return await _db.Workouts
            .AsNoTracking()
            .OrderBy(w => w.StartedAt)
            .ToListAsync();
    }

    private async Task<List<WorkoutRoute>> GetRoutesAsync()
    {
        return await _db.WorkoutRoutes
            .AsNoTracking()
            .OrderBy(r => r.WorkoutId)
            .ToListAsync();
    }

    private async Task<List<WorkoutSplit>> GetSplitsAsync()
    {
        return await _db.WorkoutSplits
            .AsNoTracking()
            .OrderBy(s => s.WorkoutId)
            .ThenBy(s => s.Idx)
            .ToListAsync();
    }

    private async Task<List<WorkoutTimeSeries>> GetTimeSeriesAsync()
    {
        return await _db.WorkoutTimeSeries
            .AsNoTracking()
            .OrderBy(t => t.WorkoutId)
            .ThenBy(t => t.ElapsedSeconds)
            .ToListAsync();
    }

    private async Task<List<WorkoutMedia>> GetMediaMetadataAsync()
    {
        return await _db.WorkoutMedia
            .AsNoTracking()
            .OrderBy(m => m.WorkoutId)
            .ThenBy(m => m.CreatedAt)
            .ToListAsync();
    }

    private async Task<List<BestEffort>> GetBestEffortsAsync()
    {
        return await _db.BestEfforts
            .AsNoTracking()
            .OrderBy(b => b.DistanceM)
            .ToListAsync();
    }

    private async Task WriteJsonEntryAsync<T>(ZipArchiveEntry entry, T data, JsonSerializerOptions options)
    {
        using (var entryStream = entry.Open())
        using (var writer = new Utf8JsonWriter(entryStream, new JsonWriterOptions { Indented = true }))
        {
            JsonSerializer.Serialize(writer, data, options);
            await entryStream.FlushAsync();
        }
    }

    private ExportManifest CreateManifest(DateTime exportDate, ExportStatistics statistics)
    {
        // Get Tempo version using same logic as VersionEndpoints
        var tempoVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION") ?? "unknown";
        if (tempoVersion == "unknown")
        {
            try
            {
                var versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "VERSION");
                if (File.Exists(versionFilePath))
                {
                    tempoVersion = File.ReadAllText(versionFilePath).Trim();
                }
                else
                {
                    versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "VERSION");
                    if (File.Exists(versionFilePath))
                    {
                        tempoVersion = File.ReadAllText(versionFilePath).Trim();
                    }
                }
            }
            catch
            {
                // Keep "unknown" if file reading fails
            }
        }

        return new ExportManifest
        {
            Version = "1.0.0",
            TempoVersion = tempoVersion,
            ExportDate = exportDate,
            ExportedBy = GetUsername(),
            Statistics = statistics,
            DataFormat = new ExportDataFormat
            {
                Settings = "data/settings.json",
                Shoes = "data/shoes.json",
                Workouts = "data/workouts.json",
                Routes = "data/routes.json",
                Splits = "data/splits.json",
                TimeSeries = "data/time-series.json",
                MediaMetadata = "data/media-metadata.json",
                BestEfforts = "data/best-efforts.json"
            }
        };
    }

    private string CreateReadmeContent()
    {
        return @"Tempo Data Export
==================

This ZIP file contains a complete export of your Tempo running data.

Structure:
----------
- manifest.json          Export metadata and version information
- data/                  JSON files containing all database records
  - settings.json        User settings (heart rate zones, unit preferences)
  - shoes.json           All shoe records
  - workouts.json        All workout records
  - routes.json          All workout route data (GeoJSON)
  - splits.json          All workout split records
  - time-series.json     All time series data points
  - media-metadata.json  Media file metadata
  - best-efforts.json    Best effort records
- workouts/              Binary files organized by workout
  - {workoutId}/
    - raw/               Original workout files (GPX, FIT, CSV)
    - media/             Photos and videos
      - {mediaId}/
        - {filename}

Import:
-------
This export can be imported back into Tempo using the import feature.
The export format is versioned and may be updated in future versions.

For more information, visit: https://github.com/trevordavies095/tempo
";
    }

    private class ExportManifest
    {
        public string Version { get; set; } = string.Empty;
        public string TempoVersion { get; set; } = string.Empty;
        public DateTime ExportDate { get; set; }
        public string ExportedBy { get; set; } = string.Empty;
        public ExportStatistics Statistics { get; set; } = new();
        public ExportDataFormat DataFormat { get; set; } = new();
    }

    private class ExportStatistics
    {
        public int Settings { get; set; }
        public int Shoes { get; set; }
        public int Workouts { get; set; }
        public int Routes { get; set; }
        public int Splits { get; set; }
        public int TimeSeries { get; set; }
        public int MediaFiles { get; set; }
        public int BestEfforts { get; set; }
        public long TotalSizeBytes { get; set; }
    }

    private class ExportDataFormat
    {
        public string Settings { get; set; } = string.Empty;
        public string Shoes { get; set; } = string.Empty;
        public string Workouts { get; set; } = string.Empty;
        public string Routes { get; set; } = string.Empty;
        public string Splits { get; set; } = string.Empty;
        public string TimeSeries { get; set; } = string.Empty;
        public string MediaMetadata { get; set; } = string.Empty;
        public string BestEfforts { get; set; } = string.Empty;
    }
}

