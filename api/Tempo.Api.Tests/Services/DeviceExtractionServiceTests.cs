using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for DeviceExtractionService covering device name extraction and Apple Watch mapping
/// </summary>
public class DeviceExtractionServiceTests
{
    private readonly Mock<ILogger> _loggerMock;

    public DeviceExtractionServiceTests()
    {
        _loggerMock = new Mock<ILogger>();
    }

    #region MapAppleWatchIdentifier Tests

    [Fact]
    public void MapAppleWatchIdentifier_WithValidIdentifier_ReturnsDeviceName()
    {
        // Act & Assert
        DeviceExtractionService.MapAppleWatchIdentifier("Watch7,12").Should().Be("Apple Watch Ultra 3");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch7,17").Should().Be("Apple Watch Series 11");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch6,18").Should().Be("Apple Watch Ultra");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch5,1").Should().Be("Apple Watch Series 5");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch4,1").Should().Be("Apple Watch Series 4");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch3,1").Should().Be("Apple Watch Series 3");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch2,3").Should().Be("Apple Watch Series 2");
        DeviceExtractionService.MapAppleWatchIdentifier("Watch1,1").Should().Be("Apple Watch (1st generation)");
    }

    [Fact]
    public void MapAppleWatchIdentifier_WithNull_ReturnsNull()
    {
        // Act
        var result = DeviceExtractionService.MapAppleWatchIdentifier(null);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MapAppleWatchIdentifier_WithEmptyString_ReturnsNull()
    {
        // Act
        var result = DeviceExtractionService.MapAppleWatchIdentifier("");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MapAppleWatchIdentifier_WithWhitespace_ReturnsNull()
    {
        // Act
        var result = DeviceExtractionService.MapAppleWatchIdentifier("   ");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MapAppleWatchIdentifier_WithInvalidIdentifier_ReturnsNull()
    {
        // Act
        var result = DeviceExtractionService.MapAppleWatchIdentifier("InvalidIdentifier");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void MapAppleWatchIdentifier_WithWhitespaceAroundIdentifier_TrimsAndReturnsDeviceName()
    {
        // Act
        var result = DeviceExtractionService.MapAppleWatchIdentifier("  Watch7,12  ");

        // Assert
        result.Should().Be("Apple Watch Ultra 3");
    }

    #endregion

    #region ExtractDeviceName Tests

    [Fact]
    public void ExtractDeviceName_WithProductName_ReturnsProductName()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            productName = "Garmin Forerunner 945"
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().Be("Garmin Forerunner 945");
    }

    [Fact]
    public void ExtractDeviceName_WithAppleWatchProductName_MapsToFriendlyName()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            productName = "Watch7,12"
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().Be("Apple Watch Ultra 3");
    }

    [Fact]
    public void ExtractDeviceName_WithManufacturerAndProductCode_ReturnsCombinedName()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            manufacturer = 1, // Garmin
            product = 9999
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().Be("Garmin");
    }

    [Fact]
    public void ExtractDeviceName_WithGarminKnownProductCode_ReturnsFriendlyModelName()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            manufacturer = 1, // Garmin
            product = 4315 // Fr965
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().Be("Garmin Forerunner 965");
    }

    [Fact]
    public void ExtractDeviceName_WithProductNameAndGarminProductCode_PrefersProductName()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            productName = "Custom Garmin Name",
            manufacturer = 1, // Garmin
            product = 4315 // Fr965
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().Be("Custom Garmin Name");
    }

    [Fact]
    public void ExtractDeviceName_WithEmptyElement_ReturnsNull()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new { });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, _loggerMock.Object);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractDeviceName_WithNullLogger_DoesNotThrow()
    {
        // Arrange
        var json = JsonSerializer.Serialize(new
        {
            productName = "Test Device"
        });
        var element = JsonSerializer.Deserialize<JsonElement>(json);

        // Act
        var result = DeviceExtractionService.ExtractDeviceName(element, null);

        // Assert
        result.Should().Be("Test Device");
    }

    #endregion
}

