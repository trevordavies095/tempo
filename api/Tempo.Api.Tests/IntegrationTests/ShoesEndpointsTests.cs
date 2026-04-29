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
/// Integration tests for ShoesEndpoints
/// </summary>
[Collection("Integration Tests")]
public class ShoesEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ShoesEndpointsTests(TempoWebApplicationFactory factory)
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

    [Fact]
    public async Task GetShoes_WhenAuthenticated_ReturnsAllShoesWithMileage()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var shoe1 = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus", initialMileage: 1000.0);
            var shoe2 = await TestDataSeeder.SeedShoeAsync(db, "Adidas", "Ultraboost");
            await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe1.Id, distanceM: 5000.0);
        }

        // Act
        var response = await client.GetAsync("/shoes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<ShoeResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result[0].brand.Should().Be("Adidas"); // Sorted by brand
        result[1].brand.Should().Be("Nike");
        result[1].totalMileage.Should().BeApproximately(6.0, 0.001); // 1km initial + 5km workout = 6km
        result.Should().OnlyContain(s => !s.isRetired);
    }

    [Fact]
    public async Task GetShoes_StatusActive_ExcludesRetiredShoes()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedShoeAsync(db, "Active", "One");
            await TestDataSeeder.SeedShoeAsync(db, "Retired", "Two", isRetired: true);
        }

        var response = await client.GetAsync("/shoes");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<ShoeResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].brand.Should().Be("Active");
    }

    [Fact]
    public async Task GetShoes_StatusRetired_ReturnsOnlyRetiredShoes()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedShoeAsync(db, "Active", "One");
            await TestDataSeeder.SeedShoeAsync(db, "Retired", "Two", isRetired: true);
        }

        var response = await client.GetAsync("/shoes?status=retired");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<ShoeResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].brand.Should().Be("Retired");
        result[0].isRetired.Should().BeTrue();
    }

    [Fact]
    public async Task GetShoes_StatusAll_ReturnsActiveAndRetired()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedShoeAsync(db, "A", "One");
            await TestDataSeeder.SeedShoeAsync(db, "B", "Two", isRetired: true);
        }

        var response = await client.GetAsync("/shoes?status=all");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<ShoeResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetShoes_InvalidStatus_ReturnsBadRequest()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var response = await client.GetAsync("/shoes?status=invalid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetShoes_WhenUnauthenticated_ReturnsUnauthorized()
    {
        // Arrange
        var client = TestHttpClientFactory.CreateUnauthenticatedClient(_factory);

        // Act
        var response = await client.GetAsync("/shoes");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateShoe_WithValidData_CreatesShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "Nike",
            model = "Pegasus 40",
            initialMileageM = 1000.0
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        result.Should().NotBeNull();
        result!.brand.Should().Be("Nike");
        result.model.Should().Be("Pegasus 40");
        result.initialMileageM.Should().Be(1000.0);
        result.id.Should().NotBeEmpty();
        result.isRetired.Should().BeFalse();
    }

    [Fact]
    public async Task CreateShoe_WithMissingBrand_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "",
            model = "Pegasus 40"
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateShoe_WithMissingModel_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "Nike",
            model = ""
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateShoe_WithLongBrand_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = new string('a', 101), // 101 characters
            model = "Pegasus 40"
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateShoe_WithLongModel_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "Nike",
            model = new string('a', 101) // 101 characters
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateShoe_WithNegativeInitialMileage_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "Nike",
            model = "Pegasus 40",
            initialMileageM = -100.0
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateShoe_WithWhitespace_TrimsValues()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "  Nike  ",
            model = "  Pegasus 40  "
        };

        // Act
        var response = await client.PostAsJsonAsync("/shoes", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        result.Should().NotBeNull();
        result!.brand.Should().Be("Nike");
        result.model.Should().Be("Pegasus 40");
    }

    [Fact]
    public async Task UpdateShoe_WithValidData_UpdatesShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 39");
        }

        var request = new
        {
            brand = "Nike",
            model = "Pegasus 40",
            initialMileageM = 2000.0
        };

        // Act
        var response = await client.PatchAsJsonAsync($"/shoes/{shoe.Id}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        result.Should().NotBeNull();
        result!.brand.Should().Be("Nike");
        result.model.Should().Be("Pegasus 40");
        result.initialMileageM.Should().Be(2000.0);
    }

    [Fact]
    public async Task UpdateShoe_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();
        var request = new
        {
            brand = "Nike",
            model = "Pegasus 40"
        };

        // Act
        var response = await client.PatchAsJsonAsync($"/shoes/{nonExistentId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateShoe_WithPartialUpdate_UpdatesOnlyProvidedFields()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 39", initialMileage: 1000.0);
        }

        var request = new
        {
            model = "Pegasus 40"
            // Only updating model, not brand or initialMileageM
        };

        // Act
        var response = await client.PatchAsJsonAsync($"/shoes/{shoe.Id}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        result.Should().NotBeNull();
        result!.brand.Should().Be("Nike"); // Unchanged
        result.model.Should().Be("Pegasus 40"); // Updated
        result.initialMileageM.Should().Be(1000.0); // Unchanged
    }

    [Fact]
    public async Task UpdateShoe_SetIsRetired_ClearsDefaultShoe()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus");
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: shoe.Id);
        }

        var response = await client.PatchAsJsonAsync($"/shoes/{shoe.Id}", new { isRetired = true });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ShoeResponse>();
        body.Should().NotBeNull();
        body!.isRetired.Should().BeTrue();

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            settings.Should().NotBeNull();
            settings!.DefaultShoeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task SetDefaultShoe_WithRetiredShoe_ReturnsBadRequest()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Old", isRetired: true);
        }

        var response = await client.PutAsJsonAsync("/settings/default-shoe", new { defaultShoeId = shoe.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateShoe_WithInvalidJson_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 39");
        }

        var invalidJson = "{ invalid json }";
        var content = new StringContent(invalidJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync($"/shoes/{shoe.Id}", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteShoe_WithNoWorkouts_DeletesShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 40");
        }

        // Act
        var response = await client.DeleteAsync($"/shoes/{shoe.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify shoe is deleted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var deletedShoe = await db.Shoes.FindAsync(shoe.Id);
            deletedShoe.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteShoe_WithWorkouts_SetsWorkoutShoeIdToNull()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 40");
            workout = await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id);
        }

        // Act
        var response = await client.DeleteAsync($"/shoes/{shoe.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify workout's ShoeId is set to null
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var updatedWorkout = await db.Workouts.FindAsync(workout.Id);
            updatedWorkout.Should().NotBeNull();
            updatedWorkout!.ShoeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteShoe_WithDefaultShoe_ClearsDefaultShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 40");
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: shoe.Id);
        }

        // Act
        var response = await client.DeleteAsync($"/shoes/{shoe.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify default shoe is cleared
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var settings = await db.UserSettings.FirstOrDefaultAsync();
            settings.Should().NotBeNull();
            settings!.DefaultShoeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteShoe_WithNonExistentId_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/shoes/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShoeMileage_WithWorkouts_ReturnsCalculatedMileage()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 40", initialMileage: 2000.0);
            await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id, distanceM: 5000.0);
            await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id, distanceM: 3000.0);
        }

        // Act
        var response = await client.GetAsync($"/shoes/{shoe.Id}/mileage");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeMileageResponse>();
        result.Should().NotBeNull();
        result!.shoeId.Should().Be(shoe.Id);
        result.totalMileage.Should().BeApproximately(10.0, 0.001); // 2km + 5km + 3km = 10km
        result.unit.Should().Be("km");
    }

    [Fact]
    public async Task GetShoeMileage_WithNonExistentShoe_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/shoes/{nonExistentId}/mileage");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetShoeMileage_RespectsUnitPreference()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Shoe shoe;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            shoe = await TestDataSeeder.SeedShoeAsync(db, "Nike", "Pegasus 40");
            await TestDataSeeder.SeedWorkoutAsync(db, shoeId: shoe.Id, distanceM: 1609.344); // 1 mile
            await TestDataSeeder.SeedUserSettingsAsync(db, unitPreference: "imperial");
        }

        // Act
        var response = await client.GetAsync($"/shoes/{shoe.Id}/mileage");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ShoeMileageResponse>();
        result.Should().NotBeNull();
        result!.totalMileage.Should().BeApproximately(1.0, 0.001); // 1 mile
        result.unit.Should().Be("miles");
    }

    [Fact]
    public async Task CreateShoe_AllowsDuplicateBrandAndModel()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var request = new
        {
            brand = "Nike",
            model = "Pegasus 40"
        };

        // Act - Create first shoe
        var response1 = await client.PostAsJsonAsync("/shoes", request);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act - Create duplicate brand+model
        var response2 = await client.PostAsJsonAsync("/shoes", request);

        // Assert - Should succeed (no uniqueness constraint)
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await response1.Content.ReadFromJsonAsync<ShoeResponse>();
        var result2 = await response2.Content.ReadFromJsonAsync<ShoeResponse>();
        result1!.id.Should().NotBe(result2!.id); // Different IDs
        result1.brand.Should().Be(result2.brand);
        result1.model.Should().Be(result2.model);
    }

    private class ShoeResponse
    {
        public Guid id { get; set; }
        public string brand { get; set; } = string.Empty;
        public string model { get; set; } = string.Empty;
        public double? initialMileageM { get; set; }
        public bool isRetired { get; set; }
        public double totalMileage { get; set; }
        public string unit { get; set; } = string.Empty;
        public DateTime createdAt { get; set; }
        public DateTime updatedAt { get; set; }
    }

    private class ShoeMileageResponse
    {
        public Guid shoeId { get; set; }
        public double totalMileage { get; set; }
        public string unit { get; set; } = string.Empty;
    }
}

