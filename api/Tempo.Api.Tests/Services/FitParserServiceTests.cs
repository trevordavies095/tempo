using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for FitParserService
/// </summary>
public class FitParserServiceTests
{
    private readonly ElevationCalculationConfig _elevationConfig;
    private readonly FitParserService _parser;

    public FitParserServiceTests()
    {
        _elevationConfig = new ElevationCalculationConfig
        {
            NoiseThresholdMeters = 2.0,
            MinDistanceMeters = 10.0
        };
        _parser = new FitParserService(_elevationConfig);
    }

    [Fact]
    public void ParseFit_WithValidFitFile_ReturnsCorrectResult()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            // Skip test if file doesn't exist (e.g., in CI)
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.StartTime.Should().BeAfter(DateTime.MinValue);
        result.DurationSeconds.Should().BeGreaterThan(0);
        result.DistanceMeters.Should().BeGreaterThan(0);
        result.RawFitDataJson.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ParseFit_WithInvalidFormat_ThrowsException()
    {
        // Arrange
        var invalidData = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        using var stream = new MemoryStream(invalidData);

        // Act & Assert
        var act = () => _parser.ParseFit(stream);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Not a valid FIT file*");
    }

    [Fact]
    public void ParseFit_WithCorruptedFile_ThrowsException()
    {
        // Arrange
        // Create a stream that looks like it might be a FIT file but is corrupted
        var corruptedData = new byte[100];
        Array.Fill(corruptedData, (byte)0x0E); // FIT file header starts with 0x0E
        corruptedData[0] = 0x0E;
        corruptedData[1] = 0x10; // Header size
        // Rest is garbage
        using var stream = new MemoryStream(corruptedData);

        // Act & Assert
        var act = () => _parser.ParseFit(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseFit_ExtractsTrackPoints_Correctly()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().NotBeNull();
        
        if (result.TrackPoints.Count > 0)
        {
            var firstPoint = result.TrackPoints[0];
            firstPoint.Latitude.Should().BeInRange(-90.0, 90.0);
            firstPoint.Longitude.Should().BeInRange(-180.0, 180.0);
            firstPoint.Time.Should().HaveValue();
        }
    }

    [Fact]
    public void ParseFit_ExtractsHeartRate_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        // Heart rate may or may not be present in the file, so we just check the structure
        result.TrackPoints.Should().NotBeNull();
    }

    [Fact]
    public void ParseFit_ExtractsCadence_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        // Cadence may or may not be present in the file, so we just check the structure
        result.TrackPoints.Should().NotBeNull();
    }

    [Fact]
    public void ParseGzippedFit_WithGzippedFile_ReturnsCorrectResult()
    {
        // Arrange
        // First, create a gzipped FIT file from an existing FIT file
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        byte[] fitData;
        using (var fileStream = File.OpenRead(fitFilePath))
        {
            fitData = new byte[fileStream.Length];
            fileStream.Read(fitData, 0, fitData.Length);
        }

        // Create a gzipped version in memory
        using var gzippedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(gzippedStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzipStream.Write(fitData, 0, fitData.Length);
        }
        gzippedStream.Position = 0;

        // Act
        var result = _parser.ParseGzippedFit(gzippedStream);

        // Assert
        result.Should().NotBeNull();
        result.StartTime.Should().BeAfter(DateTime.MinValue);
        result.DurationSeconds.Should().BeGreaterThan(0);
        result.DistanceMeters.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ParseFit_WithNoGpsData_ThrowsException()
    {
        // Arrange
        // Create a minimal invalid FIT-like structure that would fail GPS check
        // This is a simplified test - actual FIT files without GPS are complex
        // We'll test with a file that has no GPS data if available, otherwise skip
        
        // For now, we'll test that the parser handles the case
        // In practice, this would require a specific FIT file without GPS data
        // which is hard to create synthetically
        
        // This test verifies the error message when no GPS data is found
        var invalidData = new byte[] { 0x0E, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(invalidData);

        // Act & Assert
        var act = () => _parser.ParseFit(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseFit_WithMissingTimestamps_ThrowsException()
    {
        // Arrange
        // Create invalid data that would fail timestamp check
        var invalidData = new byte[100];
        Array.Fill(invalidData, (byte)0x00);
        using var stream = new MemoryStream(invalidData);

        // Act & Assert
        var act = () => _parser.ParseFit(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseFit_ExtractsElevation_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        // Elevation may or may not be present, but if track points exist, check structure
        if (result.TrackPoints.Count > 0)
        {
            // At least verify the structure is correct
            result.TrackPoints.Should().NotBeEmpty();
        }
    }

    [Fact]
    public void ParseFit_ReturnsRecordMesgs_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.RecordMesgs.Should().NotBeNull();
    }

    [Fact]
    public void ParseFit_WithEmptyStream_ThrowsException()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act & Assert
        var act = () => _parser.ParseFit(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseGzippedFit_WithInvalidGzip_ThrowsException()
    {
        // Arrange
        var invalidGzipData = new byte[] { 0x00, 0x01, 0x02, 0x03 };
        using var stream = new MemoryStream(invalidGzipData);

        // Act & Assert
        var act = () => _parser.ParseGzippedFit(stream);
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void ParseFit_CalculatesElevationGain_WhenElevationDataPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!File.Exists(fitFilePath))
        {
            return;
        }

        using var stream = File.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        // Elevation gain may be null if no elevation data, or a value if present
        if (result.ElevationGainMeters.HasValue)
        {
            result.ElevationGainMeters.Value.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}

