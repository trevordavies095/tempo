using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class HealthKitWorkoutDecoderTests
{
    private readonly HealthKitWorkoutDecoder _decoder = new();

    [Fact]
    public void Decode_RejectsNullRequest()
    {
        var result = _decoder.Decode(null);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("required");
    }

    [Fact]
    public void Decode_RejectsUnsupportedSchemaVersion()
    {
        var result = _decoder.Decode(ValidOutdoorRequest(schemaVersion: 2));

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("schemaVersion");
    }

    [Fact]
    public void Decode_RejectsMissingSummary()
    {
        var request = ValidOutdoorRequest();
        request.Summary = null;

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("summary");
    }

    [Fact]
    public void Decode_RejectsMissingHealthKitUuid()
    {
        var request = ValidOutdoorRequest();
        request.HealthKitUuid = "  ";

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("healthKitUuid");
    }

    [Fact]
    public void Decode_RejectsNonPositiveDuration()
    {
        var request = ValidOutdoorRequest();
        request.Summary!.DurationS = 0;

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("durationS");
    }

    [Fact]
    public void Decode_RejectsNonPositiveDistance()
    {
        var request = ValidOutdoorRequest();
        request.Summary!.DistanceM = 0;

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("distanceM");
    }

    [Fact]
    public void Decode_RejectsIndoorWorkouts()
    {
        var request = ValidOutdoorRequest();
        request.Summary!.IsIndoor = true;

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Indoor");
    }

    [Fact]
    public void Decode_RejectsTooManyTrackPoints()
    {
        var request = ValidOutdoorRequest();
        request.TrackPoints = Enumerable.Range(0, HealthKitWorkoutDecoder.MaxTrackPoints + 1)
            .Select(i => new HealthKitTrackPointDto
            {
                T = $"2024-06-15T10:00:{i % 60:D2}Z",
                Lat = 37.77 + i * 0.0001,
                Lon = -122.41 + i * 0.0001
            })
            .ToList();

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("maximum");
    }

    [Fact]
    public void Decode_RejectsFewerThanTwoPositionedPoints()
    {
        var request = ValidOutdoorRequest();
        request.TrackPoints = new List<HealthKitTrackPointDto>
        {
            new() { T = "2024-06-15T10:00:00Z", Lat = 37.77, Lon = -122.41 }
        };

        var result = _decoder.Decode(request);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("two trackPoints");
    }

    [Fact]
    public void Decode_Outdoor_UsesSummaryDistanceAndDuration()
    {
        var request = ValidOutdoorRequest();
        request.Summary!.DistanceM = 5000;
        request.Summary.DurationS = 1800;

        var result = _decoder.Decode(request);

        result.Success.Should().BeTrue();
        result.Decoded!.DistanceM.Should().Be(5000);
        result.Decoded.DurationS.Should().Be(1800);
        result.Decoded.SeriesPoints.Should().BeNull();
        result.Overlay!.Source.Should().Be("healthkit");
        result.Overlay.RawHealthKitDataJson.Should().NotBeNullOrEmpty();
        result.Overlay.Device.Should().Be("Apple Watch");
        result.Overlay.EnergyKcal.Should().Be(420);
        result.Overlay.AvgHeartRateBpm.Should().Be(150);
        result.Decoded.TrackPoints.Should().HaveCount(3);
        result.Decoded.TrackPoints[0].HeartRateBpm.Should().Be(140);
        result.Decoded.TrackPoints[0].PowerWatts.Should().Be(250);
    }

    private static HealthKitImportRequest ValidOutdoorRequest(int schemaVersion = 1) => new()
    {
        SchemaVersion = schemaVersion,
        HealthKitUuid = "A1B2C3D4-E5F6-7890-ABCD-EF1234567890",
        SourceApp = new HealthKitSourceAppDto { Name = "Apple Watch", BundleId = "com.apple.health" },
        Summary = new HealthKitSummaryDto
        {
            StartedAt = new DateTime(2024, 6, 15, 10, 0, 0, DateTimeKind.Utc),
            DurationS = 1800,
            DistanceM = 5000,
            IsIndoor = false,
            EnergyKcal = 420,
            AvgHeartRateBpm = 150,
            MaxHeartRateBpm = 175
        },
        TrackPoints = new List<HealthKitTrackPointDto>
        {
            new()
            {
                T = "2024-06-15T10:00:00Z",
                Lat = 37.7749,
                Lon = -122.4194,
                Ele = 10,
                Hr = 140,
                Cad = 160,
                Pwr = 250,
                DistM = 0
            },
            new()
            {
                T = "2024-06-15T10:15:00Z",
                Lat = 37.7849,
                Lon = -122.4094,
                Ele = 25,
                Hr = 155,
                Cad = 165,
                Pwr = 270,
                DistM = 2500
            },
            new()
            {
                T = "2024-06-15T10:30:00Z",
                Lat = 37.7949,
                Lon = -122.3994,
                Ele = 40,
                Hr = 160,
                Cad = 168,
                Pwr = 280,
                DistM = 5000
            }
        }
    };
}
