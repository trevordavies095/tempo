using System.Text.Json;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

public sealed class TrackGeometryResult
{
    public double? ElevGainM { get; init; }
    public required WorkoutRoute Route { get; init; }
    public required IReadOnlyList<WorkoutSplit> Splits { get; init; }
    public required IReadOnlyList<WorkoutTimeSeries> TimeSeries { get; init; }
}

/// <summary>
/// In-process track geometry: TrackPoints in; elevation, route, splits, and time series out.
/// </summary>
public class TrackGeometry
{
    private readonly ElevationCalculationConfig _elevationConfig;

    public TrackGeometry(ElevationCalculationConfig elevationConfig)
    {
        _elevationConfig = elevationConfig;
    }

    public TrackGeometryResult Derive(
        IReadOnlyList<TrackPoint> points,
        DateTime startedAt,
        double splitDistanceMeters,
        Guid workoutId,
        double distanceMeters,
        int durationSeconds,
        IReadOnlyList<TrackPoint>? seriesPoints = null)
    {
        var positioned = points.Where(p => p.HasPosition).ToList();

        return new TrackGeometryResult
        {
            ElevGainM = CalculateElevationGain(positioned),
            Route = CreateRoute(workoutId, positioned),
            Splits = CalculateSplits(positioned, distanceMeters, durationSeconds, splitDistanceMeters, workoutId),
            TimeSeries = seriesPoints == null
                ? CreateGpxTimeSeries(workoutId, startedAt, points)
                : CreateFitTimeSeries(workoutId, startedAt, seriesPoints)
        };
    }

    private double? CalculateElevationGain(List<TrackPoint> trackPoints)
    {
        if (!trackPoints.Any(p => p.Elevation.HasValue))
        {
            return null;
        }

        double totalChange = 0.0;
        double accumulatedChange = 0.0;
        double accumulatedOpposite = 0.0;
        double accumulatedDistance = 0.0;
        double? lastElevation = null;
        TrackPoint? lastPoint = null;

        foreach (var point in trackPoints)
        {
            if (!point.Elevation.HasValue)
            {
                if (lastPoint != null)
                {
                    accumulatedDistance += Haversine(lastPoint, point);
                }
                lastPoint = point;
                continue;
            }

            double currentElevation = point.Elevation.Value;

            if (lastElevation.HasValue && lastPoint != null)
            {
                accumulatedDistance += Haversine(lastPoint, point);
                double elevationDiff = currentElevation - lastElevation.Value;

                if (elevationDiff > 0)
                {
                    if (accumulatedOpposite > 0)
                    {
                        accumulatedOpposite = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedChange += elevationDiff;
                }
                else if (elevationDiff < 0)
                {
                    if (accumulatedChange > 0)
                    {
                        if (accumulatedChange >= _elevationConfig.NoiseThresholdMeters &&
                            accumulatedDistance >= _elevationConfig.MinDistanceMeters)
                        {
                            totalChange += accumulatedChange;
                        }
                        accumulatedChange = 0.0;
                        accumulatedDistance = 0.0;
                    }
                    accumulatedOpposite += Math.Abs(elevationDiff);
                }
            }

            lastElevation = currentElevation;
            lastPoint = point;
        }

        if (accumulatedChange > 0 &&
            accumulatedChange >= _elevationConfig.NoiseThresholdMeters &&
            accumulatedDistance >= _elevationConfig.MinDistanceMeters)
        {
            totalChange += accumulatedChange;
        }

        return totalChange > 0 ? totalChange : null;
    }

    private static WorkoutRoute CreateRoute(Guid workoutId, List<TrackPoint> trackPoints)
    {
        var coordinates = trackPoints
            .Select(p => new[] { p.Longitude!.Value, p.Latitude!.Value })
            .ToList();
        var routeGeoJson = JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates
        });

