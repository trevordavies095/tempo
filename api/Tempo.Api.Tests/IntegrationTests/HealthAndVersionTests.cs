using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Tempo.Api.Models;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for health and version endpoints
/// </summary>
[Collection("Integration Tests")]
public class HealthAndVersionTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    /// <summary>Matches TempoWebApplicationFactory test JWT secret (not exported).</summary>
    private const string TestJwtSecretKey = "TestSecretKeyForJWTTokenGeneration12345678901234567890";

    public HealthAndVersionTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WithHealthyStatus()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task GetReady_ReturnsOk_WhenDatabaseIsReachable()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ReadyResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("ready");
        result.Checks.Should().NotBeNull();
        result.Checks!.Database.Should().Be("ok");
    }

    [Fact]
    public async Task GetHealth_ReturnsOk_WhenDatabaseIsUnreachable()
    {
        using var factory = new UnreachablePostgresWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<HealthResponse>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("healthy");
    }

    [Fact]
    public async Task GetReady_ReturnsServiceUnavailable_WhenDatabaseIsUnreachable()
    {
        using var factory = new UnreachablePostgresWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"not_ready\"");
        body.Should().Contain("\"database\":\"fail\"");
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Host);
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Username);
        body.Should().NotContain(UnreachablePostgresWebApplicationFactory.Password);

        var result = System.Text.Json.JsonSerializer.Deserialize<ReadyResponse>(
            body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result.Should().NotBeNull();
        result!.Status.Should().Be("not_ready");
        result.Checks!.Database.Should().Be("fail");
    }

    [Fact]
    public async Task GetVersion_ReturnsVersionFromEnvironmentVariables_WhenSet()
    {
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");
        var originalApiImage = Environment.GetEnvironmentVariable("TEMPO_API_IMAGE");
        var originalFrontendImage = Environment.GetEnvironmentVariable("TEMPO_FRONTEND_IMAGE");

        try
        {
            const string testVersion = "1.2.3-test";
            const string testBuildDate = "2024-01-15T10:30:00Z";
            const string testGitCommit = "abc123def456";

            Environment.SetEnvironmentVariable("TEMPO_VERSION", testVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", testBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", testGitCommit);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", null);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", null);

            using var factory = new TempoWebApplicationFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.Version.Should().Be(testVersion);
            result.BuildDate.Should().Be(testBuildDate);
            result.GitCommit.Should().Be(testGitCommit);
            AssertSupportSnapshotDefaults(result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", originalApiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", originalFrontendImage);
        }
    }

    [Fact]
    public async Task GetVersion_ReturnsVersionFromFile_WhenEnvVarsNotSet()
    {
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");
        var originalApiImage = Environment.GetEnvironmentVariable("TEMPO_API_IMAGE");
        var originalFrontendImage = Environment.GetEnvironmentVariable("TEMPO_FRONTEND_IMAGE");

        try
        {
            Environment.SetEnvironmentVariable("TEMPO_VERSION", null);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", null);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", null);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", null);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", null);

            var repoRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
            var repoRootPath = Path.GetFullPath(repoRoot);
            var versionFilePath = Path.Combine(repoRootPath, "VERSION");

            if (!File.Exists(versionFilePath))
            {
                const string testVersion = "2.1.0-test-file";
                await File.WriteAllTextAsync(versionFilePath, testVersion);

                try
                {
                    using var factory = new TempoWebApplicationFactory();
                    var client = factory.CreateClient();

                    var response = await client.GetAsync("/version");

                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                    result.Should().NotBeNull();
                    result!.Version.Should().NotBe("unknown");
                    result.BuildDate.Should().Be("unknown");
                    result.GitCommit.Should().Be("unknown");
                    AssertSupportSnapshotDefaults(result);
                }
                finally
                {
                    if (File.Exists(versionFilePath))
                    {
                        File.Delete(versionFilePath);
                    }
                }
            }
            else
            {
                var expectedVersion = (await File.ReadAllTextAsync(versionFilePath)).Trim();

                using var factory = new TempoWebApplicationFactory();
                var client = factory.CreateClient();

                var response = await client.GetAsync("/version");

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                result.Should().NotBeNull();
                result!.Version.Should().Be(expectedVersion);
                result.BuildDate.Should().Be("unknown");
                result.GitCommit.Should().Be("unknown");
                AssertSupportSnapshotDefaults(result);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", originalApiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", originalFrontendImage);
        }
    }

    [Fact]
    public async Task GetVersion_ReturnsUnknown_WhenNeitherEnvVarsNorFileExist()
    {
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");
        var originalApiImage = Environment.GetEnvironmentVariable("TEMPO_API_IMAGE");
        var originalFrontendImage = Environment.GetEnvironmentVariable("TEMPO_FRONTEND_IMAGE");

        try
        {
            Environment.SetEnvironmentVariable("TEMPO_VERSION", null);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", null);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", null);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", null);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", null);

            var testOutputDir = Path.Combine(Path.GetTempPath(), $"tempo-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(testOutputDir);

            try
            {
                using var factory = new TempoWebApplicationFactory()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Testing");
                        builder.UseContentRoot(testOutputDir);
                    });
                var client = factory.CreateClient();

                var response = await client.GetAsync("/version");

                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                result.Should().NotBeNull();
                result!.Version.Should().Be("unknown");
                result.BuildDate.Should().Be("unknown");
                result.GitCommit.Should().Be("unknown");
                AssertSupportSnapshotDefaults(result);
            }
            finally
            {
                if (Directory.Exists(testOutputDir))
                {
                    Directory.Delete(testOutputDir, recursive: true);
                }
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", originalApiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", originalFrontendImage);
        }
    }

    [Fact]
    public async Task GetVersion_EchoesImageEnvVars_WhenSet()
    {
        var originalApiImage = Environment.GetEnvironmentVariable("TEMPO_API_IMAGE");
        var originalFrontendImage = Environment.GetEnvironmentVariable("TEMPO_FRONTEND_IMAGE");

        try
        {
            const string apiImage = "ghcr.io/trevordavies095/tempo/api:v2.9.0";
            const string frontendImage = "ghcr.io/trevordavies095/tempo/frontend:v2.9.0";
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", apiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", frontendImage);

            using var factory = new TempoWebApplicationFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.ApiImage.Should().Be(apiImage);
            result.FrontendImage.Should().Be(frontendImage);
            result.Snapshot.Should().Be(ExpectedSnapshot(result));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", originalApiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", originalFrontendImage);
        }
    }

    [Fact]
    public async Task GetVersion_TreatsWhitespaceImageEnvAsUnknown()
    {
        var originalApiImage = Environment.GetEnvironmentVariable("TEMPO_API_IMAGE");
        var originalFrontendImage = Environment.GetEnvironmentVariable("TEMPO_FRONTEND_IMAGE");

        try
        {
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", "   ");
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", "\t");

            using var factory = new TempoWebApplicationFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.ApiImage.Should().Be("unknown");
            result.FrontendImage.Should().Be("unknown");
            result.Snapshot.Should().Be(ExpectedSnapshot(result));
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEMPO_API_IMAGE", originalApiImage);
            Environment.SetEnvironmentVariable("TEMPO_FRONTEND_IMAGE", originalFrontendImage);
        }
    }

    [Fact]
    public async Task GetVersion_ReturnsDebugLogProfile_WhenConfigured()
    {
        var originalProfile = Environment.GetEnvironmentVariable("Tempo__Logging__Profile");

        try
        {
            Environment.SetEnvironmentVariable("Tempo__Logging__Profile", "debug");

            using var factory = new TempoWebApplicationFactory();
            var client = factory.CreateClient();

            var response = await client.GetAsync("/version");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.LogProfile.Should().Be("debug");
            result.Snapshot.Should().Be(ExpectedSnapshot(result));
            result.Snapshot.Should().Contain("logProfile: debug");
        }
        finally
        {
            Environment.SetEnvironmentVariable("Tempo__Logging__Profile", originalProfile);
        }
    }

    [Fact]
    public async Task GetVersion_BodyContainsNoSecrets()
    {
        using var factory = new TempoWebApplicationFactory();
        var client = factory.CreateClient();

        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        connectionString.Should().NotBeNullOrWhiteSpace();

        var response = await client.GetAsync("/version");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(TestJwtSecretKey);
        body.Should().NotContain(connectionString!);

        var result = System.Text.Json.JsonSerializer.Deserialize<VersionResponse>(
            body,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        result.Should().NotBeNull();
        result!.Snapshot.Should().NotContain(TestJwtSecretKey);
        result.Snapshot.Should().NotContain(connectionString!);
        AssertSupportSnapshotDefaults(result);
    }

    private static void AssertSupportSnapshotDefaults(VersionResponse result)
    {
        result.LogProfile.Should().Be("standard");
        result.Environment.Should().Be("Testing");
        result.ApiImage.Should().Be("unknown");
        result.FrontendImage.Should().Be("unknown");
        result.Snapshot.Should().NotBeNullOrWhiteSpace();
        result.Snapshot.Should().Be(ExpectedSnapshot(result));
    }

    private static string ExpectedSnapshot(VersionResponse result) =>
        VersionInfo.FormatSnapshot(
            result.Version,
            result.GitCommit,
            result.BuildDate,
            result.LogProfile,
            result.Environment,
            result.ApiImage,
            result.FrontendImage);

    private class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
    }

    private class ReadyResponse
    {
        public string Status { get; set; } = string.Empty;
        public ReadyChecks? Checks { get; set; }
    }

    private class ReadyChecks
    {
        public string Database { get; set; } = string.Empty;
    }

    private class VersionResponse
    {
        public string Version { get; set; } = string.Empty;
        public string BuildDate { get; set; } = string.Empty;
        public string GitCommit { get; set; } = string.Empty;
        public string LogProfile { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public string ApiImage { get; set; } = string.Empty;
        public string FrontendImage { get; set; } = string.Empty;
        public string Snapshot { get; set; } = string.Empty;
    }
}
