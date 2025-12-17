using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Tempo.Api.Tests;

/// <summary>
/// Unit-style tests for API startup configuration validation
/// These tests verify that Program.cs validation logic works correctly
/// 
/// Uses a test collection to ensure isolation from other tests that may set environment variables
/// </summary>
[Collection("Startup Configuration Tests")]
public class StartupConfigurationTests
{
    [Fact]
    public void Startup_ThrowsException_WhenJwtSecretKeyIsMissing()
    {
        // Arrange - save original environment variables
        var originalJwtSecret = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        try
        {
            // Force clear JWT secret key from environment BEFORE creating factory
            // This is necessary because other tests (like TempoWebApplicationFactory) may have set it
            // We need to clear it multiple times to ensure it's definitely gone
            Environment.SetEnvironmentVariable("JWT__SecretKey", null);
            Environment.SetEnvironmentVariable("JWT:SecretKey", null); // Also try with colon separator
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
            
            // Force garbage collection to ensure any cached values are cleared
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Create a temporary directory without appsettings.json
            var tempDir = Path.Combine(Path.GetTempPath(), $"tempo-test-config-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Act - try to create the application factory and access Server to trigger startup
                // This will execute Program.cs which will throw during configuration
                // Note: This test is challenging because WebApplicationFactory loads default configuration
                // sources (appsettings.json, environment variables) before ConfigureAppConfiguration runs.
                // We work around this by using a custom content root and clearing all sources.
                var act = () =>
                {
                    using var factory = new WebApplicationFactory<Program>()
                        .WithWebHostBuilder(builder =>
                        {
                            builder.UseEnvironment("Production");
                            builder.UseContentRoot(tempDir); // Use temp dir without appsettings.json
                            builder.ConfigureAppConfiguration((context, config) =>
                            {
                                // Clear ALL configuration sources including environment variables and appsettings.json
                                // This ensures JWT:SecretKey is not available from any source
                                var sourcesToRemove = config.Sources.ToList();
                                foreach (var source in sourcesToRemove)
                                {
                                    config.Sources.Remove(source);
                                }
                                
                                // Also explicitly clear environment variables for this test
                                Environment.SetEnvironmentVariable("JWT__SecretKey", null);
                                
                                // Add only what we need - explicitly exclude JWT:SecretKey
                                // The in-memory collection is added last, so it should override appsettings.json
                                // But since we cleared all sources, appsettings.json shouldn't be loaded anyway
                                config.AddInMemoryCollection(new Dictionary<string, string?>
                                {
                                    { "ConnectionStrings:DefaultConnection", "Data Source=:memory:" },
                                    // JWT:SecretKey is intentionally omitted - this should make Configuration["JWT:SecretKey"] return null
                                });
                            });
                        });
                    // Access Server to trigger application startup and Program.cs execution
                    _ = factory.Server;
                };

                // Assert
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("*JWT:SecretKey is not configured*");
            }
            finally
            {
                // Clean up temp directory
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecret);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
        }
    }

    [Fact]
    public void Startup_ThrowsException_WhenJwtSecretKeyIsPlaceholder()
    {
        // Arrange - save original environment variables
        var originalJwtSecret = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        try
        {
            // Set placeholder value
            const string placeholderValue = "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE";
            Environment.SetEnvironmentVariable("JWT__SecretKey", placeholderValue);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

            // Act - try to create the application factory with Production environment
            // This will execute Program.cs which will throw during configuration
            var act = () =>
            {
                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Production");
                    });
                // Access Server to trigger application startup
                _ = factory.Server;
            };

            // Assert
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*must be changed from the default placeholder value*");
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecret);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
        }
    }

    [Fact]
    public void Startup_Succeeds_WhenJwtSecretKeyIsPlaceholderInTestingEnvironment()
    {
        // Arrange - save original environment variables
        var originalJwtSecret = Environment.GetEnvironmentVariable("JWT__SecretKey");
        var originalConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        var originalEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
        
        try
        {
            // Set placeholder value
            const string placeholderValue = "CHANGE_THIS_IN_PRODUCTION_USE_ENVIRONMENT_VARIABLE";
            Environment.SetEnvironmentVariable("JWT__SecretKey", placeholderValue);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=:memory:?cache=shared");
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");

            // Act - try to create the application factory with Testing environment
            // This should succeed because validation is skipped in Testing environment
            var act = () =>
            {
                using var factory = new WebApplicationFactory<Program>()
                    .WithWebHostBuilder(builder =>
                    {
                        builder.UseEnvironment("Testing");
                    });
                // Access Server to trigger application startup
                _ = factory.Server;
            };

            // Assert - should not throw exception
            act.Should().NotThrow();
        }
        finally
        {
            // Restore original environment variables
            Environment.SetEnvironmentVariable("JWT__SecretKey", originalJwtSecret);
            Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", originalConnectionString);
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnvironment);
        }
    }
}
