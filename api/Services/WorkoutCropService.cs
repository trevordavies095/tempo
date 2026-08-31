using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Crops/trims workouts by removing time from the beginning or end.
/// </summary>
public class WorkoutCropService
{
    private readonly TempoDbContext _db;
    private readonly TrackPointRehydration _rehydration;
    private readonly TrackGeometry _trackGeometry;
    private readonly ILogger<WorkoutCropService> _logger;
    private const int MinimumRemainingDurationSeconds = 10;

    public WorkoutCropService(
        TempoDbContext db,
        TrackPointRehydration rehydration,
        TrackGeometry trackGeometry,
        ILogger<WorkoutCropService> logger)
    {
        _db = db;
        _rehydration = rehydration;
        _trackGeometry = trackGeometry;
        _logger = logger;
    }

    /// <summary>
    /// Crops a workout by removing time from the beginning and/or end.
    /// </summary>
    public async Task<Workout> CropWorkoutAsync(
        Workout workout,
        int startTrimSeconds,
        int endTrimSeconds)
    {
        if (workout.Route == null)
        {
            throw new InvalidOperationException("Workout has no route data. Cannot crop workout without route.");
        }

        var originalDurationS = workout.DurationS;
        var originalStartedAt = workout.StartedAt;
        var newDurationS = originalDurationS - startTrimSeconds - endTrimSeconds;

        if (newDurationS < MinimumRemainingDurationSeconds)
        {
            throw new InvalidOperationException(
                $"Cropping would result in a workout shorter than {MinimumRemainingDurationSeconds} seconds. " +
                $"Original duration: {originalDurationS}s, Trim: {startTrimSeconds}s start + {endTrimSeconds}s end = {startTrimSeconds + endTrimSeconds}s");
        }

        _logger.LogInformation(
            "Cropping workout {WorkoutId}: Original duration {OriginalDuration}s, " +
            "Trimming {StartTrim}s from start and {EndTrim}s from end, " +
            "New duration: {NewDuration}s",
            workout.Id, originalDurationS, startTrimSeconds, endTrimSeconds, newDurationS);

        var trackPoints = _rehydration.Rehydrate(workout);
        if (trackPoints == null || trackPoints.Count < 2)
        {
            throw new InvalidOperationException("Workout has insufficient track point data. Cannot crop.");
        }

        var timeSeries = await _db.WorkoutTimeSeries
            .Where(ts => ts.WorkoutId == workout.Id)
            .OrderBy(ts => ts.ElapsedSeconds)
            .ToListAsync();

        StampSensorsFromTimeSeries(trackPoints, timeSeries, originalStartedAt);

        var endBound = originalDurationS - endTrimSeconds;
        var sliced = SliceByElapsed(trackPoints, originalStartedAt, startTrimSeconds, endBound);
        if (sliced.Count < 2)
        {
            throw new InvalidOperationException("Cropping would leave insufficient track points.");
        }

        var newStartedAt = originalStartedAt.AddSeconds(startTrimSeconds);
        var splitDistanceMeters = await GetSplitDistanceMetersAsync();
        var geometry = _trackGeometry.Derive(sliced, newStartedAt, splitDistanceMeters, workout.Id);

        workout.DurationS = newDurationS;
        workout.StartedAt = newStartedAt;
        workout.DistanceM = geometry.DistanceM;
        workout.ElevGainM = geometry.ElevGainM;
        workout.AvgPaceS = newDurationS > 0 && workout.DistanceM > 0
            ? newDurationS / (workout.DistanceM / 1000.0)
            : 0;

        workout.Route.RouteGeoJson = geometry.Route.RouteGeoJson;
        workout.Route.PreviewGeoJson = geometry.Route.PreviewGeoJson;

        ApplySeriesAggregates(workout, geometry.TimeSeries);

        var oldSplits = await _db.WorkoutSplits.Where(s => s.WorkoutId == workout.Id).ToListAsync();
        if (oldSplits.Count > 0)
        {
            _db.WorkoutSplits.RemoveRange(oldSplits);
        }
        _db.WorkoutSplits.AddRange(geometry.Splits);

        if (timeSeries.Count > 0)
        {
            _db.WorkoutTimeSeries.RemoveRange(timeSeries);
        }
        if (geometry.TimeSeries.Count > 0)
        {
            _db.WorkoutTimeSeries.AddRange(geometry.TimeSeries);
        }

        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Successfully cropped workout {WorkoutId}: Duration {NewDuration}s, Distance {Distance}m",
            workout.Id, newDurationS, workout.DistanceM);

