using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Utils;

namespace Tempo.Api.Endpoints;

public static class WorkoutsEndpoints
{
    private const int TimeSeriesDefaultPageSize = 1000;
    private const int TimeSeriesMaxPageSize = 5000;

    // Response class for similar routes
    private class SimilarRouteResponse
    {
        public Guid WorkoutId { get; set; }
        public DateTime StartedAt { get; set; }
        public int DurationS { get; set; }
        public double DistanceM { get; set; }
        public double AvgPaceS { get; set; }
        public double? SimilarityScore { get; set; }
        public int? TimeDifferenceS { get; set; } // Negative = faster, Positive = slower
        public double? PaceDifferenceS { get; set; } // Negative = faster pace
        public int? RelativeEffort { get; set; }
        public double? ElevGainM { get; set; }
    }

    /// <summary>
    /// Import workout file(s)
    /// </summary>
    /// <param name="request">HTTP request containing multipart/form-data with file(s)</param>
    /// <param name="db">Database context</param>
    /// <param name="workoutIntake">Workout intake module</param>
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
        WorkoutIntake workoutIntake,
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

        await SaveUnitPreferenceToSettingsAsync(db, unitPreference, logger);

        if (files.Count == 1)
        {
            var singleFileResult = await ProcessImportedFile(files[0], workoutIntake);
            if (singleFileResult.Action == "error")
            {
                return Results.BadRequest(new { error = singleFileResult.ErrorMessage ?? "Error processing file" });
            }
            return Results.Ok(MapIntakeHttpResponse(singleFileResult));
        }

        var successful = 0;
        var skipped = 0;
        var updated = 0;
        var errors = new List<object>();
        var totalProcessed = files.Count;

