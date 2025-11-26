namespace Tempo.Api.Endpoints;

public static class VersionEndpoints
{
    public static void MapVersionEndpoints(this WebApplication app)
    {
        app.MapGet("/version", () =>
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

            return Results.Ok(new
            {
                version = version,
                buildDate = buildDate,
                gitCommit = gitCommit
            });
        })
        .WithTags("Version")
        .WithName("GetVersion")
        .Produces(200);
    }
}

