using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class WorkoutsEndpoints
{
    public static void MapWorkoutsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/workouts")
            .WithTags("Workouts");

        group.MapPost("/import", async (
            HttpRequest request,
            TempoDbContext db,
            GpxParserService gpxParser,
            FitParserService fitParser,
            ILogger<Program> logger) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data" });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            // Determine file type from extension
            string? fileType = null;
            if (file.FileName.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
            {
                fileType = "gpx";
            }
            else if (file.FileName.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
            {
                fileType = "fit";
            }
            else if (file.FileName.EndsWith(".fit", StringComparison.OrdinalIgnoreCase))
            {
                fileType = "fit";
            }
            else
            {
                return Results.BadRequest(new { error = "File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)" });
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
                        if (file.FileName.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            fitResult = fitParser.ParseGzippedFit(stream);
                        }
                        else
                        {
                            fitResult = fitParser.ParseFit(stream);
                        }
                    }
                }

                // Extract data from parse result
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
                    return Results.Problem(
                        detail: "Failed to parse file",
                        statusCode: 500,
                        title: "Error processing file"
                    );
                }

                // Calculate average pace (seconds per km - stored in metric)
                var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                    ? (int)(durationSeconds / (distanceMeters / 1000.0))
                    : 0;

                // Create workout
                // Ensure StartedAt is UTC (defensive conversion)
                var startedAtUtc = startTime.Kind switch
                {
                    DateTimeKind.Utc => startTime,
                    DateTimeKind.Local => startTime.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
                };

                var workout = new Workout
                {
                    Id = Guid.NewGuid(),
                    StartedAt = startedAtUtc,
                    DurationS = durationSeconds,
                    DistanceM = distanceMeters,
                    AvgPaceS = avgPaceS,
                    ElevGainM = elevationGainMeters,
                    Source = "apple_watch",
                    RawFileData = rawFileData,
                    RawFileName = file.FileName,
                    RawFileType = fileType,
                    CreatedAt = DateTime.UtcNow
                };

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
                    durationSeconds
                );

                foreach (var split in splits)
                {
                    split.WorkoutId = workout.Id;
                }

                // Save to database
                db.Workouts.Add(workout);
                db.WorkoutRoutes.Add(route);
                db.WorkoutSplits.AddRange(splits);
                await db.SaveChangesAsync();

                logger.LogInformation("Imported workout {WorkoutId} with {Distance} meters", workout.Id, workout.DistanceM);

                return Results.Ok(new
                {
                    id = workout.Id,
                    startedAt = workout.StartedAt,
                    durationS = workout.DurationS,
                    distanceM = workout.DistanceM,
                    avgPaceS = workout.AvgPaceS,
                    elevGainM = workout.ElevGainM,
                    splitsCount = splits.Count
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error importing workout file");
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error processing workout file"
                );
            }
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .WithSummary("Import a workout file")
        .WithDescription("Uploads and processes a GPX or FIT file, extracting workout data and saving it to the database");

        group.MapGet("", async (
            TempoDbContext db,
            ILogger<Program> logger,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null,
            [FromQuery] double? minDistanceM = null,
            [FromQuery] double? maxDistanceM = null) =>
        {
            // Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            if (pageSize > 100) pageSize = 100;

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

            // Get total count before pagination
            var totalCount = await query.CountAsync();

            // Calculate total pages
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            // Validate page number
            if (page > totalPages && totalPages > 0)
            {
                return Results.NotFound(new { error = "Page not found" });
            }

            // Apply ordering and pagination
            var workouts = await query
                .OrderByDescending(w => w.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .AsNoTracking()
                .ToListAsync();

            // Map to response
            var items = workouts.Select(w => new
            {
                id = w.Id,
                startedAt = w.StartedAt,
                durationS = w.DurationS,
                distanceM = w.DistanceM,
                avgPaceS = w.AvgPaceS,
                elevGainM = w.ElevGainM,
                runType = w.RunType,
                source = w.Source,
                hasRoute = w.Route != null,
                splitsCount = w.Splits.Count
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

        group.MapGet("/{id:guid}", async (
            Guid id,
            TempoDbContext db,
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

            // Parse weather JSON if exists
            object? weather = null;
            if (!string.IsNullOrEmpty(workout.Weather))
            {
                try
                {
                    weather = JsonSerializer.Deserialize<object>(workout.Weather);
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

            return Results.Ok(new
            {
                id = workout.Id,
                startedAt = workout.StartedAt,
                durationS = workout.DurationS,
                distanceM = workout.DistanceM,
                avgPaceS = workout.AvgPaceS,
                elevGainM = workout.ElevGainM,
                runType = workout.RunType,
                notes = workout.Notes,
                source = workout.Source,
                weather = weather,
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
            ILogger<Program> logger) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Request must be multipart/form-data" });
            }

            var form = await request.ReadFormAsync();
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { error = "No file uploaded" });
            }

            if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File must be a ZIP file" });
            }

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

                        // Build notes from CSV metadata
                        var notesParts = new List<string>();
                        if (!string.IsNullOrWhiteSpace(activity.ActivityName))
                        {
                            notesParts.Add(activity.ActivityName);
                        }
                        if (!string.IsNullOrWhiteSpace(activity.ActivityDescription))
                        {
                            notesParts.Add(activity.ActivityDescription);
                        }
                        if (!string.IsNullOrWhiteSpace(activity.ActivityPrivateNote))
                        {
                            notesParts.Add(activity.ActivityPrivateNote);
                        }
                        var notes = notesParts.Count > 0 ? string.Join("\n\n", notesParts) : null;

                        // Create workout
                        var workout = new Workout
                        {
                            Id = Guid.NewGuid(),
                            StartedAt = startedAtUtc,
                            DurationS = durationSeconds,
                            DistanceM = distanceMeters,
                            AvgPaceS = avgPaceS,
                            ElevGainM = elevationGainMeters,
                            Source = "strava_import",
                            Notes = notes,
                            RawFileData = rawFileData,
                            RawFileName = Path.GetFileName(activity.Filename),
                            RawFileType = fileType,
                            CreatedAt = DateTime.UtcNow
                        };

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
                            durationSeconds
                        );

                        foreach (var split in splits)
                        {
                            split.WorkoutId = workout.Id;
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
            var now = DateTime.UtcNow;
            if (timezoneOffsetMinutes.HasValue)
            {
                now = now.AddMinutes(-timezoneOffsetMinutes.Value);
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
            var now = DateTime.UtcNow;
            if (timezoneOffsetMinutes.HasValue)
            {
                now = now.AddMinutes(-timezoneOffsetMinutes.Value);
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
    }
}

