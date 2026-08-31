using System.Text.Json;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

public sealed class TrackGeometryResult
{
    public double DistanceM { get; init; }
    public int DurationS { get; init; }
    public double? ElevGainM { get; init; }
    public required WorkoutRoute Route { get; init; }
    /// <summary>True when the route LineString has at least one coordinate.</summary>
    public bool HasRouteCoordinates { get; init; }
    public required IReadOnlyList<WorkoutSplit> Splits { get; init; }
    public required IReadOnlyList<WorkoutTimeSeries> TimeSeries { get; init; }
}

/// <summary>
/// In-process track geometry: TrackPoints in; elevation, route, splits, and time series out.
/// </summary>
public class TrackGeometry
{
    public const int RoutePreviewMaxPoints = 100;

    /// <summary>
    /// Valid JSON stored when source geometry is empty or unparseable.
    /// Must be legal jsonb — an empty string is rejected by PostgreSQL.
    /// </summary>
    public const string EmptyRoutePreviewSentinel = "[]";

    /// <summary>
    /// True when the list endpoint should fall back to the full route (or omit the preview).
    /// </summary>
    public static bool IsUnusableListPreview(string? previewGeoJson) =>
        string.IsNullOrWhiteSpace(previewGeoJson) || previewGeoJson == EmptyRoutePreviewSentinel;

    private const int PreviewToleranceSearchIterations = 40;
    private const double PreviewToleranceStartMeters = 1.0;
    private const double PreviewToleranceMaxMeters = 20_000_000.0;

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
        var splits = positioned.Count >= 2
            ? CalculateSplitsFromHaversine(positioned, distanceMeters, durationSeconds, splitDistanceMeters, workoutId)
            : CalculateSplitsFromDistanceStream(
                ResolveDistanceStream(points, seriesPoints),
                distanceMeters,
                durationSeconds,
                splitDistanceMeters,
                workoutId);

