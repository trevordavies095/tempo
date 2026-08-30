using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

public sealed class WorkoutIntakeOverlay
{
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public string? RawStravaDataJson { get; init; }
    public string? RawHealthKitDataJson { get; init; }
    public Guid? HealthKitUuid { get; init; }
    public string? Source { get; init; }
    public string? Device { get; init; }
    public byte? AvgHeartRateBpm { get; init; }
    public byte? MaxHeartRateBpm { get; init; }
    public ushort? EnergyKcal { get; init; }
}

public sealed class WorkoutIntakeResult
{
    public string Action { get; init; } = "created";
    public string? ErrorMessage { get; init; }
    public Workout? Workout { get; init; }
    public int SplitsCount { get; init; }
}

/// <summary>
/// Decoded workout ready for the persist pipeline. Produced by file parsers today;
/// HealthKit (and other adapters) can build this without a file stream.
/// </summary>
public sealed class DecodedWorkout
{
    public DateTime StartedAt { get; init; }
    public int DurationS { get; init; }
    public double DistanceM { get; init; }
    public List<TrackPoint> TrackPoints { get; init; } = new();
    /// <summary>
    /// Null = GPX-style time series from TrackPoints; non-null = FIT series path.
    /// </summary>
    public IReadOnlyList<TrackPoint>? SeriesPoints { get; init; }
    public string? Name { get; init; }
    public string? RawGpxDataJson { get; init; }
    public string? RawFitDataJson { get; init; }
    public byte[]? RawFileData { get; init; }
    public string? RawFileName { get; init; }
    public string? RawFileType { get; init; }
}

/// <summary>
/// Workout intake: file decode adapters feed PersistAsync (geometry, duplicate policy,
/// weather, relative effort, best efforts). Persist is the single pipeline for all sources.
/// </summary>
public class WorkoutIntake
{
    private readonly TempoDbContext _db;
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;
    private readonly TrackGeometry _trackGeometry;
    private readonly IWeatherService _weatherService;
    private readonly HeartRateZoneService _zoneService;
    private readonly IRelativeEffortService _relativeEffortService;
    private readonly IBestEffortService _bestEffortService;
    private readonly ILogger<WorkoutIntake> _logger;

    public WorkoutIntake(
        TempoDbContext db,
        GpxParserService gpxParser,
        FitParserService fitParser,
        TrackGeometry trackGeometry,
        IWeatherService weatherService,
        HeartRateZoneService zoneService,
        IRelativeEffortService relativeEffortService,
        IBestEffortService bestEffortService,
        ILogger<WorkoutIntake> logger)
    {
        _db = db;
        _gpxParser = gpxParser;
        _fitParser = fitParser;
        _trackGeometry = trackGeometry;
        _weatherService = weatherService;
        _zoneService = zoneService;
        _relativeEffortService = relativeEffortService;
        _bestEffortService = bestEffortService;
        _logger = logger;
    }

    /// <summary>
    /// File decode adapter: validate stream, parse GPX/FIT, then persist.
    /// </summary>
    public async Task<WorkoutIntakeResult> ProcessAsync(
        Stream stream,
        string fileName,
        WorkoutIntakeOverlay? overlay = null)
    {
        if (stream == null)
        {
            return Error("File is empty");
        }

        byte[] rawFileData;
        using (var memoryStream = new MemoryStream())
        {
            await stream.CopyToAsync(memoryStream);
            rawFileData = memoryStream.ToArray();
        }

        if (rawFileData.Length == 0)
        {
            return Error("File is empty");
        }

        var (fileType, _, isFitGz) = DetermineFileType(fileName);
        if (fileType == null)
        {
            return Error("File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)");
        }

        try
        {
            var (parseResult, fitResult) = ParseWorkoutFile(rawFileData, fileType, isFitGz);
            var decoded = ToDecodedWorkout(parseResult, fitResult, rawFileData, fileName, fileType);
            return await PersistAsync(decoded, overlay);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error parsing workout file");
            return Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing workout file");
            return Error(ex.Message);
        }
    }

