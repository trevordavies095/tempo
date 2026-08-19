using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class TrackGeometryTests
{
    private readonly TrackGeometry _geometry;
    private readonly DateTime _start = new(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _workoutId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    public TrackGeometryTests()
    {
        _geometry = new TrackGeometry(new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        });
    }

    [Fact]
    public void Derive_WithKnownDistances_ReturnsCorrectSplits()
    {
        var points = CreateTrackPointsWithKnownDistance(5000.0, 1800);

        var result = _geometry.Derive(points, _start, 1000.0, _workoutId, 5000.0, 1800);

        result.Splits.Should().NotBeEmpty();
        result.Splits[0].DistanceM.Should().BeApproximately(1000.0, 100.0);
        result.Splits.Should().OnlyContain(s => s.WorkoutId == _workoutId);
    }

    [Fact]
    public void Derive_WithMetricPreference_Returns1000mSplits()
    {
        var points = CreateTrackPointsWithKnownDistance(5000.0, 1800);

        var result = _geometry.Derive(points, _start, 1000.0, _workoutId, 5000.0, 1800);

        result.Splits.Should().NotBeEmpty();
        result.Splits[0].DistanceM.Should().BeApproximately(1000.0, 50.0);
    }

    [Fact]
    public void Derive_WithImperialPreference_Returns1609mSplits()
    {
        var points = CreateTrackPointsWithKnownDistance(8046.72, 1800);

        var result = _geometry.Derive(points, _start, 1609.344, _workoutId, 8046.72, 1800);

        result.Splits.Should().NotBeEmpty();
        result.Splits[0].DistanceM.Should().BeApproximately(1609.344, 100.0);
    }

    [Fact]
    public void Derive_GpxSeries_IncludesHeartRate_ExcludesElevationOnly()
    {
        var points = new List<TrackPoint>
        {
            new()
            {
                Latitude = 37.7749,
                Longitude = -122.4194,
                Time = _start,
                HeartRateBpm = 150,
                Elevation = 10
            },
            new()
            {
                Latitude = 37.7750,
                Longitude = -122.4195,
                Time = _start.AddSeconds(10),
                Elevation = 12
            }
        };

        var result = _geometry.Derive(points, _start, 1000.0, _workoutId, 100, 10);

        result.TimeSeries.Should().HaveCount(1);
        result.TimeSeries[0].HeartRateBpm.Should().Be(150);
        result.TimeSeries[0].ElapsedSeconds.Should().Be(0);
        result.TimeSeries[0].ElevationM.Should().Be(10);
    }

    [Fact]
    public void Derive_FitSeries_IncludesIndoorHeartRate_DropsNegativeElapsed()
    {
        var gps = new List<TrackPoint>
        {
            new()
            {
                Latitude = 37.7749,
                Longitude = -122.4194,
                Time = _start,
                Elevation = 10
            }
        };
        var series = new List<TrackPoint>
        {
            new()
            {
                Time = _start.AddSeconds(-5),
                HeartRateBpm = 140
            },
            new()
            {
                Time = _start.AddSeconds(3),
                HeartRateBpm = 155
            }
        };

        var result = _geometry.Derive(gps, _start, 1000.0, _workoutId, 100, 10, series);

        result.TimeSeries.Should().HaveCount(1);
        result.TimeSeries[0].HeartRateBpm.Should().Be(155);
        result.TimeSeries[0].ElapsedSeconds.Should().Be(3);
    }

    private static List<TrackPoint> CreateTrackPointsWithKnownDistance(double totalDistanceMeters, int totalDurationSeconds)
    {
        var numPoints = 100;
        var points = new List<TrackPoint>();
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var startLat = 37.7749;
        var startLon = -122.4194;
        var degreeIncrement = totalDistanceMeters / (111000.0 * (numPoints - 1));

        for (int i = 0; i < numPoints; i++)
        {
            var elapsedSeconds = (int)((double)i / (numPoints - 1) * totalDurationSeconds);
            points.Add(new TrackPoint
            {
                Latitude = startLat + (i * degreeIncrement),
                Longitude = startLon + (i * degreeIncrement),
                Time = startTime.AddSeconds(elapsedSeconds),
                Elevation = 100.0 + (i * 0.1)
            });
        }

        return points;
    }
}