        return new TrackGeometryResult
        {
            DistanceM = distanceMeters,
            DurationS = durationSeconds,
            ElevGainM = CalculateElevationGain(positioned),
            Route = CreateRoute(workoutId, positioned),
            HasRouteCoordinates = positioned.Count > 0,
            Splits = splits,
            TimeSeries = seriesPoints == null
                ? CreateGpxTimeSeries(workoutId, startedAt, points)
                : CreateFitTimeSeries(workoutId, startedAt, seriesPoints)
        };
    }

    public TrackGeometryResult Derive(
        IReadOnlyList<TrackPoint> points,
        DateTime startedAt,
        double splitDistanceMeters,
        Guid workoutId)
    {
        var positioned = points.Where(p => p.HasPosition).ToList();
        var distanceMeters = DistanceFromPoints(positioned);
        var timed = points.Where(p => p.Time.HasValue).Select(p => p.Time!.Value).ToList();
        var durationSeconds = timed.Count >= 2
            ? (int)Math.Round((timed.Max() - timed.Min()).TotalSeconds)
            : 0;

        return Derive(points, startedAt, splitDistanceMeters, workoutId, distanceMeters, durationSeconds);
    }

    private static List<TrackPoint> ResolveDistanceStream(
        IReadOnlyList<TrackPoint> points,
        IReadOnlyList<TrackPoint>? seriesPoints)
    {
        var fromPoints = points
            .Where(p => p.DistanceM.HasValue && p.Time.HasValue)
            .OrderBy(p => p.DistanceM!.Value)
            .ToList();
        if (fromPoints.Count >= 2)
        {
            return fromPoints;
        }

        if (seriesPoints == null)
        {
            return fromPoints;
        }

        return seriesPoints
            .Where(p => p.DistanceM.HasValue && p.Time.HasValue)
            .OrderBy(p => p.DistanceM!.Value)
            .ToList();
    }

    private static double DistanceFromPoints(List<TrackPoint> positioned)
    {
        double total = 0;
        for (var i = 1; i < positioned.Count; i++)
        {
            total += Haversine(positioned[i - 1], positioned[i]);
        }

        return total;
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
            RouteGeoJson = routeGeoJson,
            PreviewGeoJson = BuildRoutePreviewGeoJson(routeGeoJson)
        };
    }

    /// <summary>
    /// Builds a ≤ 100-point LineString preview. Routes with ≤ 100 points are returned verbatim.
    /// Empty or unparseable GeoJSON returns <see cref="EmptyRoutePreviewSentinel"/>.
    /// </summary>
    public static string BuildRoutePreviewGeoJson(string? routeGeoJson)
    {
        if (!TryParseLineStringCoordinates(routeGeoJson, out var coordinates))
        {
            return EmptyRoutePreviewSentinel;
        }

        if (coordinates.Count <= RoutePreviewMaxPoints)
        {
            return routeGeoJson!;
        }

        var simplified = SimplifyToMaxPoints(coordinates);
        return JsonSerializer.Serialize(new
        {
            type = "LineString",
            coordinates = simplified
        });
    }

    private static bool TryParseLineStringCoordinates(string? routeGeoJson, out List<double[]> coordinates)
    {
        coordinates = new List<double[]>();
        if (string.IsNullOrWhiteSpace(routeGeoJson))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(routeGeoJson);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("coordinates", out var coordsEl)
                || coordsEl.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var pt in coordsEl.EnumerateArray())
            {
                if (pt.ValueKind != JsonValueKind.Array || pt.GetArrayLength() < 2)
                {
                    return false;
                }

                coordinates.Add(new[] { pt[0].GetDouble(), pt[1].GetDouble() });
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static List<double[]> SimplifyToMaxPoints(IReadOnlyList<double[]> coordinates)
    {
        var lo = 0.0;
        var hi = PreviewToleranceStartMeters;
        var simplified = DouglasPeucker(coordinates, hi);
        while (simplified.Count > RoutePreviewMaxPoints && hi < PreviewToleranceMaxMeters)
        {
            hi *= 2;
            simplified = DouglasPeucker(coordinates, hi);
        }

        var best = simplified;
        for (var i = 0; i < PreviewToleranceSearchIterations; i++)
        {
            var mid = (lo + hi) / 2.0;
            simplified = DouglasPeucker(coordinates, mid);
            if (simplified.Count <= RoutePreviewMaxPoints)
            {
                hi = mid;
                best = simplified;
            }
            else
            {
                lo = mid;
            }
        }

        return best;
    }

    private static List<double[]> DouglasPeucker(IReadOnlyList<double[]> points, double epsilonMeters)
    {
        var n = points.Count;
        if (n <= 2)
        {
            return points.ToList();
        }

        var keep = new bool[n];
        keep[0] = true;
        keep[n - 1] = true;

        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, n - 1));

        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            if (end <= start + 1)
            {
                continue;
            }

            var maxDist = -1.0;
            var maxIdx = start;
            for (var i = start + 1; i < end; i++)
            {
                var d = PerpendicularDistanceMeters(points[i], points[start], points[end]);
                if (d > maxDist)
                {
                    maxDist = d;
                    maxIdx = i;
                }
            }

            if (maxDist > epsilonMeters)
            {
                keep[maxIdx] = true;
                stack.Push((start, maxIdx));
                stack.Push((maxIdx, end));
            }
        }

        var result = new List<double[]>();
        for (var i = 0; i < n; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    private static double PerpendicularDistanceMeters(double[] point, double[] lineStart, double[] lineEnd)
    {
        const double earthRadiusM = 6371000.0;
        const double degToRad = Math.PI / 180.0;
        var lat0 = (lineStart[1] + lineEnd[1]) * 0.5 * degToRad;
        var mx = earthRadiusM * degToRad * Math.Cos(lat0);
        var my = earthRadiusM * degToRad;

        var ax = lineStart[0] * mx;
        var ay = lineStart[1] * my;
        var bx = lineEnd[0] * mx;
        var by = lineEnd[1] * my;
        var px = point[0] * mx;
        var py = point[1] * my;

        var dx = bx - ax;
        var dy = by - ay;
        var lengthSq = dx * dx + dy * dy;
        if (lengthSq < 1e-12)
        {
            var ex = px - ax;
            var ey = py - ay;
            return Math.Sqrt(ex * ex + ey * ey);
        }

        var cross = (px - ax) * dy - (py - ay) * dx;
        return Math.Abs(cross) / Math.Sqrt(lengthSq);
    }

    private static List<WorkoutSplit> CalculateSplitsFromHaversine(
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
                EmitSplit(
                    splits,
                    trackPoints,
                    workoutId,
                    ref splitIndex,
                    splitStartIndex,
                    i,
                    accumulatedDistance - splitStartDistance,
                    distanceMeters,
                    durationSeconds);

                splitStartDistance = accumulatedDistance;
                lastSplitStartIndex = splitStartIndex;
                splitStartIndex = i;
            }
        }

        FinalizeRemainder(
            splits,
            trackPoints,
            workoutId,
            splitIndex,
            splitStartIndex,
            lastSplitStartIndex,
            accumulatedDistance - splitStartDistance,
            splitDistanceMeters,
            distanceMeters,
            durationSeconds);

        return splits;
    }

    private static List<WorkoutSplit> CalculateSplitsFromDistanceStream(
        List<TrackPoint> distancePoints,
        double distanceMeters,
        int durationSeconds,
        double splitDistanceMeters,
        Guid workoutId)
    {
        if (distancePoints.Count < 2)
        {
            return new List<WorkoutSplit>();
        }

        var splits = new List<WorkoutSplit>();
        var splitStartDistance = distancePoints[0].DistanceM ?? 0.0;
        var splitStartIndex = 0;
        var lastSplitStartIndex = 0;
        var splitIndex = 0;
        var accumulatedDistance = splitStartDistance;

        for (int i = 1; i < distancePoints.Count; i++)
        {
            accumulatedDistance = distancePoints[i].DistanceM!.Value;

            if (accumulatedDistance - splitStartDistance >= splitDistanceMeters)
            {
                EmitSplit(
                    splits,
                    distancePoints,
                    workoutId,
                    ref splitIndex,
                    splitStartIndex,
                    i,
                    accumulatedDistance - splitStartDistance,
                    distanceMeters,
                    durationSeconds);

                splitStartDistance = accumulatedDistance;
                lastSplitStartIndex = splitStartIndex;
                splitStartIndex = i;
            }
        }

        FinalizeRemainder(
            splits,
            distancePoints,
            workoutId,
            splitIndex,
            splitStartIndex,
            lastSplitStartIndex,
            accumulatedDistance - splitStartDistance,
            splitDistanceMeters,
            distanceMeters,
            durationSeconds);

        return splits;
    }

    private static void EmitSplit(
        List<WorkoutSplit> splits,
        List<TrackPoint> trackPoints,
        Guid workoutId,
        ref int splitIndex,
        int splitStartIndex,
        int endIndex,
        double splitDistance,
        double distanceMeters,
        int durationSeconds)
    {
        var splitDuration = SplitDuration(
            trackPoints, splitStartIndex, endIndex, splitDistance, distanceMeters, durationSeconds);
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
    }

    private static void FinalizeRemainder(
        List<WorkoutSplit> splits,
        List<TrackPoint> trackPoints,
        Guid workoutId,
        int splitIndex,
        int splitStartIndex,
        int lastSplitStartIndex,
        double remainingDistance,
        double splitDistanceMeters,
        double distanceMeters,
        int durationSeconds)
    {
        if (remainingDistance <= 0)
        {
            return;
        }

        if (remainingDistance >= splitDistanceMeters * 0.1 && splits.Count > 0)
        {
            EmitSplit(
                splits,
                trackPoints,
                workoutId,
                ref splitIndex,
                splitStartIndex,
                trackPoints.Count - 1,
                remainingDistance,
                distanceMeters,
                durationSeconds);
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
            else if (distanceMeters > 0)
            {
                mergedDuration = (int)((totalLastSplitDistance / distanceMeters) * durationSeconds);
            }

            lastSplit.DistanceM = totalLastSplitDistance;
            lastSplit.DurationS = mergedDuration;
            lastSplit.PaceS = mergedDuration > 0 ? mergedDuration / (totalLastSplitDistance / 1000.0) : lastSplit.PaceS;
        }
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

        if (distanceMeters <= 0)
        {
            return 0;
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
                point.TemperatureC.HasValue ||
                point.DistanceM.HasValue)
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
                    ElevationM = point.Elevation,
                    DistanceM = point.DistanceM
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
