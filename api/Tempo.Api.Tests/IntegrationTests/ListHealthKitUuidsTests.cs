using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for GET /workouts/healthkit-uuids
/// </summary>
[Collection("Integration Tests")]
public class ListHealthKitUuidsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ListHealthKitUuidsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
    }

    [Fact]
    public async Task ListHealthKitUuids_Returns401_WhenNotAuthenticated()
    {
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/workouts/healthkit-uuids");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListHealthKitUuids_ReturnsEmptyArray_WhenNoHealthKitRows()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedWorkoutAsync(db, name: "GPX only");
        }

        var response = await client.GetAsync("/workouts/healthkit-uuids");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthKitUuidsResponse>();
        result.Should().NotBeNull();
        result!.Uuids.Should().BeEmpty();
    }

    [Fact]
    public async Task ListHealthKitUuids_ReturnsOnlyNonNullUuids()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);

        var uuid1 = Guid.Parse("A1B2C3D4-E5F6-7890-ABCD-EF1234567890");
        var uuid2 = Guid.Parse("B2C3D4E5-F6A7-8901-BCDE-F12345678901");

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();

            var stamped1 = await TestDataSeeder.SeedWorkoutAsync(db, name: "HK 1");
            stamped1.HealthKitUuid = uuid1;

            await TestDataSeeder.SeedWorkoutAsync(db, name: "GPX only");

            var stamped2 = await TestDataSeeder.SeedWorkoutAsync(db, name: "HK 2");
            stamped2.HealthKitUuid = uuid2;

            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/workouts/healthkit-uuids");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthKitUuidsResponse>();
        result.Should().NotBeNull();
        result!.Uuids.Should().BeEquivalentTo(new[] { uuid1, uuid2 });
    }

    private sealed class HealthKitUuidsResponse
    {
        [JsonPropertyName("uuids")]
        public List<Guid> Uuids { get; set; } = [];
    }
}
