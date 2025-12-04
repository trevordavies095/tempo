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
/// Service for importing complete Tempo export ZIP files.
/// </summary>
public class ImportService
{
    private readonly TempoDbContext _db;
    private readonly MediaService _mediaService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ImportService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ImportService(
        TempoDbContext db,
        MediaService mediaService,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ImportService> logger)
    {
        _db = db;
        _mediaService = mediaService;
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

    /// <summary>
    /// Imports a complete Tempo export ZIP file.
    /// </summary>
    public async Task<ImportResult> ImportExportAsync(Stream zipStream)
    {
        var result = new ImportResult
        {
            ImportedAt = DateTime.UtcNow
        };

        string? tempDir = null;
        try
        {
            // Extract ZIP archive
            tempDir = ExtractZipArchive(zipStream);
            _logger.LogInformation("Extracted export ZIP to {TempDir}", tempDir);

            // Validate and load manifest
            var manifestPath = Path.Combine(tempDir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("manifest.json not found in export ZIP");
            }

            var manifest = await LoadAndValidateManifestAsync(manifestPath);
            result.Manifest = manifest;

            // Validate ZIP structure
            ValidateZipStructure(tempDir, manifest);

            // Import data in correct order
            // Import shoes first so that settings can reference them via DefaultShoeId
            await ImportShoesAsync(tempDir, manifest, result);
            await ImportUserSettingsAsync(tempDir, manifest, result);
            await ImportWorkoutsAsync(tempDir, manifest, result);
            await ImportRoutesAsync(tempDir, manifest, result);
            await ImportSplitsAsync(tempDir, manifest, result);
            await ImportTimeSeriesAsync(tempDir, manifest, result);
            await ImportBestEffortsAsync(tempDir, manifest, result);
            await ImportMediaFilesAsync(tempDir, manifest, result);
            await ImportRawFilesAsync(tempDir, manifest, result);

            // Set success based on whether any errors were accumulated
            result.Success = result.Errors.Count == 0;
            
            if (result.Success)
            {
                _logger.LogInformation("Import completed successfully. Imported: {Workouts} workouts, {Shoes} shoes, {Media} media files",
                    result.Statistics.Workouts.Imported, result.Statistics.Shoes.Imported, result.Statistics.Media.Imported);
            }
            else
            {
                _logger.LogWarning("Import completed with {ErrorCount} errors. Imported: {Workouts} workouts, {Shoes} shoes, {Media} media files",
                    result.Errors.Count, result.Statistics.Workouts.Imported, result.Statistics.Shoes.Imported, result.Statistics.Media.Imported);
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Import failed: {ex.Message}");
            _logger.LogError(ex, "Import failed");
            throw;
        }
        finally
        {
            // Clean up temp directory
            if (tempDir != null && Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                    _logger.LogInformation("Cleaned up temp directory {TempDir}", tempDir);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temp directory {TempDir}", tempDir);
                }
            }
        }

        return result;
    }

    private string ExtractZipArchive(Stream zipStream)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var tempDirFullPath = Path.GetFullPath(tempDir);

        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                // Validate path to prevent Zip Slip attacks
                var entryPath = Path.Combine(tempDir, entry.FullName);
                var entryFullPath = Path.GetFullPath(entryPath);
                
                // Ensure the resolved path stays within the temp directory
                if (!entryFullPath.StartsWith(tempDirFullPath, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Invalid entry path detected: {entry.FullName}. Path traversal is not allowed.");
                }

                var entryDir = Path.GetDirectoryName(entryFullPath);
                if (!string.IsNullOrEmpty(entryDir))
                {
                    Directory.CreateDirectory(entryDir);
                }

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    using (var entryStream = entry.Open())
                    using (var fileStream = new FileStream(entryFullPath, FileMode.Create))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }
        }

