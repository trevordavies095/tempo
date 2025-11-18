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
            WeatherService weatherService,
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

            // Validate file extension
            var fileName = file.FileName.ToLowerInvariant();
            bool isGpx = fileName.EndsWith(".gpx");
            bool isFit = fileName.EndsWith(".fit");
            bool isFitGz = fileName.EndsWith(".fit.gz");

            if (!isGpx && !isFit && !isFitGz)
            {
                return Results.BadRequest(new { error = "File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)" });
            }

            try
            {
                // Parse file based on type
                GpxParserService.GpxParseResult? parseResult = null;
                FitParserService.FitParseResult? fitResult = null;

                using (var stream = file.OpenReadStream())
                {
                    if (isGpx)
                    {
                        parseResult = gpxParser.ParseGpx(stream);
                    }
                    else if (isFitGz)
                    {
                        try
                        {
                            fitResult = fitParser.ParseGzippedFit(stream);
                        }
                        catch (NotSupportedException ex)
                        {
                            return Results.BadRequest(new { error = ex.Message });
                        }
                    }
                    else if (isFit)
                    {
                        try
                        {
                            fitResult = fitParser.ParseFit(stream);
                        }
                        catch (NotSupportedException ex)
                        {
                            return Results.BadRequest(new { error = ex.Message });
                        }
                    }
                }

                // Use GPX result or convert FIT result
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
                    return Results.BadRequest(new { error = "Failed to parse file" });
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
                    RawGpxData = rawGpxDataJson,
                    RawFitData = rawFitDataJson,
                    Source = isGpx ? "apple_watch" : "fit_import",
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
        .WithDescription("Uploads and processes a GPX or FIT file (.gpx, .fit, or .fit.gz), extracting workout data and saving it to the database");

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
                runType = w.RunType,
                source = w.Source,
                device = w.Device,
                name = w.Name,
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

                        // Parse the activity file
                        GpxParserService.GpxParseResult? parseResult = null;
                        FitParserService.FitParseResult? fitResult = null;

                        if (filePath.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
                        {
                            using (var stream = File.OpenRead(filePath))
                            {
                                parseResult = gpxParser.ParseGpx(stream);
                            }
                        }
                        else if (filePath.EndsWith(".fit.gz", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using (var stream = File.OpenRead(filePath))
                                {
                                    fitResult = fitParser.ParseGzippedFit(stream);
                                }
                            }
                            catch (NotSupportedException ex)
                            {
                                errors.Add(new { filename = activity.Filename, error = ex.Message });
                                continue;
                            }
                        }
                        else
                        {
                            errors.Add(new { filename = activity.Filename, error = "Unsupported file format. Only .gpx and .fit.gz files are supported." });
                            continue;
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
                            skipped++;
                            logger.LogInformation("Skipped duplicate workout: {Filename} at {StartTime}", activity.Filename, startTime);
                            
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

                var validRunTypes = new[] { "Race", "Workout", "Long Run" };
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