        return new WorkoutRoute
        {
            Id = Guid.NewGuid(),
            WorkoutId = workoutId,
            RouteGeoJson = routeGeoJson
        };
    }

    private static List<WorkoutSplit> CalculateSplits(
        List<TrackPoint> trackPoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters,
        Guid workoutId)
    {
        var splits = new List<WorkoutSplit>();
        var accumulatedDistance = 0.0;
        var splitStartDistance = 0.0;
        var splitStartIndex = 0;
        var lastSplitStartIndex = 0;
        var splitIndex = 0;

        for (int i = 1; i < trackPoints.Count; i++)
        {
            var segmentDistance = Haversine(trackPoints[i - 1], trackPoints[i]);
            accumulatedDistance += segmentDistance;

            if (accumulatedDistance - splitStartDistance >= splitDistanceMeters)
            {
                var splitDistance = accumulatedDistance - splitStartDistance;
                var splitDuration = SplitDuration(trackPoints, splitStartIndex, i, splitDistance, distanceMeters, durationSeconds);
                var splitPace = splitDuration > 0 ? splitDuration / (splitDistance / 1000.0) : 0;

                splits.Add(new WorkoutSplit
                {
                    Id = Guid.NewGuid(),
                    WorkoutId = workoutId,
                    Idx = splitIndex++,
                    DistanceM = splitDistance,
                    DurationS = splitDuration,
                    PaceS = splitPace
                });

                splitStartDistance = accumulatedDistance;
                lastSplitStartIndex = splitStartIndex;
                splitStartIndex = i;
            }
        }

        var remainingDistance = accumulatedDistance - splitStartDistance;
        if (remainingDistance > 0)
        {
            if (remainingDistance >= splitDistanceMeters * 0.1 && splits.Count > 0)
            {
                var finalSplitDuration = SplitDuration(
                    trackPoints,
                    splitStartIndex,
                    trackPoints.Count - 1,
                    remainingDistance,
                    distanceMeters,
                    durationSeconds);
                var finalSplitPace = finalSplitDuration > 0 ? finalSplitDuration / (remainingDistance / 1000.0) : 0;

                splits.Add(new WorkoutSplit
                {
                    Id = Guid.NewGuid(),
                    WorkoutId = workoutId,
                    Idx = splitIndex,
                    DistanceM = remainingDistance,
                    DurationS = finalSplitDuration,
                    PaceS = finalSplitPace
                });
            }
            else if (splits.Count > 0)
            {
                var lastSplit = splits[^1];
                var totalLastSplitDistance = lastSplit.DistanceM + remainingDistance;

                var mergedDuration = lastSplit.DurationS;
                if (trackPoints.Count > 1 && trackPoints[^1].Time.HasValue)
                {
                    var lastSplitStartTime = trackPoints[lastSplitStartIndex].Time;
                    if (lastSplitStartTime.HasValue && trackPoints[^1].Time.HasValue)
                    {
                        mergedDuration = (int)(trackPoints[^1].Time!.Value - lastSplitStartTime.Value).TotalSeconds;
                    }
                }
                else
                {
                    mergedDuration = (int)((totalLastSplitDistance / distanceMeters) * durationSeconds);
                }

                lastSplit.DistanceM = totalLastSplitDistance;
                lastSplit.DurationS = mergedDuration;
                lastSplit.PaceS = mergedDuration > 0 ? mergedDuration / (totalLastSplitDistance / 1000.0) : lastSplit.PaceS;
            }
        }

        return splits;
    }

    private static int SplitDuration(
        List<TrackPoint> trackPoints,
        int startIndex,
        int endIndex,
        double splitDistance,
        double distanceMeters,
        int durationSeconds)
    {
        if (trackPoints[endIndex].Time.HasValue && trackPoints[startIndex].Time.HasValue)
        {
            return (int)(trackPoints[endIndex].Time!.Value - trackPoints[startIndex].Time!.Value).TotalSeconds;
        }

        return (int)((splitDistance / distanceMeters) * durationSeconds);
    }

    private static List<WorkoutTimeSeries> CreateGpxTimeSeries(
        Guid workoutId,
        DateTime startTime,
        IReadOnlyList<TrackPoint> trackPoints)
    {
        var timeSeries = new List<WorkoutTimeSeries>();

        foreach (var point in trackPoints)
        {
            if (!point.Time.HasValue) continue;

            var elapsedSeconds = (int)(point.Time.Value - startTime).TotalSeconds;

            if (point.HeartRateBpm.HasValue ||
                point.CadenceRpm.HasValue ||
                point.PowerWatts.HasValue ||
                point.TemperatureC.HasValue)
            {
                timeSeries.Add(new WorkoutTimeSeries
                {
                    Id = Guid.NewGuid(),
                    WorkoutId = workoutId,
                    ElapsedSeconds = elapsedSeconds,
                    HeartRateBpm = point.HeartRateBpm,
                    CadenceRpm = point.CadenceRpm,
                    PowerWatts = point.PowerWatts,
                    TemperatureC = point.TemperatureC,
                    ElevationM = point.Elevation
                });
            }
        }

        return timeSeries;
    }

    private static List<WorkoutTimeSeries> CreateFitTimeSeries(
        Guid workoutId,
        DateTime startTime,
        IReadOnlyList<TrackPoint> seriesPoints)
    {
        var timeSeries = new List<WorkoutTimeSeries>();

        foreach (var point in seriesPoints)
        {
            if (!point.Time.HasValue) continue;

            var elapsedSeconds = (int)(point.Time.Value - startTime).TotalSeconds;
            if (elapsedSeconds < 0) continue;

            var hasValidData = point.HeartRateBpm.HasValue ||
                               point.CadenceRpm.HasValue ||
                               point.PowerWatts.HasValue ||
                               point.SpeedMps.HasValue ||
                               point.TemperatureC.HasValue ||
                               point.Elevation.HasValue ||
                               point.GradePercent.HasValue ||
                               point.VerticalSpeedMps.HasValue ||
                               point.DistanceM.HasValue;

            if (!hasValidData) continue;

            timeSeries.Add(new WorkoutTimeSeries
            {
                Id = Guid.NewGuid(),
                WorkoutId = workoutId,
                ElapsedSeconds = elapsedSeconds,
                HeartRateBpm = point.HeartRateBpm,
                CadenceRpm = point.CadenceRpm,
                PowerWatts = point.PowerWatts,
                SpeedMps = point.SpeedMps,
                TemperatureC = point.TemperatureC,
                ElevationM = point.Elevation,
                GradePercent = point.GradePercent,
                VerticalSpeedMps = point.VerticalSpeedMps,
                DistanceM = point.DistanceM
            });
        }

        return timeSeries;
    }

    private static double Haversine(TrackPoint a, TrackPoint b)
    {
        return GeoUtils.HaversineDistance(
            a.Latitude!.Value,
            a.Longitude!.Value,
            b.Latitude!.Value,
            b.Longitude!.Value);
    }
}
