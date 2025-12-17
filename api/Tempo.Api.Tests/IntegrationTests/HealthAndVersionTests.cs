using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for health and version endpoints
/// </summary>
public class HealthAndVersionTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

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
    public async Task GetVersion_ReturnsVersionFromEnvironmentVariables_WhenSet()
    {
        // Arrange - save original environment variables
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");

        try
        {
            // Set environment variables
            const string testVersion = "1.2.3-test";
            const string testBuildDate = "2024-01-15T10:30:00Z";
            const string testGitCommit = "abc123def456";
            
            Environment.SetEnvironmentVariable("TEMPO_VERSION", testVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", testBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", testGitCommit);

            // Create a new factory with the environment variables set
            using var factory = new TempoWebApplicationFactory();
            var client = factory.CreateClient();

            // Act
            var response = await client.GetAsync("/version");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
            result.Should().NotBeNull();
            result!.Version.Should().Be(testVersion);
            result.BuildDate.Should().Be(testBuildDate);
            result.GitCommit.Should().Be(testGitCommit);
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
        }
    }

    [Fact]
    public async Task GetVersion_ReturnsVersionFromFile_WhenEnvVarsNotSet()
    {
        // Arrange - save original environment variables
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");

        try
        {
            // Clear environment variables
            Environment.SetEnvironmentVariable("TEMPO_VERSION", null);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", null);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", null);

            // Use the existing VERSION file in the repository root
            // The VersionEndpoints tries to read from current directory first, then from repo root
            // Since we can't easily change the current directory in a test, we'll rely on the
            // repository root path that VersionEndpoints uses as fallback
            var repoRoot = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..");
            var repoRootPath = Path.GetFullPath(repoRoot);
            var versionFilePath = Path.Combine(repoRootPath, "VERSION");
            
            // Check if VERSION file exists (it should in the repo)
            if (!File.Exists(versionFilePath))
            {
                // If it doesn't exist, create a temporary one for testing
                const string testVersion = "2.1.0-test-file";
                await File.WriteAllTextAsync(versionFilePath, testVersion);
                
                try
                {
                    using var factory = new TempoWebApplicationFactory();
                    var client = factory.CreateClient();

                    // Act
                    var response = await client.GetAsync("/version");

                    // Assert
                    response.StatusCode.Should().Be(HttpStatusCode.OK);
                    var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                    result.Should().NotBeNull();
                    // The version should be read from the file (either the repo VERSION or our test file)
                    result!.Version.Should().NotBe("unknown");
                    result.BuildDate.Should().Be("unknown");
                    result.GitCommit.Should().Be("unknown");
                }
                finally
                {
                    // Clean up test file if we created it
                    if (File.Exists(versionFilePath))
                    {
                        File.Delete(versionFilePath);
                    }
                }
            }
            else
            {
                // VERSION file exists - test that it's read correctly
                var expectedVersion = (await File.ReadAllTextAsync(versionFilePath)).Trim();
                
                using var factory = new TempoWebApplicationFactory();
                var client = factory.CreateClient();

                // Act
                var response = await client.GetAsync("/version");

                // Assert
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                result.Should().NotBeNull();
                result!.Version.Should().Be(expectedVersion);
                result.BuildDate.Should().Be("unknown");
                result.GitCommit.Should().Be("unknown");
            }
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
        }
    }

    [Fact]
    public async Task GetVersion_ReturnsUnknown_WhenNeitherEnvVarsNorFileExist()
    {
        // Arrange - save original environment variables
        var originalVersion = Environment.GetEnvironmentVariable("TEMPO_VERSION");
        var originalBuildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE");
        var originalGitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT");

        try
        {
            // Clear environment variables
            Environment.SetEnvironmentVariable("TEMPO_VERSION", null);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", null);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", null);

            // Create a temporary directory without VERSION file
            var testOutputDir = Path.Combine(Path.GetTempPath(), $"tempo-test-{Guid.NewGuid()}");
            Directory.CreateDirectory(testOutputDir);

            try
            {
                // Create factory and change working directory to test directory (without VERSION file)
                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Testing");
                        builder.UseContentRoot(testOutputDir);
                    });
                var client = factory.CreateClient();

                // Act
                var response = await client.GetAsync("/version");

                // Assert
                response.StatusCode.Should().Be(HttpStatusCode.OK);
                var result = await response.Content.ReadFromJsonAsync<VersionResponse>();
                result.Should().NotBeNull();
                result!.Version.Should().Be("unknown");
                result.BuildDate.Should().Be("unknown");
                result.GitCommit.Should().Be("unknown");
            }
            finally
            {
                // Clean up test directory
                if (Directory.Exists(testOutputDir))
                {
                    Directory.Delete(testOutputDir, recursive: true);
                }
            }
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("TEMPO_VERSION", originalVersion);
            Environment.SetEnvironmentVariable("TEMPO_BUILD_DATE", originalBuildDate);
            Environment.SetEnvironmentVariable("TEMPO_GIT_COMMIT", originalGitCommit);
        }
    }

    private class HealthResponse
    {
        public string Status { get; set; } = string.Empty;
    }

    private class VersionResponse
    {
        public string Version { get; set; } = string.Empty;
        public string BuildDate { get; set; } = string.Empty;
        public string GitCommit { get; set; } = string.Empty;
    }
}
