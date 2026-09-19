using Microsoft.AspNetCore.Mvc;
using Tempo.Api.Logging;
using Tempo.Api.Models;

namespace Tempo.Api.Endpoints;

public static class VersionEndpoints
{
    /// <summary>
    /// Get version and support-snapshot information
    /// </summary>
    /// <returns>Returns version, build metadata, logging profile, image tags, and a copyable snapshot</returns>
    /// <remarks>
    /// Public identity endpoint (no authentication). Version fields come from Docker bake-in
    /// environment variables or the VERSION file. Image tags come from TEMPO_API_IMAGE /
    /// TEMPO_FRONTEND_IMAGE when set. Undetermined fields are "unknown". Does not touch Postgres
    /// and never includes secrets.
    /// </remarks>
    private static IResult GetVersion(
        [FromServices] LoggingProfileKind loggingProfile,
        IHostEnvironment hostEnvironment)
    {
        // Try to get version from environment variable (set during Docker build)
        var version = Environment.GetEnvironmentVariable("TEMPO_VERSION") ?? "unknown";
        var buildDate = Environment.GetEnvironmentVariable("TEMPO_BUILD_DATE") ?? "unknown";
        var gitCommit = Environment.GetEnvironmentVariable("TEMPO_GIT_COMMIT") ?? "unknown";

        // If version is still unknown, try to read from VERSION file
        if (version == "unknown")
        {
            try
            {
                // Try current directory first (for published output with VERSION file copied)
                var versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "VERSION");
                if (File.Exists(versionFilePath))
                {
                    version = File.ReadAllText(versionFilePath).Trim();
                }
                else
                {
                    // Try repository root (for development)
                    versionFilePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "VERSION");
                    if (File.Exists(versionFilePath))
                    {
                        version = File.ReadAllText(versionFilePath).Trim();
                    }
                }
            }
            catch
            {
                // If file reading fails, keep "unknown"
            }
        }

        var info = new VersionInfo
        {
            Version = version,
            BuildDate = buildDate,
            GitCommit = gitCommit,
            LogProfile = LoggingProfile.ToConfigName(loggingProfile),
            Environment = hostEnvironment.EnvironmentName,
            ApiImage = ReadImageEnv("TEMPO_API_IMAGE"),
            FrontendImage = ReadImageEnv("TEMPO_FRONTEND_IMAGE"),
        };
        info.ApplySnapshot();

        return Results.Ok(info);
    }

    private static string ReadImageEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();
    }

    public static void MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/version", GetVersion)
            .WithTags("Version")
            .WithName("GetVersion")
            .WithSummary("Get version and support snapshot")
            .WithDescription(
                "Public identity dump for bug reports. Returns version, build date, git commit, " +
                "logging profile, environment, image tags, and a frozen snapshot text block. " +
                "Undetermined fields are \"unknown\". Does not require authentication.")
            .Produces<VersionInfo>(200);
    }
}
