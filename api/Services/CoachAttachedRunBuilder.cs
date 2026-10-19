using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Builds privacy-safe attachedRun packets for mid-week coach exchanges.
/// Pure helpers are static for unit tests; <see cref="BuildAsync"/> loads Tempo data.
/// </summary>
public sealed class CoachAttachedRunBuilder
{
    private readonly TempoDbContext _db;
    private readonly CoachConfig _coachConfig;

    public CoachAttachedRunBuilder(TempoDbContext db, CoachConfig coachConfig)
    {
        _db = db;
        _coachConfig = coachConfig;
    }

    public async Task<(CoachAttachedRun? Run, string? Error)> BuildAsync(
        Guid workoutId,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var workout = await _db.Workouts.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workoutId, cancellationToken);
        if (workout == null)
        {
            return (null, "Workout not found");
        }

        var splits = await _db.WorkoutSplits.AsNoTracking()
            .Where(s => s.WorkoutId == workoutId && s.Kind == WorkoutSplitKinds.Distance)
            .OrderBy(s => s.Idx)
            .ToListAsync(cancellationToken);

        var series = await _db.WorkoutTimeSeries.AsNoTracking()
            .Where(s => s.WorkoutId == workoutId)
            .OrderBy(s => s.ElapsedSeconds)
            .ToListAsync(cancellationToken);

        var targetPoints = Math.Max(2, _coachConfig.ExchangeSeriesPoints);
        var seriesPoints = DownsampleSeries(series, targetPoints);
        var packetSplits = EnrichSplits(splits, series);
        var weather = ParseAmbientWeather(workout.Weather);
        var hrDrift = ComputeHrDriftBpm(series);

        var localDate = LocalDateFromUtc(workout.StartedAt, timezoneOffsetMinutes);
        var (planned, adherenceHint) = await ResolvePlannedAsync(
            localDate,
            workout.DistanceM,
            timezoneOffsetMinutes,
            cancellationToken);

        var run = new CoachAttachedRun(
            WorkoutId: workout.Id,
            LocalDate: localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartedAtUtc: DateTime.SpecifyKind(workout.StartedAt, DateTimeKind.Utc),
            DistanceM: workout.DistanceM,
            DurationS: workout.DurationS,
            MovingTimeS: workout.MovingTimeS,
            AvgPaceSPerKm: workout.AvgPaceS,
            AvgHeartRateBpm: workout.AvgHeartRateBpm,
            MaxHeartRateBpm: workout.MaxHeartRateBpm,
            AvgCadenceSpm: workout.AvgCadenceRpm,
            MaxCadenceSpm: workout.MaxCadenceRpm,
            ElevGainM: workout.ElevGainM,
            ElevLossM: workout.ElevLossM,
            Rpe: workout.Rpe,
            ShoeId: workout.ShoeId,
            Notes: workout.Notes,
            HrDriftBpm: hrDrift,
            Splits: packetSplits,
            Series: seriesPoints,
            Weather: weather,
            Planned: planned,
            AdherenceHint: adherenceHint);

