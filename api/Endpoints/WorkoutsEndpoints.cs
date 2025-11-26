using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class WorkoutsEndpoints
{
    // Result class for file processing
    private class FileProcessResult
    {
        public string Action { get; set; } = "created"; // "created", "updated", "skipped", "error"
        public string? ErrorMessage { get; set; }
        public object? Response { get; set; }
    }
    public static void MapWorkoutsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/workouts")
            .WithTags("Workouts");

        group.MapPost("/import", async (
            HttpRequest request,
            TempoDbContext db,
            GpxParserService gpxParser,
            FitParserService fitParser,
            WeatherService weatherService,
            HeartRateZoneService zoneService,
            RelativeEffortService relativeEffortService,
            ILogger<Program> logger) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data" });
            }

            var form = await request.ReadFormAsync();
            var files = form.Files.GetFiles("file");

            if (files == null || files.Count == 0)
            {
                return Results.BadRequest(new { error = "No files uploaded" });
            }

            // Read unit preference from form (default to "metric" for backward compatibility)
            var unitPreference = form["unitPreference"].ToString();
            if (string.IsNullOrWhiteSpace(unitPreference))
            {
                unitPreference = "metric";
            }

            // Calculate split distance based on unit preference
            // 1000.0 meters = 1 km for metric, 1609.344 meters = 1 mile for imperial
            var splitDistanceMeters = unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
                ? 1609.344
                : 1000.0;

            // Handle single file for backward compatibility
            if (files.Count == 1)
            {
                var singleFileResult = await ProcessSingleFile(
                    files[0],
                    db,
                    gpxParser,
                    fitParser,
                    weatherService,
                    zoneService,
                    relativeEffortService,
                    splitDistanceMeters,
                    logger);

                // Return single file response format for backward compatibility
                if (singleFileResult.Action == "error")
                {
                    return Results.BadRequest(new { error = singleFileResult.ErrorMessage ?? "Error processing file" });
                }
                return Results.Ok(singleFileResult.Response);
            }

            // Handle multiple files
            var successful = 0;
            var skipped = 0;
            var updated = 0;
            var errors = new List<object>();
            var totalProcessed = files.Count;

            foreach (var file in files)
            {
                try
                {
                    var result = await ProcessSingleFile(
                        file,
                        db,
                        gpxParser,
                        fitParser,
                        weatherService,
                        zoneService,
                        relativeEffortService,
                        splitDistanceMeters,
                        logger);

                    if (result.Action == "error")
                    {
                        errors.Add(new { filename = file.FileName, error = result.ErrorMessage ?? "Unknown error" });
                    }
                    else if (result.Action == "created")
                    {
                        successful++;
                    }
                    else if (result.Action == "updated")
                    {
                        updated++;
                    }
                    else if (result.Action == "skipped")
                    {
                        skipped++;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing file {FileName}", file.FileName);
                    errors.Add(new { filename = file.FileName, error = ex.Message });
                }
            }

            return Results.Ok(new
            {
                totalProcessed,
                successful,
                skipped,
                updated,
                errors = errors.Count,
                errorDetails = errors
            });
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .WithSummary("Import workout file(s)")
        .WithDescription("Uploads and processes one or more GPX or FIT files (.gpx, .fit, or .fit.gz), extracting workout data and saving it to the database. Supports multiple files for batch import.");

        group.MapGet("", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] double? minDistanceM = null,
            [FromQuery] double? maxDistanceM = null,
            [FromQuery] string? keyword = null,
            [FromQuery] string? runType = null,
            [FromQuery] string? sortBy = null,
            [FromQuery] string? sortOrder = null) =>
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

            // No default date filter - callers should explicitly pass date ranges if needed
            // This allows the activities page to show all activities by default

            // Normalize dates to UTC for PostgreSQL compatibility
            if (startDate.HasValue)
            {
                var start = startDate.Value;
                if (start.Kind == DateTimeKind.Unspecified)
                {
                    start = DateTime.SpecifyKind(start, DateTimeKind.Utc);
                }
                else if (start.Kind == DateTimeKind.Local)
                {
                    start = start.ToUniversalTime();
                }
                startDate = start.Date; // Ensure start of day
            }

            if (endDate.HasValue)
            {
                var end = endDate.Value;
                if (end.Kind == DateTimeKind.Unspecified)
                {
                    end = DateTime.SpecifyKind(end, DateTimeKind.Utc);
                }
                else if (end.Kind == DateTimeKind.Local)
                {
                    end = end.ToUniversalTime();
                }
                endDate = end.Date.AddDays(1).AddTicks(-1); // End of day (23:59:59.999)
            }

            // Build query
            var query = db.Workouts
                .Include(w => w.Route)
                .Include(w => w.Splits)
                .AsQueryable();

            // Apply filters
            if (startDate.HasValue)
            {
                query = query.Where(w => w.StartedAt >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(w => w.StartedAt <= endDate.Value);
            }

            if (minDistanceM.HasValue)
            {
                query = query.Where(w => w.DistanceM >= minDistanceM.Value);
            }

            if (maxDistanceM.HasValue)
            {
                query = query.Where(w => w.DistanceM <= maxDistanceM.Value);
            }

            // Apply keyword search (case-insensitive partial matching across Name, Device, and Source)
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                var keywordPattern = $"%{keyword}%";
                query = query.Where(w =>
                    (w.Name != null && EF.Functions.ILike(w.Name, keywordPattern)) ||
                    (w.Device != null && EF.Functions.ILike(w.Device, keywordPattern)) ||
                    (w.Source != null && EF.Functions.ILike(w.Source, keywordPattern))
                );
            }

            // Apply runType filter
            if (!string.IsNullOrWhiteSpace(runType))
            {
                query = query.Where(w => w.RunType == runType);
            }

            // Get total count before pagination
            var totalCount = await query.CountAsync();

            // Calculate total pages
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Validate page number
            if (page > totalPages && totalPages > 0)
            {
                return Results.NotFound(new { error = "Page not found" });
            }

            // Apply dynamic sorting
            var isDescending = sortOrder?.ToLower() == "desc" || (string.IsNullOrWhiteSpace(sortOrder) && string.IsNullOrWhiteSpace(sortBy));
            var sortByLower = sortBy?.ToLower();

            if (sortByLower == "name")
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.Name ?? "")
                    : query.OrderBy(w => w.Name ?? "");
            }
            else if (sortByLower == "duration" || sortByLower == "durations")
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.DurationS)
                    : query.OrderBy(w => w.DurationS);
            }
            else if (sortByLower == "distance" || sortByLower == "distancem")
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.DistanceM)
                    : query.OrderBy(w => w.DistanceM);
            }
            else if (sortByLower == "elevation" || sortByLower == "elevgainm")
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.ElevGainM ?? 0)
                    : query.OrderBy(w => w.ElevGainM ?? 0);
            }
            else if (sortByLower == "relativeeffort" || sortByLower == "relative-effort")
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.RelativeEffort ?? 0)
                    : query.OrderBy(w => w.RelativeEffort ?? 0);
            }
            else // Default: sort by startedAt
            {
                query = isDescending
                    ? query.OrderByDescending(w => w.StartedAt)
                    : query.OrderBy(w => w.StartedAt);
            }

            // Apply pagination
            var workouts = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            // Map to response
            var items = workouts.Select(w =>
            {
                // Parse route GeoJSON if exists
                object? routeGeoJson = null;
                if (w.Route != null && !string.IsNullOrEmpty(w.Route.RouteGeoJson))
                {
                    try
                    {
                        routeGeoJson = JsonSerializer.Deserialize<object>(w.Route.RouteGeoJson);
                    }
                    catch (JsonException ex)
                    {
                        logger.LogWarning(ex, "Failed to parse route GeoJSON for workout {WorkoutId}", w.Id);
                    }
                }

                return new
                {
                    id = w.Id,
                    startedAt = w.StartedAt,
                    durationS = w.DurationS,
                    distanceM = w.DistanceM,
                    avgPaceS = w.AvgPaceS,
                    elevGainM = w.ElevGainM,
                    elevLossM = w.ElevLossM,
                    minElevM = w.MinElevM,
                    maxElevM = w.MaxElevM,
                    maxSpeedMps = w.MaxSpeedMps,
                    avgSpeedMps = w.AvgSpeedMps,
                    movingTimeS = w.MovingTimeS,
                    maxHeartRateBpm = w.MaxHeartRateBpm,
                    avgHeartRateBpm = w.AvgHeartRateBpm,
                    minHeartRateBpm = w.MinHeartRateBpm,
                    maxCadenceRpm = w.MaxCadenceRpm,
                    avgCadenceRpm = w.AvgCadenceRpm,
                    maxPowerWatts = w.MaxPowerWatts,
                    avgPowerWatts = w.AvgPowerWatts,
                    calories = w.Calories,
                    relativeEffort = w.RelativeEffort,
                    runType = w.RunType,
                    source = w.Source,
                    device = w.Device,
                    name = w.Name,
                    hasRoute = w.Route != null,
                    route = routeGeoJson,
                    splitsCount = w.Splits.Count
                };
            }).ToList();

            return Results.Ok(new
            {
                items,
                totalCount,
                page,
                pageSize,
                totalPages
            });
        })
        .Produces(200)
        .Produces(404)
        .WithSummary("List workouts")
        .WithDescription("Returns a paginated list of workouts with optional filtering");

        // Media routes must come before the generic /{id:guid} route to ensure proper routing
        group.MapPost("/{id:guid}/media", async (
            Guid id,
            HttpRequest request,
            TempoDbContext db,
            MediaService mediaService,
            ILogger<Program> logger) =>
        {
            // Verify workout exists
            var workoutExists = await db.Workouts.AnyAsync(w => w.Id == id);
            if (!workoutExists)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data" });
            }

            var form = await request.ReadFormAsync();
            var files = form.Files.GetFiles("files");

            if (files == null || files.Count == 0)
            {
                return Results.BadRequest(new { error = "No files provided" });
            }

            var uploadedMedia = new List<WorkoutMedia>();
            var errors = new List<object>();

            // Process each file
            foreach (var file in files)
            {
                try
                {
                    var mediaRecord = mediaService.UploadMediaFile(file, id);
                    if (mediaRecord != null)
                    {
                        uploadedMedia.Add(mediaRecord);
                        logger.LogInformation("Uploaded media file {FileName} for workout {WorkoutId}", 
                            file.FileName, id);
                    }
                    else
                    {
                        errors.Add(new { filename = file.FileName, error = "Failed to process file" });
                        logger.LogWarning("Failed to upload media file {FileName} for workout {WorkoutId}", 
                            file.FileName, id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error uploading media file {FileName} for workout {WorkoutId}", 
                        file.FileName, id);
                    errors.Add(new { filename = file.FileName, error = ex.Message });
                }
            }

            // If no files were successfully uploaded, return error
            if (uploadedMedia.Count == 0)
            {
                return Results.BadRequest(new 
                { 
                    error = "No files were successfully uploaded",
                    errors = errors
                });
            }

            // Batch insert all successfully uploaded media records
            db.WorkoutMedia.AddRange(uploadedMedia);
            await db.SaveChangesAsync();

            logger.LogInformation("Successfully uploaded {Count} media files for workout {WorkoutId}", 
                uploadedMedia.Count, id);

            // Return uploaded media metadata
            var response = uploadedMedia.Select(m => new
            {
                id = m.Id,
                filename = m.Filename,
                mimeType = m.MimeType,
                fileSizeBytes = m.FileSizeBytes,
                caption = m.Caption,
                createdAt = m.CreatedAt
            }).ToList();

            // Include errors if any files failed
            if (errors.Count > 0)
            {
                return Results.Ok(new
                {
                    uploaded = response,
                    errors = errors,
                    successCount = uploadedMedia.Count,
                    errorCount = errors.Count
                });
            }

            return Results.Ok(response);
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Upload media files to workout")
        .WithDescription("Uploads one or more media files (images/videos) to a workout");

        group.MapDelete("/{id:guid}/media/{mediaId:guid}", async (
            Guid id,
            Guid mediaId,
            TempoDbContext db,
            ILogger<Program> logger) =>
        {
            // Verify workout exists
            var workoutExists = await db.Workouts.AnyAsync(w => w.Id == id);
            if (!workoutExists)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Get media record
            var media = await db.WorkoutMedia
                .FirstOrDefaultAsync(m => m.Id == mediaId && m.WorkoutId == id);

            if (media == null)
            {
                return Results.NotFound(new { error = "Media not found" });
            }

            // Delete file from filesystem
            try
            {
                if (File.Exists(media.FilePath))
                {
                    File.Delete(media.FilePath);
                    logger.LogInformation("Deleted media file from filesystem: {FilePath}", media.FilePath);
                }
                else
                {
                    logger.LogWarning("Media file not found on filesystem (orphaned record): {FilePath}", media.FilePath);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error deleting media file from filesystem: {FilePath}", media.FilePath);
                // Continue with database deletion even if file deletion fails
            }

            // Delete database record
            db.WorkoutMedia.Remove(media);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted media {MediaId} for workout {WorkoutId}", mediaId, id);

            return Results.NoContent();
        })
        .Produces(204)
        .Produces(404)
        .WithSummary("Delete workout media")
        .WithDescription("Deletes a media file from a workout (removes file from filesystem and database record)");

        group.MapGet("/{id:guid}/media/{mediaId:guid}", async (
            Guid id,
            Guid mediaId,
            TempoDbContext db,
            ILogger<Program> logger) =>
        {
            // Verify workout exists
            var workoutExists = await db.Workouts.AnyAsync(w => w.Id == id);
            if (!workoutExists)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Get media record
            var media = await db.WorkoutMedia
                .FirstOrDefaultAsync(m => m.Id == mediaId && m.WorkoutId == id);

            if (media == null)
            {
                return Results.NotFound(new { error = "Media not found" });
            }

            // Verify file exists on filesystem
            if (!File.Exists(media.FilePath))
            {
                logger.LogWarning("Media file not found on filesystem: {FilePath}", media.FilePath);
                return Results.NotFound(new { error = "Media file not found" });
            }

            // Return file with appropriate content type
            var fileStream = File.OpenRead(media.FilePath);
            return Results.File(
                fileStream,
                contentType: media.MimeType,
                fileDownloadName: media.Filename,
                enableRangeProcessing: true // Support range requests for video seeking
            );
        })
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout media file")
        .WithDescription("Retrieves and serves a specific media file for a workout");

        group.MapGet("/{id:guid}/media", async (
            Guid id,
            TempoDbContext db,
            ILogger<Program> logger) =>
        {
            logger.LogInformation("Fetching media for workout {WorkoutId}", id);

            // Verify workout exists
            var workoutExists = await db.Workouts.AnyAsync(w => w.Id == id);
            if (!workoutExists)
            {
                logger.LogWarning("Workout {WorkoutId} not found", id);
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Check total media count in database for debugging
            var totalMediaCount = await db.WorkoutMedia.CountAsync();
            logger.LogInformation("Total media records in database: {TotalCount}", totalMediaCount);

            // Get all media for this workout
            var media = await db.WorkoutMedia
                .Where(m => m.WorkoutId == id)
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    id = m.Id,
                    filename = m.Filename,
                    mimeType = m.MimeType,
                    fileSizeBytes = m.FileSizeBytes,
                    caption = m.Caption,
                    createdAt = m.CreatedAt
                })
                .ToListAsync();

            logger.LogInformation("Found {MediaCount} media records for workout {WorkoutId}", media.Count, id);
            if (media.Count > 0)
            {
                logger.LogInformation("Media filenames: {Filenames}", string.Join(", ", media.Select(m => m.filename)));
            }

            return Results.Ok(media);
        })
        .Produces(200)
        .Produces(404)
        .WithSummary("List workout media")
        .WithDescription("Retrieves all media files associated with a workout");

        group.MapPost("/{id:guid}/recalculate-effort", async (
            Guid id,
            TempoDbContext db,
            HeartRateZoneService zoneService,
            RelativeEffortService relativeEffortService,
            ILogger<Program> logger) =>
        {
            var workout = await db.Workouts
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workout == null)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            try
            {
                var settings = await db.UserSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    return Results.BadRequest(new { error = "Heart rate zones not configured. Please configure heart rate zones in settings first." });
                }

                var zones = zoneService.GetZonesFromUserSettings(settings);
                var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                
                workout.RelativeEffort = relativeEffort;
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    id = workout.Id,
                    relativeEffort = workout.RelativeEffort
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error recalculating Relative Effort for workout {WorkoutId}", id);
                return Results.Problem("Failed to recalculate Relative Effort");
            }
        })
        .Produces(200)
        .Produces(404)
        .Produces(400)
        .WithSummary("Recalculate Relative Effort")
        .WithDescription("Recalculates the Relative Effort score for a workout using current heart rate zone settings");

        group.MapGet("/{id:guid}", async (
            Guid id,
            TempoDbContext db,
            WeatherService weatherService,
            ILogger<Program> logger) =>
        {
            var workout = await db.Workouts
                .Include(w => w.Route)
                .Include(w => w.Splits.OrderBy(s => s.Idx))
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workout == null)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Parse route GeoJSON if exists
            object? routeGeoJson = null;
            if (workout.Route != null && !string.IsNullOrEmpty(workout.Route.RouteGeoJson))
            {
                try
                {
                    routeGeoJson = JsonSerializer.Deserialize<object>(workout.Route.RouteGeoJson);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse route GeoJSON for workout {WorkoutId}", workout.Id);
                }
            }

            // Parse weather JSON if exists and normalize humidity values
            object? weather = null;
            if (!string.IsNullOrEmpty(workout.Weather))
            {
                try
                {
                    var weatherElement = JsonSerializer.Deserialize<JsonElement>(workout.Weather);
                    var weatherDict = new Dictionary<string, object>();
                    
                    foreach (var prop in weatherElement.EnumerateObject())
                    {
                        var value = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString() ?? (object)string.Empty,
                            JsonValueKind.Number => prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Null => null!,
                            _ => prop.Value.GetRawText()
                        };
                        weatherDict[prop.Name] = value;
                    }
                    
                    // Normalize humidity field names and values
                    if (weatherDict.ContainsKey("relativeHumidity") && !weatherDict.ContainsKey("humidity"))
                    {
                        weatherDict["humidity"] = WeatherService.NormalizeHumidityValue(weatherDict["relativeHumidity"]);
                        weatherDict.Remove("relativeHumidity");
                    }
                    else if (weatherDict.ContainsKey("humidity"))
                    {
                        weatherDict["humidity"] = WeatherService.NormalizeHumidityValue(weatherDict["humidity"]);
                    }
                    
                    weather = weatherDict;
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse weather JSON for workout {WorkoutId}", workout.Id);
                }
            }

            // Map splits
            var splits = workout.Splits.Select(s => new
            {
                idx = s.Idx,
                distanceM = s.DistanceM,
                durationS = s.DurationS,
                paceS = s.PaceS
            }).ToList();

            // Parse raw data JSON if exists
            object? rawGpxData = null;
            if (!string.IsNullOrEmpty(workout.RawGpxData))
            {
                try
                {
                    rawGpxData = JsonSerializer.Deserialize<object>(workout.RawGpxData);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse RawGpxData JSON for workout {WorkoutId}", workout.Id);
                }
            }

            object? rawFitData = null;
            if (!string.IsNullOrEmpty(workout.RawFitData))
            {
                try
                {
                    rawFitData = JsonSerializer.Deserialize<object>(workout.RawFitData);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse RawFitData JSON for workout {WorkoutId}", workout.Id);
                }
            }

            object? rawStravaData = null;
            if (!string.IsNullOrEmpty(workout.RawStravaData))
            {
                try
                {
                    rawStravaData = JsonSerializer.Deserialize<object>(workout.RawStravaData);
                }
                catch (JsonException ex)
                {
                    logger.LogWarning(ex, "Failed to parse RawStravaData JSON for workout {WorkoutId}", workout.Id);
                }
            }

            return Results.Ok(new
            {
                id = workout.Id,
                startedAt = workout.StartedAt,
                durationS = workout.DurationS,
                distanceM = workout.DistanceM,
                avgPaceS = workout.AvgPaceS,
                elevGainM = workout.ElevGainM,
                elevLossM = workout.ElevLossM,
                minElevM = workout.MinElevM,
                maxElevM = workout.MaxElevM,
                maxSpeedMps = workout.MaxSpeedMps,
                avgSpeedMps = workout.AvgSpeedMps,
                movingTimeS = workout.MovingTimeS,
                maxHeartRateBpm = workout.MaxHeartRateBpm,
                avgHeartRateBpm = workout.AvgHeartRateBpm,
                minHeartRateBpm = workout.MinHeartRateBpm,
                maxCadenceRpm = workout.MaxCadenceRpm,
                avgCadenceRpm = workout.AvgCadenceRpm,
                maxPowerWatts = workout.MaxPowerWatts,
                avgPowerWatts = workout.AvgPowerWatts,
                calories = workout.Calories,
                relativeEffort = workout.RelativeEffort,
                runType = workout.RunType,
                notes = workout.Notes,
                source = workout.Source,
                device = workout.Device,
                name = workout.Name,
                weather = weather,
                rawGpxData = rawGpxData,
                rawFitData = rawFitData,
                rawStravaData = rawStravaData,
                createdAt = workout.CreatedAt,
                route = routeGeoJson,
                splits = splits
            });
        })
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout details")
        .WithDescription("Retrieves complete workout data including route and splits");

        group.MapPost("/import/bulk", async (
            HttpRequest request,
            TempoDbContext db,
            GpxParserService gpxParser,
            StravaCsvParserService csvParser,
            FitParserService fitParser,
            MediaService mediaService,
            WeatherService weatherService,
            HeartRateZoneService zoneService,
            RelativeEffortService relativeEffortService,
            ILogger<Program> logger) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data" });
            }

            // Enable request body buffering to ensure the full request is received before processing
            // Use 500MB buffer size to match MaxRequestBodySize and MultipartBodyLengthLimit
            request.EnableBuffering(500_000_000);

            Microsoft.AspNetCore.Http.IFormCollection form;
            try
            {
                form = await request.ReadFormAsync();
            }
            catch (Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException ex) when (ex.Message.Contains("Unexpected end of request content"))
            {
                logger.LogError(ex, "Request body was incomplete or connection was closed prematurely during bulk import");
                return Results.BadRequest(new { error = "Upload failed: The request was incomplete. This may be due to a timeout or connection issue. Please try again with a stable connection." });
            }

            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File must be a ZIP file" });
            }

            // Read unit preference from form (default to "metric" for backward compatibility)
            var unitPreference = form["unitPreference"].ToString();
            if (string.IsNullOrWhiteSpace(unitPreference))
            {
                unitPreference = "metric";
            }

            // Calculate split distance based on unit preference
            // 1000.0 meters = 1 km for metric, 1609.344 meters = 1 mile for imperial
            var splitDistanceMeters = unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
                ? 1609.344
                : 1000.0;

            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            var errors = new List<object>();
            var successful = 0;
            var skipped = 0;
            var updated = 0;
            var totalProcessed = 0;

            try
            {
                Directory.CreateDirectory(tempDir);

                // Extract ZIP file
                using (var zipStream = file.OpenReadStream())
                using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        var entryPath = Path.Combine(tempDir, entry.FullName);
                        var entryDir = Path.GetDirectoryName(entryPath);
                        if (!string.IsNullOrEmpty(entryDir))
                        {
                            Directory.CreateDirectory(entryDir);
                        }

                        if (!string.IsNullOrEmpty(entry.Name))
                        {
                            using (var entryStream = entry.Open())
                            using (var fileStream = new FileStream(entryPath, FileMode.Create))
                            {
                                entryStream.CopyTo(fileStream);
                            }
                        }
                    }
                }

                // Find and parse activities.csv
                var csvPath = Path.Combine(tempDir, "activities.csv");
                if (!File.Exists(csvPath))
                {
                    return Results.BadRequest(new { error = "ZIP file must contain activities.csv in the root" });
                }

                List<StravaCsvParserService.StravaActivityRecord> allActivities;
                using (var csvStream = File.OpenRead(csvPath))
                {
                    allActivities = csvParser.ParseActivitiesCsv(csvStream);
                }

                // Filter for Run activities only
                var runActivities = csvParser.GetRunActivities(allActivities);
                totalProcessed = runActivities.Count;

                logger.LogInformation("Found {Total} run activities to process", totalProcessed);

                // Process each activity file
                var workoutsToAdd = new List<Workout>();
                var routesToAdd = new List<WorkoutRoute>();
                var splitsToAdd = new List<WorkoutSplit>();
                var mediaToAdd = new List<WorkoutMedia>();

                foreach (var activity in runActivities)
                {
                    try
                    {
                        var filePath = Path.Combine(tempDir, activity.Filename.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(filePath))
                        {
                            errors.Add(new { filename = activity.Filename, error = "File not found in ZIP archive" });
                            continue;
                        }

                        // Read file into byte array before parsing
                        byte[] rawFileData;
                        using (var fileStream = File.OpenRead(filePath))
                        using (var memoryStream = new MemoryStream())
                        {
                            await fileStream.CopyToAsync(memoryStream);
                            rawFileData = memoryStream.ToArray();
                        }

                        // Determine file type
                        string? fileType = null;
                        if (filePath.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "gpx";
                        }
                        else if (filePath.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "fit";
                        }
                        else if (filePath.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "fit";
                        }
                        else
                        {
                            errors.Add(new { filename = activity.Filename, error = "Unsupported file format. Only .gpx and .fit/.fit.gz files are supported." });
                            continue;
                        }

                        // Parse the activity file
                        GpxParserService.GpxParseResult? parseResult = null;
                        FitParserService.FitParseResult? fitResult = null;

                        if (fileType == "gpx")
                        {
                            using (var stream = new MemoryStream(rawFileData))
                            {
                                parseResult = gpxParser.ParseGpx(stream);
                            }
                        }
                        else if (fileType == "fit")
                        {
                            try
                            {
                                using (var stream = new MemoryStream(rawFileData))
                                {
                                    if (filePath.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
                                    {
                                        fitResult = fitParser.ParseGzippedFit(stream);
                                    }
                                    else
                                    {
                                        fitResult = fitParser.ParseFit(stream);
                                    }
                                }
                            }
                            catch (NotSupportedException ex)
                            {
                                errors.Add(new { filename = activity.Filename, error = ex.Message });
                                continue;
                            }
                        }

                        // Use GPX result or convert FIT result
                        DateTime startTime;
                        int durationSeconds;
                        double distanceMeters;
                        double? elevationGainMeters;
                        List<GpxParserService.GpxPoint> trackPoints;

                        if (parseResult != null)
                        {
                            startTime = parseResult.StartTime;
                            durationSeconds = parseResult.DurationSeconds;
                            distanceMeters = parseResult.DistanceMeters;
                            elevationGainMeters = parseResult.ElevationGainMeters;
                            trackPoints = parseResult.TrackPoints;
                        }
                        else if (fitResult != null)
                        {
                            startTime = fitResult.StartTime;
                            durationSeconds = fitResult.DurationSeconds;
                            distanceMeters = fitResult.DistanceMeters;
                            elevationGainMeters = fitResult.ElevationGainMeters;
                            trackPoints = fitResult.TrackPoints;
                        }
                        else
                        {
                            errors.Add(new { filename = activity.Filename, error = "Failed to parse file" });
                            continue;
                        }

                        // Check for duplicate using database query
                        var existingWorkout = await db.Workouts
                            .Where(w => w.StartedAt == startTime &&
                                        Math.Abs(w.DistanceM - distanceMeters) < 1.0 &&
                                        Math.Abs(w.DurationS - durationSeconds) < 1)
                            .FirstOrDefaultAsync();

                        if (existingWorkout != null)
                        {
                            // Check if existing workout is missing raw file data
                            bool needsRawFileUpdate = existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0;
                            
                            if (needsRawFileUpdate)
                            {
                                // Backfill raw file data for existing workout
                                existingWorkout.RawFileData = rawFileData;
                                existingWorkout.RawFileName = Path.GetFileName(activity.Filename);
                                existingWorkout.RawFileType = fileType;
                                
                                // Save the update immediately
                                await db.SaveChangesAsync();
                                
                                updated++;
                                logger.LogInformation("Updated duplicate workout {WorkoutId} with raw file data: {Filename} at {StartTime}", 
                                    existingWorkout.Id, activity.Filename, startTime);
                            }
                            else
                            {
                                skipped++;
                                logger.LogInformation("Skipped duplicate workout (already has raw file): {Filename} at {StartTime}", 
                                    activity.Filename, startTime);
                            }
                            
                            // Still process media files for the existing workout if they don't already exist
                            if (!string.IsNullOrWhiteSpace(activity.Media))
                            {
                                var mediaPaths = activity.Media
                                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                                
                                foreach (var mediaPath in mediaPaths)
                                {
                                    try
                                    {
                                        // Extract filename from path (e.g., "media/file.jpg" -> "file.jpg")
                                        var filename = Path.GetFileName(mediaPath);
                                        
                                        // Check if media already exists for this workout
                                        try
                                        {
                                            var mediaExists = await db.WorkoutMedia
                                                .AnyAsync(m => m.WorkoutId == existingWorkout.Id && m.Filename == filename);
                                            
                                            if (mediaExists)
                                            {
                                                logger.LogInformation("Media file already exists for workout {WorkoutId}: {Filename}", 
                                                    existingWorkout.Id, filename);
                                                continue;
                                            }
                                        }
                                        catch (Exception ex) when (ex.Message.Contains("does not exist") || ex.Message.Contains("relation"))
                                        {
                                            // Table doesn't exist yet - skip duplicate check and proceed with import
                                            logger.LogWarning("WorkoutMedia table not found, skipping duplicate check for {Filename}", filename);
                                        }
                                        
                                        // Locate media file in extracted ZIP temp directory
                                        var mediaFilePath = Path.Combine(tempDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
                                        
                                        // Copy media file and create record
                                        var mediaRecord = mediaService.CopyMediaFile(mediaFilePath, existingWorkout.Id);
                                        if (mediaRecord != null)
                                        {
                                            mediaToAdd.Add(mediaRecord);
                                            logger.LogInformation("Added media file for existing workout {WorkoutId}: {MediaPath}", 
                                                existingWorkout.Id, mediaPath);
                                        }
                                        else
                                        {
                                            logger.LogWarning("Failed to process media file for existing workout: {MediaPath}", mediaPath);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning(ex, "Error processing media file {MediaPath} for existing workout {WorkoutId}", 
                                            mediaPath, existingWorkout.Id);
                                        // Continue processing other media files
                                    }
                                }
                            }
                            
                            continue;
                        }

                        // Calculate average pace (seconds per km - stored in metric)
                        var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                            ? (int)(durationSeconds / (distanceMeters / 1000.0))
                            : 0;

                        // Ensure StartedAt is UTC
                        var startedAtUtc = startTime.Kind switch
                        {
                            DateTimeKind.Utc => startTime,
                            DateTimeKind.Local => startTime.ToUniversalTime(),
                            _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
                        };

                        // Build notes from CSV metadata (excluding ActivityName, which goes to Name field)
                        var notesParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
                        {
                            notesParts.Add(activity.ActivityDescription);
                        }
                        if (!string.IsNullOrWhiteSpace(activity.ActivityPrivateNote))
                        {
                            notesParts.Add(activity.ActivityPrivateNote);
                        }
                        var notes = notesParts.Count > 0 ? string.Join("\n\n", notesParts) : null;

                        // Extract metrics from RawStravaData JSON
                        var stravaData = new Dictionary<string, object>();
                        if (!string.IsNullOrEmpty(activity.RawStravaDataJson))
                        {
                            try
                            {
                                var rawStrava = JsonSerializer.Deserialize<JsonElement>(activity.RawStravaDataJson);
                                foreach (var prop in rawStrava.EnumerateObject())
                                {
                                    stravaData[prop.Name] = prop.Value;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to parse RawStravaData JSON for activity {ActivityId}", activity.ActivityId);
                            }
                        }

                        // Extract metrics from GPX/FIT calculated data
                        var calculated = new Dictionary<string, object>();
                        if (parseResult != null && !string.IsNullOrEmpty(parseResult.RawGpxDataJson))
                        {
                            try
                            {
                                var rawGpx = JsonSerializer.Deserialize<JsonElement>(parseResult.RawGpxDataJson);
                                if (rawGpx.TryGetProperty("calculated", out var calculatedElement))
                                {
                                    foreach (var prop in calculatedElement.EnumerateObject())
                                    {
                                        calculated[prop.Name] = prop.Value;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to parse RawGpxData JSON for additional metrics");
                            }
                        }

                        // Create workout
                        var workout = new Workout
                        {
                            Id = Guid.NewGuid(),
                            StartedAt = startedAtUtc,
                            DurationS = durationSeconds,
                            DistanceM = distanceMeters,
                            AvgPaceS = avgPaceS,
                            ElevGainM = elevationGainMeters,
                            RawGpxData = parseResult?.RawGpxDataJson,
                            RawFitData = fitResult?.RawFitDataJson,
                            RawStravaData = activity.RawStravaDataJson,
                            Source = "strava_import",
                            Name = !string.IsNullOrWhiteSpace(activity.ActivityName) ? activity.ActivityName : null,
                            Notes = notes,
                            RawFileData = rawFileData,
                            RawFileName = Path.GetFileName(activity.Filename),
                            RawFileType = fileType,
                            RunType = "Easy Run",
                            CreatedAt = DateTime.UtcNow
                        };

                        // Populate additional metrics from calculated data (GPX)
                        if (calculated.TryGetValue("elevLossM", out var elevLoss) && elevLoss is JsonElement elevLossElem && elevLossElem.ValueKind == JsonValueKind.Number)
                            workout.ElevLossM = elevLossElem.GetDouble();
                        if (calculated.TryGetValue("minElevM", out var minElev) && minElev is JsonElement minElevElem && minElevElem.ValueKind == JsonValueKind.Number)
                            workout.MinElevM = minElevElem.GetDouble();
                        if (calculated.TryGetValue("maxElevM", out var maxElev) && maxElev is JsonElement maxElevElem && maxElevElem.ValueKind == JsonValueKind.Number)
                            workout.MaxElevM = maxElevElem.GetDouble();
                        if (calculated.TryGetValue("maxSpeedMps", out var maxSpeed) && maxSpeed is JsonElement maxSpeedElem && maxSpeedElem.ValueKind == JsonValueKind.Number)
                            workout.MaxSpeedMps = maxSpeedElem.GetDouble();
                        if (calculated.TryGetValue("avgSpeedMps", out var avgSpeed) && avgSpeed is JsonElement avgSpeedElem && avgSpeedElem.ValueKind == JsonValueKind.Number)
                            workout.AvgSpeedMps = avgSpeedElem.GetDouble();

                        // Populate metrics from FIT session data
                        if (fitResult != null && !string.IsNullOrEmpty(fitResult.RawFitDataJson))
                        {
                            try
                            {
                                var rawFit = JsonSerializer.Deserialize<JsonElement>(fitResult.RawFitDataJson);
                                if (rawFit.TryGetProperty("session", out var sessionElement))
                                {
                                    if (sessionElement.TryGetProperty("totalMovingTime", out var movingTime) && movingTime.ValueKind == JsonValueKind.Number)
                                        workout.MovingTimeS = (int)Math.Round(movingTime.GetDouble());
                                    if (sessionElement.TryGetProperty("maxHeartRate", out var maxHr) && maxHr.ValueKind == JsonValueKind.Number)
                                        workout.MaxHeartRateBpm = (byte)maxHr.GetInt32();
                                    if (sessionElement.TryGetProperty("avgHeartRate", out var avgHr) && avgHr.ValueKind == JsonValueKind.Number)
                                        workout.AvgHeartRateBpm = (byte)avgHr.GetInt32();
                                    if (sessionElement.TryGetProperty("minHeartRate", out var minHr) && minHr.ValueKind == JsonValueKind.Number)
                                        workout.MinHeartRateBpm = (byte)minHr.GetInt32();
                                    if (sessionElement.TryGetProperty("maxCadence", out var maxCad) && maxCad.ValueKind == JsonValueKind.Number)
                                        workout.MaxCadenceRpm = (byte)maxCad.GetInt32();
                                    if (sessionElement.TryGetProperty("avgCadence", out var avgCad) && avgCad.ValueKind == JsonValueKind.Number)
                                        workout.AvgCadenceRpm = (byte)avgCad.GetInt32();
                                    if (sessionElement.TryGetProperty("maxPower", out var maxPow) && maxPow.ValueKind == JsonValueKind.Number)
                                        workout.MaxPowerWatts = (ushort)maxPow.GetInt32();
                                    if (sessionElement.TryGetProperty("avgPower", out var avgPow) && avgPow.ValueKind == JsonValueKind.Number)
                                        workout.AvgPowerWatts = (ushort)avgPow.GetInt32();
                                    if (sessionElement.TryGetProperty("totalCalories", out var cals) && cals.ValueKind == JsonValueKind.Number)
                                        workout.Calories = (ushort)cals.GetInt32();
                                }

                                // Extract device information
                                if (rawFit.TryGetProperty("device", out var deviceElement))
                                {
                                    if (deviceElement.ValueKind == JsonValueKind.Object)
                                    {
                                        logger.LogDebug("Found device element in FIT file for activity {ActivityId}: {DeviceData}", activity.ActivityId, deviceElement.GetRawText());
                                        workout.Device = ExtractDeviceName(deviceElement, logger);
                                        if (string.IsNullOrWhiteSpace(workout.Device))
                                        {
                                            logger.LogDebug("Device extraction returned null for activity {ActivityId}. Device element: {DeviceData}", activity.ActivityId, deviceElement.GetRawText());
                                        }
                                    }
                                    else
                                    {
                                        logger.LogDebug("Device element exists but is not an object for activity {ActivityId}. Type: {Type}, Value: {Value}", activity.ActivityId, deviceElement.ValueKind, deviceElement.GetRawText());
                                    }
                                }
                                else
                                {
                                    logger.LogDebug("No device element found in RawFitData for activity {ActivityId}", activity.ActivityId);
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to extract metrics from RawFitData JSON");
                            }
                        }

                        // Infer device from Source field if device is missing or "Development"
                        if (string.IsNullOrWhiteSpace(workout.Device) || workout.Device == "Development")
                        {
                            if (workout.Source == "apple_watch")
                            {
                                workout.Device = "Apple Watch";
                            }
                        }

                        // Populate metrics from Strava CSV data
                        if (stravaData.TryGetValue("movingTime", out var stravaMovingTime) && stravaMovingTime is JsonElement stravaMovingTimeElem && stravaMovingTimeElem.ValueKind == JsonValueKind.Number)
                            workout.MovingTimeS = (int)Math.Round(stravaMovingTimeElem.GetDouble());
                        if (stravaData.TryGetValue("maxHeartRate", out var stravaMaxHr) && stravaMaxHr is JsonElement stravaMaxHrElem && stravaMaxHrElem.ValueKind == JsonValueKind.Number)
                            workout.MaxHeartRateBpm = (byte)stravaMaxHrElem.GetInt32();
                        if (stravaData.TryGetValue("avgHeartRate", out var stravaAvgHr) && stravaAvgHr is JsonElement stravaAvgHrElem && stravaAvgHrElem.ValueKind == JsonValueKind.Number)
                            workout.AvgHeartRateBpm = (byte)stravaAvgHrElem.GetInt32();
                        if (stravaData.TryGetValue("maxCadence", out var stravaMaxCad) && stravaMaxCad is JsonElement stravaMaxCadElem && stravaMaxCadElem.ValueKind == JsonValueKind.Number)
                            workout.MaxCadenceRpm = (byte)stravaMaxCadElem.GetInt32();
                        if (stravaData.TryGetValue("avgCadence", out var stravaAvgCad) && stravaAvgCad is JsonElement stravaAvgCadElem && stravaAvgCadElem.ValueKind == JsonValueKind.Number)
                            workout.AvgCadenceRpm = (byte)stravaAvgCadElem.GetInt32();
                        if (stravaData.TryGetValue("maxWatts", out var stravaMaxWatts) && stravaMaxWatts is JsonElement stravaMaxWattsElem && stravaMaxWattsElem.ValueKind == JsonValueKind.Number)
                            workout.MaxPowerWatts = (ushort)stravaMaxWattsElem.GetInt32();
                        if (stravaData.TryGetValue("avgWatts", out var stravaAvgWatts) && stravaAvgWatts is JsonElement stravaAvgWattsElem && stravaAvgWattsElem.ValueKind == JsonValueKind.Number)
                            workout.AvgPowerWatts = (ushort)stravaAvgWattsElem.GetInt32();
                        if (stravaData.TryGetValue("calories", out var stravaCals) && stravaCals is JsonElement stravaCalsElem && stravaCalsElem.ValueKind == JsonValueKind.Number)
                            workout.Calories = (ushort)stravaCalsElem.GetInt32();
                        if (stravaData.TryGetValue("elevationLoss", out var stravaElevLoss) && stravaElevLoss is JsonElement stravaElevLossElem && stravaElevLossElem.ValueKind == JsonValueKind.Number)
                            workout.ElevLossM = stravaElevLossElem.GetDouble();
                        if (stravaData.TryGetValue("elevationLow", out var stravaMinElev) && stravaMinElev is JsonElement stravaMinElevElem && stravaMinElevElem.ValueKind == JsonValueKind.Number)
                            workout.MinElevM = stravaMinElevElem.GetDouble();
                        if (stravaData.TryGetValue("elevationHigh", out var stravaMaxElev) && stravaMaxElev is JsonElement stravaMaxElevElem && stravaMaxElevElem.ValueKind == JsonValueKind.Number)
                            workout.MaxElevM = stravaMaxElevElem.GetDouble();
                        if (stravaData.TryGetValue("maxSpeed", out var stravaMaxSpeed) && stravaMaxSpeed is JsonElement stravaMaxSpeedElem && stravaMaxSpeedElem.ValueKind == JsonValueKind.Number)
                            workout.MaxSpeedMps = stravaMaxSpeedElem.GetDouble();
                        if (stravaData.TryGetValue("avgSpeed", out var stravaAvgSpeed) && stravaAvgSpeed is JsonElement stravaAvgSpeedElem && stravaAvgSpeedElem.ValueKind == JsonValueKind.Number)
                            workout.AvgSpeedMps = stravaAvgSpeedElem.GetDouble();

                        // Create route GeoJSON
                        var coordinates = trackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
                        var routeGeoJson = JsonSerializer.Serialize(new
                        {
                            type = "LineString",
                            coordinates = coordinates
                        });

                        var route = new WorkoutRoute
                        {
                            Id = Guid.NewGuid(),
                            WorkoutId = workout.Id,
                            RouteGeoJson = routeGeoJson
                        };

                        // Calculate splits
                        var splits = gpxParser.CalculateSplits(
                            trackPoints,
                            distanceMeters,
                            durationSeconds,
                            splitDistanceMeters
                        );

                        foreach (var split in splits)
                        {
                            split.WorkoutId = workout.Id;
                        }

                        // Fetch weather data - try Strava data first, then FIT data, then Open-Meteo
                        if (trackPoints.Count > 0)
                        {
                            try
                            {
                                var firstPoint = trackPoints[0];
                                var weatherJson = await weatherService.GetWeatherForWorkoutAsync(
                                    rawStravaDataJson: activity.RawStravaDataJson,
                                    rawFitDataJson: fitResult?.RawFitDataJson,
                                    latitude: firstPoint.Latitude,
                                    longitude: firstPoint.Longitude,
                                    startTime: startedAtUtc
                                );
                                if (!string.IsNullOrEmpty(weatherJson))
                                {
                                    workout.Weather = weatherJson;
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to fetch weather data for workout from {Filename}", activity.Filename);
                                // Continue without weather - not a critical error
                            }
                        }

                        workoutsToAdd.Add(workout);
                        routesToAdd.Add(route);
                        splitsToAdd.AddRange(splits);
                        successful++;

                        // Process media files for this workout
                        if (!string.IsNullOrWhiteSpace(activity.Media))
                        {
                            var mediaPaths = activity.Media
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            
                            foreach (var mediaPath in mediaPaths)
                            {
                                try
                                {
                                    // Locate media file in extracted ZIP temp directory
                                    var mediaFilePath = Path.Combine(tempDir, mediaPath.Replace('/', Path.DirectorySeparatorChar));
                                    
                                    // Copy media file and create record
                                    var mediaRecord = mediaService.CopyMediaFile(mediaFilePath, workout.Id);
                                    if (mediaRecord != null)
                                    {
                                        mediaToAdd.Add(mediaRecord);
                                        logger.LogInformation("Added media file for workout {WorkoutId}: {MediaPath}", 
                                            workout.Id, mediaPath);
                                    }
                                    else
                                    {
                                        logger.LogWarning("Failed to process media file: {MediaPath}", mediaPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Error processing media file {MediaPath} for workout {WorkoutId}", 
                                        mediaPath, workout.Id);
                                    // Continue processing other media files
                                }
                            }
                        }

                        logger.LogInformation("Processed workout from {Filename}: {Distance}m in {Duration}s", 
                            activity.Filename, distanceMeters, durationSeconds);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing activity file {Filename}", activity.Filename);
                        errors.Add(new { filename = activity.Filename, error = ex.Message });
                    }
                }

                // Batch insert all workouts, routes, and splits
                if (workoutsToAdd.Count > 0)
                {
                    db.Workouts.AddRange(workoutsToAdd);
                    db.WorkoutRoutes.AddRange(routesToAdd);
                    db.WorkoutSplits.AddRange(splitsToAdd);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Bulk imported {Count} workouts", workoutsToAdd.Count);

                    // Calculate Relative Effort for all imported workouts
                    try
                    {
                        var settings = await db.UserSettings.FirstOrDefaultAsync();
                        if (settings != null)
                        {
                            var zones = zoneService.GetZonesFromUserSettings(settings);
                            foreach (var workout in workoutsToAdd)
                            {
                                try
                                {
                                    var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                                    if (relativeEffort.HasValue)
                                    {
                                        workout.RelativeEffort = relativeEffort.Value;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                                }
                            }
                            await db.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to calculate Relative Effort for bulk imported workouts");
                        // Continue - Relative Effort is optional
                    }
                }

                // Save media records separately (for both new workouts and existing workouts)
                // This ensures media is saved even when all workouts are duplicates
                if (mediaToAdd.Count > 0)
                {
                    db.WorkoutMedia.AddRange(mediaToAdd);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Added {MediaCount} media files to database", mediaToAdd.Count);
                }

                return Results.Ok(new
                {
                    totalProcessed,
                    successful,
                    skipped,
                    updated,
                    errors = errors.Count,
                    errorDetails = errors
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing bulk import");
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error processing bulk import"
                );
            }
            finally
            {
                // Clean up temporary directory
                try
                {
                    if (Directory.Exists(tempDir))
                    {
                        Directory.Delete(tempDir, true);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to clean up temporary directory {TempDir}", tempDir);
                }
            }
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .WithSummary("Bulk import Strava export")
        .WithDescription("Uploads and processes a ZIP file containing Strava export (activities.csv + activity files), importing all run activities with duplicate detection");

        group.MapGet("/stats/weekly", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] int? timezoneOffsetMinutes = null) =>
        {
            // Get current week boundaries (Monday-Sunday) in the specified timezone
            // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
            // To convert UTC to local: UTC + offset (since offset is already negative)
            var now = DateTime.UtcNow;
            if (timezoneOffsetMinutes.HasValue)
            {
                now = now.AddMinutes(timezoneOffsetMinutes.Value);
            }

            // Calculate start of current week (Monday)
            var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            var weekStart = now.Date.AddDays(-daysSinceMonday);
            var weekEnd = weekStart.AddDays(7).AddTicks(-1);

            // Convert back to UTC for database query
            var weekStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(weekStart.AddMinutes(timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(weekStart, DateTimeKind.Utc);
            var weekEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(weekEnd.AddMinutes(timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(weekEnd, DateTimeKind.Utc);

            // Query workouts for the current week
            var workouts = await db.Workouts
                .Where(w => w.StartedAt >= weekStartUtc && w.StartedAt <= weekEndUtc)
                .AsNoTracking()
                .ToListAsync();

            // Group by day of week and sum distances
            // DayOfWeek enum: Sunday=0, Monday=1, ..., Saturday=6
            // We want: Monday=0, Tuesday=1, ..., Sunday=6
            var dailyTotals = new double[7]; // M T W T F S S

            foreach (var workout in workouts)
            {
                // Convert UTC to local timezone
                // timezoneOffsetMinutes is already negative (from -getTimezoneOffset()), so add it directly
                var localTime = timezoneOffsetMinutes.HasValue
                    ? workout.StartedAt.AddMinutes(timezoneOffsetMinutes.Value)
                    : workout.StartedAt;

                // Get day of week (0=Monday, 6=Sunday)
                var dayOfWeek = ((int)localTime.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                
                // Convert meters to miles and add to daily total
                var miles = workout.DistanceM / 1609.344;
                dailyTotals[dayOfWeek] += miles;
            }

            return Results.Ok(new
            {
                weekStart = weekStart.ToString("yyyy-MM-dd"),
                weekEnd = weekEnd.ToString("yyyy-MM-dd"),
                dailyMiles = dailyTotals
            });
        })
        .Produces(200)
        .WithSummary("Get weekly stats")
        .WithDescription("Returns daily miles for the current week (Monday-Sunday), grouped by day of week");

        group.MapGet("/stats/yearly", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] int? timezoneOffsetMinutes = null) =>
        {
            // Get current year boundaries in the specified timezone
            // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
            // To convert UTC to local: UTC + offset (since offset is already negative)
            var now = DateTime.UtcNow;
            if (timezoneOffsetMinutes.HasValue)
            {
                now = now.AddMinutes(timezoneOffsetMinutes.Value);
            }

            var currentYear = now.Year;
            var previousYear = currentYear - 1;

            // Calculate year boundaries in local timezone
            var currentYearStart = new DateTime(currentYear, 1, 1, 0, 0, 0);
            var currentYearEnd = new DateTime(currentYear, 12, 31, 23, 59, 59, 999);
            var previousYearStart = new DateTime(previousYear, 1, 1, 0, 0, 0);
            var previousYearEnd = new DateTime(previousYear, 12, 31, 23, 59, 59, 999);

            // Convert to UTC for database queries
            var currentYearStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(currentYearStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(currentYearStart, DateTimeKind.Utc);
            var currentYearEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(currentYearEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(currentYearEnd, DateTimeKind.Utc);
            var previousYearStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(previousYearStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(previousYearStart, DateTimeKind.Utc);
            var previousYearEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(previousYearEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(previousYearEnd, DateTimeKind.Utc);

            // Query and sum distances for current year
            var currentYearDistanceM = await db.Workouts
                .Where(w => w.StartedAt >= currentYearStartUtc && w.StartedAt <= currentYearEndUtc)
                .AsNoTracking()
                .SumAsync(w => (double?)w.DistanceM) ?? 0.0;

            // Query and sum distances for previous year
            var previousYearDistanceM = await db.Workouts
                .Where(w => w.StartedAt >= previousYearStartUtc && w.StartedAt <= previousYearEndUtc)
                .AsNoTracking()
                .SumAsync(w => (double?)w.DistanceM) ?? 0.0;

            // Convert to miles
            var currentYearMiles = currentYearDistanceM / 1609.344;
            var previousYearMiles = previousYearDistanceM / 1609.344;

            return Results.Ok(new
            {
                currentYear = currentYearMiles,
                previousYear = previousYearMiles,
                currentYearLabel = currentYear.ToString(),
                previousYearLabel = previousYear.ToString()
            });
        })
        .Produces(200)
        .WithSummary("Get yearly stats")
        .WithDescription("Returns total miles for the current year and previous year");

        group.MapGet("/stats/yearly-weekly", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] string? periodEndDate = null,
            [FromQuery] int? timezoneOffsetMinutes = null) =>
        {
            // Get current date in the specified timezone
            // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
            // To convert UTC to local: UTC + offset (since offset is already negative)
            var now = DateTime.UtcNow;
            if (timezoneOffsetMinutes.HasValue)
            {
                now = now.AddMinutes(timezoneOffsetMinutes.Value);
            }

            // Compute period bounds
            // If periodEndDate not provided, default to today (last 12 months ending today)
            // This ensures alignment with /workouts/stats/available-periods
            DateTime periodEnd;
            var today = now.Date;
            if (!string.IsNullOrEmpty(periodEndDate) && DateTime.TryParse(periodEndDate, out var parsedEndDate))
            {
                periodEnd = parsedEndDate.Date;
            }
            else
            {
                // Default to today (so the period is the last 12 months ending today)
                periodEnd = today;
            }

            // Period start = periodEnd.AddYears(-1).AddDays(1) (inclusive)
            // This gives us the last 12 months ending on periodEnd
            var periodStart = periodEnd.AddYears(-1).AddDays(1);
            
            // Calculate total days in period (365 or 366)
            var totalDays = (periodEnd - periodStart).Days + 1;

            // Convert period bounds to UTC for database queries
            var periodStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(periodStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(periodStart, DateTimeKind.Utc);
            var periodEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(periodEnd.AddMinutes(-timezoneOffsetMinutes.Value).AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                : DateTime.SpecifyKind(periodEnd.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

            // Get all workouts in the period
            var workouts = await db.Workouts
                .Where(w => w.StartedAt >= periodStartUtc && w.StartedAt <= periodEndUtc)
                .AsNoTracking()
                .ToListAsync();

            // Initialize 52 week buckets (indexed 0-51)
            var weekBuckets = new Dictionary<int, double>();
            for (int i = 0; i < 52; i++)
            {
                weekBuckets[i] = 0.0;
            }

            // Group workouts by week index
            foreach (var workout in workouts)
            {
                // Convert workout date to local timezone for calculation
                var workoutDate = workout.StartedAt;
                if (timezoneOffsetMinutes.HasValue)
                {
                    workoutDate = workoutDate.AddMinutes(timezoneOffsetMinutes.Value);
                }
                var workoutDateOnly = workoutDate.Date;

                // Calculate dayIndex (0-based integer number of days from periodStart)
                var dayIndex = (workoutDateOnly - periodStart).Days;

                // Calculate weekIndex (0-51)
                var weekIndex = (int)Math.Floor(dayIndex * 52.0 / totalDays);
                
                // Clamp to valid range (shouldn't be necessary, but safety check)
                if (weekIndex < 0) weekIndex = 0;
                if (weekIndex >= 52) weekIndex = 51;

                // Update bucket distance
                weekBuckets[weekIndex] += workout.DistanceM;
            }

            // Build response with 52 weeks
            // Calculate theoretical date range for each bucket to ensure complete coverage
            var weeks = new List<object>();
            for (int weekIndex = 0; weekIndex < 52; weekIndex++)
            {
                var distanceM = weekBuckets[weekIndex];
                
                // Calculate theoretical date range for this bucket
                // This ensures every date in the period is covered exactly once
                var dayIndexStart = (int)Math.Floor(weekIndex * totalDays / 52.0);
                var dayIndexEnd = (int)Math.Floor((weekIndex + 1) * totalDays / 52.0) - 1;
                
                // Ensure dayIndexEnd doesn't exceed totalDays - 1
                if (dayIndexEnd >= totalDays)
                {
                    dayIndexEnd = totalDays - 1;
                }
                
                var weekStartDate = periodStart.AddDays(dayIndexStart);
                var weekEndDate = periodStart.AddDays(dayIndexEnd);

                weeks.Add(new
                {
                    weekNumber = weekIndex + 1, // 1-52
                    weekStart = weekStartDate.ToString("yyyy-MM-dd"),
                    weekEnd = weekEndDate.ToString("yyyy-MM-dd"),
                    distanceM = distanceM
                });
            }

            return Results.Ok(new
            {
                weeks,
                dateRangeStart = periodStart.ToString("yyyy-MM-dd"),
                dateRangeEnd = periodEnd.ToString("yyyy-MM-dd")
            });
        })
        .Produces(200)
        .WithSummary("Get yearly weekly stats")
        .WithDescription("Returns 52 equal week buckets within a 1-year period, covering all dates with no gaps or overlaps. If periodEndDate not provided, defaults to today (last 12 months ending today).");

        group.MapGet("/stats/available-periods", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] int? timezoneOffsetMinutes = null) =>
        {
            // Get current date in the specified timezone
            // We need to get today's date in the local timezone, not UTC
            // The date should be the calendar date in the user's timezone, not UTC
            // Frontend sends timezoneOffsetMinutes as -getTimezoneOffset() (negative for timezones behind UTC)
            // For example, EST (UTC-5) sends -300, PST (UTC-8) sends -480
            // To convert UTC to local: UTC + offset (since offset is already negative)
            var nowUtc = DateTime.UtcNow;
            DateTime today;
            if (timezoneOffsetMinutes.HasValue)
            {
                // Convert UTC to local time by adding the offset
                // timezoneOffsetMinutes is already negative for timezones behind UTC (e.g., EST = -300)
                // So we add the negative number, which subtracts time (correct conversion)
                var localDateTime = nowUtc.AddMinutes(timezoneOffsetMinutes.Value);
                // Get just the date part (midnight of that day in local time)
                today = localDateTime.Date;
            }
            else
            {
                // No timezone offset provided, use UTC date
                today = nowUtc.Date;
            }
            
            // Ensure today is a date at midnight (no time component)
            today = today.Date;
            
            // Log the calculated today date for debugging
            logger.LogDebug("Calculated today's date: {Today} (UTC now: {UtcNow}, timezone offset: {Offset})", 
                today.ToString("yyyy-MM-dd"), nowUtc.ToString("yyyy-MM-dd HH:mm:ss"), 
                timezoneOffsetMinutes?.ToString() ?? "none");

            // Get first workout date to know when to stop
            var firstWorkout = await db.Workouts
                .AsNoTracking()
                .OrderBy(w => w.StartedAt)
                .FirstOrDefaultAsync();

            DateTime? firstWorkoutDate = null;
            if (firstWorkout != null)
            {
                firstWorkoutDate = firstWorkout.StartedAt;
                if (timezoneOffsetMinutes.HasValue)
                {
                    firstWorkoutDate = firstWorkoutDate.Value.AddMinutes(timezoneOffsetMinutes.Value);
                }
            }

            // Generate consecutive 1-year periods going backwards from today
            // Current period: End = today (inclusive), Start = today.AddYears(-1).AddDays(1) (last 12 months ending today)
            // Previous periods: For older period N with newer period N+1:
            //   End(N) = Start(N+1).AddDays(-1)
            //   Start(N) = End(N).AddYears(-1).AddDays(1)
            // Example: If today = Nov 18, 2025:
            //   Period 1: Nov 19, 2024 - Nov 18, 2025
            //   Period 2: Nov 19, 2023 - Nov 18, 2024
            //   Period 3: Nov 19, 2022 - Nov 18, 2023
            var periods = new List<object>();
            var currentPeriodEnd = today;
            var currentPeriodStart = today.AddYears(-1).AddDays(1);
            
            // Log first period for debugging
            logger.LogDebug("First period (last 12 months ending today): {Start} - {End}", currentPeriodStart.ToString("yyyy-MM-dd"), currentPeriodEnd.ToString("yyyy-MM-dd"));
            
            while (true)
            {
                periods.Add(new
                {
                    periodEndDate = currentPeriodEnd.ToString("yyyy-MM-dd"),
                    dateRangeStart = currentPeriodStart.ToString("yyyy-MM-dd"),
                    dateRangeEnd = currentPeriodEnd.ToString("yyyy-MM-dd"),
                    dateRangeLabel = $"{currentPeriodStart:MMM d, yyyy} - {currentPeriodEnd:MMM d, yyyy}"
                });
                
                // Calculate previous period
                // Previous period ends 1 day before current period starts
                var previousPeriodEnd = currentPeriodStart.AddDays(-1);
                // Previous period starts 1 year before its end date (so it's exactly 1 year)
                var previousPeriodStart = previousPeriodEnd.AddYears(-1).AddDays(1);
                
                // Stop if we've generated more than 20 periods (safety limit)
                if (periods.Count > 20)
                {
                    break;
                }
                
                // Stop if we've gone past the first workout date
                if (firstWorkoutDate.HasValue && previousPeriodEnd.Date < firstWorkoutDate.Value.Date)
                {
                    break;
                }
                
                currentPeriodStart = previousPeriodStart;
                currentPeriodEnd = previousPeriodEnd;
            }

            return Results.Ok(periods);
        })
        .Produces(200)
        .WithSummary("Get available periods")
        .WithDescription("Returns consecutive 1-year periods (365/366 days) going backwards from today. Current period is the last 12 months ending today.");

        group.MapGet("/stats/available-years", async (
            TempoDbContext db,
            ILogger<Program> logger) =>
        {
            // Get distinct years from workouts
            var years = await db.Workouts
                .AsNoTracking()
                .Select(w => w.StartedAt.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync();

            return Results.Ok(years);
        })
        .Produces(200)
        .WithSummary("Get available years")
        .WithDescription("Returns list of years that have workouts");

        group.MapPatch("/{id:guid}", async (
            Guid id,
            HttpRequest request,
            TempoDbContext db,
            ILogger<Program> logger) =>
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

            var root = jsonDoc.RootElement;

            // Find workout
            var workout = await db.Workouts.FindAsync(id);
            if (workout == null)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Validate and update RunType if provided
            if (root.TryGetProperty("runType", out var runTypeElement))
            {
                string? runTypeValue = null;
                if (runTypeElement.ValueKind == JsonValueKind.String)
                {
                    runTypeValue = runTypeElement.GetString();
                }
                else if (runTypeElement.ValueKind == JsonValueKind.Null)
                {
                    runTypeValue = null;
                }
                else
                {
                    return Results.BadRequest(new { error = "runType must be a string or null" });
                }

                var validRunTypes = new[] { "Race", "Workout", "Long Run", "Easy Run" };
                if (runTypeValue != null && !validRunTypes.Contains(runTypeValue))
                {
                    return Results.BadRequest(new { error = $"Invalid runType. Must be one of: {string.Join(", ", validRunTypes)}, or null" });
                }
                workout.RunType = runTypeValue;
            }

            // Update Notes if provided
            if (root.TryGetProperty("notes", out var notesElement))
            {
                if (notesElement.ValueKind == JsonValueKind.String)
                {
                    workout.Notes = notesElement.GetString();
                }
                else if (notesElement.ValueKind == JsonValueKind.Null)
                {
                    workout.Notes = null;
                }
                else
                {
                    return Results.BadRequest(new { error = "notes must be a string or null" });
                }
            }

            // Save changes
            var runTypeUpdated = root.TryGetProperty("runType", out _);
            var notesUpdated = root.TryGetProperty("notes", out _);
            await db.SaveChangesAsync();

            logger.LogInformation("Updated workout {WorkoutId}: RunType={RunType}, RunTypeUpdated={RunTypeUpdated}, NotesUpdated={NotesUpdated}",
                workout.Id, workout.RunType ?? "null", runTypeUpdated, notesUpdated);

            return Results.Ok(new
            {
                id = workout.Id,
                runType = workout.RunType,
                notes = workout.Notes
            });
        })
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Update workout")
        .WithDescription("Updates workout RunType and/or Notes");

        group.MapDelete("/{id:guid}", async (
            Guid id,
            TempoDbContext db,
            MediaStorageConfig mediaConfig,
            ILogger<Program> logger) =>
        {
            // Find workout with related media
            var workout = await db.Workouts
                .Include(w => w.Media)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (workout == null)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Delete all media files from filesystem
            foreach (var media in workout.Media)
            {
                try
                {
                    if (File.Exists(media.FilePath))
                    {
                        File.Delete(media.FilePath);
                        logger.LogInformation("Deleted media file from filesystem: {FilePath}", media.FilePath);
                    }
                    else
                    {
                        logger.LogWarning("Media file not found on filesystem (orphaned record): {FilePath}", media.FilePath);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error deleting media file from filesystem: {FilePath}", media.FilePath);
                    // Continue with deletion even if file deletion fails
                }
            }

            // Delete workout's media directory if it exists
            try
            {
                var workoutMediaDir = Path.Combine(mediaConfig.RootPath, id.ToString());
                if (Directory.Exists(workoutMediaDir))
                {
                    Directory.Delete(workoutMediaDir, recursive: true);
                    logger.LogInformation("Deleted workout media directory: {Directory}", workoutMediaDir);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error deleting workout media directory for workout {WorkoutId}", id);
                // Continue with database deletion even if directory deletion fails
            }

            // Delete workout from database (cascade delete will handle related records)
            db.Workouts.Remove(workout);
            await db.SaveChangesAsync();

            logger.LogInformation("Deleted workout {WorkoutId}", id);

            return Results.NoContent();
        })
        .Produces(204)
        .Produces(404)
        .WithSummary("Delete workout")
        .WithDescription("Deletes a workout and all associated data (route, splits, media files, and database records)");
    }

    /// <summary>
    /// Processes a single workout file and returns the result.
    /// </summary>
    private static async Task<FileProcessResult> ProcessSingleFile(
        IFormFile file,
        TempoDbContext db,
        GpxParserService gpxParser,
        FitParserService fitParser,
        WeatherService weatherService,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        double splitDistanceMeters,
        ILogger logger)
    {
        if (file == null || file.Length == 0)
        {
            return new FileProcessResult { Action = "error", ErrorMessage = "File is empty" };
        }

        // Determine file type from extension
        string? fileType = null;
        var fileName = file.FileName.ToLowerInvariant();
        bool isGpx = fileName.EndsWith(".gpx");
        bool isFit = fileName.EndsWith(".fit");
        bool isFitGz = fileName.EndsWith(".fit.gz");

        if (isGpx)
        {
            fileType = "gpx";
        }
        else if (isFitGz || isFit)
        {
            fileType = "fit";
        }
        else
        {
            return new FileProcessResult { Action = "error", ErrorMessage = "File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)" };
        }

        try
        {
            // Read file into byte array before parsing
            byte[] rawFileData;
            using (var stream = file.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                rawFileData = memoryStream.ToArray();
            }

            // Parse the file based on type
            GpxParserService.GpxParseResult? parseResult = null;
            FitParserService.FitParseResult? fitResult = null;

            if (fileType == "gpx")
            {
                using (var stream = new MemoryStream(rawFileData))
                {
                    parseResult = gpxParser.ParseGpx(stream);
                }
            }
            else if (fileType == "fit")
            {
                using (var stream = new MemoryStream(rawFileData))
                {
                    try
                    {
                        if (isFitGz)
                        {
                            fitResult = fitParser.ParseGzippedFit(stream);
                        }
                        else
                        {
                            fitResult = fitParser.ParseFit(stream);
                        }
                    }
                    catch (NotSupportedException ex)
                    {
                        return new FileProcessResult { Action = "error", ErrorMessage = ex.Message };
                    }
                }
            }

            // Extract data from parse result
            DateTime startTime;
            int durationSeconds;
            double distanceMeters;
            double? elevationGainMeters;
            List<GpxParserService.GpxPoint> trackPoints;
            string? rawGpxDataJson = null;
            string? rawFitDataJson = null;

            if (parseResult != null)
            {
                startTime = parseResult.StartTime;
                durationSeconds = parseResult.DurationSeconds;
                distanceMeters = parseResult.DistanceMeters;
                elevationGainMeters = parseResult.ElevationGainMeters;
                trackPoints = parseResult.TrackPoints;
                rawGpxDataJson = parseResult.RawGpxDataJson;
            }
            else if (fitResult != null)
            {
                startTime = fitResult.StartTime;
                durationSeconds = fitResult.DurationSeconds;
                distanceMeters = fitResult.DistanceMeters;
                elevationGainMeters = fitResult.ElevationGainMeters;
                trackPoints = fitResult.TrackPoints;
                rawFitDataJson = fitResult.RawFitDataJson;
            }
            else
            {
                return new FileProcessResult { Action = "error", ErrorMessage = "Failed to parse file" };
            }

            // Calculate average pace (seconds per km - stored in metric)
            var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                ? (int)(durationSeconds / (distanceMeters / 1000.0))
                : 0;

            // Extract additional metrics from RawGpxData JSON (for GPX files)
            var calculated = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(rawGpxDataJson))
            {
                try
                {
                    var rawData = JsonSerializer.Deserialize<JsonElement>(rawGpxDataJson);
                    if (rawData.TryGetProperty("calculated", out var calculatedElement))
                    {
                        foreach (var prop in calculatedElement.EnumerateObject())
                        {
                            calculated[prop.Name] = prop.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse RawGpxData JSON for additional metrics");
                }
            }

            // Ensure StartedAt is UTC (defensive conversion)
            var startedAtUtc = startTime.Kind switch
            {
                DateTimeKind.Utc => startTime,
                DateTimeKind.Local => startTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            };

            // Check for duplicate using database query
            var existingWorkout = await db.Workouts
                .Where(w => w.StartedAt == startedAtUtc &&
                            Math.Abs(w.DistanceM - distanceMeters) < 1.0 &&
                            Math.Abs(w.DurationS - durationSeconds) < 1)
                .FirstOrDefaultAsync();

            if (existingWorkout != null)
            {
                // Check if existing workout is missing raw file data
                bool needsRawFileUpdate = existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0;
                
                if (needsRawFileUpdate)
                {
                    // Backfill raw file data for existing workout
                    existingWorkout.RawFileData = rawFileData;
                    existingWorkout.RawFileName = file.FileName;
                    existingWorkout.RawFileType = fileType;
                    
                    // Also update RawGpxData and RawFitData if available
                    if (!string.IsNullOrEmpty(rawGpxDataJson))
                    {
                        existingWorkout.RawGpxData = rawGpxDataJson;
                    }
                    if (!string.IsNullOrEmpty(rawFitDataJson))
                    {
                        existingWorkout.RawFitData = rawFitDataJson;
                    }
                    
                    // Save the update immediately
                    await db.SaveChangesAsync();
                    
                    logger.LogInformation("Updated duplicate workout {WorkoutId} with raw file data: {Filename} at {StartTime}", 
                        existingWorkout.Id, file.FileName, startedAtUtc);
                    
                    return new FileProcessResult
                    {
                        Action = "updated",
                        Response = new
                        {
                            id = existingWorkout.Id,
                            startedAt = existingWorkout.StartedAt,
                            durationS = existingWorkout.DurationS,
                            distanceM = existingWorkout.DistanceM,
                            avgPaceS = existingWorkout.AvgPaceS,
                            elevGainM = existingWorkout.ElevGainM,
                            action = "updated",
                            message = "Workout already exists and was updated with raw file data"
                        }
                    };
                }
                else
                {
                    // Duplicate exists and already has raw file data
                    logger.LogInformation("Skipped duplicate workout (already has raw file): {Filename} at {StartTime}", 
                        file.FileName, startedAtUtc);
                    
                    return new FileProcessResult
                    {
                        Action = "skipped",
                        Response = new
                        {
                            id = existingWorkout.Id,
                            startedAt = existingWorkout.StartedAt,
                            durationS = existingWorkout.DurationS,
                            distanceM = existingWorkout.DistanceM,
                            avgPaceS = existingWorkout.AvgPaceS,
                            elevGainM = existingWorkout.ElevGainM,
                            action = "skipped",
                            message = "Workout already exists and has raw file data"
                        }
                    };
                }
            }

            // Create workout (no duplicate found)
            var workout = new Workout
            {
                Id = Guid.NewGuid(),
                StartedAt = startedAtUtc,
                DurationS = durationSeconds,
                DistanceM = distanceMeters,
                AvgPaceS = avgPaceS,
                ElevGainM = elevationGainMeters,
                RawFileData = rawFileData,
                RawFileName = file.FileName,
                RawFileType = fileType,
                RawGpxData = rawGpxDataJson,
                RawFitData = rawFitDataJson,
                Source = isGpx ? "apple_watch" : "fit_import",
                RunType = "Easy Run",
                CreatedAt = DateTime.UtcNow
            };

            // Populate additional metrics from calculated data (GPX)
            if (calculated.TryGetValue("elevLossM", out var elevLoss) && elevLoss is JsonElement elevLossElem && elevLossElem.ValueKind == JsonValueKind.Number)
                workout.ElevLossM = elevLossElem.GetDouble();
            if (calculated.TryGetValue("minElevM", out var minElev) && minElev is JsonElement minElevElem && minElevElem.ValueKind == JsonValueKind.Number)
                workout.MinElevM = minElevElem.GetDouble();
            if (calculated.TryGetValue("maxElevM", out var maxElev) && maxElev is JsonElement maxElevElem && maxElevElem.ValueKind == JsonValueKind.Number)
                workout.MaxElevM = maxElevElem.GetDouble();
            if (calculated.TryGetValue("maxSpeedMps", out var maxSpeed) && maxSpeed is JsonElement maxSpeedElem && maxSpeedElem.ValueKind == JsonValueKind.Number)
                workout.MaxSpeedMps = maxSpeedElem.GetDouble();
            if (calculated.TryGetValue("avgSpeedMps", out var avgSpeed) && avgSpeed is JsonElement avgSpeedElem && avgSpeedElem.ValueKind == JsonValueKind.Number)
                workout.AvgSpeedMps = avgSpeedElem.GetDouble();

            // Populate metrics from FIT session data
            if (fitResult != null && !string.IsNullOrEmpty(rawFitDataJson))
            {
                try
                {
                    var rawFit = JsonSerializer.Deserialize<JsonElement>(rawFitDataJson);
                    if (rawFit.TryGetProperty("session", out var sessionElement))
                    {
                        if (sessionElement.TryGetProperty("totalMovingTime", out var movingTime) && movingTime.ValueKind == JsonValueKind.Number)
                            workout.MovingTimeS = (int)Math.Round(movingTime.GetDouble());
                        if (sessionElement.TryGetProperty("maxHeartRate", out var maxHr) && maxHr.ValueKind == JsonValueKind.Number)
                            workout.MaxHeartRateBpm = (byte)maxHr.GetInt32();
                        if (sessionElement.TryGetProperty("avgHeartRate", out var avgHr) && avgHr.ValueKind == JsonValueKind.Number)
                            workout.AvgHeartRateBpm = (byte)avgHr.GetInt32();
                        if (sessionElement.TryGetProperty("minHeartRate", out var minHr) && minHr.ValueKind == JsonValueKind.Number)
                            workout.MinHeartRateBpm = (byte)minHr.GetInt32();
                        if (sessionElement.TryGetProperty("maxCadence", out var maxCad) && maxCad.ValueKind == JsonValueKind.Number)
                            workout.MaxCadenceRpm = (byte)maxCad.GetInt32();
                        if (sessionElement.TryGetProperty("avgCadence", out var avgCad) && avgCad.ValueKind == JsonValueKind.Number)
                            workout.AvgCadenceRpm = (byte)avgCad.GetInt32();
                        if (sessionElement.TryGetProperty("maxPower", out var maxPow) && maxPow.ValueKind == JsonValueKind.Number)
                            workout.MaxPowerWatts = (ushort)maxPow.GetInt32();
                        if (sessionElement.TryGetProperty("avgPower", out var avgPow) && avgPow.ValueKind == JsonValueKind.Number)
                            workout.AvgPowerWatts = (ushort)avgPow.GetInt32();
                        if (sessionElement.TryGetProperty("totalCalories", out var cals) && cals.ValueKind == JsonValueKind.Number)
                            workout.Calories = (ushort)cals.GetInt32();
                    }

                    // Extract device information
                    if (rawFit.TryGetProperty("device", out var deviceElement))
                    {
                        if (deviceElement.ValueKind == JsonValueKind.Object)
                        {
                            logger.LogDebug("Found device element in FIT file: {DeviceData}", deviceElement.GetRawText());
                            workout.Device = ExtractDeviceName(deviceElement, logger);
                            if (string.IsNullOrWhiteSpace(workout.Device))
                            {
                                logger.LogDebug("Device extraction returned null. Device element: {DeviceData}", deviceElement.GetRawText());
                            }
                        }
                        else
                        {
                            logger.LogDebug("Device element exists but is not an object. Type: {Type}, Value: {Value}", deviceElement.ValueKind, deviceElement.GetRawText());
                        }
                    }
                    else
                    {
                        logger.LogDebug("No device element found in RawFitData");
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to extract metrics from RawFitData JSON");
                }
            }

            // Infer device from Source field if device is missing or "Development"
            if (string.IsNullOrWhiteSpace(workout.Device) || workout.Device == "Development")
            {
                if (workout.Source == "apple_watch")
                {
                    workout.Device = "Apple Watch";
                }
            }

            // Create route GeoJSON
            var coordinates = trackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
            var routeGeoJson = JsonSerializer.Serialize(new
            {
                type = "LineString",
                coordinates = coordinates
            });

            var route = new WorkoutRoute
            {
                Id = Guid.NewGuid(),
                WorkoutId = workout.Id,
                RouteGeoJson = routeGeoJson
            };

            // Calculate splits
            var splits = gpxParser.CalculateSplits(
                trackPoints,
                distanceMeters,
                durationSeconds,
                splitDistanceMeters
            );

            foreach (var split in splits)
            {
                split.WorkoutId = workout.Id;
            }

            // Fetch weather data if GPS coordinates are available
            if (trackPoints.Count > 0)
            {
                var firstPoint = trackPoints[0];
                try
                {
                    var weatherJson = await weatherService.GetWeatherForWorkoutAsync(
                        rawStravaDataJson: null,
                        rawFitDataJson: rawFitDataJson,
                        latitude: firstPoint.Latitude,
                        longitude: firstPoint.Longitude,
                        startTime: startedAtUtc
                    );
                    if (!string.IsNullOrEmpty(weatherJson))
                    {
                        workout.Weather = weatherJson;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to fetch weather data for workout");
                    // Continue without weather - not a critical error
                }
            }

            // Save to database
            db.Workouts.Add(workout);
            db.WorkoutRoutes.Add(route);
            db.WorkoutSplits.AddRange(splits);
            await db.SaveChangesAsync();

            // Calculate Relative Effort after workout is saved
            try
            {
                var settings = await db.UserSettings.FirstOrDefaultAsync();
                if (settings != null)
                {
                    var zones = zoneService.GetZonesFromUserSettings(settings);
                    var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                    if (relativeEffort.HasValue)
                    {
                        workout.RelativeEffort = relativeEffort.Value;
                        await db.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                // Continue - Relative Effort is optional
            }

            logger.LogInformation("Imported workout {WorkoutId} with {Distance} meters", workout.Id, workout.DistanceM);

            return new FileProcessResult
            {
                Action = "created",
                Response = new
                {
                    id = workout.Id,
                    startedAt = workout.StartedAt,
                    durationS = workout.DurationS,
                    distanceM = workout.DistanceM,
                    avgPaceS = workout.AvgPaceS,
                    elevGainM = workout.ElevGainM,
                    splitsCount = splits.Count,
                    action = "created",
                    message = "Workout imported successfully"
                }
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing workout file");
            return new FileProcessResult { Action = "error", ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Maps Apple Watch identifiers to friendly device names.
    /// Based on AppleDB device information: https://appledb.dev/device-selection/Apple-Watch.html
    /// </summary>
    private static string? MapAppleWatchIdentifier(string identifier)
    {
        // Normalize identifier (remove any extra whitespace, case-insensitive)
        var normalized = identifier?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        // Apple Watch identifier pattern: "Watch" followed by number, comma, number
        // Try exact match first, then partial match
        return normalized switch
        {
            // Watch 7 series (S10/S9)
            "Watch7,12" => "Apple Watch Ultra 3",
            "Watch7,17" or "Watch7,18" or "Watch7,19" => "Apple Watch Series 11",
            "Watch7,13" or "Watch7,14" or "Watch7,15" => "Apple Watch SE 3",
            "Watch7,8" or "Watch7,9" or "Watch7,10" => "Apple Watch Series 10",
            "Watch7,5" => "Apple Watch Ultra 2",
            "Watch7,1" or "Watch7,2" or "Watch7,3" => "Apple Watch Series 9",
            
            // Watch 6 series (S8/S7/S6)
            "Watch6,18" => "Apple Watch Ultra",
            "Watch6,14" or "Watch6,15" or "Watch6,16" => "Apple Watch Series 8",
            "Watch6,10" or "Watch6,11" or "Watch6,12" => "Apple Watch SE (2nd generation)",
            "Watch6,6" or "Watch6,7" or "Watch6,8" => "Apple Watch Series 7",
            "Watch6,1" or "Watch6,2" or "Watch6,3" => "Apple Watch Series 6",
            
            // Watch 5 series (S5)
            "Watch5,9" or "Watch5,10" or "Watch5,11" => "Apple Watch SE (1st generation)",
            "Watch5,1" or "Watch5,2" or "Watch5,3" => "Apple Watch Series 5",
            
            // Watch 4 series (S4)
            "Watch4,1" or "Watch4,2" or "Watch4,3" => "Apple Watch Series 4",
            
            // Watch 3 series (S3)
            "Watch3,1" or "Watch3,2" or "Watch3,3" => "Apple Watch Series 3",
            
            // Watch 2 series (S2/S1P)
            "Watch2,3" or "Watch2,4" => "Apple Watch Series 2",
            "Watch2,6" or "Watch2,7" => "Apple Watch Series 1",
            
            // Watch 1 series (S1)
            "Watch1,1" or "Watch1,2" => "Apple Watch (1st generation)",
            
            _ => null
        };
    }

    /// <summary>
    /// Extracts device name from FIT device JSON element.
    /// According to FIT spec: ProductName is most reliable, then manufacturer+product codes.
    /// </summary>
    private static string? ExtractDeviceName(JsonElement deviceElement, ILogger? logger = null)
    {
        // 1. Check ProductName first (most reliable - actual device name string)
        if (deviceElement.TryGetProperty("productName", out var productNameElement) && 
            productNameElement.ValueKind == JsonValueKind.String)
        {
            var productName = productNameElement.GetString();
            if (!string.IsNullOrWhiteSpace(productName))
            {
                // Check if ProductName is an Apple Watch identifier and map it to friendly name
                var mappedName = MapAppleWatchIdentifier(productName);
                if (mappedName != null)
                {
                    if (logger != null)
                    {
                        logger.LogDebug("Mapped Apple Watch identifier {Identifier} to {FriendlyName}", productName, mappedName);
                    }
                    return mappedName;
                }
                
                if (logger != null)
                {
                    logger.LogDebug("Using ProductName from FIT file: {ProductName}", productName);
                }
                return productName;
            }
        }

        string? manufacturer = null;
        ushort? productCode = null;

        // 2. Extract manufacturer code
        if (deviceElement.TryGetProperty("manufacturer", out var manufacturerElement))
        {
            if (manufacturerElement.ValueKind == JsonValueKind.Number)
            {
                var manufacturerCode = manufacturerElement.GetInt32();
                
                // Handle extended manufacturer codes (>255 are manufacturer-specific)
                // For Garmin, extended codes might still be Garmin devices
                if (manufacturerCode > 255)
                {
                    // Extended manufacturer codes: check if it might be Garmin
                    // Many Garmin devices use extended codes, but we can't definitively identify them
                    // For now, if we have a product code that looks like a Garmin product, assume Garmin
                    if (deviceElement.TryGetProperty("product", out var prodCheck) && 
                        prodCheck.ValueKind == JsonValueKind.Number)
                    {
                        var prod = prodCheck.GetInt32();
                        // Garmin products typically have specific ranges
                        // If product code is reasonable (< 10000), might be Garmin
                        if (prod < 10000)
                        {
                            manufacturer = "Garmin";
                            if (logger != null)
                            {
                                logger.LogDebug("Extended manufacturer code {Code} with product {Product} - assuming Garmin", 
                                    manufacturerCode, prod);
                            }
                        }
                        else
                        {
                            if (logger != null)
                            {
                                logger.LogDebug("Extended manufacturer code: {Code} (unknown manufacturer)", manufacturerCode);
                            }
                        }
                    }
                    else
                    {
                        if (logger != null)
                        {
                            logger.LogDebug("Extended manufacturer code: {Code} (unknown manufacturer)", manufacturerCode);
                        }
                    }
                }
                else
                {
                    // Standard manufacturer codes (0-255)
                    manufacturer = manufacturerCode switch
                    {
                        1 => "Garmin",
                        2 => "Garmin", // GarminFr405Antfs
                        23 => "Suunto",
                        32 => "Wahoo Fitness",
                        71 => "TomTom",
                        73 => "Wattbike",
                        94 => "Stryd",
                        123 => "Polar",
                        129 => "Coros",
                        142 => "Tag Heuer",
                        144 => "Zwift",
                        182 => "Strava",
                        265 => "Strava",
                        294 => "Coros",
                        // 255 is "Development" in FIT spec but not helpful for end users
                        255 => null,
                        _ => null
                    };
                    
                    if (manufacturer == null && logger != null)
                    {
                        logger.LogDebug("Unknown manufacturer code: {Code}", manufacturerCode);
                    }
                }
            }
            else if (manufacturerElement.ValueKind == JsonValueKind.String)
            {
                manufacturer = manufacturerElement.GetString();
            }
        }

        // 3. Extract product code
        if (deviceElement.TryGetProperty("product", out var productElement))
        {
            if (productElement.ValueKind == JsonValueKind.Number)
            {
                productCode = (ushort)productElement.GetInt32();
            }
            else if (productElement.ValueKind == JsonValueKind.String)
            {
                // Product as string - use directly
                var productStr = productElement.GetString();
                if (!string.IsNullOrWhiteSpace(productStr))
                {
                    // If we have manufacturer, combine them
                    if (!string.IsNullOrWhiteSpace(manufacturer))
                    {
                        return $"{manufacturer} {productStr}".Trim();
                    }
                    return productStr;
                }
            }
        }

        // 4. Combine manufacturer and product code
        if (!string.IsNullOrWhiteSpace(manufacturer) && productCode.HasValue)
        {
            // For known manufacturers, show manufacturer name
            // For Garmin, we could map product codes, but that's extensive
            // For now, show manufacturer name (cleaner than "Garmin 108")
            return manufacturer;
        }

        // 5. Fallback: show product code if available
        if (productCode.HasValue)
        {
            return $"Product {productCode.Value}";
        }

        // 6. Fallback: show manufacturer if available
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            return manufacturer;
        }

        if (logger != null)
        {
            logger.LogDebug("No device information extracted from FIT file");
        }

        return null;
    }
}

