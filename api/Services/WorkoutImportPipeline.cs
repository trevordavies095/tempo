using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

/// <summary>
/// Orchestrates the complete workout import pipeline for a single file:
/// parse → deduplicate → build entities → fetch weather.
/// Callers receive an <see cref="ImportResult"/> and own the database write for the <see cref="Created"/> path.
/// The <see cref="Updated"/> and <see cref="Skipped"/> paths are fully handled (and saved) by the pipeline.
/// </summary>
public class WorkoutImportPipeline
{
    private readonly TempoDbContext _db;
    private readonly GpxParserService _gpxParser;
    private readonly FitParserService _fitParser;
    private readonly WeatherService _weatherService;
    private readonly ILogger<WorkoutImportPipeline> _logger;

    public WorkoutImportPipeline(
        TempoDbContext db,
        GpxParserService gpxParser,
        FitParserService fitParser,
        WeatherService weatherService,
        ILogger<WorkoutImportPipeline> logger)
    {
        _db = db;
        _gpxParser = gpxParser;
        _fitParser = fitParser;
        _weatherService = weatherService;
        _logger = logger;
    }

    // ── Input / Output types ────────────────────────────────────────────

    public record ImportInput(byte[] RawData, string FileName, ImportOptions Options);

    /// <param name="SplitDistanceMeters">1000.0 for metric, 1609.344 for imperial.</param>
    /// <param name="ExplicitSource">
    ///   Overrides the inferred source tag. Pass <c>"strava_import"</c> for Strava bulk imports;
    ///   leave null to infer <c>"gpx_import"</c> or <c>"fit_import"</c> from the file extension.
    /// </param>
    /// <param name="Name">Pre-filled name (e.g. from Strava CSV activity name). When null the pipeline
    ///   attempts to extract a name from GPX metadata.</param>
    /// <param name="Notes">Pre-filled notes (e.g. from Strava activity description).</param>
    /// <param name="RawStravaDataJson">Strava activity JSON blob, if any.
    ///   Used for metrics population and weather location lookup.</param>
    public record ImportOptions(
        double SplitDistanceMeters,
        string? ExplicitSource = null,
        string? Name = null,
        string? Notes = null,
        string? RawStravaDataJson = null);

    public abstract record ImportResult;

    /// <summary>New workout built and ready for the caller to persist.</summary>
    public record Created(
        Workout Workout,
        WorkoutRoute Route,
        List<WorkoutSplit> Splits,
        List<WorkoutTimeSeries> TimeSeries) : ImportResult;

    /// <summary>Duplicate found; stale raw data was patched and already saved by the pipeline.</summary>
    public record Updated(Workout ExistingWorkout) : ImportResult;

    /// <summary>Duplicate found with complete data — nothing to do.</summary>
    public record Skipped(Guid ExistingWorkoutId) : ImportResult;

    // ── Main entry point ────────────────────────────────────────────────