        return (run, null);
    }

    /// <summary>
    /// Evenly samples time series to at most <paramref name="targetPoints"/> points,
    /// always including first and last when the source is non-empty.
    /// </summary>
    public static IReadOnlyList<CoachAttachedRunSeriesPoint> DownsampleSeries(
        IReadOnlyList<WorkoutTimeSeries> source,
        int targetPoints)
    {
        if (source.Count == 0)
        {
            return [];
        }

        var ordered = source.OrderBy(s => s.ElapsedSeconds).ToList();
        var target = Math.Max(2, targetPoints);
        if (ordered.Count <= target)
        {
            return ordered.Select(ToSeriesPoint).ToList();
        }

        var result = new List<CoachAttachedRunSeriesPoint>(target);
        for (var i = 0; i < target; i++)
        {
            var index = i == target - 1
                ? ordered.Count - 1
                : (int)Math.Round(i * (ordered.Count - 1) / (double)(target - 1));
            result.Add(ToSeriesPoint(ordered[index]));
        }

        // Deduplicate adjacent elapsed duplicates from rounding while keeping endpoints.
        var deduped = new List<CoachAttachedRunSeriesPoint>(result.Count);
        foreach (var point in result)
        {
            if (deduped.Count > 0 && deduped[^1].ElapsedS == point.ElapsedS)
            {
                deduped[^1] = point;
                continue;
            }

            deduped.Add(point);
        }

        return deduped;
    }

    /// <summary>
    /// Pace/HR from split rows; cadence/elev averaged from series within elapsed bounds when available.
    /// </summary>
    public static IReadOnlyList<CoachAttachedRunSplit> EnrichSplits(
        IReadOnlyList<WorkoutSplit> splits,
        IReadOnlyList<WorkoutTimeSeries> series)
    {
        if (splits.Count == 0)
        {
            return [];
        }

        var orderedSeries = series.OrderBy(s => s.ElapsedSeconds).ToList();
        var lastIdx = splits.Max(s => s.Idx);

        return splits
            .OrderBy(s => s.Idx)
            .Select(split =>
            {
                var inclusiveEnd = split.Idx == lastIdx;
                var (avgCadence, avgElev) = AverageCadenceAndElev(
                    orderedSeries,
                    split.StartElapsedS,
                    split.EndElapsedS,
                    inclusiveEnd);

                return new CoachAttachedRunSplit(
                    Idx: split.Idx,
                    DistanceM: split.DistanceM,
                    DurationS: split.DurationS,
                    PaceSPerKm: split.PaceS,
                    AvgHeartRateBpm: split.AvgHeartRateBpm,
                    AvgCadenceSpm: avgCadence,
                    AvgElevM: avgElev,
                    StartElapsedS: split.StartElapsedS,
                    EndElapsedS: split.EndElapsedS);
            })
            .ToList();
    }

    /// <summary>
    /// Second-half avg HR minus first-half avg HR when both halves have samples; else null.
    /// </summary>
    public static double? ComputeHrDriftBpm(IReadOnlyList<WorkoutTimeSeries> series)
    {
        var hr = series
            .Where(s => s.HeartRateBpm.HasValue && s.HeartRateBpm.Value > 0)
            .OrderBy(s => s.ElapsedSeconds)
            .Select(s => (double)s.HeartRateBpm!.Value)
            .ToList();

        if (hr.Count < 4)
        {
            return null;
        }

        var mid = hr.Count / 2;
        var first = hr.Take(mid).Average();
        var second = hr.Skip(mid).Average();
        return Math.Round(second - first, 1);
    }

    /// <summary>
    /// Magnus formula dew point (°C) from ambient temperature (°C) and relative humidity (0–100).
    /// </summary>
    public static double? ComputeDewPointC(double? temperatureC, double? humidityPercent)
    {
        if (temperatureC is null || humidityPercent is null)
        {
            return null;
        }

        var rh = humidityPercent.Value;
        if (rh <= 0 || rh > 100)
        {
            return null;
        }

        const double a = 17.27;
        const double b = 237.7;
        var t = temperatureC.Value;
        var alpha = (a * t / (b + t)) + Math.Log(rh / 100.0);
        var dew = b * alpha / (a - alpha);
        return Math.Round(dew, 1);
    }

    public static CoachAttachedRunWeather? ParseAmbientWeather(string? weatherJson)
    {
        if (string.IsNullOrWhiteSpace(weatherJson))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(weatherJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var temperature = TryGetDouble(root, "temperature", "temperature_2m");
            var humidity = TryGetDouble(root, "humidity", "relative_humidity_2m", "relativeHumidity");
            var feelsLike = TryGetDouble(root, "temperatureFeelsLike", "apparentTemperature", "feelsLike");
            var windSpeed = TryGetDouble(root, "windSpeed", "wind_speed_10m");
            var windDirection = TryGetDouble(root, "windDirection", "wind_direction_10m");
            var precipitation = TryGetDouble(root, "precipitation");
            var pressure = TryGetDouble(root, "pressure", "surface_pressure");
            var weatherCode = TryGetInt(root, "weatherCode", "weather_code");
            var condition = TryGetString(root, "condition");
            var source = TryGetString(root, "source");
            var dewPoint = ComputeDewPointC(temperature, humidity);

            if (temperature is null
                && humidity is null
                && feelsLike is null
                && windSpeed is null
                && precipitation is null
                && pressure is null
                && weatherCode is null
                && condition is null)
            {
                return null;
            }

            return new CoachAttachedRunWeather(
                TemperatureC: temperature,
                HumidityPercent: humidity,
                DewPointC: dewPoint,
                FeelsLikeC: feelsLike,
                WindSpeed: windSpeed,
                WindDirection: windDirection,
                Precipitation: precipitation,
                Pressure: pressure,
                WeatherCode: weatherCode,
                Condition: condition,
                Source: source);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<(CoachAttachedRunPlanned? Planned, CoachAttachedRunAdherenceHint? Hint)> ResolvePlannedAsync(
        DateOnly localDate,
        double actualDistanceM,
        int timezoneOffsetMinutes,
        CancellationToken cancellationToken)
    {
        var weekStartMin = localDate.AddDays(-6);
        var weeks = await _db.CoachWeeks.AsNoTracking()
            .Where(w =>
                (w.Status == CoachWeekStatuses.Committed || w.Status == CoachWeekStatuses.Archived)
                && w.WeekStartDate >= weekStartMin
                && w.WeekStartDate <= localDate)
            .OrderBy(w => w.WeekStartDate)
            .ThenBy(w => w.UpdatedAt)
            .ToListAsync(cancellationToken);

        CoachWeeklyCommitDay? matchedDay = null;
        foreach (var week in weeks)
        {
            var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
                week.CommitJson,
                week.WeekStartDate,
                out var commit);
            if (schemaError != null || commit == null)
            {
                continue;
            }

            foreach (var day in commit.Days)
            {
                if (!DateOnly.TryParse(day.Date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date == localDate)
                {
                    matchedDay = day;
                }
            }
        }

        if (matchedDay == null)
        {
            return (null, null);
        }

        CoachAttachedRunPlanned? planned = null;
        if (matchedDay.Run != null)
        {
            planned = new CoachAttachedRunPlanned(
                DayType: matchedDay.DayType,
                Type: matchedDay.Run.Type,
                DistanceM: matchedDay.Run.DistanceM,
                Structure: matchedDay.Run.Structure,
                PaceOrHr: matchedDay.Run.PaceOrHr,
                ShoeId: matchedDay.Run.ShoeId,
                Notes: matchedDay.Run.Notes);
        }
        else
        {
            planned = new CoachAttachedRunPlanned(
                DayType: matchedDay.DayType,
                Type: null,
                DistanceM: null,
                Structure: null,
                PaceOrHr: null,
                ShoeId: null,
                Notes: null);
        }

        var hints = CoachAdherenceHints.Build(
            [new CoachAdherenceHints.PlannedDay(localDate, matchedDay.DayType, matchedDay.Run?.DistanceM)],
            [new CoachAdherenceHints.ActualRun(localDate, actualDistanceM)]);
        var hint = hints.FirstOrDefault(h => h.Date == localDate);
        CoachAttachedRunAdherenceHint? adherence = hint == null
            ? null
            : new CoachAttachedRunAdherenceHint(hint.Code, hint.Message);

        // timezoneOffsetMinutes reserved for future multi-workout-day matching; localDate already applied.
        _ = timezoneOffsetMinutes;

        return (planned, adherence);
    }

    private static CoachAttachedRunSeriesPoint ToSeriesPoint(WorkoutTimeSeries s)
    {
        double? paceSPerKm = null;
        if (s.SpeedMps is > 0)
        {
            paceSPerKm = Math.Round(1000.0 / s.SpeedMps.Value, 1);
        }

        return new CoachAttachedRunSeriesPoint(
            ElapsedS: s.ElapsedSeconds,
            HeartRateBpm: s.HeartRateBpm,
            PaceSPerKm: paceSPerKm,
            CadenceSpm: s.CadenceRpm,
            ElevationM: s.ElevationM,
            GradePercent: s.GradePercent);
    }

    private static (byte? AvgCadence, double? AvgElev) AverageCadenceAndElev(
        IReadOnlyList<WorkoutTimeSeries> orderedSeries,
        int startElapsedS,
        int endElapsedS,
        bool inclusiveEnd)
    {
        if (orderedSeries.Count == 0)
        {
            return (null, null);
        }

        var window = orderedSeries.Where(s =>
            s.ElapsedSeconds >= startElapsedS
            && (inclusiveEnd
                ? s.ElapsedSeconds <= endElapsedS
                : s.ElapsedSeconds < endElapsedS)).ToList();

        if (window.Count == 0)
        {
            return (null, null);
        }

        var cadences = window.Where(s => s.CadenceRpm.HasValue).Select(s => (double)s.CadenceRpm!.Value).ToList();
        var elevs = window.Where(s => s.ElevationM.HasValue).Select(s => s.ElevationM!.Value).ToList();

        byte? avgCadence = cadences.Count == 0
            ? null
            : (byte)Math.Clamp((int)Math.Round(cadences.Average()), 0, 255);
        double? avgElev = elevs.Count == 0 ? null : Math.Round(elevs.Average(), 1);
        return (avgCadence, avgElev);
    }

    public static DateOnly LocalDateFromUtc(DateTime startedAtUtc, int timezoneOffsetMinutes)
    {
        var local = startedAtUtc.AddMinutes(timezoneOffsetMinutes);
        return DateOnly.FromDateTime(local);
    }

    private static double? TryGetDouble(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d))
            {
                return d;
            }

            if (prop.ValueKind == JsonValueKind.String
                && double.TryParse(prop.GetString(), CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (!root.TryGetProperty(name, out var prop))
            {
                continue;
            }

            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var i))
            {
                return i;
            }

            if (prop.ValueKind == JsonValueKind.String
                && int.TryParse(prop.GetString(), CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static string? TryGetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                var s = prop.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    return s;
                }
            }
        }

        return null;
    }
}

