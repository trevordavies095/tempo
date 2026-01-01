using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for SettingsEndpoints (heart rate zones)
/// </summary>
[Collection("Integration Tests")]
public class SettingsEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public SettingsEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper method to ensure database is clean before a test (but preserves test user)
    /// </summary>
    private async Task EnsureCleanDatabaseAsync()
    {
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Clear all data except users (we need the test user for authentication)
            await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        }
    }

    #region GetHeartRateZones Tests

    [Fact]
    public async Task GetHeartRateZones_WithNoSettings_ReturnsDefaultAgeBasedZones()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        // Act
        var response = await client.GetAsync("/settings/heart-rate-zones");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.Age.Should().Be(30);
        result.MaxHeartRateBpm.Should().Be(190); // 220 - 30
        result.RestingHeartRateBpm.Should().BeNull();
        result.Zones.Should().NotBeNull();
        result.Zones.Should().HaveCount(5);
        
        // Verify zone boundaries match expected percentages for age 30 (max HR 190)
        result.Zones[0].ZoneNumber.Should().Be(1);
        result.Zones[0].MinBpm.Should().Be(95); // 50% of 190
        result.Zones[0].MaxBpm.Should().Be(114); // 60% of 190
        result.Zones[4].ZoneNumber.Should().Be(5);
        result.Zones[4].MinBpm.Should().Be(171); // 90% of 190
        result.Zones[4].MaxBpm.Should().Be(190); // 100% of 190
    }

    [Fact]
    public async Task GetHeartRateZones_WithExistingAgeBasedSettings_ReturnsCorrectZones()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db, age: 35);
        }

        // Act
        var response = await client.GetAsync("/settings/heart-rate-zones");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.Age.Should().Be(35);
        result.MaxHeartRateBpm.Should().BeNull(); // AgeBased doesn't store maxHeartRateBpm in settings
        result.RestingHeartRateBpm.Should().BeNull();
        result.Zones.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetHeartRateZones_WithExistingKarvonenSettings_ReturnsCorrectZones()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var settings = new UserSettings
            {
                CalculationMethod = HeartRateCalculationMethod.Karvonen,
                MaxHeartRateBpm = 200,
                RestingHeartRateBpm = 60,
                Zone1MinBpm = 130,
                Zone1MaxBpm = 144,
                Zone2MinBpm = 144,
                Zone2MaxBpm = 158,
                Zone3MinBpm = 158,
                Zone3MaxBpm = 172,
                Zone4MinBpm = 172,
                Zone4MaxBpm = 186,
                Zone5MinBpm = 186,
                Zone5MaxBpm = 200,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/settings/heart-rate-zones");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("Karvonen");
        result.MaxHeartRateBpm.Should().Be(200);
        result.RestingHeartRateBpm.Should().Be(60);
        result.Age.Should().BeNull();
        result.Zones.Should().HaveCount(5);
        result.Zones[0].MinBpm.Should().Be(130);
        result.Zones[4].MaxBpm.Should().Be(200);
    }

    [Fact]
    public async Task GetHeartRateZones_WithExistingCustomSettings_ReturnsCorrectZones()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var settings = new UserSettings
            {
                CalculationMethod = HeartRateCalculationMethod.Custom,
                Zone1MinBpm = 100,
                Zone1MaxBpm = 120,
                Zone2MinBpm = 120,
                Zone2MaxBpm = 140,
                Zone3MinBpm = 140,
                Zone3MaxBpm = 160,
                Zone4MinBpm = 160,
                Zone4MaxBpm = 180,
                Zone5MinBpm = 180,
                Zone5MaxBpm = 200,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.UserSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync("/settings/heart-rate-zones");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("Custom");
        result.Zones.Should().HaveCount(5);
        result.Zones[0].MinBpm.Should().Be(100);
        result.Zones[0].MaxBpm.Should().Be(120);
        result.Zones[4].MinBpm.Should().Be(180);
        result.Zones[4].MaxBpm.Should().Be(200);
    }

    #endregion

    #region UpdateHeartRateZones Tests - Validation Failures

    [Fact]
    public async Task UpdateHeartRateZones_WithAgeBasedMissingAge_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "AgeBased"
            // age is missing
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Age is required");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithKarvonenMissingMaxHr_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Karvonen",
            restingHeartRateBpm = 60
            // maxHeartRateBpm is missing
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Max heart rate and resting heart rate are required");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithKarvonenMissingRestingHr_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Karvonen",
            maxHeartRateBpm = 200
            // restingHeartRateBpm is missing
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Max heart rate and resting heart rate are required");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithCustomNullZones_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Custom",
            zones = (List<object>?)null
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Exactly 5 zones are required");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithCustomWrongZoneCount_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Custom",
            zones = new[]
            {
                new { minBpm = 95, maxBpm = 114 },
                new { minBpm = 114, maxBpm = 133 },
                new { minBpm = 133, maxBpm = 152 },
                new { minBpm = 152, maxBpm = 171 }
                // Only 4 zones
            }
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Exactly 5 zones are required");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithCustomOverlappingZones_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Custom",
            zones = new[]
            {
                new { minBpm = 95, maxBpm = 114 },
                new { minBpm = 110, maxBpm = 133 }, // Overlaps with zone 1
                new { minBpm = 133, maxBpm = 152 },
                new { minBpm = 152, maxBpm = 171 },
                new { minBpm = 171, maxBpm = 190 }
            }
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("overlap");
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithInvalidCalculationMethod_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "InvalidMethod"
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadFromJsonAsync<ErrorResponse>();
        error.Should().NotBeNull();
        error!.Error.Should().Contain("Invalid calculation method");
    }

    #endregion

    #region UpdateHeartRateZones Tests - Successful Updates

    [Fact]
    public async Task UpdateHeartRateZones_WithAgeBased_CreatesNewSettings()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "AgeBased",
            age = 30
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.Age.Should().Be(30);
        result.MaxHeartRateBpm.Should().BeNull(); // AgeBased doesn't store maxHeartRateBpm in settings
        result.IsFirstTimeSetup.Should().BeTrue();
        result.Zones.Should().HaveCount(5);
        
        // Verify settings persisted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            settings.Should().NotBeNull();
            settings!.CalculationMethod.Should().Be(HeartRateCalculationMethod.AgeBased);
            settings.Age.Should().Be(30);
        }
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithAgeBased_UpdatesExistingSettings()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Create existing settings
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserSettingsAsync(db, age: 25);
        }
        
        var request = new
        {
            calculationMethod = "AgeBased",
            age = 35
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.Age.Should().Be(35);
        result.MaxHeartRateBpm.Should().BeNull(); // AgeBased doesn't store maxHeartRateBpm in settings
        result.IsFirstTimeSetup.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithKarvonen_CreatesSettings()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Karvonen",
            maxHeartRateBpm = 200,
            restingHeartRateBpm = 60
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("Karvonen");
        result.MaxHeartRateBpm.Should().Be(200);
        result.RestingHeartRateBpm.Should().Be(60);
        result.Zones.Should().HaveCount(5);
        
        // Verify zones calculated correctly using Karvonen formula
        // HRR = 200 - 60 = 140
        // Zone 1: (140 * 0.50) + 60 = 130, (140 * 0.60) + 60 = 144
        result.Zones[0].MinBpm.Should().Be(130);
        result.Zones[0].MaxBpm.Should().Be(144);
    }

    [Fact]
    public async Task UpdateHeartRateZones_WithCustom_CreatesSettings()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        var request = new
        {
            calculationMethod = "Custom",
            zones = new[]
            {
                new { minBpm = 100, maxBpm = 120 },
                new { minBpm = 120, maxBpm = 140 },
                new { minBpm = 140, maxBpm = 160 },
                new { minBpm = 160, maxBpm = 180 },
                new { minBpm = 180, maxBpm = 200 }
            }
        };

        // Act
        var response = await client.PutAsJsonAsync("/settings/heart-rate-zones", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("Custom");
        result.Zones.Should().HaveCount(5);
        result.Zones[0].MinBpm.Should().Be(100);
        result.Zones[0].MaxBpm.Should().Be(120);
        result.Zones[4].MinBpm.Should().Be(180);
        result.Zones[4].MaxBpm.Should().Be(200);
        
        // Verify settings can be retrieved
        var getResponse = await client.GetAsync("/settings/heart-rate-zones");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var getResult = await getResponse.Content.ReadFromJsonAsync<HeartRateZonesResponse>();
        getResult!.CalculationMethod.Should().Be("Custom");
    }

    #endregion

    #region UpdateHeartRateZonesWithRecalc Tests

    [Fact]
    public async Task UpdateHeartRateZonesWithRecalc_WithRecalculateTrue_UpdatesZonesAndRecalculatesWorkouts()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Create workouts with HR data
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Create workout with time series HR data
            var workout1 = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
            await TestDataSeeder.SeedWorkoutWithTimeSeriesAsync(db, workout1, includeHeartRate: true);
            
            // Create workout with avg HR
            var workout2 = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
            workout2.AvgHeartRateBpm = 150;
            await db.SaveChangesAsync();
        }
        
        var request = new
        {
            calculationMethod = "AgeBased",
            age = 30,
            recalculateExisting = true
        };

        // Act
        var response = await client.PostAsJsonAsync("/settings/heart-rate-zones/update-with-recalc", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesWithRecalcResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.RecalculatedCount.Should().BeGreaterThan(0);
        result.RecalculatedErrorCount.Should().Be(0);
        
        // Verify workouts have updated RelativeEffort
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workouts = await db.Workouts.Where(w => w.AvgHeartRateBpm != null || 
                db.WorkoutTimeSeries.Any(ts => ts.WorkoutId == w.Id && ts.HeartRateBpm != null))
                .ToListAsync();
            workouts.Should().NotBeEmpty();
            workouts.All(w => w.RelativeEffort.HasValue).Should().BeTrue();
        }
    }

    [Fact]
    public async Task UpdateHeartRateZonesWithRecalc_WithRecalculateTrueNoQualifyingWorkouts_UpdatesZonesWithZeroCount()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Create workout without HR data
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
        }
        
        var request = new
        {
            calculationMethod = "AgeBased",
            age = 30,
            recalculateExisting = true
        };

        // Act
        var response = await client.PostAsJsonAsync("/settings/heart-rate-zones/update-with-recalc", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesWithRecalcResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.RecalculatedCount.Should().BeNull(); // null when no qualifying workouts (not 0)
        result.RecalculatedErrorCount.Should().BeNull();
        
        // Verify zones are still updated
        var getResponse = await client.GetAsync("/settings/heart-rate-zones");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UpdateHeartRateZonesWithRecalc_WithRecalculateFalse_UpdatesZonesOnly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        // Create workout with HR data
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await TestDataSeeder.SeedWorkoutAsync(db, durationS: 1800);
            workout.AvgHeartRateBpm = 150;
            await db.SaveChangesAsync();
        }
        
        var request = new
        {
            calculationMethod = "AgeBased",
            age = 30,
            recalculateExisting = false
        };

        // Act
        var response = await client.PostAsJsonAsync("/settings/heart-rate-zones/update-with-recalc", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<UpdateHeartRateZonesWithRecalcResponse>();
        result.Should().NotBeNull();
        result!.CalculationMethod.Should().Be("AgeBased");
        result.RecalculatedCount.Should().BeNull();
        result.RecalculatedErrorCount.Should().BeNull();
        
        // Verify zones are updated
        var getResponse = await client.GetAsync("/settings/heart-rate-zones");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Response Models

    private class HeartRateZonesResponse
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<ZoneResponse> Zones { get; set; } = new();
    }

    private class ZoneResponse
    {
        public int ZoneNumber { get; set; }
        public int MinBpm { get; set; }
        public int MaxBpm { get; set; }
    }

    private class UpdateHeartRateZonesResponse
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<ZoneResponse> Zones { get; set; } = new();
        public bool IsFirstTimeSetup { get; set; }
    }

    private class UpdateHeartRateZonesWithRecalcResponse
    {
        public string CalculationMethod { get; set; } = string.Empty;
        public int? Age { get; set; }
        public int? RestingHeartRateBpm { get; set; }
        public int? MaxHeartRateBpm { get; set; }
        public List<ZoneResponse> Zones { get; set; } = new();
        public bool IsFirstTimeSetup { get; set; }
        public int? RecalculatedCount { get; set; }
        public int? RecalculatedErrorCount { get; set; }
    }

    private class ErrorResponse
    {
        public string Error { get; set; } = string.Empty;
    }

    #endregion
}

