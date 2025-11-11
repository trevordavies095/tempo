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

            if (!file.FileName.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { error = "File must be a GPX file" });
            }

            try
            {
                // Parse GPX file
                GpxParserService.GpxParseResult parseResult;
                using (var stream = file.OpenReadStream())
                {
                    parseResult = gpxParser.ParseGpx(stream);
                }

                // Calculate average pace (seconds per km)
                var avgPaceS = parseResult.DistanceMeters > 0 && parseResult.DurationSeconds > 0
                    ? (int)(parseResult.DurationSeconds / (parseResult.DistanceMeters / 1000.0))
                    : 0;

                // Create workout
                // Ensure StartedAt is UTC (defensive conversion)
                var startedAtUtc = parseResult.StartTime.Kind switch
                {
                    DateTimeKind.Utc => parseResult.StartTime,
                    DateTimeKind.Local => parseResult.StartTime.ToUniversalTime(),
                    _ => DateTime.SpecifyKind(parseResult.StartTime, DateTimeKind.Utc)
                };

                var workout = new Workout
                {
                    Id = Guid.NewGuid(),
                    StartedAt = startedAtUtc,
                    DurationS = parseResult.DurationSeconds,
                    DistanceM = parseResult.DistanceMeters,
                    AvgPaceS = avgPaceS,
                    ElevGainM = parseResult.ElevationGainMeters,
                    Source = "apple_watch",
                    CreatedAt = DateTime.UtcNow
                };

                // Create route GeoJSON
                var coordinates = parseResult.TrackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
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
                    parseResult.TrackPoints,
                    parseResult.DistanceMeters,
                    parseResult.DurationSeconds
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
                logger.LogError(ex, "Error importing GPX file");
                return Results.Problem(
                    detail: ex.Message,
                    statusCode: 500,
                    title: "Error processing GPX file"
                );
            }
        })
        .Accepts<IFormFile>("multipart/form-data")
        .Produces(200)
        .Produces(400)
        .Produces(500)
        .WithSummary("Import a GPX workout file")
        .WithDescription("Uploads and processes a GPX file, extracting workout data and saving it to the database");

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
                        var existing = await db.Workouts
                            .Where(w => w.StartedAt == startTime &&
                                        Math.Abs(w.DistanceM - distanceMeters) < 1.0 &&
                                        Math.Abs(w.DurationS - durationSeconds) < 1)
                            .AnyAsync();

                        if (existing)
                        {
                            skipped++;
                            logger.LogInformation("Skipped duplicate workout: {Filename} at {StartTime}", activity.Filename, startTime);
                            continue;
                        }

                        // Calculate average pace
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

                        logger.LogInformation("Processed workout from {Filename}: {Distance}m in {Duration}s", 
                            activity.Filename, distanceMeters, durationSeconds);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing activity file {Filename}", activity.Filename);
                        errors.Add(new { filename = activity.Filename, error = ex.Message });
                    }
                }

                // Batch insert all workouts
                if (workoutsToAdd.Count > 0)
                {
                    db.Workouts.AddRange(workoutsToAdd);
                    db.WorkoutRoutes.AddRange(routesToAdd);
                    db.WorkoutSplits.AddRange(splitsToAdd);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Bulk imported {Count} workouts", workoutsToAdd.Count);
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
    }
}

