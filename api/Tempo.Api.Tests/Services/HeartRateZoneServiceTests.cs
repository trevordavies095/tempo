using FluentAssertions;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for HeartRateZoneService
/// </summary>
public class HeartRateZoneServiceTests
{
    private readonly HeartRateZoneService _service;

    public HeartRateZoneServiceTests()
    {
        _service = new HeartRateZoneService();
    }

    #region CalculateZonesFromAge Tests

    [Fact]
    public void CalculateZonesFromAge_WithValidAge30_ReturnsCorrectZones()
    {
        // Arrange
        var age = 30;
        var expectedMaxHr = 220 - age; // 190

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: 50-60% of 190 = 95-114
        zones[0].MinBpm.Should().Be(95);
        zones[0].MaxBpm.Should().Be(114);
        
        // Zone 2: 60-70% of 190 = 114-133
        zones[1].MinBpm.Should().Be(114);
        zones[1].MaxBpm.Should().Be(133);
        
        // Zone 3: 70-80% of 190 = 133-152
        zones[2].MinBpm.Should().Be(133);
        zones[2].MaxBpm.Should().Be(152);
        
        // Zone 4: 80-90% of 190 = 152-171
        zones[3].MinBpm.Should().Be(152);
        zones[3].MaxBpm.Should().Be(171);
        
        // Zone 5: 90-100% of 190 = 171-190
        zones[4].MinBpm.Should().Be(171);
        zones[4].MaxBpm.Should().Be(190);
    }

    [Fact]
    public void CalculateZonesFromAge_WithValidAge25_ReturnsCorrectZones()
    {
        // Arrange
        var age = 25;
        var expectedMaxHr = 220 - age; // 195

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: 50% of 195 = 98, 60% = 117
        zones[0].MinBpm.Should().Be(98);
        zones[0].MaxBpm.Should().Be(117);
        
        // Zone 5: 90% of 195 = 176, 100% = 195
        zones[4].MinBpm.Should().Be(176);
        zones[4].MaxBpm.Should().Be(195);
    }

    [Fact]
    public void CalculateZonesFromAge_WithValidAge40_ReturnsCorrectZones()
    {
        // Arrange
        var age = 40;
        var expectedMaxHr = 220 - age; // 180

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: 50% of 180 = 90, 60% = 108
        zones[0].MinBpm.Should().Be(90);
        zones[0].MaxBpm.Should().Be(108);
        
        // Zone 5: 90% of 180 = 162, 100% = 180
        zones[4].MinBpm.Should().Be(162);
        zones[4].MaxBpm.Should().Be(180);
    }

    [Fact]
    public void CalculateZonesFromAge_WithEdgeCaseAge1_ReturnsCorrectZones()
    {
        // Arrange
        var age = 1;
        var expectedMaxHr = 220 - age; // 219

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: 50% of 219 = 110, 60% = 131
        zones[0].MinBpm.Should().Be(110);
        zones[0].MaxBpm.Should().Be(131);
        
        // Zone 5: 90% of 219 = 197, 100% = 219
        zones[4].MinBpm.Should().Be(197);
        zones[4].MaxBpm.Should().Be(219);
    }

    [Fact]
    public void CalculateZonesFromAge_WithEdgeCaseAge120_ReturnsCorrectZones()
    {
        // Arrange
        var age = 120;
        var expectedMaxHr = 220 - age; // 100

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: 50% of 100 = 50, 60% = 60
        zones[0].MinBpm.Should().Be(50);
        zones[0].MaxBpm.Should().Be(60);
        
        // Zone 5: 90% of 100 = 90, 100% = 100
        zones[4].MinBpm.Should().Be(90);
        zones[4].MaxBpm.Should().Be(100);
    }

    [Fact]
    public void CalculateZonesFromAge_WithInvalidAge0_ThrowsArgumentException()
    {
        // Arrange
        var age = 0;

        // Act
        var act = () => _service.CalculateZonesFromAge(age);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Age must be between 1 and 120*")
            .WithParameterName("age");
    }