        return workout;
    }

    private async Task<double> GetSplitDistanceMetersAsync()
    {
        var settings = await _db.UserSettings.FirstOrDefaultAsync();
        var unitPreference = settings?.UnitPreference;
        return unitPreference != null && unitPreference.Equals("imperial", StringComparison.OrdinalIgnoreCase)
            ? 1609.344
            : 1000.0;
    }

    private static void StampSensorsFromTimeSeries(
        List<TrackPoint> trackPoints,
        List<WorkoutTimeSeries> timeSeries,
        DateTime startedAt)
    {
        if (timeSeries.Count == 0)
        {
            return;
        }

        foreach (var point in trackPoints)
        {
            if (!point.Time.HasValue)
            {
                continue;
            }

            var elapsed = (int)Math.Round((point.Time.Value - startedAt).TotalSeconds);
            var nearest = timeSeries
                .OrderBy(ts => Math.Abs(ts.ElapsedSeconds - elapsed))
                .First();

            point.HeartRateBpm ??= nearest.HeartRateBpm;
            point.CadenceRpm ??= nearest.CadenceRpm;
            point.PowerWatts ??= nearest.PowerWatts;
            point.TemperatureC ??= nearest.TemperatureC;
            point.SpeedMps ??= nearest.SpeedMps;
            point.Elevation ??= nearest.ElevationM;
            point.DistanceM ??= nearest.DistanceM;
        }
    }

    private static List<TrackPoint> SliceByElapsed(
        List<TrackPoint> trackPoints,
        DateTime startedAt,
        int startTrimSeconds,
        int endBoundSeconds)
    {
        var timed = trackPoints.Where(p => p.Time.HasValue).OrderBy(p => p.Time).ToList();
        if (timed.Count < 2)
        {
            return timed;
        }

        var sliced = new List<TrackPoint>();
        var startBound = InterpolateAtElapsed(timed, startedAt, startTrimSeconds);
        if (startBound != null)
        {
            sliced.Add(startBound);
        }

        foreach (var point in timed)
        {
            var elapsed = (point.Time!.Value - startedAt).TotalSeconds;
            if (elapsed > startTrimSeconds && elapsed < endBoundSeconds)
            {
                sliced.Add(point);
            }
        }

        var endBound = InterpolateAtElapsed(timed, startedAt, endBoundSeconds);
        if (endBound != null
            && (sliced.Count == 0 || sliced[^1].Time != endBound.Time))
        {
            sliced.Add(endBound);
        }

        return sliced;
    }

    private static TrackPoint? InterpolateAtElapsed(
        List<TrackPoint> timed,
        DateTime startedAt,
        int targetElapsed)
    {
        var targetTime = startedAt.AddSeconds(targetElapsed);
        if (targetElapsed <= 0)
        {
            return ClonePoint(timed[0], startedAt);
        }

        TrackPoint? before = null;
        TrackPoint? after = null;
        foreach (var point in timed)
        {
            var elapsed = (point.Time!.Value - startedAt).TotalSeconds;
            if (elapsed <= targetElapsed)
            {
                before = point;
            }
            if (elapsed >= targetElapsed)
            {
                after = point;
                break;
            }
        }

        if (before == null)
        {
            return ClonePoint(timed[0], targetTime);
        }

        if (after == null)
        {
            return ClonePoint(timed[^1], targetTime);
        }

        if (before.Time == after.Time)
        {
            return ClonePoint(before, targetTime);
        }

        var span = (after.Time!.Value - before.Time!.Value).TotalSeconds;
        var t = span > 0 ? (targetTime - before.Time.Value).TotalSeconds / span : 0;
        return Lerp(before, after, t, targetTime);
    }

    private static TrackPoint ClonePoint(TrackPoint source, DateTime time)
    {
        return new TrackPoint
        {
            Latitude = source.Latitude,
            Longitude = source.Longitude,
            Elevation = source.Elevation,
            Time = time,
            HeartRateBpm = source.HeartRateBpm,
            CadenceRpm = source.CadenceRpm,
            PowerWatts = source.PowerWatts,
            TemperatureC = source.TemperatureC,
            SpeedMps = source.SpeedMps,
            DistanceM = source.DistanceM,
            GradePercent = source.GradePercent,
            VerticalSpeedMps = source.VerticalSpeedMps
        };
    }

    private static TrackPoint Lerp(TrackPoint a, TrackPoint b, double t, DateTime time)
    {
        t = Math.Clamp(t, 0, 1);
        return new TrackPoint
        {
            Latitude = LerpNullable(a.Latitude, b.Latitude, t),
            Longitude = LerpNullable(a.Longitude, b.Longitude, t),
            Elevation = LerpNullable(a.Elevation, b.Elevation, t),
            Time = time,
            HeartRateBpm = LerpByte(a.HeartRateBpm, b.HeartRateBpm, t),
            CadenceRpm = LerpByte(a.CadenceRpm, b.CadenceRpm, t),
            PowerWatts = LerpUshort(a.PowerWatts, b.PowerWatts, t),
            TemperatureC = a.TemperatureC ?? b.TemperatureC,
            SpeedMps = LerpNullable(a.SpeedMps, b.SpeedMps, t),
            DistanceM = LerpNullable(a.DistanceM, b.DistanceM, t),
            GradePercent = LerpNullable(a.GradePercent, b.GradePercent, t),
            VerticalSpeedMps = LerpNullable(a.VerticalSpeedMps, b.VerticalSpeedMps, t)
        };
    }

    private static double? LerpNullable(double? a, double? b, double t)
    {
        if (a.HasValue && b.HasValue)
        {
            return a.Value + (b.Value - a.Value) * t;
        }

        return a ?? b;
    }

    private static byte? LerpByte(byte? a, byte? b, double t)
    {
        if (a.HasValue && b.HasValue)
        {
            return (byte)Math.Round(a.Value + (b.Value - a.Value) * t);
        }

        return a ?? b;
    }

    private static ushort? LerpUshort(ushort? a, ushort? b, double t)
    {
        if (a.HasValue && b.HasValue)
        {
            return (ushort)Math.Round(a.Value + (b.Value - a.Value) * t);
        }

        return a ?? b;
    }

    private static void ApplySeriesAggregates(Workout workout, IReadOnlyList<WorkoutTimeSeries> timeSeries)
    {
        if (timeSeries.Count == 0)
        {
            return;
        }

        var heartRates = timeSeries.Where(ts => ts.HeartRateBpm.HasValue).Select(ts => ts.HeartRateBpm!.Value).ToList();
        if (heartRates.Count > 0)
        {
            workout.MaxHeartRateBpm = heartRates.Max();
            workout.MinHeartRateBpm = heartRates.Min();
            workout.AvgHeartRateBpm = (byte)Math.Round(heartRates.Average(x => (double)x));
        }

        var cadences = timeSeries.Where(ts => ts.CadenceRpm.HasValue).Select(ts => ts.CadenceRpm!.Value).ToList();
        if (cadences.Count > 0)
        {
            workout.MaxCadenceRpm = cadences.Max();
            workout.AvgCadenceRpm = (byte)Math.Round(cadences.Average(x => (double)x));
        }

        var powers = timeSeries.Where(ts => ts.PowerWatts.HasValue).Select(ts => ts.PowerWatts!.Value).ToList();
        if (powers.Count > 0)
        {
            workout.MaxPowerWatts = powers.Max();
            workout.AvgPowerWatts = (ushort)Math.Round(powers.Average(x => (double)x));
        }

        var speeds = timeSeries.Where(ts => ts.SpeedMps.HasValue).Select(ts => ts.SpeedMps!.Value).ToList();
        if (speeds.Count > 0)
        {
            workout.MaxSpeedMps = speeds.Max();
            workout.AvgSpeedMps = speeds.Average();
        }

        var elevations = timeSeries.Where(ts => ts.ElevationM.HasValue).Select(ts => ts.ElevationM!.Value).ToList();
        if (elevations.Count > 0)
        {
            workout.MinElevM = elevations.Min();
            workout.MaxElevM = elevations.Max();
        }
    }
}
