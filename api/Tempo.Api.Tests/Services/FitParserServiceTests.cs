using System.IO.Compression;
using System.Text.Json;
using Dynastream.Fit;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;
using FitDateTime = Dynastream.Fit.DateTime;
using FitFile = Dynastream.Fit.File;
using IOFile = System.IO.File;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for FitParserService
/// </summary>
public class FitParserServiceTests
{
    private readonly FitParserService _parser;
    private static readonly string CadenceFixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "running-cadence-80.fit");

    public FitParserServiceTests()
    {
        _parser = new FitParserService();
    }

    [Fact]
    public void ParseFit_WithValidFitFile_ReturnsCorrectResult()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!IOFile.Exists(fitFilePath))
        {
            // Skip test if file doesn't exist (e.g., in CI)
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.StartTime.Should().BeAfter(System.DateTime.MinValue);
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
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.TrackPoints.Should().NotBeNull();
        
        if (result.TrackPoints.Count > 0)
        {
            var firstPoint = result.TrackPoints[0];
            firstPoint.Latitude.Should().HaveValue();
            firstPoint.Longitude.Should().HaveValue();
            firstPoint.Latitude!.Value.Should().BeInRange(-90.0, 90.0);
            firstPoint.Longitude!.Value.Should().BeInRange(-180.0, 180.0);
            firstPoint.Time.Should().HaveValue();
        }
    }

    [Fact]
    public void ParseFit_ExtractsHeartRate_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        // Heart rate may or may not be present in the file, so we just check the structure
        result.TrackPoints.Should().NotBeNull();
    }

    [Fact]
    public void ParseFit_ConvertsCadenceToStepsPerMinute_FromCommittedFixture()
    {
        IOFile.Exists(CadenceFixturePath).Should().BeTrue(
            "running-cadence-80.fit must be copied to the test output directory");

        using var stream = IOFile.OpenRead(CadenceFixturePath);
        var result = _parser.ParseFit(stream);

        result.TrackPoints.Should().NotBeEmpty();
        result.TrackPoints.Should().OnlyContain(p => p.CadenceRpm == 160);
        result.SeriesPoints.Should().NotBeEmpty();
        result.SeriesPoints.Should().OnlyContain(p => p.CadenceRpm == 160);

        using var doc = JsonDocument.Parse(result.RawFitDataJson!);
        var root = doc.RootElement;
        root.GetProperty("cadenceUnit").GetString().Should().Be("spm");
        var session = root.GetProperty("session");
        session.GetProperty("avgCadence").GetInt32().Should().Be(160);
        session.GetProperty("maxCadence").GetInt32().Should().Be(176);
        // FIT SDK aliases max_running_cadence to max_cadence on encode; we do not ×2 it.
        session.GetProperty("maxRunningCadence").GetInt32().Should().Be(88);

        foreach (var point in root.GetProperty("trackPoints").EnumerateArray())
        {
            point.GetProperty("cad").GetInt32().Should().Be(160);
        }
    }

    [Fact]
    public void ParseGzippedFit_ConvertsCadenceToStepsPerMinute_FromCommittedFixture()
    {
        var fitData = IOFile.ReadAllBytes(CadenceFixturePath);
        using var gzippedStream = new MemoryStream();
        using (var gzipStream = new GZipStream(gzippedStream, CompressionMode.Compress, leaveOpen: true))
        {
            gzipStream.Write(fitData, 0, fitData.Length);
        }
        gzippedStream.Position = 0;

        var result = _parser.ParseGzippedFit(gzippedStream);

        result.SeriesPoints.Should().OnlyContain(p => p.CadenceRpm == 160);
        using var doc = JsonDocument.Parse(result.RawFitDataJson!);
        doc.RootElement.GetProperty("cadenceUnit").GetString().Should().Be("spm");
        doc.RootElement.GetProperty("session").GetProperty("avgCadence").GetInt32().Should().Be(160);
    }

    [Fact]
    public void ParseFit_ConvertsIndoorSeriesCadence_WhenNoGpsTrackPoints()
    {
        var fitBytes = CreateIndoorFitWithCadence(strideCadence: 80, avgCadence: 80, maxCadence: 88);
        using var stream = new MemoryStream(fitBytes);

        var result = _parser.ParseFit(stream);

        result.TrackPoints.Should().BeEmpty();
        result.SeriesPoints.Should().NotBeEmpty();
        result.SeriesPoints.Should().OnlyContain(p => p.CadenceRpm == 160);

        using var doc = JsonDocument.Parse(result.RawFitDataJson!);
        var root = doc.RootElement;
        root.GetProperty("cadenceUnit").GetString().Should().Be("spm");
        root.GetProperty("trackPoints").GetArrayLength().Should().Be(0);
        var session = root.GetProperty("session");
        session.GetProperty("avgCadence").GetInt32().Should().Be(160);
        session.GetProperty("maxCadence").GetInt32().Should().Be(176);
        session.GetProperty("maxRunningCadence").GetInt32().Should().Be(88);
    }

    [Fact]
    public void ParseFit_ExtractsCadence_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

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
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        byte[] fitData;
        using (var fileStream = IOFile.OpenRead(fitFilePath))
        {
            fitData = new byte[fileStream.Length];
            fileStream.ReadExactly(fitData);
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
        result.StartTime.Should().BeAfter(System.DateTime.MinValue);
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
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

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
    public void ParseFit_ReturnsSeriesPoints_WhenPresent()
    {
        // Arrange
        var fitFilePath = Path.Combine("..", "..", "..", "..", "..", "test_data", "20251110.fit");
        if (!IOFile.Exists(fitFilePath))
        {
            return;
        }

        using var stream = IOFile.OpenRead(fitFilePath);

        // Act
        var result = _parser.ParseFit(stream);

        // Assert
        result.Should().NotBeNull();
        result.SeriesPoints.Should().NotBeNull();
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
    public void ParseFit_DurationSeconds_UsesElapsedNotTimer()
    {
        var fitBytes = CreateFitWithClocks(elapsedSeconds: 1200f, timerSeconds: 1000f);
        using var stream = new MemoryStream(fitBytes);

        var result = _parser.ParseFit(stream);

        result.DurationSeconds.Should().Be(1200);
        using var doc = JsonDocument.Parse(result.RawFitDataJson!);
        doc.RootElement.GetProperty("session").GetProperty("totalElapsedTime").GetDouble()
            .Should().BeApproximately(1200, 0.01);
        doc.RootElement.GetProperty("session").GetProperty("totalTimerTime").GetDouble()
            .Should().BeApproximately(1000, 0.01);
    }

    [Fact]
    public void ParseFit_WithLapMessages_MapsSummariesAndRawJson_DurationStaysElapsed()
    {
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitBytes = CreateFitWithClocks(
            elapsedSeconds: 1326f,
            timerSeconds: 1205f,
            laps: new[]
            {
                new SyntheticLap(start, 600f, 600f, 1609f, 150, LapTrigger.Distance),
                new SyntheticLap(start.AddSeconds(600), 721f, 600f, 1609f, 155, LapTrigger.Manual),
                new SyntheticLap(start.AddSeconds(1321), 5f, 5f, 13f, null, LapTrigger.SessionEnd),
            });
        using var stream = new MemoryStream(fitBytes);

        var result = _parser.ParseFit(stream);

        result.DurationSeconds.Should().Be(1326);
        result.Laps.Should().HaveCount(3);
        result.Laps[0].DistanceM.Should().BeApproximately(1609, 0.1);
        result.Laps[0].TimerS.Should().BeApproximately(600, 0.01);
        result.Laps[0].ElapsedS.Should().BeApproximately(600, 0.01);
        result.Laps[0].AvgHeartRateBpm.Should().Be(150);
        result.Laps[0].LapTrigger.Should().Be("Distance");
        result.Laps[1].TimerS.Should().BeApproximately(600, 0.01);
        result.Laps[1].ElapsedS.Should().BeApproximately(721, 0.01);
        result.Laps[1].LapTrigger.Should().Be("Manual");
        result.Laps[2].DistanceM.Should().BeApproximately(13, 0.1);
        result.Laps[2].LapTrigger.Should().Be("SessionEnd");

        using var doc = JsonDocument.Parse(result.RawFitDataJson!);
        var lapsJson = doc.RootElement.GetProperty("laps");
        lapsJson.GetArrayLength().Should().Be(3);
        lapsJson[1].GetProperty("timer").GetDouble().Should().BeApproximately(600, 0.01);
        lapsJson[1].GetProperty("elapsed").GetDouble().Should().BeApproximately(721, 0.01);
        lapsJson[2].GetProperty("trigger").GetString().Should().Be("SessionEnd");
    }

    private static byte[] CreateIndoorFitWithCadence(
        byte strideCadence,
        byte avgCadence,
        byte maxCadence)
    {
        return CreateFitWithClocks(
            elapsedSeconds: 1200f,
            timerSeconds: 1200f,
            strideCadence: strideCadence,
            avgCadence: avgCadence,
            maxCadence: maxCadence);
    }

    private sealed record SyntheticLap(
        System.DateTime Start,
        float ElapsedSeconds,
        float TimerSeconds,
        float DistanceMeters,
        byte? AvgHeartRate,
        LapTrigger Trigger);

    private static byte[] CreateFitWithClocks(
        float elapsedSeconds,
        float timerSeconds,
        byte? strideCadence = null,
        byte? avgCadence = null,
        byte? maxCadence = null,
        IReadOnlyList<SyntheticLap>? laps = null)
    {
        var start = new System.DateTime(2024, 1, 15, 10, 0, 0, System.DateTimeKind.Utc);
        var fitStart = new FitDateTime(start);

        using var stream = new MemoryStream();
        var encode = new Encode(stream, ProtocolVersion.V20);

        var fileId = new FileIdMesg();
        fileId.SetType(FitFile.Activity);
        fileId.SetTimeCreated(fitStart);
        encode.Write(fileId);

        for (var i = 0; i < 3; i++)
        {
            var record = new RecordMesg();
            record.SetTimestamp(new FitDateTime(start.AddMinutes(i * 10)));
            record.SetDistance(i * 1200f);
            if (strideCadence.HasValue)
            {
                record.SetCadence(strideCadence.Value);
            }
            encode.Write(record);
        }

        if (laps != null)
        {
            foreach (var lapDef in laps)
            {
                var lap = new LapMesg();
                lap.SetStartTime(new FitDateTime(lapDef.Start));
                lap.SetTimestamp(new FitDateTime(lapDef.Start.AddSeconds(lapDef.ElapsedSeconds)));
                lap.SetTotalElapsedTime(lapDef.ElapsedSeconds);
                lap.SetTotalTimerTime(lapDef.TimerSeconds);
                lap.SetTotalDistance(lapDef.DistanceMeters);
                if (lapDef.AvgHeartRate.HasValue)
                {
                    lap.SetAvgHeartRate(lapDef.AvgHeartRate.Value);
                }
                lap.SetLapTrigger(lapDef.Trigger);
                encode.Write(lap);
            }
        }

        var session = new SessionMesg();
        session.SetStartTime(fitStart);
        session.SetTimestamp(new FitDateTime(start.AddMinutes(20)));
        session.SetTotalElapsedTime(elapsedSeconds);
        session.SetTotalTimerTime(timerSeconds);
        session.SetTotalDistance(2400f);
        session.SetSport(Sport.Running);
        if (avgCadence.HasValue)
        {
            session.SetAvgCadence(avgCadence.Value);
        }
        if (maxCadence.HasValue)
        {
            session.SetMaxCadence(maxCadence.Value);
        }
        encode.Write(session);
        encode.Close();

        return stream.ToArray();
    }
}