    [Fact]
    public void CalculateZonesFromAge_WithInvalidAge121_ThrowsArgumentException()
    {
        // Arrange
        var age = 121;

        // Act
        var act = () => _service.CalculateZonesFromAge(age);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Age must be between 1 and 120*")
            .WithParameterName("age");
    }

    [Fact]
    public void CalculateZonesFromAge_WithInvalidAgeNegative_ThrowsArgumentException()
    {
        // Arrange
        var age = -5;

        // Act
        var act = () => _service.CalculateZonesFromAge(age);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Age must be between 1 and 120*")
            .WithParameterName("age");
    }

    [Fact]
    public void CalculateZonesFromAge_VerifiesZonePercentages()
    {
        // Arrange
        var age = 30;
        var maxHr = 220 - age; // 190

        // Act
        var zones = _service.CalculateZonesFromAge(age);

        // Assert
        // Verify zone boundaries match expected percentages
        zones[0].MinBpm.Should().Be((int)Math.Round(maxHr * 0.50)); // 50%
        zones[0].MaxBpm.Should().Be((int)Math.Round(maxHr * 0.60)); // 60%
        zones[1].MinBpm.Should().Be((int)Math.Round(maxHr * 0.60)); // 60%
        zones[1].MaxBpm.Should().Be((int)Math.Round(maxHr * 0.70)); // 70%
        zones[2].MinBpm.Should().Be((int)Math.Round(maxHr * 0.70)); // 70%
        zones[2].MaxBpm.Should().Be((int)Math.Round(maxHr * 0.80)); // 80%
        zones[3].MinBpm.Should().Be((int)Math.Round(maxHr * 0.80)); // 80%
        zones[3].MaxBpm.Should().Be((int)Math.Round(maxHr * 0.90)); // 90%
        zones[4].MinBpm.Should().Be((int)Math.Round(maxHr * 0.90)); // 90%
        zones[4].MaxBpm.Should().Be((int)Math.Round(maxHr * 1.00)); // 100%
    }

    #endregion

    #region CalculateZonesFromKarvonen Tests

    [Fact]
    public void CalculateZonesFromKarvonen_WithValidInputs_ReturnsCorrectZones()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 60;
        var hrReserve = maxHr - restingHr; // 140

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: (140 * 0.50) + 60 = 70 + 60 = 130, (140 * 0.60) + 60 = 84 + 60 = 144
        zones[0].MinBpm.Should().Be(130);
        zones[0].MaxBpm.Should().Be(144);
        
        // Zone 2: (140 * 0.60) + 60 = 84 + 60 = 144, (140 * 0.70) + 60 = 98 + 60 = 158
        zones[1].MinBpm.Should().Be(144);
        zones[1].MaxBpm.Should().Be(158);
        
        // Zone 3: (140 * 0.70) + 60 = 98 + 60 = 158, (140 * 0.80) + 60 = 112 + 60 = 172
        zones[2].MinBpm.Should().Be(158);
        zones[2].MaxBpm.Should().Be(172);
        
        // Zone 4: (140 * 0.80) + 60 = 112 + 60 = 172, (140 * 0.90) + 60 = 126 + 60 = 186
        zones[3].MinBpm.Should().Be(172);
        zones[3].MaxBpm.Should().Be(186);
        
