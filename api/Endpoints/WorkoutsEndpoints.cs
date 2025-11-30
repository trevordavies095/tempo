using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using static Tempo.Api.Services.WorkoutQueryService;
using static Tempo.Api.Services.DeviceExtractionService;

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

    /// <summary>
    /// Import workout file(s)
    /// </summary>
    /// <param name="request">HTTP request containing multipart/form-data with file(s)</param>
    /// <param name="db">Database context</param>
    /// <param name="gpxParser">GPX parser service</param>
    /// <param name="fitParser">FIT parser service</param>
    /// <param name="weatherService">Weather service</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Import result with created/updated/skipped counts and any errors</returns>
    /// <remarks>
    /// Uploads and processes one or more GPX or FIT files (.gpx, .fit, or .fit.gz), extracting workout data
    /// and saving it to the database. Supports multiple files for batch import. Accepts optional unitPreference
    /// form field (metric or imperial) to determine split calculation distance.
    /// </remarks>
    private static async Task<IResult> ImportWorkout(
        HttpRequest request,
        TempoDbContext db,
        GpxParserService gpxParser,
        FitParserService fitParser,
        WeatherService weatherService,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        BestEffortService bestEffortService,
        ILogger<Program> logger)
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

        // Save unit preference to UserSettings
        await SaveUnitPreferenceToSettingsAsync(db, unitPreference, logger);

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
                bestEffortService,
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
                    bestEffortService,
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
    }

    /// <summary>
    /// List workouts with pagination and filtering
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100)</param>
    /// <param name="startDate">Filter workouts starting from this date (inclusive)</param>
    /// <param name="endDate">Filter workouts ending before this date (inclusive)</param>
    /// <param name="minDistanceM">Minimum distance in meters</param>
    /// <param name="maxDistanceM">Maximum distance in meters</param>
    /// <param name="keyword">Search keyword (searches Name, Device, and Source fields)</param>
    /// <param name="runType">Filter by run type (e.g., "Race", "Workout", "Long Run", "Easy Run")</param>
    /// <param name="sortBy">Sort field: "name", "duration", "distance", "elevation", "relativeeffort", or default "startedAt"</param>
    /// <param name="sortOrder">Sort order: "asc" or "desc" (default: "desc" for startedAt)</param>
    /// <returns>Paginated list of workouts with metadata</returns>
    /// <remarks>
    /// Returns a paginated list of workouts with optional filtering by date range, distance, keyword search,
    /// and run type. Supports dynamic sorting by various fields. Dates are normalized to UTC for database queries.
    /// </remarks>
    private static async Task<IResult> ListWorkouts(
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
        [FromQuery] string? sortOrder = null)
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
    }

    /// <summary>
    /// Upload media files to a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="request">HTTP request containing multipart/form-data with files</param>
    /// <param name="db">Database context</param>
    /// <param name="mediaService">Media service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of uploaded media metadata and any errors</returns>
    /// <remarks>
    /// Uploads one or more media files (images/videos) to a workout. Files are validated for size and MIME type,
    /// stored on the filesystem, and metadata is saved to the database. Returns error details if any files fail to upload.
    /// </remarks>
    private static async Task<IResult> UploadWorkoutMedia(
        Guid id,
        HttpRequest request,
        TempoDbContext db,
        MediaService mediaService,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// Delete a media file from a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="mediaId">Media ID</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>No content on success</returns>
    /// <remarks>
    /// Deletes a media file from a workout by removing the file from the filesystem and the database record.
    /// Continues with database deletion even if file deletion fails (handles orphaned records).
    /// </remarks>
    private static async Task<IResult> DeleteWorkoutMedia(
        Guid id,
        Guid mediaId,
        TempoDbContext db,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// Get a specific media file for a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="mediaId">Media ID</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Media file stream with appropriate content type</returns>
    /// <remarks>
    /// Retrieves and serves a specific media file for a workout. Supports range requests for video seeking.
    /// Returns the file with the appropriate MIME type and filename for download.
    /// </remarks>
    private static async Task<IResult> GetWorkoutMediaFile(
        Guid id,
        Guid mediaId,
        TempoDbContext db,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// List all media files for a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of media metadata</returns>
    /// <remarks>
    /// Retrieves all media files associated with a workout, ordered by creation date.
    /// Returns metadata including filename, MIME type, file size, caption, and creation timestamp.
    /// </remarks>
    private static async Task<IResult> ListWorkoutMedia(
        Guid id,
        TempoDbContext db,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// Recalculate relative effort for a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated workout with new relative effort value</returns>
    /// <remarks>
    /// Recalculates the Relative Effort score for a workout using the current heart rate zone configuration.
    /// Requires heart rate zones to be configured in settings first.
    /// </remarks>
    private static async Task<IResult> RecalculateWorkoutEffort(
        Guid id,
        TempoDbContext db,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        ILogger<Program> logger)
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
    }

    /// <summary>
    /// Recalculate splits for a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="splitRecalculationService">Split recalculation service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated workout with new splits count</returns>
    /// <remarks>
    /// Recalculates splits for a workout using the current unit preference. Splits are calculated as
    /// 1km for metric or 1 mile for imperial. Requires the workout to have route data.
    /// </remarks>
    private static async Task<IResult> RecalculateWorkoutSplits(
        Guid id,
        TempoDbContext db,
        SplitRecalculationService splitRecalculationService,
        ILogger<Program> logger)
    {
        var workout = await db.Workouts
            .Include(w => w.Route)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (workout == null)
        {
            return Results.NotFound(new { error = "Workout not found" });
        }

        if (workout.Route == null)
        {
            return Results.BadRequest(new { error = "Workout has no route data. Splits cannot be recalculated." });
        }

        try
        {
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";

            var success = await splitRecalculationService.RecalculateSplitsForWorkoutAsync(workout, unitPreference);

            if (!success)
            {
                return Results.BadRequest(new { error = "Failed to recalculate splits. Insufficient track point data." });
            }

            // Reload splits to return updated count
            await db.Entry(workout).Collection(w => w.Splits).LoadAsync();
            var splitsCount = workout.Splits.Count;

            return Results.Ok(new
            {
                id = workout.Id,
                splitsCount = splitsCount
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating splits for workout {WorkoutId}", id);
            return Results.Problem("Failed to recalculate splits");
        }
    }

    /// <summary>
    /// Crop/trim a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="request">HTTP request containing JSON body with startTrimSeconds and endTrimSeconds</param>
    /// <param name="db">Database context</param>
    /// <param name="cropService">Workout crop service</param>
    /// <param name="splitRecalculationService">Split recalculation service</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated workout with all derived data recalculated</returns>
    /// <remarks>
    /// Crops/trims a workout by removing time from the beginning and/or end. Updates all derived data including
    /// distance, duration, pace, splits, and relative effort. Requires the workout to have route data.
    /// </remarks>
    private static async Task<IResult> CropWorkout(
        Guid id,
        HttpRequest request,
        TempoDbContext db,
        WorkoutCropService cropService,
        SplitRecalculationService splitRecalculationService,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        BestEffortService bestEffortService,
        ILogger<Program> logger)
    {
        // Parse request body
        JsonDocument? jsonDoc;
        try
        {
            jsonDoc = await JsonDocument.ParseAsync(request.Body);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse crop request body");
            return Results.BadRequest(new { error = "Invalid request body" });
        }

        if (jsonDoc == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        var root = jsonDoc.RootElement;

        // Extract trim values
        if (!root.TryGetProperty("startTrimSeconds", out var startTrimElement) ||
            !startTrimElement.TryGetInt32(out var startTrimSeconds))
        {
            return Results.BadRequest(new { error = "startTrimSeconds is required and must be an integer" });
        }

        if (!root.TryGetProperty("endTrimSeconds", out var endTrimElement) ||
            !endTrimElement.TryGetInt32(out var endTrimSeconds))
        {
            return Results.BadRequest(new { error = "endTrimSeconds is required and must be an integer" });
        }

        // Validate trim values
        if (startTrimSeconds < 0 || endTrimSeconds < 0)
        {
            return Results.BadRequest(new { error = "Trim values must be non-negative" });
        }

        // Load workout with related data
        var workout = await db.Workouts
            .Include(w => w.Route)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (workout == null)
        {
            return Results.NotFound(new { error = "Workout not found" });
        }

        if (workout.Route == null)
        {
            return Results.BadRequest(new { error = "Workout has no route data. Cannot crop workout without route." });
        }

        // Validate crop parameters
        if (startTrimSeconds + endTrimSeconds >= workout.DurationS)
        {
            return Results.BadRequest(new { error = $"Cannot crop entire workout. Trim values ({startTrimSeconds}s + {endTrimSeconds}s) must be less than workout duration ({workout.DurationS}s)" });
        }

        // Check which best efforts reference this workout before cropping
        // This is needed to recalculate best efforts for distances the workout may no longer qualify for after cropping
        var affectedBestEfforts = await db.BestEfforts
            .Where(be => be.WorkoutId == id)
            .Select(be => be.Distance)
            .ToListAsync();

        try
        {
            // Perform crop
            await cropService.CropWorkoutAsync(workout, startTrimSeconds, endTrimSeconds);

            // Recalculate splits
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";
            await splitRecalculationService.RecalculateSplitsForWorkoutAsync(workout, unitPreference);

            // Recalculate relative effort if heart rate zones are configured
            if (settings != null)
            {
                var zones = zoneService.GetZonesFromUserSettings(settings);
                var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                workout.RelativeEffort = relativeEffort;
                await db.SaveChangesAsync();
            }

            // Update best efforts since distance/duration changed
            try
            {
                await bestEffortService.UpdateBestEffortsForNewWorkoutAsync(db, workout);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update best efforts after cropping workout {WorkoutId}", workout.Id);
                // Don't fail crop if best effort update fails
            }

            // Recalculate affected best efforts if this workout previously held records
            // for distances it may no longer qualify for after cropping
            if (affectedBestEfforts.Any())
            {
                try
                {
                    // Reload workout to get updated distance after crop
                    await db.Entry(workout).ReloadAsync();
                    
                    // For each affected distance, check if workout still qualifies
                    foreach (var distanceName in affectedBestEfforts)
                    {
                        if (BestEffortService.StandardDistances.TryGetValue(distanceName, out var targetDistanceM))
                        {
                            // Check if workout still qualifies for this distance
                            var stillQualifies = workout.DistanceM >= targetDistanceM;
                            
                            // Check if this workout still holds the record
                            var currentBestEffort = await db.BestEfforts
                                .FirstOrDefaultAsync(be => be.Distance == distanceName);
                            
                            var workoutStillHoldsRecord = currentBestEffort != null && currentBestEffort.WorkoutId == id;
                            
                            // If workout no longer qualifies OR no longer holds the record, recalculate from all workouts
                            if (!stillQualifies || !workoutStillHoldsRecord)
                            {
                                var qualifyingWorkouts = await db.Workouts
                                    .Include(w => w.Route)
                                    .Where(w => w.DistanceM >= targetDistanceM)
                                    .ToListAsync();

                                BestEffortService.BestEffortResult? newBestEffort = null;
                                foreach (var remainingWorkout in qualifyingWorkouts)
                                {
                                    var result = await bestEffortService.CalculateBestEffortForWorkoutAsync(
                                        db, remainingWorkout, distanceName, targetDistanceM);
                                    if (result != null && (newBestEffort == null || result.TimeS < newBestEffort.TimeS))
                                    {
                                        newBestEffort = result;
                                    }
                                }

                                // Update or remove best effort
                                if (newBestEffort != null)
                                {
                                    if (currentBestEffort != null)
                                    {
                                        currentBestEffort.TimeS = newBestEffort.TimeS;
                                        currentBestEffort.WorkoutId = Guid.Parse(newBestEffort.WorkoutId);
                                        currentBestEffort.WorkoutDate = DateTime.SpecifyKind(DateTime.Parse(newBestEffort.WorkoutDate), DateTimeKind.Utc);
                                        currentBestEffort.CalculatedAt = DateTime.UtcNow;
                                    }
                                    else
                                    {
                                        db.BestEfforts.Add(new BestEffort
                                        {
                                            Distance = distanceName,
                                            DistanceM = targetDistanceM,
                                            TimeS = newBestEffort.TimeS,
                                            WorkoutId = Guid.Parse(newBestEffort.WorkoutId),
                                            WorkoutDate = DateTime.SpecifyKind(DateTime.Parse(newBestEffort.WorkoutDate), DateTimeKind.Utc),
                                            CalculatedAt = DateTime.UtcNow
                                        });
                                    }
                                }
                                else if (currentBestEffort != null)
                                {
                                    // No qualifying workouts remain, remove best effort
                                    db.BestEfforts.Remove(currentBestEffort);
                                }
                            }
                        }
                    }

                    await db.SaveChangesAsync();
                    logger.LogInformation("Recalculated affected best efforts after cropping workout {WorkoutId}", id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to recalculate affected best efforts after cropping workout {WorkoutId}", id);
                    // Don't fail crop if best effort recalculation fails
                }
            }

            // Reload workout with updated data
            await db.Entry(workout).ReloadAsync();
            await db.Entry(workout).Reference(w => w.Route).LoadAsync();
            await db.Entry(workout).Collection(w => w.Splits).LoadAsync();

            // Parse route GeoJSON for response
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

            // Map splits
            var splits = workout.Splits.Select(s => new
            {
                idx = s.Idx,
                distanceM = s.DistanceM,
                durationS = s.DurationS,
                paceS = s.PaceS
            }).ToList();

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
                route = routeGeoJson,
                splits = splits
            });
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Invalid crop operation for workout {WorkoutId}", id);
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error cropping workout {WorkoutId}", id);
            return Results.Problem("Failed to crop workout");
        }
    }

    /// <summary>
    /// Get workout details
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="weatherService">Weather service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Complete workout data including route, splits, weather, and raw data</returns>
    /// <remarks>
    /// Retrieves complete workout data including route (as GeoJSON), splits, weather information,
    /// and raw GPX/FIT/Strava data. Weather humidity values are normalized for consistency.
    /// </remarks>
    private static async Task<IResult> GetWorkout(
        Guid id,
        TempoDbContext db,
        WeatherService weatherService,
        ILogger<Program> logger)
    {
        var workout = await db.Workouts
            .Include(w => w.Route)
            .Include(w => w.Splits.OrderBy(s => s.Idx))
            .Include(w => w.Shoe)
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

        // Include shoe information if assigned
        object? shoe = null;
        if (workout.Shoe != null)
        {
            shoe = new
            {
                id = workout.Shoe.Id,
                brand = workout.Shoe.Brand,
                model = workout.Shoe.Model
            };
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
            shoeId = workout.ShoeId,
            shoe = shoe,
            weather = weather,
            rawGpxData = rawGpxData,
            rawFitData = rawFitData,
            rawStravaData = rawStravaData,
            createdAt = workout.CreatedAt,
            route = routeGeoJson,
            splits = splits
        });
    }

    /// <summary>
    /// Bulk import Strava export
    /// </summary>
    /// <param name="request">HTTP request containing multipart/form-data with ZIP file</param>
    /// <param name="bulkImportService">Bulk import service</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Import results with counts of successful, skipped, updated, and error details</returns>
    /// <remarks>
    /// Uploads and processes a ZIP file containing Strava export (activities.csv + activity files),
    /// importing all run activities with duplicate detection. Supports optional unitPreference form field.
    /// Only "Run" activities are imported; others are skipped.
    /// </remarks>
    private static async Task<IResult> BulkImportWorkouts(
        HttpRequest request,
        BulkImportService bulkImportService,
        TempoDbContext db,
        ILogger<Program> logger)
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

        // Save unit preference to UserSettings
        await SaveUnitPreferenceToSettingsAsync(db, unitPreference, logger);

        // Calculate split distance based on unit preference
        // 1000.0 meters = 1 km for metric, 1609.344 meters = 1 mile for imperial
        var splitDistanceMeters = unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? 1609.344
            : 1000.0;

        string? tempDir = null;
        var errors = new List<object>();
        var successful = 0;
        var skipped = 0;
        var updated = 0;
        var totalProcessed = 0;

        try
        {
            // Extract ZIP file
            using (var zipStream = file.OpenReadStream())
            {
                tempDir = bulkImportService.ExtractZipArchive(zipStream);
            }

            // Parse activities.csv
            var allActivities = bulkImportService.ParseActivitiesCsv(tempDir);
            var csvParser = bulkImportService.GetCsvParser();
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
                var result = await bulkImportService.ProcessActivityFileAsync(activity, tempDir, splitDistanceMeters);
                
                if (!result.Success)
                {
                    errors.Add(new { filename = activity.Filename, error = result.ErrorMessage });
                        continue;
                    }

                if (result.Action == "skipped")
                {
                    skipped++;
                    // Process media for skipped workouts
                    if (result.MediaPaths.Count > 0)
                    {
                        var media = await bulkImportService.ProcessMediaFilesAsync(result.Workout!.Id, result.MediaPaths, tempDir);
                        mediaToAdd.AddRange(media);
                    }
                            continue;
                }

                if (result.Action == "updated")
                {
                            updated++;
                    // Process media for updated workouts
                    if (result.MediaPaths.Count > 0)
                    {
                        var media = await bulkImportService.ProcessMediaFilesAsync(result.Workout!.Id, result.MediaPaths, tempDir);
                        mediaToAdd.AddRange(media);
                    }
                                            continue;
                }

                // Created new workout
                if (result.Workout != null && result.Route != null && result.Splits != null)
                {
                    workoutsToAdd.Add(result.Workout);
                    routesToAdd.Add(result.Route);
                    splitsToAdd.AddRange(result.Splits);
                    successful++;

                    // Process media files
                    if (result.MediaPaths.Count > 0)
                    {
                        var media = await bulkImportService.ProcessMediaFilesAsync(result.Workout.Id, result.MediaPaths, tempDir);
                        mediaToAdd.AddRange(media);
                    }
                }
            }

            // Assign default shoe to all new workouts if set
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings != null && settings.DefaultShoeId.HasValue)
            {
                // Verify the shoe still exists
                var defaultShoe = await db.Shoes.FindAsync(settings.DefaultShoeId.Value);
                if (defaultShoe != null)
                {
                    foreach (var workout in workoutsToAdd)
                    {
                        workout.ShoeId = settings.DefaultShoeId.Value;
                    }
                    logger.LogInformation("Assigned default shoe {ShoeId} to {Count} workouts", settings.DefaultShoeId.Value, workoutsToAdd.Count);
                }
            }

            // Batch save workouts
            await bulkImportService.BatchSaveWorkoutsAsync(workoutsToAdd, routesToAdd, splitsToAdd);

            // Calculate relative effort
            await bulkImportService.CalculateAndSaveRelativeEffortAsync(workoutsToAdd);

            // Save media records
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
            if (tempDir != null)
            {
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
        }
    }

    /// <summary>
    /// Get weekly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Daily miles for the current week (Monday-Sunday)</returns>
    /// <remarks>
    /// Returns daily miles for the current week (Monday-Sunday), grouped by day of week.
    /// Distances are converted from meters to miles. Week boundaries are calculated in the specified timezone.
    /// </remarks>
    private static async Task<IResult> GetWeeklyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
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
            ? DateTime.SpecifyKind(weekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekStart, DateTimeKind.Utc);
        var weekEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
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
    }

    /// <summary>
    /// Get relative effort stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Cumulative relative effort for the current week and 3-week average range</returns>
    /// <remarks>
    /// Returns cumulative relative effort for the current week (Monday-Sunday) and calculates
    /// the 3-week average and range from the previous 3 complete weeks.
    /// </remarks>
    private static async Task<IResult> GetRelativeEffortStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current week boundaries (Monday-Sunday) in the specified timezone
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
            ? DateTime.SpecifyKind(weekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekStart, DateTimeKind.Utc);
        var weekEndUtc = timezoneOffsetMinutes.HasValue
            ? DateTime.SpecifyKind(weekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
            : DateTime.SpecifyKind(weekEnd, DateTimeKind.Utc);

        // Query workouts for the current week with relative effort
        var currentWeekWorkouts = await db.Workouts
            .Where(w => w.StartedAt >= weekStartUtc && w.StartedAt <= weekEndUtc)
            .AsNoTracking()
            .ToListAsync();

        // Calculate daily relative effort totals (Monday-Sunday)
        var dailyEffort = new int[7]; // M T W T F S S
        foreach (var workout in currentWeekWorkouts)
        {
            if (!workout.RelativeEffort.HasValue)
            {
                continue;
            }

            // Convert UTC to local timezone
            var localTime = timezoneOffsetMinutes.HasValue
                ? workout.StartedAt.AddMinutes(timezoneOffsetMinutes.Value)
                : workout.StartedAt;

            // Get day of week (0=Monday, 6=Sunday)
            var dayOfWeek = ((int)localTime.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
            dailyEffort[dayOfWeek] += workout.RelativeEffort.Value;
        }

        // Calculate cumulative values (Monday = day 1, Tuesday = day 1 + day 2, etc.)
        var cumulativeEffort = new int[7];
        int runningTotal = 0;
        for (int i = 0; i < 7; i++)
        {
            runningTotal += dailyEffort[i];
            cumulativeEffort[i] = runningTotal;
        }

        // Calculate previous 3 complete weeks
        var previousWeeks = new List<int>();
        for (int weekOffset = 1; weekOffset <= 3; weekOffset++)
        {
            var prevWeekStart = weekStart.AddDays(-7 * weekOffset);
            var prevWeekEnd = prevWeekStart.AddDays(7).AddTicks(-1);

            var prevWeekStartUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(prevWeekStart.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(prevWeekStart, DateTimeKind.Utc);
            var prevWeekEndUtc = timezoneOffsetMinutes.HasValue
                ? DateTime.SpecifyKind(prevWeekEnd.AddMinutes(-timezoneOffsetMinutes.Value), DateTimeKind.Utc)
                : DateTime.SpecifyKind(prevWeekEnd, DateTimeKind.Utc);

            var prevWeekTotal = await db.Workouts
                .Where(w => w.StartedAt >= prevWeekStartUtc && w.StartedAt <= prevWeekEndUtc)
                .AsNoTracking()
                .SumAsync(w => (int?)w.RelativeEffort) ?? 0;

            previousWeeks.Add(prevWeekTotal);
        }

        // Calculate 3-week average and range
        var threeWeekAverage = previousWeeks.Count > 0 ? previousWeeks.Average() : 0.0;
        var rangeMin = previousWeeks.Count > 0 ? previousWeeks.Min() : 0;
        var rangeMax = previousWeeks.Count > 0 ? previousWeeks.Max() : 0;

        // Calculate current week total (last cumulative value)
        var currentWeekTotal = cumulativeEffort[6];

        return Results.Ok(new
        {
            weekStart = weekStart.ToString("yyyy-MM-dd"),
            weekEnd = weekEnd.ToString("yyyy-MM-dd"),
            currentWeek = cumulativeEffort,
            previousWeeks = previousWeeks,
            threeWeekAverage = Math.Round(threeWeekAverage, 1),
            rangeMin = rangeMin,
            rangeMax = rangeMax,
            currentWeekTotal = currentWeekTotal
        });
    }

    /// <summary>
    /// Get best efforts for all standard distances
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="bestEffortService">Best effort service</param>
    /// <returns>List of best effort times for each standard distance</returns>
    /// <remarks>
    /// Returns the fastest time achieved for each standard running distance (400m through Marathon).
    /// Best efforts are calculated from any segment within any workout, not just workouts of that exact distance.
    /// </remarks>
    private static async Task<IResult> GetBestEfforts(
        TempoDbContext db,
        BestEffortService bestEffortService)
    {
        try
        {
            var bestEfforts = await bestEffortService.GetBestEffortsAsync(db);
            return Results.Ok(new { distances = bestEfforts });
        }
        catch (Exception ex)
        {
            return Results.Problem($"Failed to retrieve best efforts: {ex.Message}");
        }
    }

    /// <summary>
    /// Recalculate all best efforts
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="bestEffortService">Best effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Success message with count of best efforts calculated</returns>
    /// <remarks>
    /// Performs a full recalculation of all best efforts across all workouts.
    /// This may take some time depending on the number of workouts and time series data.
    /// </remarks>
    private static async Task<IResult> RecalculateBestEfforts(
        TempoDbContext db,
        BestEffortService bestEffortService,
        ILogger<Program> logger)
    {
        try
        {
            logger.LogInformation("Starting manual recalculation of best efforts");
            var bestEfforts = await bestEffortService.CalculateAllBestEffortsAsync(db);
            logger.LogInformation("Completed manual recalculation of best efforts. Found {Count} best efforts", bestEfforts.Count);
            
            return Results.Ok(new
            {
                message = "Best efforts recalculated successfully",
                count = bestEfforts.Count
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating best efforts");
            return Results.Problem($"Failed to recalculate best efforts: {ex.Message}");
        }
    }

    /// <summary>
    /// Get yearly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Total miles for the current year and previous year</returns>
    /// <remarks>
    /// Returns total miles for the current year and previous year. Year boundaries are calculated
    /// in the specified timezone. Distances are converted from meters to miles.
    /// </remarks>
    private static async Task<IResult> GetYearlyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
    {
        // Get current date in the specified timezone
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
    }

    /// <summary>
    /// Get yearly weekly stats
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="periodEndDate">End date of the period (YYYY-MM-DD format). If not provided, defaults to today.</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>52 equal week buckets within a 1-year period</returns>
    /// <remarks>
    /// Returns 52 equal week buckets within a 1-year period, covering all dates with no gaps or overlaps.
    /// If periodEndDate not provided, defaults to today (last 12 months ending today).
    /// Each bucket represents approximately 1/52 of the total period.
    /// </remarks>
    private static async Task<IResult> GetYearlyWeeklyStats(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] string? periodEndDate = null,
        [FromQuery] int? timezoneOffsetMinutes = null)
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
    }

    /// <summary>
    /// Get available periods
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="timezoneOffsetMinutes">Timezone offset in minutes (negative for timezones behind UTC)</param>
    /// <returns>Consecutive 1-year periods going backwards from today</returns>
    /// <remarks>
    /// Returns consecutive 1-year periods (365/366 days) going backwards from today.
    /// Current period is the last 12 months ending today. Stops when reaching the first workout date
    /// or after 20 periods (safety limit).
    /// </remarks>
    private static async Task<IResult> GetAvailablePeriods(
        TempoDbContext db,
        ILogger<Program> logger,
        [FromQuery] int? timezoneOffsetMinutes = null)
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
    }

    /// <summary>
    /// Get available years
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>List of years that have workouts</returns>
    /// <remarks>
    /// Returns a list of distinct years (in descending order) that have workouts in the database.
    /// </remarks>
    private static async Task<IResult> GetAvailableYears(
        TempoDbContext db,
        ILogger<Program> logger)
    {
        // Get distinct years from workouts
        var years = await db.Workouts
            .AsNoTracking()
            .Select(w => w.StartedAt.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();

        return Results.Ok(years);
    }

    /// <summary>
    /// Update workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="request">HTTP request containing JSON body with optional runType, notes, and/or name</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated workout fields</returns>
    /// <remarks>
    /// Updates workout RunType, Notes, and/or Name. All fields are optional - only provided fields are updated.
    /// RunType must be one of: "Race", "Workout", "Long Run", "Easy Run", or null.
    /// Name must be 200 characters or less.
    /// </remarks>
    private static async Task<IResult> UpdateWorkout(
        Guid id,
        HttpRequest request,
        TempoDbContext db,
        ILogger<Program> logger)
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

        // Validate and update Name if provided
        if (root.TryGetProperty("name", out var nameElement))
        {
            string? nameValue = null;
            if (nameElement.ValueKind == JsonValueKind.String)
            {
                nameValue = nameElement.GetString();
                // Validate max length (200 characters as per model constraint)
                if (nameValue != null && nameValue.Length > 200)
                {
                    return Results.BadRequest(new { error = "name must be 200 characters or less" });
                }
            }
            else if (nameElement.ValueKind == JsonValueKind.Null)
            {
                nameValue = null;
            }
            else
            {
                return Results.BadRequest(new { error = "name must be a string or null" });
            }
            workout.Name = nameValue;
        }

        // Validate and update ShoeId if provided
        if (root.TryGetProperty("shoeId", out var shoeIdElement))
        {
            Guid? shoeIdValue = null;
            if (shoeIdElement.ValueKind == JsonValueKind.String)
            {
                if (Guid.TryParse(shoeIdElement.GetString(), out var parsedGuid))
                {
                    shoeIdValue = parsedGuid;
                    // Validate that the shoe exists
                    var shoe = await db.Shoes.FindAsync(shoeIdValue.Value);
                    if (shoe == null)
                    {
                        return Results.BadRequest(new { error = "Shoe not found" });
                    }
                }
                else
                {
                    return Results.BadRequest(new { error = "shoeId must be a valid GUID" });
                }
            }
            else if (shoeIdElement.ValueKind == JsonValueKind.Null)
            {
                shoeIdValue = null;
            }
            else
            {
                return Results.BadRequest(new { error = "shoeId must be a string GUID or null" });
            }
            workout.ShoeId = shoeIdValue;
        }

        // Save changes
        var runTypeUpdated = root.TryGetProperty("runType", out _);
        var notesUpdated = root.TryGetProperty("notes", out _);
        var nameUpdated = root.TryGetProperty("name", out _);
        var shoeIdUpdated = root.TryGetProperty("shoeId", out _);
        await db.SaveChangesAsync();

        logger.LogInformation("Updated workout {WorkoutId}: RunType={RunType}, RunTypeUpdated={RunTypeUpdated}, NotesUpdated={NotesUpdated}, NameUpdated={NameUpdated}, ShoeIdUpdated={ShoeIdUpdated}",
            workout.Id, workout.RunType ?? "null", runTypeUpdated, notesUpdated, nameUpdated, shoeIdUpdated);

        return Results.Ok(new
        {
            id = workout.Id,
            runType = workout.RunType,
            notes = workout.Notes,
            name = workout.Name,
            shoeId = workout.ShoeId
        });
    }

    /// <summary>
    /// Delete workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="mediaConfig">Media storage configuration</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>No content on success</returns>
    /// <remarks>
    /// Deletes a workout and all associated data including route, splits, media files (from filesystem),
    /// and database records. Continues with deletion even if individual file deletions fail.
    /// </remarks>
    private static async Task<IResult> DeleteWorkout(
        Guid id,
        TempoDbContext db,
        MediaStorageConfig mediaConfig,
        BestEffortService bestEffortService,
        ILogger<Program> logger)
    {
        // Find workout with related media
        var workout = await db.Workouts
            .Include(w => w.Media)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (workout == null)
        {
            return Results.NotFound(new { error = "Workout not found" });
        }

        // Check which best efforts reference this workout before deletion
        var affectedBestEfforts = await db.BestEfforts
            .Where(be => be.WorkoutId == id)
            .Select(be => be.Distance)
            .ToListAsync();

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
            logger.LogWarning(ex, "Error deleting workout media directory: {Directory}", Path.Combine(mediaConfig.RootPath, id.ToString()));
            // Continue with deletion even if directory deletion fails
        }

        // Delete workout (cascade will handle route, splits, and media records)
        db.Workouts.Remove(workout);
        await db.SaveChangesAsync();

        // Recalculate affected best efforts if this workout was referenced
        if (affectedBestEfforts.Any())
        {
            try
            {
                // For each affected distance, recalculate from all remaining workouts
                foreach (var distanceName in affectedBestEfforts)
                {
                    if (BestEffortService.StandardDistances.TryGetValue(distanceName, out var targetDistanceM))
                    {
                        // Find best effort from remaining workouts for this distance
                        var qualifyingWorkouts = await db.Workouts
                            .Include(w => w.Route)
                            .Where(w => w.DistanceM >= targetDistanceM && w.Id != id)
                            .ToListAsync();

                        BestEffortService.BestEffortResult? newBestEffort = null;
                        foreach (var remainingWorkout in qualifyingWorkouts)
                        {
                            var result = await bestEffortService.CalculateBestEffortForWorkoutAsync(
                                db, remainingWorkout, distanceName, targetDistanceM);
                            if (result != null && (newBestEffort == null || result.TimeS < newBestEffort.TimeS))
                            {
                                newBestEffort = result;
                            }
                        }

                        // Update or remove best effort
                        var existingBestEffort = await db.BestEfforts
                            .FirstOrDefaultAsync(be => be.Distance == distanceName);

                        if (newBestEffort != null)
                        {
                            if (existingBestEffort != null)
                            {
                                existingBestEffort.TimeS = newBestEffort.TimeS;
                                existingBestEffort.WorkoutId = Guid.Parse(newBestEffort.WorkoutId);
                                existingBestEffort.WorkoutDate = DateTime.SpecifyKind(DateTime.Parse(newBestEffort.WorkoutDate), DateTimeKind.Utc);
                                existingBestEffort.CalculatedAt = DateTime.UtcNow;
                            }
                            else
                            {
                                db.BestEfforts.Add(new BestEffort
                                {
                                    Distance = distanceName,
                                    DistanceM = targetDistanceM,
                                    TimeS = newBestEffort.TimeS,
                                    WorkoutId = Guid.Parse(newBestEffort.WorkoutId),
                                    WorkoutDate = DateTime.SpecifyKind(DateTime.Parse(newBestEffort.WorkoutDate), DateTimeKind.Utc),
                                    CalculatedAt = DateTime.UtcNow
                                });
                            }
                        }
                        else if (existingBestEffort != null)
                        {
                            // No qualifying workouts remain, remove best effort
                            db.BestEfforts.Remove(existingBestEffort);
                        }
                    }
                }

                await db.SaveChangesAsync();
                logger.LogInformation("Recalculated {Count} best efforts after deleting workout {WorkoutId}", affectedBestEfforts.Count, id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to recalculate best efforts after deleting workout {WorkoutId}", id);
                // Don't fail deletion if best effort recalculation fails
            }
        }

        logger.LogInformation("Deleted workout {WorkoutId}", id);

        return Results.NoContent();
    }

    public static void MapWorkoutsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/workouts")
            .WithTags("Workouts")
            .RequireAuthorization();

        group.MapPost("/import", ImportWorkout)
            .WithName("ImportWorkout")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces(200)
            .Produces(400)
            .Produces(500)
            .WithSummary("Import workout file(s)")
            .WithDescription("Uploads and processes one or more GPX or FIT files (.gpx, .fit, or .fit.gz), extracting workout data and saving it to the database. Supports multiple files for batch import.");

        group.MapGet("", ListWorkouts)
        .WithName("ListWorkouts")
        .Produces(200)
        .Produces(404)
        .WithSummary("List workouts")
        .WithDescription("Returns a paginated list of workouts with optional filtering");

        // Media routes must come before the generic /{id:guid} route to ensure proper routing
        group.MapPost("/{id:guid}/media", UploadWorkoutMedia)
        .WithName("UploadWorkoutMedia")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Upload media files to workout")
        .WithDescription("Uploads one or more media files (images/videos) to a workout");

        group.MapDelete("/{id:guid}/media/{mediaId:guid}", DeleteWorkoutMedia)
        .WithName("DeleteWorkoutMedia")
        .Produces(204)
        .Produces(404)
        .WithSummary("Delete workout media")
        .WithDescription("Deletes a media file from a workout (removes file from filesystem and database record)");

        group.MapGet("/{id:guid}/media/{mediaId:guid}", GetWorkoutMediaFile)
        .WithName("GetWorkoutMediaFile")
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout media file")
        .WithDescription("Retrieves and serves a specific media file for a workout");

        group.MapGet("/{id:guid}/media", ListWorkoutMedia)
        .WithName("ListWorkoutMedia")
        .Produces(200)
        .Produces(404)
        .WithSummary("List workout media")
        .WithDescription("Retrieves all media files associated with a workout");

        group.MapPost("/{id:guid}/recalculate-effort", RecalculateWorkoutEffort)
        .WithName("RecalculateWorkoutEffort")
        .Produces(200)
        .Produces(404)
        .Produces(400)
        .WithSummary("Recalculate Relative Effort")
        .WithDescription("Recalculates the Relative Effort score for a workout using current heart rate zone settings");

        group.MapPost("/{id:guid}/recalculate-splits", RecalculateWorkoutSplits)
        .WithName("RecalculateWorkoutSplits")
        .Produces(200)
        .Produces(404)
        .Produces(400)
        .WithSummary("Recalculate Splits")
        .WithDescription("Recalculates splits for a workout using current unit preference");

        group.MapPost("/{id:guid}/crop", CropWorkout)
        .WithName("CropWorkout")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Crop Workout")
        .WithDescription("Crops/trims a workout by removing time from the beginning and/or end, updating all derived data");

        group.MapGet("/{id:guid}", GetWorkout)
        .WithName("GetWorkout")
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout details")
        .WithDescription("Retrieves complete workout data including route and splits");

        group.MapPost("/import/bulk", BulkImportWorkouts)
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .WithSummary("Bulk import Strava export")
        .WithDescription("Uploads and processes a ZIP file containing Strava export (activities.csv + activity files), importing all run activities with duplicate detection");

        group.MapGet("/stats/weekly", GetWeeklyStats)
        .WithName("GetWeeklyStats")
        .Produces(200)
        .WithSummary("Get weekly stats")
        .WithDescription("Returns daily miles for the current week (Monday-Sunday), grouped by day of week");

        group.MapGet("/stats/relative-effort", GetRelativeEffortStats)
        .WithName("GetRelativeEffortStats")
        .Produces(200)
        .WithSummary("Get relative effort stats")
        .WithDescription("Returns cumulative relative effort for the current week and 3-week average range");

        group.MapGet("/stats/best-efforts", GetBestEfforts)
        .WithName("GetBestEfforts")
        .Produces(200)
        .WithSummary("Get best efforts")
        .WithDescription("Returns the fastest time achieved for each standard running distance (400m through Marathon). Best efforts are calculated from any segment within any workout.");

        group.MapPost("/stats/best-efforts/recalculate", RecalculateBestEfforts)
        .WithName("RecalculateBestEfforts")
        .Produces(200)
        .Produces(500)
        .WithSummary("Recalculate best efforts")
        .WithDescription("Performs a full recalculation of all best efforts across all workouts. This may take some time depending on the number of workouts.");

        group.MapGet("/stats/yearly", GetYearlyStats)
        .WithName("GetYearlyStats")
        .Produces(200)
        .WithSummary("Get yearly stats")
        .WithDescription("Returns total miles for the current year and previous year");

        group.MapGet("/stats/yearly-weekly", GetYearlyWeeklyStats)
        .WithName("GetYearlyWeeklyStats")
        .Produces(200)
        .WithSummary("Get yearly weekly stats")
        .WithDescription("Returns 52 equal week buckets within a 1-year period, covering all dates with no gaps or overlaps. If periodEndDate not provided, defaults to today (last 12 months ending today).");

        group.MapGet("/stats/available-periods", GetAvailablePeriods)
        .WithName("GetAvailablePeriods")
        .Produces(200)
        .WithSummary("Get available periods")
        .WithDescription("Returns consecutive 1-year periods (365/366 days) going backwards from today. Current period is the last 12 months ending today.");

        group.MapGet("/stats/available-years", GetAvailableYears)
        .WithName("GetAvailableYears")
        .Produces(200)
        .WithSummary("Get available years")
        .WithDescription("Returns list of years that have workouts");

        group.MapPatch("/{id:guid}", UpdateWorkout)
        .WithName("UpdateWorkout")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Update workout")
        .WithDescription("Updates workout RunType, Notes, and/or Name");

        group.MapDelete("/{id:guid}", DeleteWorkout)
        .WithName("DeleteWorkout")
        .Produces(204)
        .Produces(404)
        .WithSummary("Delete workout")
        .WithDescription("Deletes a workout and all associated data (route, splits, media files, and database records)");
    }

    /// <summary>
    /// Extracts calculated metrics from RawGpxData JSON.
    /// </summary>
    private static Dictionary<string, object> ExtractCalculatedMetrics(string? rawGpxDataJson, ILogger logger)
    {
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
        return calculated;
    }

    /// <summary>
    /// Determines the file type from the filename.
    /// </summary>
    private static (string? FileType, bool IsGpx, bool IsFitGz) DetermineFileType(string fileName)
    {
        var lowerFileName = fileName.ToLowerInvariant();
        bool isGpx = lowerFileName.EndsWith(".gpx");
        bool isFit = lowerFileName.EndsWith(".fit");
        bool isFitGz = lowerFileName.EndsWith(".fit.gz");

        if (isGpx)
        {
            return ("gpx", true, false);
        }
        else if (isFitGz || isFit)
        {
            return ("fit", false, isFitGz);
        }
        else
        {
            return (null, false, false);
        }
    }

    /// <summary>
    /// Parses a workout file (GPX or FIT) and returns the parse result.
    /// </summary>
    private static async Task<(GpxParserService.GpxParseResult? GpxResult, FitParserService.FitParseResult? FitResult, byte[] RawFileData)> ParseWorkoutFileAsync(
        IFormFile file,
        string fileType,
        bool isFitGz,
        GpxParserService gpxParser,
        FitParserService fitParser,
        ILogger logger)
        {
            // Read file into byte array before parsing
            byte[] rawFileData;
            using (var stream = file.OpenReadStream())
            using (var memoryStream = new MemoryStream())
            {
                await stream.CopyToAsync(memoryStream);
                rawFileData = memoryStream.ToArray();
            }

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
                    throw new InvalidOperationException(ex.Message, ex);
                }
            }
        }

        return (parseResult, fitResult, rawFileData);
    }

    /// <summary>
    /// Extracts common data from parse results.
    /// </summary>
    private static (DateTime StartTime, int DurationSeconds, double DistanceMeters, double? ElevationGainMeters, 
        List<GpxParserService.GpxPoint> TrackPoints, string? RawGpxDataJson, string? RawFitDataJson) 
        ExtractParseResultData(GpxParserService.GpxParseResult? parseResult, FitParserService.FitParseResult? fitResult)
    {
            if (parseResult != null)
            {
            return (parseResult.StartTime, parseResult.DurationSeconds, parseResult.DistanceMeters,
                parseResult.ElevationGainMeters, parseResult.TrackPoints, parseResult.RawGpxDataJson, null);
            }
            else if (fitResult != null)
            {
            return (fitResult.StartTime, fitResult.DurationSeconds, fitResult.DistanceMeters,
                fitResult.ElevationGainMeters, fitResult.TrackPoints, null, fitResult.RawFitDataJson);
            }
            else
            {
            throw new InvalidOperationException("Failed to parse file");
        }
    }

    /// <summary>
    /// Handles duplicate workout detection and updates if needed.
    /// </summary>
    private static async Task<FileProcessResult?> HandleDuplicateWorkoutAsync(
        Workout? existingWorkout,
        byte[] rawFileData,
        string fileName,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        DateTime startedAtUtc,
        TempoDbContext db,
        ILogger logger)
    {
        if (existingWorkout == null)
        {
            return null;
        }

                // Check if existing workout is missing raw file data
                bool needsRawFileUpdate = existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0;
                
                if (needsRawFileUpdate)
                {
                    // Backfill raw file data for existing workout
                    existingWorkout.RawFileData = rawFileData;
            existingWorkout.RawFileName = fileName;
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
                existingWorkout.Id, fileName, startedAtUtc);
                    
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
                fileName, startedAtUtc);
                    
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

    /// <summary>
    /// Creates a workout entity from parsed data.
    /// </summary>
    private static Workout CreateWorkoutEntity(
        DateTime startedAtUtc,
        int durationSeconds,
        double distanceMeters,
        int avgPaceS,
        double? elevationGainMeters,
        byte[] rawFileData,
        string fileName,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        bool isGpx)
    {
        return new Workout
            {
                Id = Guid.NewGuid(),
                StartedAt = startedAtUtc,
                DurationS = durationSeconds,
                DistanceM = distanceMeters,
                AvgPaceS = avgPaceS,
                ElevGainM = elevationGainMeters,
                RawFileData = rawFileData,
            RawFileName = fileName,
                RawFileType = fileType,
                RawGpxData = rawGpxDataJson,
                RawFitData = rawFitDataJson,
                Source = isGpx ? "apple_watch" : "fit_import",
                RunType = "Easy Run",
                CreatedAt = DateTime.UtcNow
            };
    }

    /// <summary>
    /// Populates workout metrics from GPX calculated data and FIT session data.
    /// </summary>
    private static void PopulateWorkoutMetrics(
        Workout workout,
        Dictionary<string, object> calculated,
        FitParserService.FitParseResult? fitResult,
        string? rawFitDataJson,
        ILogger logger)
    {
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
                        workout.Device = DeviceExtractionService.ExtractDeviceName(deviceElement, logger);
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
    }

    /// <summary>
    /// Creates a workout route from track points.
    /// </summary>
    private static WorkoutRoute CreateWorkoutRoute(Guid workoutId, List<GpxParserService.GpxPoint> trackPoints)
    {
            var coordinates = trackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
            var routeGeoJson = JsonSerializer.Serialize(new
            {
                type = "LineString",
                coordinates = coordinates
            });

        return new WorkoutRoute
            {
                Id = Guid.NewGuid(),
            WorkoutId = workoutId,
                RouteGeoJson = routeGeoJson
            };
    }

    /// <summary>
    /// Calculates splits for a workout.
    /// </summary>
    private static List<WorkoutSplit> CalculateSplits(
        GpxParserService gpxParser,
        List<GpxParserService.GpxPoint> trackPoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters,
        Guid workoutId)
    {
            var splits = gpxParser.CalculateSplits(
                trackPoints,
                distanceMeters,
                durationSeconds,
                splitDistanceMeters
            );

            foreach (var split in splits)
            {
            split.WorkoutId = workoutId;
        }

        return splits;
    }

    /// <summary>
    /// Fetches and attaches weather data to a workout.
    /// </summary>
    private static async Task FetchAndAttachWeatherAsync(
        Workout workout,
        List<GpxParserService.GpxPoint> trackPoints,
        string? rawFitDataJson,
        DateTime startedAtUtc,
        WeatherService weatherService,
        ILogger logger)
    {
        if (trackPoints.Count == 0)
        {
            return;
        }

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

    /// <summary>
    /// Calculates and saves relative effort for a workout.
    /// </summary>
    private static async Task CalculateAndSaveRelativeEffortAsync(
        Workout workout,
        TempoDbContext db,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        ILogger logger)
    {
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
        BestEffortService bestEffortService,
        double splitDistanceMeters,
        ILogger logger)
    {
        if (file == null || file.Length == 0)
        {
            return new FileProcessResult { Action = "error", ErrorMessage = "File is empty" };
        }

        // Determine file type from extension
        var (fileType, isGpx, isFitGz) = DetermineFileType(file.FileName);
        if (fileType == null)
        {
            return new FileProcessResult { Action = "error", ErrorMessage = "File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)" };
        }

        try
        {
            // Parse the file
            var (parseResult, fitResult, rawFileData) = await ParseWorkoutFileAsync(
                file, fileType, isFitGz, gpxParser, fitParser, logger);

            // Extract data from parse result
            var (startTime, durationSeconds, distanceMeters, elevationGainMeters, trackPoints, rawGpxDataJson, rawFitDataJson) =
                ExtractParseResultData(parseResult, fitResult);

            // Calculate average pace (seconds per km - stored in metric)
            var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                ? (int)(durationSeconds / (distanceMeters / 1000.0))
                : 0;

            // Extract additional metrics from RawGpxData JSON (for GPX files)
            var calculated = ExtractCalculatedMetrics(rawGpxDataJson, logger);

            // Ensure StartedAt is UTC (defensive conversion)
            var startedAtUtc = startTime.Kind switch
            {
                DateTimeKind.Utc => startTime,
                DateTimeKind.Local => startTime.ToUniversalTime(),
                _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
            };

            // Check for duplicate
            var existingWorkout = await FindDuplicateWorkoutAsync(db, startedAtUtc, distanceMeters, durationSeconds);
            var duplicateResult = await HandleDuplicateWorkoutAsync(
                existingWorkout, rawFileData, file.FileName, fileType, rawGpxDataJson, rawFitDataJson, startedAtUtc, db, logger);
            if (duplicateResult != null)
            {
                return duplicateResult;
            }

            // Create workout (no duplicate found)
            var workout = CreateWorkoutEntity(
                startedAtUtc, durationSeconds, distanceMeters, avgPaceS, elevationGainMeters,
                rawFileData, file.FileName, fileType, rawGpxDataJson, rawFitDataJson, isGpx);

            // Populate metrics
            PopulateWorkoutMetrics(workout, calculated, fitResult, rawFitDataJson, logger);

            // Create route
            var route = CreateWorkoutRoute(workout.Id, trackPoints);

            // Calculate splits
            var splits = CalculateSplits(gpxParser, trackPoints, distanceMeters, durationSeconds, splitDistanceMeters, workout.Id);

            // Fetch weather data
            await FetchAndAttachWeatherAsync(workout, trackPoints, rawFitDataJson, startedAtUtc, weatherService, logger);

            // Assign default shoe if set
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings != null && settings.DefaultShoeId.HasValue)
            {
                // Verify the shoe still exists
                var defaultShoe = await db.Shoes.FindAsync(settings.DefaultShoeId.Value);
                if (defaultShoe != null)
                {
                    workout.ShoeId = settings.DefaultShoeId.Value;
                    logger.LogInformation("Assigned default shoe {ShoeId} to workout {WorkoutId}", settings.DefaultShoeId.Value, workout.Id);
                }
            }

            // Save to database
            db.Workouts.Add(workout);
            db.WorkoutRoutes.Add(route);
            db.WorkoutSplits.AddRange(splits);
            await db.SaveChangesAsync();

            // Calculate Relative Effort after workout is saved
            await CalculateAndSaveRelativeEffortAsync(workout, db, zoneService, relativeEffortService, logger);

            // Update best efforts incrementally for new workout
            try
            {
                await bestEffortService.UpdateBestEffortsForNewWorkoutAsync(db, workout);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update best efforts for workout {WorkoutId}", workout.Id);
                // Don't fail the import if best effort update fails
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
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex, "Error parsing workout file");
            return new FileProcessResult { Action = "error", ErrorMessage = ex.Message };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error importing workout file");
            return new FileProcessResult { Action = "error", ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Saves unit preference to UserSettings if provided.
    /// </summary>
    private static async Task SaveUnitPreferenceToSettingsAsync(
        TempoDbContext db,
        string unitPreference,
        ILogger logger)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(unitPreference) ||
                (unitPreference != "metric" && unitPreference != "imperial"))
            {
                return;
            }

            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings();
                db.UserSettings.Add(settings);
            }

            // Only update if different to avoid unnecessary database writes
            if (settings.UnitPreference != unitPreference)
            {
                settings.UnitPreference = unitPreference;
                settings.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                logger.LogInformation("Updated unit preference to {UnitPreference}", unitPreference);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save unit preference to UserSettings");
            // Don't throw - this is not critical for import to succeed
        }
    }

}