        return tempDir;
    }

    private async Task<ExportManifest> LoadAndValidateManifestAsync(string manifestPath)
    {
        try
        {
            var json = await File.ReadAllTextAsync(manifestPath);
            var manifest = JsonSerializer.Deserialize<ExportManifest>(json, JsonOptions);
            
            if (manifest == null)
            {
                throw new InvalidOperationException("Failed to deserialize manifest.json");
            }

            // Validate version
            if (manifest.Version != "1.0.0")
            {
                throw new InvalidOperationException($"Unsupported export version: {manifest.Version}. Expected 1.0.0");
            }

            // Validate required fields
            if (string.IsNullOrEmpty(manifest.Version) || manifest.Statistics == null || manifest.DataFormat == null)
            {
                throw new InvalidOperationException("Manifest is missing required fields");
            }

            return manifest;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in manifest.json: {ex.Message}", ex);
        }
    }

    private void ValidateZipStructure(string tempDir, ExportManifest manifest)
    {
        var dataDir = Path.Combine(tempDir, "data");
        if (!Directory.Exists(dataDir))
        {
            throw new InvalidOperationException("data/ directory not found in export ZIP");
        }

        // Validate required file paths are present (Shoes and Workouts are mandatory)
        if (string.IsNullOrEmpty(manifest.DataFormat.Shoes))
        {
            throw new InvalidOperationException("Shoes file path is missing in export manifest");
        }
        if (string.IsNullOrEmpty(manifest.DataFormat.Workouts))
        {
            throw new InvalidOperationException("Workouts file path is missing in export manifest");
        }

        // Validate required JSON files exist
        var requiredFiles = new[]
        {
            manifest.DataFormat.Shoes,
            manifest.DataFormat.Workouts,
            manifest.DataFormat.Routes,
            manifest.DataFormat.Splits,
            manifest.DataFormat.TimeSeries,
            manifest.DataFormat.MediaMetadata,
            manifest.DataFormat.BestEfforts
        };

        foreach (var file in requiredFiles)
        {
            if (string.IsNullOrEmpty(file))
                continue;

            var filePath = Path.Combine(tempDir, file);
            if (!File.Exists(filePath))
            {
                throw new InvalidOperationException($"Required file not found: {file}");
            }
        }

        // Validate workouts directory exists
        var workoutsDir = Path.Combine(tempDir, "workouts");
        if (!Directory.Exists(workoutsDir))
        {
            throw new InvalidOperationException("workouts/ directory not found in export ZIP");
        }
    }

    private async Task ImportUserSettingsAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.Settings))
        {
            _logger.LogInformation("No settings file in export, skipping");
            return;
        }

        var settingsPath = Path.Combine(tempDir, manifest.DataFormat.Settings);
        if (!File.Exists(settingsPath))
        {
            result.Warnings.Add("Settings file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(settingsPath);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
            
            if (settings == null)
            {
                result.Statistics.Settings.Errors++;
                result.Errors.Add("Failed to deserialize settings.json");
                return;
            }

            // Validate GUID
            if (settings.Id == Guid.Empty)
            {
                result.Statistics.Settings.Errors++;
                result.Errors.Add("Invalid GUID in settings");
                return;
            }

            // Clear navigation properties (they will be resolved by EF based on foreign keys)
            settings.DefaultShoe = null;

            // Check if settings already exist
            var existing = await _db.UserSettings.FirstOrDefaultAsync();
            if (existing != null)
            {
                // Update existing settings
                existing.CalculationMethod = settings.CalculationMethod;
                existing.Age = settings.Age;
                existing.RestingHeartRateBpm = settings.RestingHeartRateBpm;
                existing.MaxHeartRateBpm = settings.MaxHeartRateBpm;
                existing.Zone1MinBpm = settings.Zone1MinBpm;
                existing.Zone1MaxBpm = settings.Zone1MaxBpm;
                existing.Zone2MinBpm = settings.Zone2MinBpm;
                existing.Zone2MaxBpm = settings.Zone2MaxBpm;
                existing.Zone3MinBpm = settings.Zone3MinBpm;
                existing.Zone3MaxBpm = settings.Zone3MaxBpm;
                existing.Zone4MinBpm = settings.Zone4MinBpm;
                existing.Zone4MaxBpm = settings.Zone4MaxBpm;
                existing.Zone5MinBpm = settings.Zone5MinBpm;
                existing.Zone5MaxBpm = settings.Zone5MaxBpm;
                existing.UnitPreference = settings.UnitPreference;
                
                // Validate shoe reference if present
                if (settings.DefaultShoeId.HasValue)
                {
                    var shoeExists = await _db.Shoes.AnyAsync(s => s.Id == settings.DefaultShoeId.Value);
                    if (!shoeExists)
                    {
                        result.Warnings.Add($"Settings references non-existent shoe {settings.DefaultShoeId}, clearing reference");
                        existing.DefaultShoeId = null;
                    }
                    else
                    {
                        existing.DefaultShoeId = settings.DefaultShoeId;
                    }
                }
                else
                {
                    existing.DefaultShoeId = null;
                }
                
                existing.CreatedAt = settings.CreatedAt;
                existing.UpdatedAt = settings.UpdatedAt;
                
                await _db.SaveChangesAsync();
                result.Statistics.Settings.Imported++;
                _logger.LogInformation("Updated existing user settings");
            }
            else
            {
                // Validate shoe reference if present
                if (settings.DefaultShoeId.HasValue)
                {
                    var shoeExists = await _db.Shoes.AnyAsync(s => s.Id == settings.DefaultShoeId.Value);
                    if (!shoeExists)
                    {
                        result.Warnings.Add($"Settings references non-existent shoe {settings.DefaultShoeId}, clearing reference");
                        settings.DefaultShoeId = null;
                    }
                }
                
                // Create new settings
                _db.UserSettings.Add(settings);
                await _db.SaveChangesAsync();
                result.Statistics.Settings.Imported++;
                _logger.LogInformation("Imported user settings");
            }
        }
        catch (Exception ex)
        {
            result.Statistics.Settings.Errors++;
            result.Errors.Add($"Error importing settings: {ex.Message}");
            _logger.LogError(ex, "Error importing settings");
        }
    }

    private async Task ImportShoesAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.Shoes))
        {
            result.Errors.Add("Shoes file path is missing in export manifest");
            return;
        }

        var shoesPath = Path.Combine(tempDir, manifest.DataFormat.Shoes);
        if (!File.Exists(shoesPath))
        {
            result.Errors.Add("Shoes file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(shoesPath);
            var shoes = JsonSerializer.Deserialize<List<Shoe>>(json, JsonOptions) ?? new List<Shoe>();

            // Track shoes we've already processed in this batch to avoid duplicates within the same import
            var processedShoeIds = new HashSet<Guid>();
            var processedShoeKeys = new HashSet<(string Brand, string Model)>();

            foreach (var shoe in shoes)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (shoe == null)
                {
                    result.Statistics.Shoes.Errors++;
                    result.Errors.Add("Null shoe element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (shoe.Id == Guid.Empty)
                    {
                        result.Statistics.Shoes.Errors++;
                        result.Errors.Add($"Invalid GUID for shoe: {shoe.Brand} {shoe.Model}");
                        continue;
                    }

                    // Check if we've already processed this shoe in the current batch
                    if (processedShoeIds.Contains(shoe.Id))
                    {
                        result.Statistics.Shoes.Skipped++;
                        result.Warnings.Add($"Duplicate shoe in export (same GUID): {shoe.Brand} {shoe.Model}");
                        continue;
                    }

                    var shoeKey = (shoe.Brand, shoe.Model);
                    if (processedShoeKeys.Contains(shoeKey))
                    {
                        result.Statistics.Shoes.Skipped++;
                        result.Warnings.Add($"Duplicate shoe in export (same Brand+Model): {shoe.Brand} {shoe.Model}");
                        continue;
                    }

                    // Check if GUID already exists in database (query directly, not using FindAsync to avoid change tracker)
                    var existingById = await _db.Shoes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Id == shoe.Id);
                    if (existingById != null)
                    {
                        result.Statistics.Shoes.Skipped++;
                        result.Warnings.Add($"Shoe with GUID {shoe.Id} already exists in database: {shoe.Brand} {shoe.Model}");
                        continue;
                    }

                    // Check for duplicate by Brand + Model in database (different GUID, same shoe)
                    var existing = await _db.Shoes
                        .AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Brand == shoe.Brand && s.Model == shoe.Model);

                    if (existing != null)
                    {
                        result.Statistics.Shoes.Skipped++;
                        result.Warnings.Add($"Shoe already exists in database: {shoe.Brand} {shoe.Model}");
                        continue;
                    }

                    // Clear navigation properties (they will be resolved by EF based on foreign keys)
                    shoe.Workouts.Clear();

                    // Import shoe
                    _db.Shoes.Add(shoe);
                    processedShoeIds.Add(shoe.Id);
                    processedShoeKeys.Add(shoeKey);
                    result.Statistics.Shoes.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.Shoes.Errors++;
                    var shoeInfo = shoe != null 
                        ? $"{shoe.Brand} {shoe.Model} (ID: {shoe.Id})" 
                        : "null shoe";
                    result.Errors.Add($"Error importing shoe {shoeInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing shoe {ShoeId}", shoe?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} shoes", result.Statistics.Shoes.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading shoes file: {ex.Message}");
            _logger.LogError(ex, "Error reading shoes file");
        }
    }

    private async Task ImportWorkoutsAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.Workouts))
        {
            result.Errors.Add("Workouts file path is missing in export manifest");
            return;
        }

        var workoutsPath = Path.Combine(tempDir, manifest.DataFormat.Workouts);
        if (!File.Exists(workoutsPath))
        {
            result.Errors.Add("Workouts file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(workoutsPath);
            var workouts = JsonSerializer.Deserialize<List<Workout>>(json, JsonOptions) ?? new List<Workout>();

            // Track workouts we've already processed in this batch to avoid duplicates within the same import
            var processedWorkoutIds = new HashSet<Guid>();
            var processedWorkoutKeys = new HashSet<(DateTime StartedAt, double DistanceM, int DurationS)>();

            foreach (var workout in workouts)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (workout == null)
                {
                    result.Statistics.Workouts.Errors++;
                    result.Errors.Add("Null workout element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (workout.Id == Guid.Empty)
                    {
                        result.Statistics.Workouts.Errors++;
                        result.Errors.Add($"Invalid GUID for workout starting at {workout.StartedAt}");
                        continue;
                    }

                    // Check if we've already processed this workout in the current batch
                    if (processedWorkoutIds.Contains(workout.Id))
                    {
                        result.Statistics.Workouts.Skipped++;
                        result.Warnings.Add($"Duplicate workout in export (same GUID): {workout.StartedAt} ({workout.DistanceM}m, {workout.DurationS}s)");
                        continue;
                    }

                    var workoutKey = (workout.StartedAt, workout.DistanceM, workout.DurationS);
                    if (processedWorkoutKeys.Contains(workoutKey))
                    {
                        result.Statistics.Workouts.Skipped++;
                        result.Warnings.Add($"Duplicate workout in export (same StartedAt/DistanceM/DurationS): {workout.StartedAt} ({workout.DistanceM}m, {workout.DurationS}s)");
                        continue;
                    }

                    // Check for duplicate using existing duplicate detection (checks database)
                    var existing = await WorkoutQueryService.FindDuplicateWorkoutAsync(
                        _db, workout.StartedAt, workout.DistanceM, workout.DurationS);

                    if (existing != null)
                    {
                        result.Statistics.Workouts.Skipped++;
                        result.Warnings.Add($"Workout already exists: {workout.StartedAt} ({workout.DistanceM}m, {workout.DurationS}s)");
                        continue;
                    }

                    // Check if GUID already exists
                    var existingById = await _db.Workouts.FindAsync(workout.Id);
                    if (existingById != null)
                    {
                        result.Statistics.Workouts.Skipped++;
                        result.Warnings.Add($"Workout with GUID {workout.Id} already exists, skipping");
                        continue;
                    }

                    // Validate shoe reference if present
                    if (workout.ShoeId.HasValue)
                    {
                        var shoeExists = await _db.Shoes.AnyAsync(s => s.Id == workout.ShoeId.Value);
                        if (!shoeExists)
                        {
                            result.Warnings.Add($"Workout {workout.Id} references non-existent shoe {workout.ShoeId}, clearing reference");
                            workout.ShoeId = null;
                        }
                    }

                    // Clear navigation properties (they will be imported separately)
                    workout.Shoe = null;
                    workout.Route = null;
                    workout.Splits.Clear();
                    workout.Media.Clear();
                    workout.TimeSeries.Clear();

                    // Import workout
                    _db.Workouts.Add(workout);
                    processedWorkoutIds.Add(workout.Id);
                    processedWorkoutKeys.Add(workoutKey);
                    result.Statistics.Workouts.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.Workouts.Errors++;
                    var workoutInfo = workout != null 
                        ? $"ID: {workout.Id}" 
                        : "null workout";
                    result.Errors.Add($"Error importing workout {workoutInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing workout {WorkoutId}", workout?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} workouts", result.Statistics.Workouts.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading workouts file: {ex.Message}");
            _logger.LogError(ex, "Error reading workouts file");
        }
    }

    private async Task ImportRoutesAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.Routes))
        {
            result.Warnings.Add("Routes file path is missing in export manifest");
            return;
        }

        var routesPath = Path.Combine(tempDir, manifest.DataFormat.Routes);
        if (!File.Exists(routesPath))
        {
            result.Warnings.Add("Routes file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(routesPath);
            var routesData = JsonSerializer.Deserialize<List<RouteImportData>>(json, JsonOptions) ?? new List<RouteImportData>();

            foreach (var routeData in routesData)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (routeData == null)
                {
                    result.Statistics.Routes.Errors++;
                    result.Errors.Add("Null route element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (routeData.Id == Guid.Empty)
                    {
                        result.Statistics.Routes.Errors++;
                        result.Errors.Add($"Invalid GUID for route {routeData.WorkoutId}");
                        continue;
                    }

                    // Validate workout exists
                    var workoutExists = await _db.Workouts.AnyAsync(w => w.Id == routeData.WorkoutId);
                    if (!workoutExists)
                    {
                        result.Statistics.Routes.Skipped++;
                        result.Warnings.Add($"Route references non-existent workout {routeData.WorkoutId}, skipping");
                        continue;
                    }

                    // Check if route already exists by ID
                    var existingById = await _db.WorkoutRoutes.FindAsync(routeData.Id);
                    if (existingById != null)
                    {
                        result.Statistics.Routes.Skipped++;
                        continue;
                    }

                    // Check if a route already exists for this WorkoutId (one-to-one relationship constraint)
                    // This prevents unique constraint violations when importing routes with different IDs
                    // for workouts that already have routes
                    var existingByWorkoutId = await _db.WorkoutRoutes
                        .FirstOrDefaultAsync(r => r.WorkoutId == routeData.WorkoutId);
                    if (existingByWorkoutId != null)
                    {
                        result.Statistics.Routes.Skipped++;
                        result.Warnings.Add($"Workout {routeData.WorkoutId} already has a route (ID: {existingByWorkoutId.Id}), skipping duplicate route (ID: {routeData.Id})");
                        continue;
                    }

                    // Serialize RouteGeoJson object back to JSON string
                    string routeGeoJsonString;
                    if (routeData.RouteGeoJson == null)
                    {
                        routeGeoJsonString = "null";
                    }
                    else
                    {
                        routeGeoJsonString = JsonSerializer.Serialize(routeData.RouteGeoJson, JsonOptions);
                    }

                    var route = new WorkoutRoute
                    {
                        Id = routeData.Id,
                        WorkoutId = routeData.WorkoutId,
                        RouteGeoJson = routeGeoJsonString
                    };

                    _db.WorkoutRoutes.Add(route);
                    result.Statistics.Routes.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.Routes.Errors++;
                    var routeInfo = routeData != null 
                        ? $"ID: {routeData.Id}" 
                        : "null route";
                    result.Errors.Add($"Error importing route {routeInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing route {RouteId}", routeData?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} routes", result.Statistics.Routes.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading routes file: {ex.Message}");
            _logger.LogError(ex, "Error reading routes file");
        }
    }

    private async Task ImportSplitsAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.Splits))
        {
            result.Warnings.Add("Splits file path is missing in export manifest");
            return;
        }

        var splitsPath = Path.Combine(tempDir, manifest.DataFormat.Splits);
        if (!File.Exists(splitsPath))
        {
            result.Warnings.Add("Splits file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(splitsPath);
            var splits = JsonSerializer.Deserialize<List<WorkoutSplit>>(json, JsonOptions) ?? new List<WorkoutSplit>();

            foreach (var split in splits)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (split == null)
                {
                    result.Statistics.Splits.Errors++;
                    result.Errors.Add("Null split element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (split.Id == Guid.Empty)
                    {
                        result.Statistics.Splits.Errors++;
                        result.Errors.Add($"Invalid GUID for split {split.WorkoutId}");
                        continue;
                    }

                    // Validate workout exists
                    var workoutExists = await _db.Workouts.AnyAsync(w => w.Id == split.WorkoutId);
                    if (!workoutExists)
                    {
                        result.Statistics.Splits.Skipped++;
                        result.Warnings.Add($"Split references non-existent workout {split.WorkoutId}, skipping");
                        continue;
                    }

                    // Check if split already exists
                    var existing = await _db.WorkoutSplits.FindAsync(split.Id);
                    if (existing != null)
                    {
                        result.Statistics.Splits.Skipped++;
                        continue;
                    }

                    // Clear navigation property
                    split.Workout = null!;

                    _db.WorkoutSplits.Add(split);
                    result.Statistics.Splits.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.Splits.Errors++;
                    var splitInfo = split != null 
                        ? $"ID: {split.Id}" 
                        : "null split";
                    result.Errors.Add($"Error importing split {splitInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing split {SplitId}", split?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} splits", result.Statistics.Splits.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading splits file: {ex.Message}");
            _logger.LogError(ex, "Error reading splits file");
        }
    }

    private async Task ImportTimeSeriesAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.TimeSeries))
        {
            result.Warnings.Add("Time series file path is missing in export manifest");
            return;
        }

        var timeSeriesPath = Path.Combine(tempDir, manifest.DataFormat.TimeSeries);
        if (!File.Exists(timeSeriesPath))
        {
            result.Warnings.Add("Time series file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(timeSeriesPath);
            var timeSeries = JsonSerializer.Deserialize<List<WorkoutTimeSeries>>(json, JsonOptions) ?? new List<WorkoutTimeSeries>();

            foreach (var ts in timeSeries)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (ts == null)
                {
                    result.Statistics.TimeSeries.Errors++;
                    result.Errors.Add("Null time series element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (ts.Id == Guid.Empty)
                    {
                        result.Statistics.TimeSeries.Errors++;
                        result.Errors.Add($"Invalid GUID for time series {ts.WorkoutId}");
                        continue;
                    }

                    // Validate workout exists
                    var workoutExists = await _db.Workouts.AnyAsync(w => w.Id == ts.WorkoutId);
                    if (!workoutExists)
                    {
                        result.Statistics.TimeSeries.Skipped++;
                        result.Warnings.Add($"Time series references non-existent workout {ts.WorkoutId}, skipping");
                        continue;
                    }

                    // Check if time series already exists
                    var existing = await _db.WorkoutTimeSeries.FindAsync(ts.Id);
                    if (existing != null)
                    {
                        result.Statistics.TimeSeries.Skipped++;
                        continue;
                    }

                    // Clear navigation property
                    ts.Workout = null!;

                    _db.WorkoutTimeSeries.Add(ts);
                    result.Statistics.TimeSeries.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.TimeSeries.Errors++;
                    var tsInfo = ts != null 
                        ? $"ID: {ts.Id}" 
                        : "null time series";
                    result.Errors.Add($"Error importing time series {tsInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing time series {TimeSeriesId}", ts?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} time series records", result.Statistics.TimeSeries.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading time series file: {ex.Message}");
            _logger.LogError(ex, "Error reading time series file");
        }
    }

    private async Task ImportBestEffortsAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.BestEfforts))
        {
            result.Warnings.Add("Best efforts file path is missing in export manifest");
            return;
        }

        var bestEffortsPath = Path.Combine(tempDir, manifest.DataFormat.BestEfforts);
        if (!File.Exists(bestEffortsPath))
        {
            result.Warnings.Add("Best efforts file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(bestEffortsPath);
            var bestEfforts = JsonSerializer.Deserialize<List<BestEffort>>(json, JsonOptions) ?? new List<BestEffort>();

            // Track best efforts we've already processed in this batch to avoid duplicates within the same import
            var processedBestEffortIds = new HashSet<Guid>();
            var processedDistances = new HashSet<string>();

            foreach (var bestEffort in bestEfforts)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (bestEffort == null)
                {
                    result.Statistics.BestEfforts.Errors++;
                    result.Errors.Add("Null best effort element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (bestEffort.Id == Guid.Empty)
                    {
                        result.Statistics.BestEfforts.Errors++;
                        result.Errors.Add($"Invalid GUID for best effort {bestEffort.Distance}");
                        continue;
                    }

                    // Check if we've already processed this best effort in the current batch
                    if (processedBestEffortIds.Contains(bestEffort.Id))
                    {
                        result.Statistics.BestEfforts.Skipped++;
                        result.Warnings.Add($"Duplicate best effort in export (same GUID): {bestEffort.Distance}");
                        continue;
                    }

                    // Check if we've already processed this distance in the current batch
                    if (processedDistances.Contains(bestEffort.Distance))
                    {
                        result.Statistics.BestEfforts.Skipped++;
                        result.Warnings.Add($"Duplicate best effort in export (same Distance): {bestEffort.Distance}");
                        continue;
                    }

                    // Validate workout exists
                    var workoutExists = await _db.Workouts.AnyAsync(w => w.Id == bestEffort.WorkoutId);
                    if (!workoutExists)
                    {
                        result.Statistics.BestEfforts.Skipped++;
                        result.Warnings.Add($"Best effort references non-existent workout {bestEffort.WorkoutId}, skipping");
                        continue;
                    }

                    // Check for duplicate by Distance (unique constraint) in database
                    var existing = await _db.BestEfforts
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Distance == bestEffort.Distance);

                    if (existing != null)
                    {
                        result.Statistics.BestEfforts.Skipped++;
                        result.Warnings.Add($"Best effort for {bestEffort.Distance} already exists, skipping");
                        continue;
                    }

                    // Check if GUID already exists in database
                    var existingById = await _db.BestEfforts
                        .AsNoTracking()
                        .FirstOrDefaultAsync(b => b.Id == bestEffort.Id);
                    if (existingById != null)
                    {
                        result.Statistics.BestEfforts.Skipped++;
                        result.Warnings.Add($"Best effort with GUID {bestEffort.Id} already exists, skipping");
                        continue;
                    }

                    // Clear navigation property
                    bestEffort.Workout = null!;

                    _db.BestEfforts.Add(bestEffort);
                    processedBestEffortIds.Add(bestEffort.Id);
                    processedDistances.Add(bestEffort.Distance);
                    result.Statistics.BestEfforts.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.BestEfforts.Errors++;
                    var bestEffortInfo = bestEffort != null 
                        ? $"ID: {bestEffort.Id}" 
                        : "null best effort";
                    result.Errors.Add($"Error importing best effort {bestEffortInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing best effort {BestEffortId}", bestEffort?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} best efforts", result.Statistics.BestEfforts.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading best efforts file: {ex.Message}");
            _logger.LogError(ex, "Error reading best efforts file");
        }
    }

    private async Task ImportMediaFilesAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        if (string.IsNullOrEmpty(manifest.DataFormat.MediaMetadata))
        {
            result.Warnings.Add("Media metadata file path is missing in export manifest");
            return;
        }

        var mediaMetadataPath = Path.Combine(tempDir, manifest.DataFormat.MediaMetadata);
        if (!File.Exists(mediaMetadataPath))
        {
            result.Warnings.Add("Media metadata file not found in export");
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(mediaMetadataPath);
            var mediaMetadata = JsonSerializer.Deserialize<List<WorkoutMedia>>(json, JsonOptions) ?? new List<WorkoutMedia>();

            foreach (var media in mediaMetadata)
            {
                // Skip null elements (can occur when deserializing JSON arrays)
                if (media == null)
                {
                    result.Statistics.Media.Errors++;
                    result.Errors.Add("Null media element found in export, skipping");
                    continue;
                }

                try
                {
                    // Validate GUID
                    if (media.Id == Guid.Empty)
                    {
                        result.Statistics.Media.Errors++;
                        result.Errors.Add($"Invalid GUID for media {media.Filename}");
                        continue;
                    }

                    // Validate workout exists
                    var workoutExists = await _db.Workouts.AnyAsync(w => w.Id == media.WorkoutId);
                    if (!workoutExists)
                    {
                        result.Statistics.Media.Skipped++;
                        result.Warnings.Add($"Media references non-existent workout {media.WorkoutId}, skipping");
                        continue;
                    }

                    // Check if media already exists
                    var existing = await _db.WorkoutMedia.FindAsync(media.Id);
                    if (existing != null)
                    {
                        result.Statistics.Media.Skipped++;
                        result.Warnings.Add($"Media with GUID {media.Id} already exists, skipping");
                        continue;
                    }

                    // Find media file in ZIP
                    var mediaZipPath = Path.Combine(tempDir, "workouts", media.WorkoutId.ToString(), "media", media.Id.ToString(), media.Filename);
                    if (!File.Exists(mediaZipPath))
                    {
                        result.Statistics.Media.Skipped++;
                        result.Warnings.Add($"Media file not found in ZIP: workouts/{media.WorkoutId}/media/{media.Id}/{media.Filename}");
                        continue;
                    }

                    // Copy media file using MediaService
                    var copiedMedia = _mediaService.CopyMediaFile(mediaZipPath, media.WorkoutId, media.Caption);
                    if (copiedMedia == null)
                    {
                        result.Statistics.Media.Errors++;
                        result.Errors.Add($"Failed to copy media file: {media.Filename}");
                        continue;
                    }

                    // Update with original GUID and metadata
                    copiedMedia.Id = media.Id;
                    copiedMedia.CreatedAt = media.CreatedAt;

                    _db.WorkoutMedia.Add(copiedMedia);
                    result.Statistics.Media.Imported++;
                }
                catch (Exception ex)
                {
                    result.Statistics.Media.Errors++;
                    var mediaInfo = media != null 
                        ? $"ID: {media.Id}" 
                        : "null media";
                    result.Errors.Add($"Error importing media {mediaInfo}: {ex.Message}");
                    _logger.LogError(ex, "Error importing media {MediaId}", media?.Id);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} media files", result.Statistics.Media.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error reading media metadata file: {ex.Message}");
            _logger.LogError(ex, "Error reading media metadata file");
        }
    }

    private async Task ImportRawFilesAsync(string tempDir, ExportManifest manifest, ImportResult result)
    {
        // Raw files are stored in workouts/{workoutId}/raw/{filename}
        var workoutsDir = Path.Combine(tempDir, "workouts");
        if (!Directory.Exists(workoutsDir))
        {
            return;
        }

        try
        {
            var workoutDirs = Directory.GetDirectories(workoutsDir);
            foreach (var workoutDir in workoutDirs)
            {
                var workoutIdStr = Path.GetFileName(workoutDir);
                if (!Guid.TryParse(workoutIdStr, out var workoutId))
                {
                    continue;
                }

                var rawDir = Path.Combine(workoutDir, "raw");
                if (!Directory.Exists(rawDir))
                {
                    continue;
                }

                var rawFiles = Directory.GetFiles(rawDir);
                if (rawFiles.Length == 0)
                {
                    continue;
                }

                // Find workout in database
                var workout = await _db.Workouts.FindAsync(workoutId);
                if (workout == null)
                {
                    result.Warnings.Add($"Raw file found for non-existent workout {workoutId}, skipping");
                    continue;
                }

                // Only process first raw file (workouts typically have one raw file)
                var rawFilePath = rawFiles[0];
                var rawFileName = Path.GetFileName(rawFilePath);
                var rawFileType = Path.GetExtension(rawFilePath).TrimStart('.').ToLowerInvariant();

                try
                {
                    var rawFileData = await File.ReadAllBytesAsync(rawFilePath);
                    workout.RawFileData = rawFileData;
                    workout.RawFileName = rawFileName;
                    workout.RawFileType = rawFileType;

                    result.Statistics.RawFiles.Imported++;
                    _logger.LogInformation("Imported raw file for workout {WorkoutId}: {FileName}", workoutId, rawFileName);
                }
                catch (Exception ex)
                {
                    result.Statistics.RawFiles.Errors++;
                    result.Errors.Add($"Error reading raw file {rawFileName} for workout {workoutId}: {ex.Message}");
                    _logger.LogError(ex, "Error reading raw file {FileName} for workout {WorkoutId}", rawFileName, workoutId);
                }
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Imported {Count} raw files", result.Statistics.RawFiles.Imported);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Error processing raw files: {ex.Message}");
            _logger.LogError(ex, "Error processing raw files");
        }
    }

    // Helper classes for deserialization
    public class ExportManifest
    {
        public string Version { get; set; } = string.Empty;
        public string TempoVersion { get; set; } = string.Empty;
        public DateTime ExportDate { get; set; }
        public string ExportedBy { get; set; } = string.Empty;
        public ExportStatistics? Statistics { get; set; }
        public ExportDataFormat? DataFormat { get; set; }
    }

    public class ExportStatistics
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

    public class ExportDataFormat
    {
        public string? Settings { get; set; }
        public string Shoes { get; set; } = string.Empty;
        public string Workouts { get; set; } = string.Empty;
        public string Routes { get; set; } = string.Empty;
        public string Splits { get; set; } = string.Empty;
        public string TimeSeries { get; set; } = string.Empty;
        public string MediaMetadata { get; set; } = string.Empty;
        public string BestEfforts { get; set; } = string.Empty;
    }

    private class RouteImportData
    {
        public Guid Id { get; set; }
        public Guid WorkoutId { get; set; }
        public object? RouteGeoJson { get; set; }
    }

    public class ImportResult
    {
        public bool Success { get; set; }
        public DateTime ImportedAt { get; set; }
        public ExportManifest? Manifest { get; set; }
        public ImportStatistics Statistics { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class ImportStatistics
    {
        public ItemStatistics Settings { get; set; } = new();
        public ItemStatistics Shoes { get; set; } = new();
        public ItemStatistics Workouts { get; set; } = new();
        public ItemStatistics Routes { get; set; } = new();
        public ItemStatistics Splits { get; set; } = new();
        public ItemStatistics TimeSeries { get; set; } = new();
        public ItemStatistics Media { get; set; } = new();
        public ItemStatistics BestEfforts { get; set; } = new();
        public ItemStatistics RawFiles { get; set; } = new();
    }

    public class ItemStatistics
    {
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
    }
}