        // Zone 5: (140 * 0.90) + 60 = 126 + 60 = 186, (140 * 1.00) + 60 = 140 + 60 = 200
        zones[4].MinBpm.Should().Be(186);
        zones[4].MaxBpm.Should().Be(200);
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithEdgeCaseMinMaxHr_ReturnsCorrectZones()
    {
        // Arrange
        var maxHr = 60;
        var restingHr = 30;
        var hrReserve = maxHr - restingHr; // 30

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: (30 * 0.50) + 30 = 15 + 30 = 45, (30 * 0.60) + 30 = 18 + 30 = 48
        zones[0].MinBpm.Should().Be(45);
        zones[0].MaxBpm.Should().Be(48);
        
        // Zone 5: (30 * 0.90) + 30 = 27 + 30 = 57, (30 * 1.00) + 30 = 30 + 30 = 60
        zones[4].MinBpm.Should().Be(57);
        zones[4].MaxBpm.Should().Be(60);
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithEdgeCaseMaxMaxHr_ReturnsCorrectZones()
    {
        // Arrange
        var maxHr = 250;
        var restingHr = 50;
        var hrReserve = maxHr - restingHr; // 200

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: (200 * 0.50) + 50 = 100 + 50 = 150, (200 * 0.60) + 50 = 120 + 50 = 170
        zones[0].MinBpm.Should().Be(150);
        zones[0].MaxBpm.Should().Be(170);
        
        // Zone 5: (200 * 0.90) + 50 = 180 + 50 = 230, (200 * 1.00) + 50 = 200 + 50 = 250
        zones[4].MinBpm.Should().Be(230);
        zones[4].MaxBpm.Should().Be(250);
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithEdgeCaseMinRestingHr_ReturnsCorrectZones()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 30;
        var hrReserve = maxHr - restingHr; // 170

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: (170 * 0.50) + 30 = 85 + 30 = 115, (170 * 0.60) + 30 = 102 + 30 = 132
        zones[0].MinBpm.Should().Be(115);
        zones[0].MaxBpm.Should().Be(132);
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithEdgeCaseMaxRestingHr_ReturnsCorrectZones()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 120;
        var hrReserve = maxHr - restingHr; // 80

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        zones.Should().NotBeNull();
        zones.Should().HaveCount(5);
        
        // Zone 1: (80 * 0.50) + 120 = 40 + 120 = 160, (80 * 0.60) + 120 = 48 + 120 = 168
        zones[0].MinBpm.Should().Be(160);
        zones[0].MaxBpm.Should().Be(168);
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithInvalidMaxHr59_ThrowsArgumentException()
    {
        // Arrange
        var maxHr = 59;
        var restingHr = 60;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Max heart rate must be between 60 and 250 BPM*")
            .WithParameterName("maxHeartRate");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithInvalidMaxHr251_ThrowsArgumentException()
    {
        // Arrange
        var maxHr = 251;
        var restingHr = 60;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Max heart rate must be between 60 and 250 BPM*")
            .WithParameterName("maxHeartRate");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithInvalidRestingHr29_ThrowsArgumentException()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 29;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Resting heart rate must be between 30 and 120 BPM*")
            .WithParameterName("restingHeartRate");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithInvalidRestingHr121_ThrowsArgumentException()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 121;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Resting heart rate must be between 30 and 120 BPM*")
            .WithParameterName("restingHeartRate");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithMaxHrEqualToRestingHr_ThrowsArgumentException()
    {
        // Arrange
        // Use values that pass range checks (both within valid ranges) but fail max > resting check
        var maxHr = 100;
        var restingHr = 100;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Max heart rate must be greater than resting heart rate*");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_WithMaxHrLessThanRestingHr_ThrowsArgumentException()
    {
        // Arrange
        // Use values that pass range checks (both within valid ranges) but fail max > resting check
        var maxHr = 100;
        var restingHr = 101;

        // Act
        var act = () => _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Max heart rate must be greater than resting heart rate*");
    }

    [Fact]
    public void CalculateZonesFromKarvonen_VerifiesZoneCalculationFormula()
    {
        // Arrange
        var maxHr = 200;
        var restingHr = 60;
        var hrReserve = maxHr - restingHr; // 140

        // Act
        var zones = _service.CalculateZonesFromKarvonen(maxHr, restingHr);

        // Assert
        // Verify zones calculated correctly: (HRR * percent) + resting HR
        zones[0].MinBpm.Should().Be((int)Math.Round((hrReserve * 0.50) + restingHr));
        zones[0].MaxBpm.Should().Be((int)Math.Round((hrReserve * 0.60) + restingHr));
        zones[1].MinBpm.Should().Be((int)Math.Round((hrReserve * 0.60) + restingHr));
        zones[1].MaxBpm.Should().Be((int)Math.Round((hrReserve * 0.70) + restingHr));
        zones[2].MinBpm.Should().Be((int)Math.Round((hrReserve * 0.70) + restingHr));
        zones[2].MaxBpm.Should().Be((int)Math.Round((hrReserve * 0.80) + restingHr));
        zones[3].MinBpm.Should().Be((int)Math.Round((hrReserve * 0.80) + restingHr));
        zones[3].MaxBpm.Should().Be((int)Math.Round((hrReserve * 0.90) + restingHr));
        zones[4].MinBpm.Should().Be((int)Math.Round((hrReserve * 0.90) + restingHr));
        zones[4].MaxBpm.Should().Be((int)Math.Round((hrReserve * 1.00) + restingHr));
    }

    #endregion

    #region ValidateCustomZones Tests

    [Fact]
    public void ValidateCustomZones_WithValidZones_ReturnsValid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },   // Zone 1
            new() { MinBpm = 114, MaxBpm = 133 },  // Zone 2
            new() { MinBpm = 133, MaxBpm = 152 },  // Zone 3
            new() { MinBpm = 152, MaxBpm = 171 },  // Zone 4
            new() { MinBpm = 171, MaxBpm = 190 }  // Zone 5
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ValidateCustomZones_WithNullZones_ReturnsInvalid()
    {
        // Arrange
        List<HeartRateZone>? zones = null;

        // Act
        var result = _service.ValidateCustomZones(zones!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Exactly 5 zones are required");
    }

    [Fact]
    public void ValidateCustomZones_WithWrongCount4_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 114, MaxBpm = 133 },
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Exactly 5 zones are required");
    }

    [Fact]
    public void ValidateCustomZones_WithWrongCount6_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 114, MaxBpm = 133 },
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 },
            new() { MinBpm = 190, MaxBpm = 200 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Exactly 5 zones are required");
    }

    [Fact]
    public void ValidateCustomZones_WithNullZoneInList_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            null!,
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("All zones must be defined");
    }