    /// <summary>
    /// Runs the full import pipeline for a single workout file.
    /// </summary>
    /// <exception cref="NotSupportedException">Unrecognised file extension.</exception>
    /// <exception cref="InvalidOperationException">File could not be parsed.</exception>
    public async Task<ImportResult> RunAsync(ImportInput input, CancellationToken ct = default)
    {
        var (fileType, isGpx, isFitGz) = DetermineFileType(input.FileName);
        if (fileType == null)
            throw new NotSupportedException(
                "File must be a GPX or FIT file (.gpx, .fit, or .fit.gz)");

        var (parseResult, fitResult) = Parse(input.RawData, fileType, isFitGz);

        var (startTime, durationSeconds, distanceMeters, elevGainM, trackPoints, rawGpxDataJson, rawFitDataJson) =
            ExtractParseData(parseResult, fitResult);

        var startedAtUtc = startTime.Kind switch
        {
            DateTimeKind.Utc   => startTime,
            DateTimeKind.Local => startTime.ToUniversalTime(),
            _                  => DateTime.SpecifyKind(startTime, DateTimeKind.Utc)
        };

        var existing = await WorkoutQueryService.FindDuplicateWorkoutAsync(
            _db, startedAtUtc, distanceMeters, durationSeconds);

        if (existing != null)
        {
            return await HandleDuplicateAsync(
                existing, input.RawData, input.FileName, fileType,
                rawGpxDataJson, rawFitDataJson, trackPoints,
                distanceMeters, durationSeconds, input.Options.SplitDistanceMeters,
                startedAtUtc);
        }

        var avgPaceS = distanceMeters > 0 && durationSeconds > 0
            ? durationSeconds / (distanceMeters / 1000.0)
            : 0;

        var source = input.Options.ExplicitSource ?? (isGpx ? "gpx_import" : "fit_import");

        var workout = new Workout
        {
            Id            = Guid.NewGuid(),
            StartedAt     = startedAtUtc,
            DurationS     = durationSeconds,
            DistanceM     = distanceMeters,
            AvgPaceS      = avgPaceS,
            ElevGainM     = elevGainM,
            RawFileData   = input.RawData,
            RawFileName   = input.FileName,
            RawFileType   = fileType,
            RawGpxData    = rawGpxDataJson,
            RawFitData    = rawFitDataJson,
            RawStravaData = input.Options.RawStravaDataJson,
            Source        = source,
            Name          = input.Options.Name,
            Notes         = input.Options.Notes,
            RunType       = "Easy Run",
            CreatedAt     = DateTime.UtcNow
        };

        if (string.IsNullOrEmpty(workout.Name) && !string.IsNullOrEmpty(rawGpxDataJson))
            TryExtractGpxName(workout, rawGpxDataJson);

        var calculated = ExtractCalculatedMetrics(rawGpxDataJson);
        PopulateMetricsFromCalculated(workout, calculated);
        PopulateMetricsFromFit(workout, fitResult, rawFitDataJson);
        if (!string.IsNullOrEmpty(input.Options.RawStravaDataJson))
            PopulateMetricsFromStrava(workout, ExtractStravaData(input.Options.RawStravaDataJson));

        if (string.IsNullOrWhiteSpace(workout.Device) || workout.Device == "Development")
        {
            if (workout.Source is "gpx_import" or "apple_watch")
                workout.Device = "Apple Watch";
        }

        var route  = BuildRoute(workout.Id, trackPoints);
        var splits = BuildSplits(workout.Id, trackPoints, distanceMeters, durationSeconds,
                                 input.Options.SplitDistanceMeters);
        var timeSeries = BuildTimeSeries(workout, startedAtUtc, parseResult, fitResult, trackPoints);

        if (timeSeries.Count > 0)
            ApplyAggregateMetrics(workout, timeSeries);

        await FetchWeatherAsync(workout, trackPoints, rawFitDataJson,
                                input.Options.RawStravaDataJson, startedAtUtc);

        return new Created(workout, route, splits, timeSeries);
    }

    // ── File parsing ────────────────────────────────────────────────────