    /// <summary>
    /// Decode-agnostic persist pipeline: duplicate policy, geometry, weather, shoe,
    /// relative effort, best efforts.
    /// </summary>
    public async Task<WorkoutIntakeResult> PersistAsync(
        DecodedWorkout decoded,
        WorkoutIntakeOverlay? overlay = null)
    {
        try
        {
            var startedAtUtc = ToUtc(decoded.StartedAt);
            var distanceMeters = decoded.DistanceM;
            var durationSeconds = decoded.DurationS;
            var trackPoints = decoded.TrackPoints;
            var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                ? durationSeconds / (distanceMeters / 1000.0)
                : 0;

            var calculated = ExtractCalculatedMetrics(decoded.RawGpxDataJson);

            // HealthKit UUID identity check first — short-circuit before geometry/enrichment.
            if (overlay?.HealthKitUuid is Guid healthKitUuid)
            {
                var byUuid = await WorkoutQueryService.FindByHealthKitUuidAsync(_db, healthKitUuid);
                if (byUuid != null)
                {
                    _logger.LogInformation(
                        "Skipped duplicate workout (HealthKit UUID match): {HealthKitUuid}",
                        healthKitUuid);

                    return new WorkoutIntakeResult
                    {
                        Action = "skipped",
                        Workout = byUuid
                    };
                }
            }

            var existingWorkout = await WorkoutQueryService.FindDuplicateWorkoutAsync(
                _db, startedAtUtc, distanceMeters, durationSeconds);

            if (existingWorkout != null)
            {
                var stampedOwner = await TryStampHealthKitUuidAsync(existingWorkout, overlay);
                if (stampedOwner != null && stampedOwner.Id != existingWorkout.Id)
                {
                    // UUID already owned by another workout — identity wins over stats match.
                    return new WorkoutIntakeResult
                    {
                        Action = "skipped",
                        Workout = stampedOwner
                    };
                }

                return await HandleDuplicateAsync(existingWorkout, decoded, overlay, startedAtUtc);
            }

            var workout = CreateWorkoutEntity(decoded, startedAtUtc, avgPaceS, overlay);

            PopulateWorkoutMetrics(workout, calculated, decoded.RawFitDataJson);
            PopulateMetricsFromStrava(workout, overlay?.RawStravaDataJson);
            PopulateMetricsFromHealthKitOverlay(workout, overlay);

            var splitDistanceMeters = await GetSplitDistanceMetersAsync();
            var geometry = _trackGeometry.Derive(
                trackPoints,
                startedAtUtc,
                splitDistanceMeters,
                workout.Id,
                distanceMeters,
                durationSeconds,
                decoded.SeriesPoints);

            workout.ElevGainM = geometry.ElevGainM;

            var route = geometry.Route;
            var splits = geometry.Splits.ToList();
            var timeSeries = geometry.TimeSeries.ToList();
            if (timeSeries.Count > 0)
            {
                CalculateAggregateMetricsFromTimeSeries(workout, timeSeries);
            }
            else if (!string.IsNullOrEmpty(decoded.RawFitDataJson) && decoded.SeriesPoints != null)
            {
                _logger.LogInformation("FIT file imported with no sensor data. Workout created with available data (GPS, elevation, distance).");
            }

            await FetchAndAttachWeatherAsync(
                workout, trackPoints, overlay?.RawStravaDataJson, decoded.RawFitDataJson, startedAtUtc);
            await AssignDefaultShoeAsync(workout);

            _db.Workouts.Add(workout);
            _db.WorkoutRoutes.Add(route);
            _db.WorkoutSplits.AddRange(splits);
            if (timeSeries.Count > 0)
            {
                _db.WorkoutTimeSeries.AddRange(timeSeries);
            }

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (IsHealthKitUuidUniqueViolation(ex) && overlay?.HealthKitUuid is Guid racedUuid)
            {
                _db.ChangeTracker.Clear();
                var winner = await WorkoutQueryService.FindByHealthKitUuidAsync(_db, racedUuid);
                if (winner != null)
                {
                    _logger.LogInformation(
                        "Skipped duplicate workout (HealthKit UUID race): {HealthKitUuid}",
                        racedUuid);

                    return new WorkoutIntakeResult
                    {
                        Action = "skipped",
                        Workout = winner
                    };
                }

                throw;
            }

            await CalculateAndSaveRelativeEffortAsync(workout);

            try
            {
                await _bestEffortService.UpdateBestEffortsForNewWorkoutAsync(_db, workout);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update best efforts for workout {WorkoutId}", workout.Id);
            }

            _logger.LogInformation("Imported workout {WorkoutId} with {Distance} meters", workout.Id, workout.DistanceM);

            return new WorkoutIntakeResult
            {
                Action = "created",
                Workout = workout,
                SplitsCount = splits.Count
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Error persisting workout");
            return Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error persisting workout");
            return Error(ex.Message);
        }
    }

    /// <summary>
    /// Stamps HealthKitUuid onto an existing workout when missing.
    /// Returns null on success or when no stamp was needed.
    /// Returns the workout that already owns the UUID if stamping collides.
    /// </summary>
    private async Task<Workout?> TryStampHealthKitUuidAsync(Workout existingWorkout, WorkoutIntakeOverlay? overlay)
    {
        if (overlay?.HealthKitUuid is not Guid healthKitUuid || existingWorkout.HealthKitUuid.HasValue)
        {
            return null;
        }

        existingWorkout.HealthKitUuid = healthKitUuid;
        try
        {
            await _db.SaveChangesAsync();
            _logger.LogInformation(
                "Stamped HealthKit UUID {HealthKitUuid} onto existing workout {WorkoutId}",
                healthKitUuid, existingWorkout.Id);
            return null;
        }
        catch (DbUpdateException ex) when (IsHealthKitUuidUniqueViolation(ex))
        {
            _db.Entry(existingWorkout).Property(w => w.HealthKitUuid).CurrentValue = null;
            _db.Entry(existingWorkout).Property(w => w.HealthKitUuid).IsModified = false;
            _logger.LogWarning(
                ex,
                "Could not stamp HealthKit UUID {HealthKitUuid} onto workout {WorkoutId}; UUID already owned",
                healthKitUuid, existingWorkout.Id);

            return await WorkoutQueryService.FindByHealthKitUuidAsync(_db, healthKitUuid);
        }
    }

    private static bool IsHealthKitUuidUniqueViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
        {
            if (inner is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                return pg.ConstraintName?.Contains("HealthKitUuid", StringComparison.OrdinalIgnoreCase) == true
                    || pg.MessageText.Contains("HealthKitUuid", StringComparison.OrdinalIgnoreCase);
            }

            // SQLite (unit/integration tests): UNIQUE constraint on HealthKitUuid
            if (inner is SqliteException sqlite
                && (sqlite.SqliteErrorCode == 19 || sqlite.SqliteExtendedErrorCode == 2067)
                && sqlite.Message.Contains("HealthKitUuid", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<WorkoutIntakeResult> HandleDuplicateAsync(
        Workout existingWorkout,
        DecodedWorkout decoded,
        WorkoutIntakeOverlay? overlay,
        DateTime startedAtUtc)
    {
        var fileName = decoded.RawFileName ?? string.Empty;
        var fileType = decoded.RawFileType ?? string.Empty;
        var rawFileData = decoded.RawFileData;
        var rawGpxDataJson = decoded.RawGpxDataJson;
        var rawFitDataJson = decoded.RawFitDataJson;
        var rawHealthKitDataJson = overlay?.RawHealthKitDataJson;
        var trackPoints = decoded.TrackPoints;
        var distanceMeters = decoded.DistanceM;
        var durationSeconds = decoded.DurationS;
        var incomingIsHealthKit = !string.IsNullOrWhiteSpace(rawHealthKitDataJson);

        var needsRawFileUpdate = !incomingIsHealthKit
            && (existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0);
        var needsRawJsonUpdate = fileType switch
        {
            "fit" => IsFitJsonIncomplete(existingWorkout.RawFitData),
            "gpx" => IsGpxJsonIncomplete(existingWorkout.RawGpxData),
            _ when incomingIsHealthKit => IsHealthKitJsonIncomplete(existingWorkout.RawHealthKitData),
            _ => IsGpxJsonIncomplete(existingWorkout.RawGpxData)
        };

        // HealthKit against a complete file-backed workout: skip (do not overwrite).
        if (incomingIsHealthKit && ExistingHasCompleteFileData(existingWorkout))
        {
            _logger.LogInformation(
                "Skipped duplicate workout (existing file import is complete): at {StartTime}",
                startedAtUtc);

            return new WorkoutIntakeResult
            {
                Action = "skipped",
                Workout = existingWorkout
            };
        }

        if (!needsRawFileUpdate && !needsRawJsonUpdate)
        {
            _logger.LogInformation(
                "Skipped duplicate workout (already has complete raw data): {Filename} at {StartTime}",
                LogSanitizer.Sanitize(fileName), startedAtUtc);

            return new WorkoutIntakeResult
            {
                Action = "skipped",
                Workout = existingWorkout
            };
        }

        await _db.Entry(existingWorkout).Reference(w => w.Route).LoadAsync();

        if (needsRawFileUpdate && rawFileData != null && rawFileData.Length > 0)
        {
            existingWorkout.RawFileData = rawFileData;
            existingWorkout.RawFileName = fileName;
            existingWorkout.RawFileType = fileType;
        }

        if (fileType == "fit" && rawFitDataJson != null)
        {
            existingWorkout.RawFitData = rawFitDataJson;
        }

        if (fileType == "gpx" && rawGpxDataJson != null)
        {
            existingWorkout.RawGpxData = rawGpxDataJson;
        }

        if (incomingIsHealthKit && IsHealthKitJsonIncomplete(existingWorkout.RawHealthKitData))
        {
            existingWorkout.RawHealthKitData = rawHealthKitDataJson;
            if (string.IsNullOrWhiteSpace(existingWorkout.Source))
            {
                existingWorkout.Source = overlay?.Source ?? "healthkit";
            }
        }

        await _db.SaveChangesAsync();

        try
        {
            var splitDistanceMeters = await GetSplitDistanceMetersAsync();
            var geometry = _trackGeometry.Derive(
                trackPoints,
                startedAtUtc,
                splitDistanceMeters,
                existingWorkout.Id,
                distanceMeters,
                durationSeconds,
                decoded.SeriesPoints);

            var oldSplits = await _db.WorkoutSplits.Where(s => s.WorkoutId == existingWorkout.Id).ToListAsync();
            _db.WorkoutSplits.RemoveRange(oldSplits);
            _db.WorkoutSplits.AddRange(geometry.Splits);

            if (existingWorkout.Route == null || string.IsNullOrWhiteSpace(existingWorkout.Route.RouteGeoJson))
            {
                if (existingWorkout.Route == null)
                {
                    _db.WorkoutRoutes.Add(geometry.Route);
                }
                else
                {
                    existingWorkout.Route.RouteGeoJson = geometry.Route.RouteGeoJson;
                }
            }

            var hasSeries = await _db.WorkoutTimeSeries.AnyAsync(ts => ts.WorkoutId == existingWorkout.Id);
            if (!hasSeries && geometry.TimeSeries.Count > 0)
            {
                _db.WorkoutTimeSeries.AddRange(geometry.TimeSeries);
            }

            await _db.SaveChangesAsync();

            _logger.LogInformation(
                "Updated duplicate workout {WorkoutId} with raw data and recalculated splits: {Filename} at {StartTime}",
                existingWorkout.Id, LogSanitizer.Sanitize(fileName), startedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recalculate splits for updated workout {WorkoutId}", existingWorkout.Id);
        }

        return new WorkoutIntakeResult
        {
            Action = "updated",
            Workout = existingWorkout
        };
    }

    private static bool ExistingHasCompleteFileData(Workout existingWorkout)
    {
        if (existingWorkout.RawFileType == "fit" && !IsFitJsonIncomplete(existingWorkout.RawFitData))
        {
            return true;
        }

        if (existingWorkout.RawFileType == "gpx" && !IsGpxJsonIncomplete(existingWorkout.RawGpxData))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(existingWorkout.RawFitData) && !IsFitJsonIncomplete(existingWorkout.RawFitData))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(existingWorkout.RawGpxData) && !IsGpxJsonIncomplete(existingWorkout.RawGpxData))
        {
            return true;
        }

        return false;
    }

    private (GpxParserService.GpxParseResult? GpxResult, FitParserService.FitParseResult? FitResult) ParseWorkoutFile(
        byte[] rawFileData,
        string fileType,
        bool isFitGz)
    {
        if (fileType == "gpx")
        {
            using var stream = new MemoryStream(rawFileData);
            return (_gpxParser.ParseGpx(stream), null);
        }

        using (var stream = new MemoryStream(rawFileData))
        {
            try
            {
                var fitResult = isFitGz
                    ? _fitParser.ParseGzippedFit(stream)
                    : _fitParser.ParseFit(stream);
                return (null, fitResult);
            }
            catch (NotSupportedException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }
        }
    }

    private static DecodedWorkout ToDecodedWorkout(
        GpxParserService.GpxParseResult? parseResult,
        FitParserService.FitParseResult? fitResult,
        byte[] rawFileData,
        string fileName,
        string fileType)
    {
        if (parseResult != null)
        {
            return new DecodedWorkout
            {
                StartedAt = parseResult.StartTime,
                DurationS = parseResult.DurationSeconds,
                DistanceM = parseResult.DistanceMeters,
                TrackPoints = parseResult.TrackPoints,
                SeriesPoints = null,
                Name = parseResult.Name,
                RawGpxDataJson = parseResult.RawGpxDataJson,
                RawFitDataJson = null,
                RawFileData = rawFileData,
                RawFileName = fileName,
                RawFileType = fileType
            };
        }

        if (fitResult != null)
        {
            return new DecodedWorkout
            {
                StartedAt = fitResult.StartTime,
                DurationS = fitResult.DurationSeconds,
                DistanceM = fitResult.DistanceMeters,
                TrackPoints = fitResult.TrackPoints,
                SeriesPoints = fitResult.SeriesPoints,
                Name = null,
                RawGpxDataJson = null,
                RawFitDataJson = fitResult.RawFitDataJson,
                RawFileData = rawFileData,
                RawFileName = fileName,
                RawFileType = fileType
            };
        }

        throw new InvalidOperationException("Failed to parse file");
    }

    private Dictionary<string, object> ExtractCalculatedMetrics(string? rawGpxDataJson)
    {
        var calculated = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(rawGpxDataJson))
        {
            return calculated;
        }

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
            _logger.LogWarning(ex, "Failed to parse RawGpxData JSON for additional metrics");
        }

        return calculated;
    }

    private static Workout CreateWorkoutEntity(
        DecodedWorkout decoded,
        DateTime startedAtUtc,
        double avgPaceS,
        WorkoutIntakeOverlay? overlay)
    {
        var fileType = decoded.RawFileType ?? string.Empty;
        var isGpx = fileType == "gpx";

        var workout = new Workout
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAtUtc,
            DurationS = decoded.DurationS,
            DistanceM = decoded.DistanceM,
            AvgPaceS = avgPaceS,
            RawFileData = decoded.RawFileData,
            RawFileName = decoded.RawFileName,
            RawFileType = decoded.RawFileType,
            RawGpxData = decoded.RawGpxDataJson,
            RawFitData = decoded.RawFitDataJson,
            Source = overlay?.Source ?? (isGpx ? "gpx_import" : "fit_import"),
            RunType = "Easy Run",
            CreatedAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(overlay?.Name))
        {
            workout.Name = overlay.Name;
        }
        else if (!string.IsNullOrWhiteSpace(decoded.Name))
        {
            workout.Name = decoded.Name;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.Notes))
        {
            workout.Notes = overlay.Notes;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.RawStravaDataJson))
        {
            workout.RawStravaData = overlay.RawStravaDataJson;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.RawHealthKitDataJson))
        {
            workout.RawHealthKitData = overlay.RawHealthKitDataJson;
        }

        if (overlay?.HealthKitUuid is Guid healthKitUuid)
        {
            workout.HealthKitUuid = healthKitUuid;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.Device))
        {
            workout.Device = overlay.Device;
        }

        return workout;
    }

    private static void PopulateMetricsFromHealthKitOverlay(Workout workout, WorkoutIntakeOverlay? overlay)
    {
        if (overlay == null)
        {
            return;
        }

        if (overlay.AvgHeartRateBpm.HasValue)
        {
            workout.AvgHeartRateBpm = overlay.AvgHeartRateBpm;
        }

        if (overlay.MaxHeartRateBpm.HasValue)
        {
            workout.MaxHeartRateBpm = overlay.MaxHeartRateBpm;
        }

        if (overlay.EnergyKcal.HasValue)
        {
            workout.Calories = overlay.EnergyKcal;
        }
    }

    private void PopulateWorkoutMetrics(
        Workout workout,
        Dictionary<string, object> calculated,
        string? rawFitDataJson)
    {
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

        if (!string.IsNullOrEmpty(rawFitDataJson))
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

                if (rawFit.TryGetProperty("device", out var deviceElement))
                {
                    if (deviceElement.ValueKind == JsonValueKind.Object)
                    {
                        workout.Device = DeviceExtractionService.ExtractDeviceName(deviceElement, _logger);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract metrics from RawFitData JSON");
            }
        }

        if (string.IsNullOrWhiteSpace(workout.Device) || workout.Device == "Development")
        {
            if (workout.Source == "gpx_import" || workout.Source == "apple_watch" || workout.Source == "healthkit")
            {
                workout.Device = "Apple Watch";
            }
        }
    }

    private void PopulateMetricsFromStrava(Workout workout, string? rawStravaDataJson)
    {
        if (string.IsNullOrEmpty(rawStravaDataJson))
        {
            return;
        }

        Dictionary<string, object> stravaData;
        try
        {
            var rawStrava = JsonSerializer.Deserialize<JsonElement>(rawStravaDataJson);
            stravaData = new Dictionary<string, object>();
            foreach (var prop in rawStrava.EnumerateObject())
            {
                stravaData[prop.Name] = prop.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse RawStravaData JSON");
            return;
        }

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
    }

    private static void CalculateAggregateMetricsFromTimeSeries(Workout workout, List<WorkoutTimeSeries> timeSeries)
    {
        var heartRates = timeSeries.Where(ts => ts.HeartRateBpm.HasValue)
            .Select(ts => ts.HeartRateBpm!.Value).ToList();
        if (heartRates.Count > 0)
        {
            workout.MaxHeartRateBpm = heartRates.Max();
            workout.AvgHeartRateBpm = (byte)Math.Round(heartRates.Average(x => (double)x));
            workout.MinHeartRateBpm = heartRates.Min();
        }

        var cadences = timeSeries.Where(ts => ts.CadenceRpm.HasValue)
            .Select(ts => ts.CadenceRpm!.Value).ToList();
        if (cadences.Count > 0)
        {
            workout.MaxCadenceRpm = cadences.Max();
            workout.AvgCadenceRpm = (byte)Math.Round(cadences.Average(x => (double)x));
        }

        var powers = timeSeries.Where(ts => ts.PowerWatts.HasValue)
            .Select(ts => ts.PowerWatts!.Value).ToList();
        if (powers.Count > 0)
        {
            workout.MaxPowerWatts = powers.Max();
            workout.AvgPowerWatts = (ushort)Math.Round(powers.Average(x => (double)x));
        }

        var speeds = timeSeries.Where(ts => ts.SpeedMps.HasValue)
            .Select(ts => ts.SpeedMps!.Value).ToList();
        if (speeds.Count > 0 && !workout.MaxSpeedMps.HasValue)
        {
            workout.MaxSpeedMps = speeds.Max();
        }

        if (!workout.AvgSpeedMps.HasValue && workout.DistanceM > 0 && workout.DurationS > 0)
        {
            workout.AvgSpeedMps = workout.DistanceM / workout.DurationS;
        }
    }

    private async Task FetchAndAttachWeatherAsync(
        Workout workout,
        List<TrackPoint> trackPoints,
        string? rawStravaDataJson,
        string? rawFitDataJson,
        DateTime startedAtUtc)
    {
        var firstPoint = trackPoints.FirstOrDefault(p => p.HasPosition);
        if (firstPoint == null)
        {
            return;
        }

        try
        {
            var weatherJson = await _weatherService.GetWeatherForWorkoutAsync(
                rawStravaDataJson: rawStravaDataJson,
                rawFitDataJson: rawFitDataJson,
                latitude: firstPoint.Latitude,
                longitude: firstPoint.Longitude,
                startTime: startedAtUtc);
            if (!string.IsNullOrEmpty(weatherJson))
            {
                workout.Weather = weatherJson;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather data for workout");
        }
    }

    private async Task AssignDefaultShoeAsync(Workout workout)
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        if (settings == null || !settings.DefaultShoeId.HasValue)
        {
            return;
        }

        var defaultShoe = await _db.Shoes.FindAsync(settings.DefaultShoeId.Value);
        if (defaultShoe != null && !defaultShoe.IsRetired)
        {
            workout.ShoeId = settings.DefaultShoeId.Value;
            _logger.LogInformation("Assigned default shoe {ShoeId} to workout {WorkoutId}", settings.DefaultShoeId.Value, workout.Id);
        }
    }

    private async Task CalculateAndSaveRelativeEffortAsync(Workout workout)
    {
        try
        {
            var settings = await _db.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                return;
            }

            var zones = _zoneService.GetZonesFromUserSettings(settings);
            var relativeEffort = _relativeEffortService.CalculateRelativeEffort(workout, zones, _db);
            if (relativeEffort.HasValue)
            {
                workout.RelativeEffort = relativeEffort.Value;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate Relative Effort for workout {WorkoutId}", workout.Id);
        }
    }

    private async Task<double> GetSplitDistanceMetersAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        var unitPreference = settings?.UnitPreference;
        return unitPreference != null && unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? 1609.344
            : 1000.0;
    }

    private static bool IsHealthKitJsonIncomplete(string? rawHealthKitData)
    {
        return string.IsNullOrWhiteSpace(rawHealthKitData);
    }

    private static bool IsFitJsonIncomplete(string? rawFitData)
    {
        if (string.IsNullOrEmpty(rawFitData))
        {
            return true;
        }

        try
        {
            using var existingFitData = JsonDocument.Parse(rawFitData);
            return !existingFitData.RootElement.TryGetProperty("trackPoints", out _);
        }
        catch
        {
            return true;
        }
    }

    private static bool IsGpxJsonIncomplete(string? rawGpxData)
    {
        if (string.IsNullOrWhiteSpace(rawGpxData))
        {
            return true;
        }

        try
        {
            using var doc = JsonDocument.Parse(rawGpxData);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return true;
            }

            if (!doc.RootElement.TryGetProperty("trackPoints", out var trackPoints))
            {
                return true;
            }

            return trackPoints.ValueKind != JsonValueKind.Array || trackPoints.GetArrayLength() == 0;
        }
        catch
        {
            return true;
        }
    }

    private static (string? FileType, bool IsGpx, bool IsFitGz) DetermineFileType(string? fileName)
    {
        var lowerFileName = (fileName ?? string.Empty).ToLowerInvariant();
        if (lowerFileName.EndsWith(".gpx"))
        {
            return ("gpx", true, false);
        }

        if (lowerFileName.EndsWith(".fit.gz"))
        {
            return ("fit", false, true);
        }

        if (lowerFileName.EndsWith(".fit"))
        {
            return ("fit", false, false);
        }

        return (null, false, false);
    }

    private static DateTime ToUtc(DateTime startTime) => startTime.Kind switch
    {
        DateTimeKind.Utc => startTime,
        DateTimeKind.Local => startTime.ToUniversalTime(),
        _ => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
    };

    private static WorkoutIntakeResult Error(string message) => new()
    {
        Action = "error",
        ErrorMessage = message
    };
}