public sealed record CoachAttachedRun(
    Guid WorkoutId,
    string LocalDate,
    DateTime StartedAtUtc,
    double DistanceM,
    int DurationS,
    int? MovingTimeS,
    double AvgPaceSPerKm,
    byte? AvgHeartRateBpm,
    byte? MaxHeartRateBpm,
    byte? AvgCadenceSpm,
    byte? MaxCadenceSpm,
    double? ElevGainM,
    double? ElevLossM,
    byte? Rpe,
    Guid? ShoeId,
    string? Notes,
    double? HrDriftBpm,
    IReadOnlyList<CoachAttachedRunSplit> Splits,
    IReadOnlyList<CoachAttachedRunSeriesPoint> Series,
    CoachAttachedRunWeather? Weather,
    CoachAttachedRunPlanned? Planned,
    CoachAttachedRunAdherenceHint? AdherenceHint);

public sealed record CoachAttachedRunSplit(
    int Idx,
    double DistanceM,
    int DurationS,
    double PaceSPerKm,
    byte? AvgHeartRateBpm,
    byte? AvgCadenceSpm,
    double? AvgElevM,
    int StartElapsedS,
    int EndElapsedS);

public sealed record CoachAttachedRunSeriesPoint(
    int ElapsedS,
    byte? HeartRateBpm,
    double? PaceSPerKm,
    byte? CadenceSpm,
    double? ElevationM,
    double? GradePercent);

public sealed record CoachAttachedRunWeather(
    double? TemperatureC,
    double? HumidityPercent,
    double? DewPointC,
    double? FeelsLikeC,
    double? WindSpeed,
    double? WindDirection,
    double? Precipitation,
    double? Pressure,
    int? WeatherCode,
    string? Condition,
    string? Source);

public sealed record CoachAttachedRunPlanned(
    string DayType,
    string? Type,
    double? DistanceM,
    string? Structure,
    string? PaceOrHr,
    Guid? ShoeId,
    string? Notes);

public sealed record CoachAttachedRunAdherenceHint(string Code, string Message);