    private static (string? FileType, bool IsGpx, bool IsFitGz) DetermineFileType(string fileName)
    {
        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".gpx"))    return ("gpx", true,  false);
        if (lower.EndsWith(".fit.gz")) return ("fit", false, true);
        if (lower.EndsWith(".fit"))    return ("fit", false, false);
        return (null, false, false);
    }

    private (GpxParserService.GpxParseResult? Gpx, FitParserService.FitParseResult? Fit)
        Parse(byte[] rawData, string fileType, bool isFitGz)
    {
        if (fileType == "gpx")
        {
            using var stream = new MemoryStream(rawData);
            return (_gpxParser.ParseGpx(stream), null);
        }

        using var fitStream = new MemoryStream(rawData);
        try
        {
            var fit = isFitGz
                ? _fitParser.ParseGzippedFit(fitStream)
                : _fitParser.ParseFit(fitStream);
            return (null, fit);
        }
        catch (NotSupportedException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }
    }

    private static (DateTime StartTime, int DurationSeconds, double DistanceMeters,
        double? ElevGainM, List<GpxParserService.GpxPoint> TrackPoints,
        string? RawGpxDataJson, string? RawFitDataJson)
        ExtractParseData(GpxParserService.GpxParseResult? gpx, FitParserService.FitParseResult? fit)
    {
        if (gpx != null)
            return (gpx.StartTime, gpx.DurationSeconds, gpx.DistanceMeters,
                    gpx.ElevationGainMeters, gpx.TrackPoints, gpx.RawGpxDataJson, null);

        if (fit != null)
            return (fit.StartTime, fit.DurationSeconds, fit.DistanceMeters,
                    fit.ElevationGainMeters, fit.TrackPoints, null, fit.RawFitDataJson);

        throw new InvalidOperationException("Failed to parse file");
    }

    // ── Duplicate handling ──────────────────────────────────────────────

    private async Task<ImportResult> HandleDuplicateAsync(
        Workout existing,
        byte[] rawData,
        string fileName,
        string fileType,
        string? rawGpxDataJson,
        string? rawFitDataJson,
        List<GpxParserService.GpxPoint> trackPoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters,
        DateTime startedAtUtc)
    {
        bool needsRawFileUpdate = existing.RawFileData == null || existing.RawFileData.Length == 0;
        bool needsRawJsonUpdate = false;

        if (fileType == "fit")
        {
            if (string.IsNullOrEmpty(existing.RawFitData))
            {
                needsRawJsonUpdate = true;
            }
            else
            {
                try
                {
                    using var doc = JsonDocument.Parse(existing.RawFitData);
                    needsRawJsonUpdate = !doc.RootElement.TryGetProperty("trackPoints", out _);
                }
                catch
                {
                    needsRawJsonUpdate = true;
                }
            }
        }
        else if (fileType == "gpx" && string.IsNullOrEmpty(existing.RawGpxData))
        {
            needsRawJsonUpdate = true;
        }

        if (!needsRawFileUpdate && !needsRawJsonUpdate)
        {
            _logger.LogInformation(
                "Skipped duplicate workout (already has complete raw data): {Filename} at {StartTime}",
                LogSanitizer.Sanitize(fileName), startedAtUtc);
            return new Skipped(existing.Id);
        }

        if (needsRawFileUpdate)
        {
            existing.RawFileData = rawData;
            existing.RawFileName = fileName;
            existing.RawFileType = fileType;
        }
        if (fileType == "fit" && rawFitDataJson != null) existing.RawFitData = rawFitDataJson;
        if (fileType == "gpx" && rawGpxDataJson != null) existing.RawGpxData = rawGpxDataJson;

        await _db.SaveChangesAsync();

        try
        {
            var newSplits = BuildSplits(existing.Id, trackPoints, distanceMeters, durationSeconds, splitDistanceMeters);
            var oldSplits = await _db.WorkoutSplits.Where(s => s.WorkoutId == existing.Id).ToListAsync();
            _db.WorkoutSplits.RemoveRange(oldSplits);
            _db.WorkoutSplits.AddRange(newSplits);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to recalculate splits for updated workout {WorkoutId}", existing.Id);
        }

        _logger.LogInformation(
            "Updated duplicate workout {WorkoutId} with raw data: {Filename} at {StartTime}",
            existing.Id, LogSanitizer.Sanitize(fileName), startedAtUtc);

        return new Updated(existing);
    }

    // ── Entity builders ──────────────────────────────────────────────────

    private static WorkoutRoute BuildRoute(Guid workoutId, List<GpxParserService.GpxPoint> trackPoints)
    {
        var coordinates = trackPoints.Select(p => new[] { p.Longitude, p.Latitude }).ToList();
        return new WorkoutRoute
        {
            Id           = Guid.NewGuid(),
            WorkoutId    = workoutId,
            RouteGeoJson = JsonSerializer.Serialize(new { type = "LineString", coordinates })
        };
    }

    private List<WorkoutSplit> BuildSplits(
        Guid workoutId,
        List<GpxParserService.GpxPoint> trackPoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters)
    {
        var splits = _gpxParser.CalculateSplits(
            trackPoints, distanceMeters, durationSeconds, splitDistanceMeters);
        foreach (var split in splits) split.WorkoutId = workoutId;
        return splits;
    }

    private List<WorkoutTimeSeries> BuildTimeSeries(
        Workout workout,
        DateTime startedAtUtc,
        GpxParserService.GpxParseResult? parseResult,
        FitParserService.FitParseResult? fitResult,
        List<GpxParserService.GpxPoint> trackPoints)
    {
        if (parseResult != null)
            return BuildTimeSeriesFromGpx(workout.Id, startedAtUtc, trackPoints);

        if (fitResult?.RecordMesgs?.Count > 0)
        {
            var ts = BuildTimeSeriesFromFit(workout.Id, startedAtUtc, fitResult.RecordMesgs);
            if (ts.Count == 0)
                _logger.LogInformation(
                    "FIT file imported with no sensor data. Workout {WorkoutId} created with available data (GPS, elevation, distance).",
                    workout.Id);
            return ts;
        }

        if (fitResult != null)
            _logger.LogInformation(
                "FIT file imported with no RecordMesg data. Workout {WorkoutId} created with available data (GPS, elevation, distance).",
                workout.Id);

        return new List<WorkoutTimeSeries>();
    }

    private static List<WorkoutTimeSeries> BuildTimeSeriesFromGpx(
        Guid workoutId, DateTime startTime, List<GpxParserService.GpxPoint> trackPoints)
    {
        var result = new List<WorkoutTimeSeries>();
        foreach (var point in trackPoints)
        {
            if (!point.Time.HasValue) continue;
            if (!point.HeartRateBpm.HasValue && !point.CadenceRpm.HasValue &&
                !point.PowerWatts.HasValue   && !point.TemperatureC.HasValue) continue;

            result.Add(new WorkoutTimeSeries
            {
                Id             = Guid.NewGuid(),
                WorkoutId      = workoutId,
                ElapsedSeconds = (int)(point.Time.Value - startTime).TotalSeconds,
                HeartRateBpm   = point.HeartRateBpm,
                CadenceRpm     = point.CadenceRpm,
                PowerWatts     = point.PowerWatts,
                TemperatureC   = point.TemperatureC,
                ElevationM     = point.Elevation
            });
        }
        return result;
    }

    private static List<WorkoutTimeSeries> BuildTimeSeriesFromFit(
        Guid workoutId,
        DateTime startTime,
        ReadOnlyCollection<Dynastream.Fit.RecordMesg> records)
    {
        var result = new List<WorkoutTimeSeries>();
        if (records == null || records.Count == 0) return result;

        foreach (var record in records)
        {
            var timestamp = record.GetTimestamp()?.GetDateTime().ToUniversalTime();
            if (timestamp == null) continue;

            var elapsed = (int)(timestamp.Value - startTime).TotalSeconds;
            if (elapsed < 0) continue;

            // Speed: prefer enhanced over standard
            var enhSpeed = record.GetEnhancedSpeed();
            var stdSpeed = record.GetSpeed();
            double? speed = null;
            if (enhSpeed.HasValue && IsFiniteNonNegative(enhSpeed.Value))
                speed = (double)enhSpeed.Value;
            else if (stdSpeed.HasValue && IsFiniteNonNegative(stdSpeed.Value))
                speed = (double)stdSpeed.Value;

            // Grade: clamp to [-100, 100]
            double? grade = null;
            var rawGrade = record.GetGrade();
            if (rawGrade.HasValue && IsFinite(rawGrade.Value))
                grade = Math.Max(-100.0, Math.Min(100.0, (double)rawGrade.Value));

            // Vertical speed: cap to ±50 m/s
            double? vSpeed = null;
            var rawVSpeed = record.GetVerticalSpeed();
            if (rawVSpeed.HasValue && !double.IsNaN(rawVSpeed.Value) && !double.IsInfinity(rawVSpeed.Value)
                && rawVSpeed.Value is >= -50.0f and <= 50.0f)
                vSpeed = (double)rawVSpeed.Value;

            // Elevation: prefer enhanced
            double? elevation = null;
            var enhAlt = record.GetEnhancedAltitude();
            var stdAlt = record.GetAltitude();
            if (enhAlt.HasValue && IsFinite(enhAlt.Value))
                elevation = (double)enhAlt.Value;
            else if (stdAlt.HasValue && IsFinite(stdAlt.Value))
                elevation = (double)stdAlt.Value;

            // Distance
            double? distance = null;
            var rawDist = record.GetDistance();
            if (rawDist.HasValue && IsFiniteNonNegative(rawDist.Value))
                distance = (double)rawDist.Value;

            var hr    = record.GetHeartRate();
            var cad   = record.GetCadence();
            var power = record.GetPower();
            var temp  = record.GetTemperature();

            if (!hr.HasValue && !cad.HasValue && !power.HasValue && !speed.HasValue &&
                !temp.HasValue && !elevation.HasValue && !grade.HasValue && !vSpeed.HasValue && !distance.HasValue)
                continue;

            result.Add(new WorkoutTimeSeries
            {
                Id               = Guid.NewGuid(),
                WorkoutId        = workoutId,
                ElapsedSeconds   = elapsed,
                HeartRateBpm     = hr,
                CadenceRpm       = cad,
                PowerWatts       = power,
                SpeedMps         = speed,
                TemperatureC     = temp,
                ElevationM       = elevation,
                GradePercent     = grade,
                VerticalSpeedMps = vSpeed,
                DistanceM        = distance
            });
        }
        return result;
    }

    private static bool IsFinite(double v)         => !double.IsNaN(v) && !double.IsInfinity(v);
    private static bool IsFiniteNonNegative(double v) => IsFinite(v) && v >= 0;

    private static void ApplyAggregateMetrics(Workout workout, List<WorkoutTimeSeries> timeSeries)
    {
        var heartRates = timeSeries.Where(ts => ts.HeartRateBpm.HasValue).Select(ts => ts.HeartRateBpm!.Value).ToList();
        if (heartRates.Any())
        {
            workout.MaxHeartRateBpm = heartRates.Max();
            workout.AvgHeartRateBpm = (byte)Math.Round(heartRates.Average(x => (double)x));
            workout.MinHeartRateBpm = heartRates.Min();
        }

        var cadences = timeSeries.Where(ts => ts.CadenceRpm.HasValue).Select(ts => ts.CadenceRpm!.Value).ToList();
        if (cadences.Any())
        {
            workout.MaxCadenceRpm = cadences.Max();
            workout.AvgCadenceRpm = (byte)Math.Round(cadences.Average(x => (double)x));
        }

        var powers = timeSeries.Where(ts => ts.PowerWatts.HasValue).Select(ts => ts.PowerWatts!.Value).ToList();
        if (powers.Any())
        {
            workout.MaxPowerWatts = powers.Max();
            workout.AvgPowerWatts = (ushort)Math.Round(powers.Average(x => (double)x));
        }

        var speeds = timeSeries.Where(ts => ts.SpeedMps.HasValue).Select(ts => ts.SpeedMps!.Value).ToList();
        if (speeds.Any() && !workout.MaxSpeedMps.HasValue)
            workout.MaxSpeedMps = speeds.Max();

        if (!workout.AvgSpeedMps.HasValue && workout.DistanceM > 0 && workout.DurationS > 0)
            workout.AvgSpeedMps = workout.DistanceM / workout.DurationS;
    }

    // ── Metrics extraction ──────────────────────────────────────────────

    private Dictionary<string, object> ExtractCalculatedMetrics(string? rawGpxDataJson)
    {
        var result = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(rawGpxDataJson)) return result;
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(rawGpxDataJson);
            if (el.TryGetProperty("calculated", out var calc))
                foreach (var prop in calc.EnumerateObject())
                    result[prop.Name] = prop.Value;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse RawGpxData JSON for additional metrics"); }
        return result;
    }

    private Dictionary<string, object> ExtractStravaData(string? rawStravaDataJson)
    {
        var result = new Dictionary<string, object>();
        if (string.IsNullOrEmpty(rawStravaDataJson)) return result;
        try
        {
            var el = JsonSerializer.Deserialize<JsonElement>(rawStravaDataJson);
            foreach (var prop in el.EnumerateObject()) result[prop.Name] = prop.Value;
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse RawStravaData JSON"); }
        return result;
    }

    private static bool TryGetNumericJsonElement(Dictionary<string, object> dict, string key, out JsonElement element)
    {
        element = default;
        if (dict.TryGetValue(key, out var v) && v is JsonElement e && e.ValueKind == JsonValueKind.Number)
        {
            element = e;
            return true;
        }
        return false;
    }

    private static void PopulateMetricsFromCalculated(Workout workout, Dictionary<string, object> calculated)
    {
        if (TryGetNumericJsonElement(calculated, "elevLossM",   out var el)) workout.ElevLossM   = el.GetDouble();
        if (TryGetNumericJsonElement(calculated, "minElevM",    out var mi)) workout.MinElevM    = mi.GetDouble();
        if (TryGetNumericJsonElement(calculated, "maxElevM",    out var ma)) workout.MaxElevM    = ma.GetDouble();
        if (TryGetNumericJsonElement(calculated, "maxSpeedMps", out var ms)) workout.MaxSpeedMps = ms.GetDouble();
        if (TryGetNumericJsonElement(calculated, "avgSpeedMps", out var av)) workout.AvgSpeedMps = av.GetDouble();
    }

    private void PopulateMetricsFromFit(
        Workout workout,
        FitParserService.FitParseResult? fitResult,
        string? rawFitDataJson)
    {
        if (fitResult == null || string.IsNullOrEmpty(rawFitDataJson)) return;
        try
        {
            var rawFit = JsonSerializer.Deserialize<JsonElement>(rawFitDataJson);
            if (rawFit.TryGetProperty("session", out var session))
            {
                bool TryNum(string name, out JsonElement el) =>
                    session.TryGetProperty(name, out el) && el.ValueKind == JsonValueKind.Number;

                if (TryNum("totalMovingTime", out var mt))  workout.MovingTimeS     = (int)Math.Round(mt.GetDouble());
                if (TryNum("maxHeartRate",    out var mhr)) workout.MaxHeartRateBpm = (byte)mhr.GetInt32();
                if (TryNum("avgHeartRate",    out var ahr)) workout.AvgHeartRateBpm = (byte)ahr.GetInt32();
                if (TryNum("minHeartRate",    out var nhr)) workout.MinHeartRateBpm = (byte)nhr.GetInt32();
                if (TryNum("maxCadence",      out var mc))  workout.MaxCadenceRpm   = (byte)mc.GetInt32();
                if (TryNum("avgCadence",      out var ac))  workout.AvgCadenceRpm   = (byte)ac.GetInt32();
                if (TryNum("maxPower",        out var mp))  workout.MaxPowerWatts   = (ushort)mp.GetInt32();
                if (TryNum("avgPower",        out var ap))  workout.AvgPowerWatts   = (ushort)ap.GetInt32();
                if (TryNum("totalCalories",   out var cal)) workout.Calories        = (ushort)cal.GetInt32();
            }

            if (rawFit.TryGetProperty("device", out var deviceEl) && deviceEl.ValueKind == JsonValueKind.Object)
            {
                _logger.LogDebug("Found device element in FIT file: {DeviceData}", deviceEl.GetRawText());
                workout.Device = DeviceExtractionService.ExtractDeviceName(deviceEl, _logger);
                if (string.IsNullOrWhiteSpace(workout.Device))
                    _logger.LogDebug("Device extraction returned null. Device element: {DeviceData}", deviceEl.GetRawText());
            }
            else
            {
                _logger.LogDebug("No device element found in RawFitData");
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to extract metrics from RawFitData JSON"); }
    }

    private static void PopulateMetricsFromStrava(Workout workout, Dictionary<string, object> strava)
    {
        if (TryGetNumericJsonElement(strava, "movingTime",    out var mt))  workout.MovingTimeS     = (int)Math.Round(mt.GetDouble());
        if (TryGetNumericJsonElement(strava, "maxHeartRate",  out var mhr)) workout.MaxHeartRateBpm = (byte)mhr.GetInt32();
        if (TryGetNumericJsonElement(strava, "avgHeartRate",  out var ahr)) workout.AvgHeartRateBpm = (byte)ahr.GetInt32();
        if (TryGetNumericJsonElement(strava, "maxCadence",    out var mc))  workout.MaxCadenceRpm   = (byte)mc.GetInt32();
        if (TryGetNumericJsonElement(strava, "avgCadence",    out var ac))  workout.AvgCadenceRpm   = (byte)ac.GetInt32();
        if (TryGetNumericJsonElement(strava, "maxWatts",      out var mpw)) workout.MaxPowerWatts   = (ushort)mpw.GetInt32();
        if (TryGetNumericJsonElement(strava, "avgWatts",      out var apw)) workout.AvgPowerWatts   = (ushort)apw.GetInt32();
        if (TryGetNumericJsonElement(strava, "calories",      out var cal)) workout.Calories        = (ushort)cal.GetInt32();
        if (TryGetNumericJsonElement(strava, "elevationLoss", out var ell)) workout.ElevLossM       = ell.GetDouble();
        if (TryGetNumericJsonElement(strava, "elevationLow",  out var elo)) workout.MinElevM        = elo.GetDouble();
        if (TryGetNumericJsonElement(strava, "elevationHigh", out var ehi)) workout.MaxElevM        = ehi.GetDouble();
        if (TryGetNumericJsonElement(strava, "maxSpeed",      out var msp)) workout.MaxSpeedMps     = msp.GetDouble();
        if (TryGetNumericJsonElement(strava, "avgSpeed",      out var asp)) workout.AvgSpeedMps     = asp.GetDouble();
    }

    private void TryExtractGpxName(Workout workout, string rawGpxDataJson)
    {
        try
        {
            var rawData = JsonSerializer.Deserialize<JsonElement>(rawGpxDataJson);
            if (rawData.TryGetProperty("metadata", out var meta) &&
                meta.TryGetProperty("name", out var nameEl) &&
                nameEl.ValueKind == JsonValueKind.String)
            {
                var name = nameEl.GetString();
                if (!string.IsNullOrWhiteSpace(name)) workout.Name = name;
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to extract name from GPX metadata"); }
    }

    // ── Weather ──────────────────────────────────────────────────────────

    private async Task FetchWeatherAsync(
        Workout workout,
        List<GpxParserService.GpxPoint> trackPoints,
        string? rawFitDataJson,
        string? rawStravaDataJson,
        DateTime startedAtUtc)
    {
        if (trackPoints.Count == 0) return;
        var first = trackPoints[0];
        try
        {
            var weatherJson = await _weatherService.GetWeatherForWorkoutAsync(
                rawStravaDataJson: rawStravaDataJson,
                rawFitDataJson:    rawFitDataJson,
                latitude:          first.Latitude,
                longitude:         first.Longitude,
                startTime:         startedAtUtc);

            if (!string.IsNullOrEmpty(weatherJson))
                workout.Weather = weatherJson;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch weather data for workout {WorkoutId}", workout.Id);
        }
    }
}