        foreach (var file in files)
        {
            try
            {
                var result = await ProcessImportedFile(file, workoutIntake);

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
                logger.LogError(ex, "Error processing file {FileName}", LogSanitizer.Sanitize(file.FileName));
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
            var keywordLower = keyword.ToLowerInvariant();
            // Use database-agnostic approach: ILike for PostgreSQL, Contains for SQLite
            // Check database provider using EF Core's reliable provider detection
            var isSqlite = db.Database.IsSqlite();
            
            if (isSqlite)
            {
                // SQLite: use ToLower() for case-insensitive comparison
                query = query.Where(w =>
                    (w.Name != null && w.Name.ToLower().Contains(keywordLower)) ||
                    (w.Device != null && w.Device.ToLower().Contains(keywordLower)) ||
                    (w.Source != null && w.Source.ToLower().Contains(keywordLower))
                );
            }
            else
            {
                // PostgreSQL and other providers: use ILike for case-insensitive pattern matching
                var keywordPattern = $"%{keyword}%";
                query = query.Where(w =>
                    (w.Name != null && EF.Functions.ILike(w.Name, keywordPattern)) ||
                    (w.Device != null && EF.Functions.ILike(w.Device, keywordPattern)) ||
                    (w.Source != null && EF.Functions.ILike(w.Source, keywordPattern))
                );
            }
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
                rpe = w.Rpe,
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
                        LogSanitizer.Sanitize(file.FileName), id);
                }
                else
                {
                    errors.Add(new { filename = file.FileName, error = "Failed to process file" });
                    logger.LogWarning("Failed to upload media file {FileName} for workout {WorkoutId}", 
                        LogSanitizer.Sanitize(file.FileName), id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error uploading media file {FileName} for workout {WorkoutId}", 
                    LogSanitizer.Sanitize(file.FileName), id);
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
            var sanitizedFilenames = media.Select(m => LogSanitizer.Sanitize(m.filename));
            logger.LogInformation("Media filenames: {Filenames}", string.Join(", ", sanitizedFilenames));
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

        using (jsonDoc)
        {
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
                    // Reload Route navigation property (needed for best effort calculation fallback)
                    await db.Entry(workout).Reference(w => w.Route).LoadAsync();
                    
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
                            
                            // If workout still holds the record, verify it can actually achieve the stored time
                            // This handles the case where cropping removed the fast segment that set the record
                            var storedTimeNoLongerAchievable = false;
                            if (workoutStillHoldsRecord && currentBestEffort != null && stillQualifies)
                            {
                                var croppedWorkoutBestEffort = await bestEffortService.CalculateBestEffortForWorkoutAsync(
                                    db, workout, distanceName, targetDistanceM);
                                
                                // If the cropped workout's best effort is slower than the stored time,
                                // the stored time is no longer achievable and we need to recalculate
                                if (croppedWorkoutBestEffort == null || croppedWorkoutBestEffort.TimeS > currentBestEffort.TimeS)
                                {
                                    storedTimeNoLongerAchievable = true;
                                }
                            }
                            
                            // If workout no longer qualifies OR no longer holds the record OR stored time is no longer achievable, recalculate from all workouts
                            if (!stillQualifies || !workoutStillHoldsRecord || storedTimeNoLongerAchievable)
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
                rpe = workout.Rpe,
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
            rpe = workout.Rpe,
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
    /// Paginated WorkoutTimeSeries samples for a workout.
    /// </summary>
    private static async Task<IResult> GetWorkoutTimeSeries(
        Guid id,
        TempoDbContext db,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = TimeSeriesDefaultPageSize)
    {
        var workoutExists = await db.Workouts.AsNoTracking().AnyAsync(w => w.Id == id);
        if (!workoutExists)
        {
            return Results.NotFound(new { error = "Workout not found" });
        }

        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = TimeSeriesDefaultPageSize;
        }

        if (pageSize > TimeSeriesMaxPageSize)
        {
            pageSize = TimeSeriesMaxPageSize;
        }

        var query = db.WorkoutTimeSeries.AsNoTracking()
            .Where(ts => ts.WorkoutId == id)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ThenBy(ts => ts.Id);

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        // When totalPages is 0 (no samples), only page 1 is valid; larger pages are out of range.
        var pageOutOfRange = totalPages > 0 ? page > totalPages : page > 1;
        if (pageOutOfRange)
        {
            return Results.NotFound(new { error = "Page not found" });
        }

        var rows = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ts => new
            {
                elapsedSeconds = ts.ElapsedSeconds,
                distanceM = ts.DistanceM,
                heartRateBpm = ts.HeartRateBpm.HasValue ? (int?)ts.HeartRateBpm.Value : null,
                cadenceRpm = ts.CadenceRpm.HasValue ? (int?)ts.CadenceRpm.Value : null,
                powerWatts = ts.PowerWatts.HasValue ? (int?)ts.PowerWatts.Value : null,
                speedMps = ts.SpeedMps,
                gradePercent = ts.GradePercent,
                elevationM = ts.ElevationM,
                temperatureC = ts.TemperatureC.HasValue ? (int?)ts.TemperatureC.Value : null,
                verticalSpeedMps = ts.VerticalSpeedMps
            })
            .ToListAsync();

        return Results.Ok(new
        {
            items = rows,
            page,
            pageSize,
            totalCount,
            totalPages
        });
    }

