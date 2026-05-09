using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

[Collection("Integration Tests")]
public class ApiKeyEndpointsTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ApiKeyEndpointsTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task EnsureCleanDatabaseAsync()
    {
        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: false);
    }

    [Fact]
    public async Task CreateApiKey_WhenAuthenticated_ReturnsKeyOnceAndPersistsHash()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");

        var response = await client.PostAsJsonAsync("/auth/api-keys", new { label = "CLI" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        body.Should().NotBeNull();
        body!.Key.Should().StartWith(ApiKeyService.KeyMaterialPrefix);
        body.Label.Should().Be("CLI");
        body.KeyPrefix.Should().NotBeNullOrEmpty();

        using var scope = _factory.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
        var row = await db.ApiKeys.AsNoTracking().SingleAsync();
        row.KeyHash.Should().NotBe(body.Key);
        row.KeyPrefix.Should().Be(body.KeyPrefix);
    }

    [Fact]
    public async Task ListApiKeys_DoesNotExposeFullKey()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");
        var createResp = await client.PostAsJsonAsync("/auth/api-keys", new { label = "a" });
        createResp.EnsureSuccessStatusCode();

        var listResp = await client.GetAsync("/auth/api-keys");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await listResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var arr = doc.RootElement;
        arr.GetArrayLength().Should().Be(1);
        var item = arr[0];
        item.TryGetProperty("key", out _).Should().BeFalse("list must never include the secret");
        item.GetProperty("keyPrefix").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RevokeApiKey_SetsRevokedAt_AndListReflects()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");
        var createResp = await client.PostAsJsonAsync("/auth/api-keys", new { label = "revoke-me" });
        var created = await createResp.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        created.Should().NotBeNull();

        var delResp = await client.DeleteAsync($"/auth/api-keys/{created!.Id}");
        delResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResp = await client.GetAsync("/auth/api-keys");
        listResp.EnsureSuccessStatusCode();
        var list = await listResp.Content.ReadFromJsonAsync<List<ApiKeyListItemResponse>>();
        list.Should().NotBeNull();
        list!.Should().ContainSingle();
        list[0].RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateApiKey_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/api-keys", new { label = "x" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListApiKeys_WhenUnauthenticated_ReturnsUnauthorized()
    {
        await EnsureCleanDatabaseAsync();
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/auth/api-keys");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokeApiKey_WhenNotOwner_ReturnsNotFound()
    {
        await EnsureCleanDatabaseAsync();
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            await TestDataSeeder.SeedUserAsync(db, "alice", "Pass123!");
            await TestDataSeeder.SeedUserAsync(db, "bob", "Pass123!");
        }

        var alice = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");
        var createResp = await alice.PostAsJsonAsync("/auth/api-keys", new { label = "owned-by-alice" });
        var created = await createResp.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var bob = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "bob", "Pass123!");
        var delResp = await bob.DeleteAsync($"/auth/api-keys/{created!.Id}");

        delResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateApiKey_WithLabelTooLong_ReturnsBadRequest()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");

        var response = await client.PostAsJsonAsync("/auth/api-keys", new { label = new string('x', 201) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokeApiKey_WhenAlreadyRevoked_ReturnsNoContent()
    {
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory, "alice", "Pass123!");
        var createResp = await client.PostAsJsonAsync("/auth/api-keys", new { });
        var created = await createResp.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        var first = await client.DeleteAsync($"/auth/api-keys/{created!.Id}");
        first.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var second = await client.DeleteAsync($"/auth/api-keys/{created.Id}");
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private sealed class CreateApiKeyResponse
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
        public string Key { get; set; } = string.Empty;
        public string KeyPrefix { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    private sealed class ApiKeyListItemResponse
    {
        public Guid Id { get; set; }
        public string? Label { get; set; }
        public string KeyPrefix { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? RevokedAt { get; set; }
    }
}