    [Fact]
    public void ValidateCustomZones_WithMinGreaterThanOrEqualToMax_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 120, MaxBpm = 120 }, // min == max
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 2: Minimum BPM must be less than maximum BPM");
    }

    [Fact]
    public void ValidateCustomZones_WithMinGreaterThanMax_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 140, MaxBpm = 130 }, // min > max
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 2: Minimum BPM must be less than maximum BPM");
    }

    [Fact]
    public void ValidateCustomZones_WithBpmBelow30_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 25, MaxBpm = 114 }, // min < 30
            new() { MinBpm = 114, MaxBpm = 133 },
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 1: BPM values must be between 30 and 250");
    }

    [Fact]
    public void ValidateCustomZones_WithBpmAbove250_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 114, MaxBpm = 133 },
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 255 } // max > 250
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 5: BPM values must be between 30 and 250");
    }

    [Fact]
    public void ValidateCustomZones_WithOverlappingZones_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 110, MaxBpm = 133 }, // overlaps with zone 1 (110 < 114)
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 1 and Zone 2 overlap or are not in ascending order");
    }

    [Fact]
    public void ValidateCustomZones_WithGapsBetweenZones_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 120, MaxBpm = 133 }, // gap between zone 1 (ends at 114) and zone 2 (starts at 120)
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 1 and Zone 2 have a gap between them");
    }

    [Fact]
    public void ValidateCustomZones_WithNonAscendingOrder_ReturnsInvalid()
    {
        // Arrange
        var zones = new List<HeartRateZone>
        {
            new() { MinBpm = 95, MaxBpm = 114 },
            new() { MinBpm = 80, MaxBpm = 100 }, // zone 2 starts before zone 1 ends
            new() { MinBpm = 133, MaxBpm = 152 },
            new() { MinBpm = 152, MaxBpm = 171 },
            new() { MinBpm = 171, MaxBpm = 190 }
        };

        // Act
        var result = _service.ValidateCustomZones(zones);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Zone 1 and Zone 2 overlap or are not in ascending order");
    }

    #endregion
}

