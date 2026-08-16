using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

public sealed class WorkoutIntakeOverlay
{
    public string? Name { get; init; }
    public string? Notes { get; init; }
    public string? RawStravaDataJson { get; init; }
    public string? Source { get; init; }
}

public sealed class WorkoutIntakeResult
{
    public string Action { get; init; } = "created";
    public string? ErrorMessage { get; init; }
    public Workout? Workout { get; init; }
    public int SplitsCount { get; init; }
}

/// <summary>
/// One Workout persist pipeline: parse, geometry, duplicate policy, weather, relative effort, best efforts.
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

        var (fileType, isGpx, isFitGz) = DetermineFileType(fileName);
        if (fileType == null)
        {
            return Error("File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)");
        }

        try
        {
            var (parseResult, fitResult) = ParseWorkoutFile(rawFileData, fileType, isFitGz);
            var (startTime, durationSeconds, distanceMeters, trackPoints, rawGpxDataJson, rawFitDataJson) =
                ExtractParseResultData(parseResult, fitResult);

            var avgPaceS = distanceMeters > 0 && durationSeconds > 0
                ? durationSeconds / (distanceMeters / 1000.0)
                : 0;

            var calculated = ExtractCalculatedMetrics(rawGpxDataJson);
            var startedAtUtc = ToUtc(startTime);

            var existingWorkout = await WorkoutQueryService.FindDuplicateWorkoutAsync(
                _db, startedAtUtc, distanceMeters, durationSeconds);

            if (existingWorkout != null)
            {
                return await HandleDuplicateAsync(
                    existingWorkout,
                    rawFileData,
                    fileName,
                    fileType,
                    rawGpxDataJson,
                    rawFitDataJson,
                    trackPoints,
                    startedAtUtc,
                    distanceMeters,
                    durationSeconds,
                    parseResult,
                    fitResult);
            }

            var workout = CreateWorkoutEntity(
                startedAtUtc,
                durationSeconds,
                distanceMeters,
                avgPaceS,
                rawFileData,
                fileName,
                fileType,
                rawGpxDataJson,
                rawFitDataJson,
                isGpx,
                parseResult?.Name,
                overlay);

            PopulateWorkoutMetrics(workout, calculated, fitResult, rawFitDataJson);

            var splitDistanceMeters = await GetSplitDistanceMetersAsync();
            var geometry = _trackGeometry.Derive(
                trackPoints,
                startedAtUtc,
                splitDistanceMeters,
                workout.Id,
                distanceMeters,
                durationSeconds,
                parseResult != null ? null : fitResult?.SeriesPoints);

            workout.ElevGainM = geometry.ElevGainM;

            var route = geometry.Route;
            var splits = geometry.Splits.ToList();
            var timeSeries = geometry.TimeSeries.ToList();
            if (timeSeries.Count > 0)
            {
                CalculateAggregateMetricsFromTimeSeries(workout, timeSeries);
            }
            else if (parseResult == null && fitResult != null)
            {
                _logger.LogInformation("FIT file imported with no sensor data. Workout created with available data (GPS, elevation, distance).");
            }

            await FetchAndAttachWeatherAsync(
                workout, trackPoints, overlay?.RawStravaDataJson, rawFitDataJson, startedAtUtc);
            await AssignDefaultShoeAsync(workout);

            _db.Workouts.Add(workout);
            _db.WorkoutRoutes.Add(route);
            _db.WorkoutSplits.AddRange(splits);
            if (timeSeries.Count > 0)
            {
                _db.WorkoutTimeSeries.AddRange(timeSeries);
            }
            await _db.SaveChangesAsync();

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
            _logger.LogError(ex, "Error parsing workout file");
            return Error(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing workout file");
            return Error(ex.Message);
        }
    }

    private async Task<WorkoutIntakeResult> HandleDuplicateAsync(
        Workout existingWorkout,
        byte[] rawFileData,
        string fileName,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        List<TrackPoint> trackPoints,
        DateTime startedAtUtc,
        double distanceMeters,
        int durationSeconds,
        GpxParserService.GpxParseResult? parseResult,
        FitParserService.FitParseResult? fitResult)
    {
        var needsRawFileUpdate = existingWorkout.RawFileData == null || existingWorkout.RawFileData.Length == 0;
        var needsRawJsonUpdate = fileType == "fit"
            ? IsFitJsonIncomplete(existingWorkout.RawFitData)
            : IsGpxJsonIncomplete(existingWorkout.RawGpxData);

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

        if (needsRawFileUpdate)
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
                parseResult != null ? null : fitResult?.SeriesPoints);

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

    private static (DateTime StartTime, int DurationSeconds, double DistanceMeters,
        List<TrackPoint> TrackPoints, string? RawGpxDataJson, string? RawFitDataJson)
        ExtractParseResultData(GpxParserService.GpxParseResult? parseResult, FitParserService.FitParseResult? fitResult)
    {
        if (parseResult != null)
        {
            return (parseResult.StartTime, parseResult.DurationSeconds, parseResult.DistanceMeters,
                parseResult.TrackPoints, parseResult.RawGpxDataJson, null);
        }

        if (fitResult != null)
        {
            return (fitResult.StartTime, fitResult.DurationSeconds, fitResult.DistanceMeters,
                fitResult.TrackPoints, null, fitResult.RawFitDataJson);
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
        DateTime startedAtUtc,
        int durationSeconds,
        double distanceMeters,
        double avgPaceS,
        byte[] rawFileData,
        string fileName,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        bool isGpx,
        string? gpxName,
        WorkoutIntakeOverlay? overlay)
    {
        var workout = new Workout
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAtUtc,
            DurationS = durationSeconds,
            DistanceM = distanceMeters,
            AvgPaceS = avgPaceS,
            RawFileData = rawFileData,
            RawFileName = fileName,
            RawFileType = fileType,
            RawGpxData = rawGpxDataJson,
            RawFitData = rawFitDataJson,
            Source = overlay?.Source ?? (isGpx ? "gpx_import" : "fit_import"),
            RunType = "Easy Run",
            CreatedAt = DateTime.UtcNow
        };

        if (!string.IsNullOrWhiteSpace(overlay?.Name))
        {
            workout.Name = overlay.Name;
        }
        else if (!string.IsNullOrWhiteSpace(gpxName))
        {
            workout.Name = gpxName;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.Notes))
        {
            workout.Notes = overlay.Notes;
        }

        if (!string.IsNullOrWhiteSpace(overlay?.RawStravaDataJson))
        {
            workout.RawStravaData = overlay.RawStravaDataJson;
        }

        return workout;
    }

    private void PopulateWorkoutMetrics(
        Workout workout,
        Dictionary<string, object> calculated,
        FitParserService.FitParseResult? fitResult,
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
            if (workout.Source == "gpx_import" || workout.Source == "apple_watch")
            {
                workout.Device = "Apple Watch";
            }
        }
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
