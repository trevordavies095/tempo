using System.Text;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for GpxParserService
/// </summary>
public class GpxParserServiceTests
{
    private readonly ElevationCalculationConfig _elevationConfig;
    private readonly GpxParserService _parser;

    public GpxParserServiceTests()
    {
        _elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };
        _parser = new GpxParserService(_elevationConfig, new TrackGeometry(_elevationConfig));
    }

    [Fact]
    public void ParseGpx_WithMinimalValidGpx_ReturnsCorrectResult()
    {
        // Arrange
        var gpxXml = CreateMinimalGpxStream();

        // Act
        var result = _parser.ParseGpx(gpxXml);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().HaveCountGreaterThanOrEqualTo(2);
        result.StartTime.Should().BeAfter(DateTime.MinValue);
        result.DurationSeconds.Should().BeGreaterThan(0);
        result.DistanceMeters.Should().BeGreaterThan(0);
        result.RawGpxDataJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseGpx_WithMalformedXml_ThrowsException()
    {
        // Arrange
        var invalidXml = new MemoryStream(Encoding.UTF8.GetBytes("<gpx><trkpt>invalid</gpx>"));

        // Act & Assert
        var act = () => _parser.ParseGpx(invalidXml);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseGpx_WithMissingNamespace_ThrowsException()
    {
        // Arrange
        var xmlWithoutNamespace = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithoutNamespace));

        // Act & Assert
        var act = () => _parser.ParseGpx(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseGpx_WithNoTrackPoints_ThrowsException()
    {
        // Arrange
        var xmlNoTrackPoints = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlNoTrackPoints));

        // Act & Assert
        var act = () => _parser.ParseGpx(stream);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No track points found*");
    }

    [Fact]
    public void ParseGpx_WithSingleTrackPoint_ThrowsException()
    {
        // Arrange
        var xmlSinglePoint = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlSinglePoint));

        // Act & Assert
        var act = () => _parser.ParseGpx(stream);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must contain at least 2 track points*");
    }

    [Fact]
    public void ParseGpx_WithMissingCoordinates_HandlesCorrectly()
    {
        // Arrange
        var xmlMissingCoords = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt>
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:02:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlMissingCoords));

        // Act
        var result = _parser.ParseGpx(stream);

        // Assert
        result.Should().NotBeNull();
        // Should skip the point without coordinates
        result.TrackPoints.Should().HaveCount(2);
    }

    [Fact]
    public void ParseGpx_WithDuplicateTimestamps_HandlesCorrectly()
    {
        // Arrange
        var xmlDuplicateTimes = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7751"" lon=""-122.4196"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlDuplicateTimes));

        // Act
        var result = _parser.ParseGpx(stream);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().HaveCount(3);
        // Duration should be calculated correctly (1 minute between first and last)
        result.DurationSeconds.Should().Be(60);
    }

    [Fact]
    public void ParseGpx_WithMissingElevation_HandlesCorrectly()
    {
        // Arrange
        var xmlNoElevation = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlNoElevation));

        // Act
        var result = _parser.ParseGpx(stream);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().HaveCount(2);
        result.TrackPoints.All(p => !p.Elevation.HasValue).Should().BeTrue();
        result.ElevationGainMeters.Should().BeNull();
    }

    [Fact]
    public void ParseGpx_WithMissingHeartRate_HandlesCorrectly()
    {
        // Arrange
        var xmlNoHeartRate = CreateMinimalGpxStream();

        // Act
        var result = _parser.ParseGpx(xmlNoHeartRate);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.All(p => !p.HeartRateBpm.HasValue).Should().BeTrue();
    }

    [Fact]
    public void ParseGpx_ExtractsMetadata_Correctly()
    {
        // Arrange
        var gpxXml = CreateGpxWithMetadata();

        // Act
        var result = _parser.ParseGpx(gpxXml);

        // Assert
        result.Should().NotBeNull();
        result.RawGpxDataJson.Should().Contain("Morning Run");
        result.RawGpxDataJson.Should().Contain("Test workout");
        result.RawGpxDataJson.Should().Contain("Test Author");
        result.RawGpxDataJson.Should().Contain("running,test");
    }

    [Fact]
    public void ParseGpx_WithExtensions_ExtractsHeartRateAndCadence()
    {
        // Arrange
        var gpxXml = CreateGpxWithExtensions();

        // Act
        var result = _parser.ParseGpx(gpxXml);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().HaveCountGreaterThan(0);
        result.TrackPoints[0].HeartRateBpm.Should().Be(150);
        result.TrackPoints[0].CadenceRpm.Should().Be(170);
    }

    [Fact]
    public void ParseGpx_WithMissingTimestamps_ThrowsException()
    {
        // Arrange
        var xmlNoTimestamps = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlNoTimestamps));

        // Act & Assert
        var act = () => _parser.ParseGpx(stream);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*must contain timestamps*");
    }

    // Helper methods

    private MemoryStream CreateMinimalGpxStream()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
      <trkpt lat=""37.7751"" lon=""-122.4196"">
        <time>2024-01-15T10:02:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private MemoryStream CreateGpxWithMetadata()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <metadata>
    <name>Morning Run</name>
    <desc>Test workout</desc>
    <author>
      <name>Test Author</name>
    </author>
    <keywords>running,test</keywords>
    <time>2024-01-15T10:00:00Z</time>
  </metadata>
  <trk>
    <name>Morning Run</name>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }

    private MemoryStream CreateGpxWithExtensions()
    {
        var xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" xmlns=""http://www.topografix.com/GPX/1/1""
     xmlns:gpxtpx=""http://www.garmin.com/xmlschemas/TrackPointExtension/v1"">
  <trk>
    <trkseg>
      <trkpt lat=""37.7749"" lon=""-122.4194"">
        <time>2024-01-15T10:00:00Z</time>
        <extensions>
          <gpxtpx:TrackPointExtension>
            <gpxtpx:hr>150</gpxtpx:hr>
            <gpxtpx:cad>170</gpxtpx:cad>
          </gpxtpx:TrackPointExtension>
        </extensions>
      </trkpt>
      <trkpt lat=""37.7750"" lon=""-122.4195"">
        <time>2024-01-15T10:01:00Z</time>
      </trkpt>
    </trkseg>
  </trk>
</gpx>";
        return new MemoryStream(Encoding.UTF8.GetBytes(xml));
    }
}