    /// <summary>
    /// Get similar routes for a workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="db">Database context</param>
    /// <param name="routeMatchingService">Route matching service</param>
    /// <param name="logger">Logger instance</param>
    /// <param name="maxResults">Maximum number of results to return (default: 10)</param>
    /// <returns>List of similar routes with comparison metrics</returns>
    /// <remarks>
    /// Returns previous workouts that were completed on similar routes, allowing users to compare
    /// their current performance with past efforts. Includes time and pace differences compared to
    /// the current workout. Requires the workout to have route data.
    /// </remarks>
    private static async Task<IResult> GetSimilarRoutes(
        Guid id,
        TempoDbContext db,
        RouteMatchingService routeMatchingService,
        ILogger<Program> logger,
        int maxResults = 10)
    {
        try
        {
            // Verify workout exists and load route
            var currentWorkout = await db.Workouts
                .Include(w => w.Route)
                .AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == id);

            if (currentWorkout == null)
            {
                return Results.NotFound(new { error = "Workout not found" });
            }

            // Verify workout has route data
            if (currentWorkout.Route == null || string.IsNullOrEmpty(currentWorkout.Route.RouteGeoJson))
            {
                return Results.BadRequest(new { error = "Workout has no route data" });
            }

            // Find similar routes
            var similarRoutes = await routeMatchingService.FindSimilarRoutesAsync(id, maxResults);

            // Get current workout details for comparison
            var currentDurationS = currentWorkout.DurationS;
            var currentAvgPaceS = currentWorkout.AvgPaceS;
            var currentRelativeEffort = currentWorkout.RelativeEffort;
            var currentElevGainM = currentWorkout.ElevGainM;

            // Load full workout details for matches to access RelativeEffort and ElevGainM
            var matchWorkoutIds = similarRoutes.Select(m => m.WorkoutId).ToList();
            var matchWorkouts = await db.Workouts
                .AsNoTracking()
                .Where(w => matchWorkoutIds.Contains(w.Id))
                .ToDictionaryAsync(w => w.Id);

            // Map to response model with calculated differences
            var response = similarRoutes.Select(match =>
            {
                matchWorkouts.TryGetValue(match.WorkoutId, out var matchWorkout);

                return new SimilarRouteResponse
                {
                    WorkoutId = match.WorkoutId,
                    StartedAt = match.StartedAt,
                    DurationS = match.DurationS,
                    DistanceM = match.DistanceM,
                    AvgPaceS = match.AvgPaceS,
                    SimilarityScore = match.SimilarityScore,
                    TimeDifferenceS = match.DurationS - currentDurationS, // Negative = faster, Positive = slower
                    PaceDifferenceS = match.AvgPaceS - currentAvgPaceS, // Negative = faster pace
                    RelativeEffort = matchWorkout?.RelativeEffort,
                    ElevGainM = matchWorkout?.ElevGainM
                };
            }).ToList();

            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching similar routes for workout {WorkoutId}", id);
            return Results.Problem("An error occurred while fetching similar routes");
        }
    }

    /// <summary>
    /// Bulk import Strava export
    /// </summary>
    /// <param name="request">HTTP request containing multipart/form-data with ZIP file</param>
    /// <param name="importJobs">Import job module</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>202 with import job document; poll GET /workouts/import/jobs/{id}</returns>
    /// <remarks>
    /// Accepts a Strava export ZIP and returns 202 with a job document. Poll GET /workouts/import/jobs/{id}
    /// until completed or failed. Supports optional unitPreference form field. Only "Run" activities are imported.
    /// </remarks>
    private static async Task<IResult> BulkImportWorkouts(
        HttpRequest request,
        ImportJobService importJobs,
        ILogger<Program> logger)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Request must be multipart/form-data" });
        }

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

        var unitPreference = form["unitPreference"].ToString();
        await using var zipStream = file.OpenReadStream();
        var result = await importJobs.AcceptWholeArchiveAsync(
            zipStream,
            file.FileName,
            file.Length,
            unitPreference,
            ImportJobKinds.StravaBulk);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Create a receiving import job for chunked upload (strava_bulk or tempo_export)
    /// </summary>
    private static async Task<IResult> CreateImportJob(
        [FromBody] CreateImportJobRequest request,
        ImportJobService importJobs)
    {
        var result = await importJobs.CreateReceivingAsync(request);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Upload one 512 KiB (or final remainder) chunk of an import ZIP
    /// </summary>
    private static async Task<IResult> PutImportJobChunk(
        Guid id,
        int index,
        int total,
        HttpRequest request,
        ImportJobService importJobs)
    {
        var result = await importJobs.PutChunkAsync(id, index, total, request.Body);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Assemble chunks and queue the import job
    /// </summary>
    private static async Task<IResult> CompleteImportJob(
        Guid id,
        ImportJobService importJobs)
    {
        var result = await importJobs.CompleteAsync(id);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Get the active import job, if any
    /// </summary>
    private static async Task<IResult> GetCurrentImportJob(ImportJobService importJobs)
    {
        var result = await importJobs.GetCurrentAsync();
        return result.ToHttpResult();
    }

    /// <summary>
    /// Get Strava bulk import job status
    /// </summary>
    private static async Task<IResult> GetImportJob(
        Guid id,
        ImportJobService importJobs)
    {
        var result = await importJobs.GetAsync(id);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Cancel a receiving, queued, or running import job
    /// </summary>
    private static async Task<IResult> CancelImportJob(
        Guid id,
        ImportJobService importJobs)
    {
        var result = await importJobs.CancelAsync(id);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Export all user data to a ZIP file
    /// </summary>
    /// <param name="user">Current user claims principal</param>
    /// <param name="exportService">Export service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>ZIP file stream containing all user data</returns>
    /// <remarks>
    /// Exports all user data including workouts, media files, shoes, settings, and best efforts
    /// in a portable ZIP format that can be imported back into Tempo.
    /// </remarks>
    private static async Task<IResult> ExportAllData(
        ClaimsPrincipal user,
        ExportService exportService,
        ILogger<Program> logger)
    {
        // Validate authentication BEFORE creating the stream
        // This ensures proper error responses instead of corrupted ZIP files
        var userIdClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            logger.LogWarning("Unauthorized export attempt - invalid user authentication");
            return Results.Unauthorized();
        }

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var filename = $"tempo-export-{timestamp}.zip";

        // Now safe to create the stream - authentication is validated
        return Results.Stream(async stream =>
        {
            try
            {
                await exportService.ExportAllDataAsync(stream);
            }
            catch (Exception ex)
            {
                // Log errors during streaming (these will result in a corrupted file,
                // but at least we log them for debugging)
                logger.LogError(ex, "Error during export streaming");
                throw;
            }
        }, "application/zip", filename);
    }

    /// <summary>
    /// Import Tempo export ZIP file
    /// </summary>
    /// <param name="request">HTTP request containing multipart/form-data with ZIP file</param>
    /// <param name="importJobs">Import job module</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>202 with import job document; poll GET /workouts/import/jobs/{id}</returns>
    /// <remarks>
    /// Accepts a Tempo export ZIP and returns 202 with a job document. Poll GET /workouts/import/jobs/{id}
    /// until completed or failed. Restores workouts, media, settings, shoes, and related entities.
    /// </remarks>
    private static async Task<IResult> ImportExport(
        HttpRequest request,
        ImportJobService importJobs,
        ILogger<Program> logger)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest(new { error = "Request must be multipart/form-data" });
        }

        request.EnableBuffering(500_000_000);

        Microsoft.AspNetCore.Http.IFormCollection form;
        try
        {
            form = await request.ReadFormAsync();
        }
        catch (Microsoft.AspNetCore.Server.Kestrel.Core.BadHttpRequestException ex) when (ex.Message.Contains("Unexpected end of request content"))
        {
            logger.LogError(ex, "Request body was incomplete or connection was closed prematurely during export import");
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

        await using var zipStream = file.OpenReadStream();
        var result = await importJobs.AcceptWholeArchiveAsync(
            zipStream,
            file.FileName,
            file.Length,
            unitPreference: null,
            ImportJobKinds.TempoExport);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Get count of workouts eligible for relative effort recalculation
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Count of workouts with heart rate data</returns>
    /// <remarks>
    /// Returns the number of workouts that have heart rate data (time series, raw FIT data, or average HR)
    /// and are eligible for relative effort calculation.
    /// </remarks>
    private static async Task<IResult> GetRecalculateRelativeEffortCount(
        TempoDbContext db,
        RelativeEffortService relativeEffortService,
        ILogger<Program> logger)
    {
        try
        {
            var allQualifyingIds = await relativeEffortService.GetQualifyingWorkoutIdsAsync(db);
            return Results.Ok(new { count = allQualifyingIds.Count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting qualifying workout count");
            return Results.Problem("Failed to get qualifying workout count");
        }
    }

    /// <summary>
    /// Recalculate relative effort for all qualifying workouts
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="zoneService">Heart rate zone service</param>
    /// <param name="relativeEffortService">Relative effort service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Recalculation results with updated count and any errors</returns>
    /// <remarks>
    /// Recalculates relative effort for all workouts that have time series heart rate data using the
    /// current heart rate zone configuration. Requires heart rate zones to be configured first.
    /// </remarks>
    private static async Task<IResult> RecalculateRelativeEffort(
        TempoDbContext db,
        HeartRateZoneService zoneService,
        RelativeEffortService relativeEffortService,
        ILogger<Program> logger)
    {
        try
        {
            // Check if heart rate zones are configured
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return Results.BadRequest(new { error = "Heart rate zones not configured. Please configure heart rate zones in settings first." });
            }

            var zones = zoneService.GetZonesFromUserSettings(settings);

            // Get workouts that can have relative effort calculated
            var allQualifyingIds = await relativeEffortService.GetQualifyingWorkoutIdsAsync(db);

            if (allQualifyingIds.Count == 0)
            {
                return Results.Ok(new
                {
                    updatedCount = 0,
                    totalQualifyingWorkouts = 0,
                    message = "No workouts with heart rate data found"
                });
            }

            // Get all qualifying workouts
            var qualifyingWorkouts = await db.Workouts
                .Where(w => allQualifyingIds.Contains(w.Id))
                .ToListAsync();

            if (qualifyingWorkouts.Count == 0)
            {
                return Results.Ok(new
                {
                    updatedCount = 0,
                    message = "No workouts with time series heart rate data found"
                });
            }

            int updatedCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            // Recalculate relative effort for each qualifying workout
            foreach (var workout in qualifyingWorkouts)
            {
                try
                {
                    var relativeEffort = relativeEffortService.CalculateRelativeEffort(workout, zones, db);
                    if (relativeEffort.HasValue)
                    {
                        workout.RelativeEffort = relativeEffort.Value;
                        updatedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
                    errors.Add($"Workout {workout.Id}: {ex.Message}");
                }
            }

            // Save all changes
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                updatedCount = updatedCount,
                totalQualifyingWorkouts = qualifyingWorkouts.Count,
                errorCount = errorCount,
                errors = errors.Count > 0 ? errors : null
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating Relative Effort for all workouts");
            return Results.Problem("Failed to recalculate Relative Effort for all workouts");
        }
    }

    /// <summary>
    /// Get count of workouts eligible for split recalculation
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Count of workouts with route data</returns>
    /// <remarks>
    /// Returns the number of workouts that have route data and can have splits recalculated.
    /// </remarks>
    private static async Task<IResult> GetRecalculateSplitsCount(
        TempoDbContext db,
        ILogger<Program> logger)
    {
        try
        {
            var count = await db.Workouts
                .Where(w => w.Route != null)
                .CountAsync();
            return Results.Ok(new { count });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting workout count for split recalculation");
            return Results.Problem("Failed to get workout count");
        }
    }

    /// <summary>
    /// Recalculate splits for all workouts
    /// </summary>
    /// <param name="db">Database context</param>
    /// <param name="splitRecalculationService">Split recalculation service</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Recalculation results with updated count and any errors</returns>
    /// <remarks>
    /// Recalculates splits for all workouts that have route data using the current unit preference.
    /// Splits are calculated as 1km for metric or 1 mile for imperial.
    /// </remarks>
    private static async Task<IResult> RecalculateSplits(
        TempoDbContext db,
        SplitRecalculationService splitRecalculationService,
        ILogger<Program> logger)
    {
        try
        {
            // Get unit preference from settings
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            var unitPreference = settings?.UnitPreference ?? "metric";

            var result = await splitRecalculationService.RecalculateSplitsForAllWorkoutsAsync(unitPreference);

            return Results.Ok(new
            {
                updatedCount = result.SuccessCount,
                totalWorkouts = result.TotalWorkouts,
                errorCount = result.ErrorCount,
                errors = result.Errors.Count > 0 ? result.Errors : null
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error recalculating splits for all workouts");
            return Results.Problem("Failed to recalculate splits for all workouts");
        }
    }

    /// <summary>
    /// Update workout
    /// </summary>
    /// <param name="id">Workout ID</param>
    /// <param name="request">HTTP request containing JSON body with optional runType, notes, name, shoeId, and/or rpe</param>
    /// <param name="db">Database context</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>Updated workout fields</returns>
    /// <remarks>
    /// Updates workout RunType, Notes, Name, ShoeId, and/or Rpe. All fields are optional - only provided fields are updated.
    /// RunType must be one of: "Race", "Workout", "Long Run", "Easy Run", or null.
    /// Name must be 200 characters or less.
    /// Rpe must be a whole number from 1 to 10, or null to clear.
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

        using (jsonDoc)
        {
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

                    if (shoe.IsRetired)
                    {
                        return Results.BadRequest(new { error = "Cannot assign a retired shoe to a workout" });
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

        // Validate and update Rpe if provided (1–10 scale, user-set)
        if (root.TryGetProperty("rpe", out var rpeElement))
        {
            if (rpeElement.ValueKind == JsonValueKind.Null)
            {
                workout.Rpe = null;
            }
            else if (rpeElement.ValueKind == JsonValueKind.Number)
            {
                if (!rpeElement.TryGetInt32(out var rpeInt))
                {
                    return Results.BadRequest(new { error = "rpe must be a whole number between 1 and 10, or null" });
                }

                if (rpeInt < 1 || rpeInt > 10)
                {
                    return Results.BadRequest(new { error = "rpe must be between 1 and 10, or null" });
                }

                workout.Rpe = (byte)rpeInt;
            }
            else
            {
                return Results.BadRequest(new { error = "rpe must be a number between 1 and 10, or null" });
            }
        }

        // Save changes
        var runTypeUpdated = root.TryGetProperty("runType", out _);
        var notesUpdated = root.TryGetProperty("notes", out _);
        var nameUpdated = root.TryGetProperty("name", out _);
        var shoeIdUpdated = root.TryGetProperty("shoeId", out _);
        var rpeUpdated = root.TryGetProperty("rpe", out _);
        await db.SaveChangesAsync();

        logger.LogInformation("Updated workout {WorkoutId}: RunType={RunType}, RunTypeUpdated={RunTypeUpdated}, NotesUpdated={NotesUpdated}, NameUpdated={NameUpdated}, ShoeIdUpdated={ShoeIdUpdated}, RpeUpdated={RpeUpdated}",
            workout.Id, LogSanitizer.Sanitize(workout.RunType ?? "null"), runTypeUpdated, notesUpdated, nameUpdated, shoeIdUpdated, rpeUpdated);

        return Results.Ok(new
        {
            id = workout.Id,
            runType = workout.RunType,
            notes = workout.Notes,
            name = workout.Name,
            shoeId = workout.ShoeId,
            rpe = workout.Rpe
        });
        }
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

        group.MapGet("/{id:guid}/similar-routes", GetSimilarRoutes)
        .WithName("GetSimilarRoutes")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Get similar routes")
        .WithDescription("Returns previous workouts that were completed on similar routes, allowing users to compare their current performance with past efforts. Includes time and pace differences compared to the current workout. Requires the workout to have route data.");

        group.MapGet("/{id:guid}/time-series", GetWorkoutTimeSeries)
        .WithName("GetWorkoutTimeSeries")
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout time series")
        .WithDescription(
            "Returns paginated WorkoutTimeSeries samples. Each item includes elapsedSeconds plus optional sensors: distanceM, heartRateBpm, cadenceRpm, powerWatts, speedMps, gradePercent, elevationM, temperatureC, verticalSpeedMps. " +
            "Null fields mean that sensor was not recorded at that sample. Samples are sparse: not every elapsed second is present. GPX imports may be sparse; FIT files are often about one sample per second but not guaranteed. " +
            "The server does not interpolate missing seconds; clients may interpolate if needed. " +
            "Ordering is ascending by elapsedSeconds, then by row id when multiple samples share the same second. " +
            $"Default pageSize is {TimeSeriesDefaultPageSize} with a maximum of {TimeSeriesMaxPageSize}; use multiple requests for long activities.");

        group.MapGet("/{id:guid}", GetWorkout)
        .WithName("GetWorkout")
        .Produces(200)
        .Produces(404)
        .WithSummary("Get workout details")
        .WithDescription("Retrieves complete workout data including route and splits");

        group.MapPost("/import/bulk", BulkImportWorkouts)
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportJobDocument>(202)
        .Produces<ImportJobDocument>(409)
        .Produces(400)
        .Produces(500)
        .WithSummary("Bulk import Strava export")
        .WithDescription("Accepts a whole Strava export ZIP and returns 202 with a job document. Poll GET /workouts/import/jobs/{id} until completed or failed. Command center should use chunked create/PUT/complete instead. Supports optional unitPreference form field.");

        group.MapPost("/import/jobs", CreateImportJob)
        .WithName("CreateImportJob")
        .Accepts<CreateImportJobRequest>("application/json")
        .Produces<ImportJobDocument>(201)
        .Produces<ImportJobDocument>(409)
        .Produces(400)
        .WithSummary("Create chunked import job")
        .WithDescription("Creates a receiving import job. Body requires kind (strava_bulk | tempo_export), filename, byteSize. unitPreference is optional for strava_bulk and rejected for tempo_export. Upload 512 KiB chunks then POST complete.");

        group.MapPut("/import/jobs/{id:guid}/chunks/{index:int}", PutImportJobChunk)
        .WithName("PutImportJobChunk")
        .Accepts<byte[]>("application/octet-stream")
        .Produces<ImportJobDocument>(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Upload import job chunk")
        .WithDescription("Puts one sequential 512 KiB (or final remainder) chunk. Query total is the expected chunk count. Body is application/octet-stream.");

        group.MapPost("/import/jobs/{id:guid}/complete", CompleteImportJob)
        .WithName("CompleteImportJob")
        .Produces<ImportJobDocument>(202)
        .Produces(400)
        .Produces(404)
        .WithSummary("Complete chunked import upload")
        .WithDescription("Assembles chunks when every index is present and the length equals byteSize, then queues the job. Mismatch does not start intake.");

        group.MapGet("/import/jobs/current", GetCurrentImportJob)
        .WithName("GetCurrentImportJob")
        .Produces<ImportJobDocument>(200)
        .Produces(204)
        .WithSummary("Get current import job")
        .WithDescription("Returns the active receiving, queued, or running import job, or 204 if none.");

        group.MapGet("/import/jobs/{id:guid}", GetImportJob)
        .WithName("GetImportJob")
        .Produces<ImportJobDocument>(200)
        .Produces(404)
        .WithSummary("Get import job")
        .WithDescription("Returns the import job document: status, byte and activity progress, counters, and error details.");

        group.MapDelete("/import/jobs/{id:guid}", CancelImportJob)
        .WithName("CancelImportJob")
        .Produces<ImportJobDocument>(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Cancel import job")
        .WithDescription("Cancels a receiving, queued, or running job. Already imported Workouts are kept. The job ends as failed/cancelled and the archive is deleted.");

        group.MapPost("/export", ExportAllData)
        .WithName("ExportAllData")
        .Produces(200)
        .Produces(500)
        .WithSummary("Export all user data")
        .WithDescription("Exports all user data including workouts, media files, shoes, settings, and best efforts in a portable ZIP format that can be imported back into Tempo. Returns a ZIP file with Content-Type: application/zip.");

        group.MapPost("/import/export", ImportExport)
        .WithName("ImportExport")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<ImportJobDocument>(202)
        .Produces<ImportJobDocument>(409)
        .Produces(400)
        .Produces(500)
        .WithSummary("Import Tempo export ZIP file")
        .WithDescription("Accepts a whole Tempo export ZIP and returns 202 with a job document. Poll GET /workouts/import/jobs/{id} until completed or failed. Command center should use chunked create/PUT/complete with kind tempo_export.")
        .WithDescription("Uploads and processes a ZIP file containing a complete Tempo export, restoring all user data including workouts, media files, settings, shoes, routes, splits, time series, and best efforts. Duplicates are skipped by default.");

        group.MapGet("/recalculate-relative-effort/count", GetRecalculateRelativeEffortCount)
            .WithName("GetRecalculateRelativeEffortCount")
            .Produces(200)
            .WithSummary("Get count of workouts eligible for relative effort recalculation")
            .WithDescription("Returns the number of workouts that have heart rate data (time series, raw FIT data, or average HR)");

        group.MapPost("/recalculate-relative-effort", RecalculateRelativeEffort)
            .WithName("RecalculateRelativeEffort")
            .Produces(200)
            .Produces(400)
            .WithSummary("Recalculate relative effort for all qualifying workouts")
            .WithDescription("Recalculates relative effort for all workouts that have time series heart rate data using the current heart rate zone configuration");

        group.MapGet("/recalculate-splits/count", GetRecalculateSplitsCount)
            .WithName("GetRecalculateSplitsCount")
            .Produces(200)
            .WithSummary("Get count of workouts eligible for split recalculation")
            .WithDescription("Returns the number of workouts that have route data and can have splits recalculated");

        group.MapPost("/recalculate-splits", RecalculateSplits)
            .WithName("RecalculateSplits")
            .Produces(200)
            .Produces(400)
            .WithSummary("Recalculate splits for all workouts")
            .WithDescription("Recalculates splits for all workouts that have route data using the current unit preference");

        group.MapPatch("/{id:guid}", UpdateWorkout)
        .WithName("UpdateWorkout")
        .Produces(200)
        .Produces(400)
        .Produces(404)
        .WithSummary("Update workout")
        .WithDescription("Updates workout RunType, Notes, Name, ShoeId, and/or Rpe (1–10)");

        group.MapDelete("/{id:guid}", DeleteWorkout)
        .WithName("DeleteWorkout")
        .Produces(204)
        .Produces(404)
        .WithSummary("Delete workout")
        .WithDescription("Deletes a workout and all associated data (route, splits, media files, and database records)");
    }

    private static async Task<WorkoutIntakeResult> ProcessImportedFile(IFormFile file, WorkoutIntake workoutIntake)
    {
        await using var stream = file.OpenReadStream();
        return await workoutIntake.ProcessAsync(stream, file.FileName);
    }

    private static object MapIntakeHttpResponse(WorkoutIntakeResult result)
    {
        var workout = result.Workout!;
        if (result.Action == "created")
        {
            return new
            {
                id = workout.Id,
                startedAt = workout.StartedAt,
                durationS = workout.DurationS,
                distanceM = workout.DistanceM,
                avgPaceS = workout.AvgPaceS,
                elevGainM = workout.ElevGainM,
                splitsCount = result.SplitsCount,
                action = "created",
                message = "Workout imported successfully"
            };
        }

        if (result.Action == "updated")
        {
            return new
            {
                id = workout.Id,
                startedAt = workout.StartedAt,
                durationS = workout.DurationS,
                distanceM = workout.DistanceM,
                avgPaceS = workout.AvgPaceS,
                elevGainM = workout.ElevGainM,
                action = "updated",
                message = "Workout already exists and was updated with raw file data"
            };
        }

        return new
        {
            id = workout.Id,
            startedAt = workout.StartedAt,
            durationS = workout.DurationS,
            distanceM = workout.DistanceM,
            avgPaceS = workout.AvgPaceS,
            elevGainM = workout.ElevGainM,
            action = "skipped",
            message = "Workout already exists and has raw file data"
        };
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
                logger.LogInformation("Updated unit preference to {UnitPreference}", Utils.LogSanitizer.Sanitize(unitPreference));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save unit preference to UserSettings");
            // Don't throw - this is not critical for import to succeed
        }
    }

}

