using Serilog;
using Serilog.Events;

namespace Tempo.Api.Logging;

/// <summary>
/// Named logging profiles for self-hosted Tempo. Owns Serilog minimum levels and
/// request-middleware verbosity. Not UserSettings; host config only (restart to change).
/// </summary>
public enum LoggingProfileKind
{
    Standard,
    Debug,
}

/// <summary>
/// Bootstrap helper: parse <c>Tempo:Logging:Profile</c>, apply Serilog levels, and
/// decide Serilog request-log levels. One seam for unit tests — no ASP.NET host required.
/// </summary>
public static class LoggingProfile
{
    public const string ConfigKey = "Tempo:Logging:Profile";

    public const string StandardName = "standard";
    public const string DebugName = "debug";

    /// <summary>
    /// Resolves a profile name. Null, empty, or whitespace → <see cref="LoggingProfileKind.Standard"/>.
    /// Case-insensitive. Unknown values throw before the host listens.
    /// </summary>
    public static LoggingProfileKind Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LoggingProfileKind.Standard;
        }

        if (string.Equals(value.Trim(), StandardName, StringComparison.OrdinalIgnoreCase))
        {
            return LoggingProfileKind.Standard;
        }

        if (string.Equals(value.Trim(), DebugName, StringComparison.OrdinalIgnoreCase))
        {
            return LoggingProfileKind.Debug;
        }

        throw new InvalidOperationException(
            $"Unknown Tempo:Logging:Profile '{value.Trim()}'. Allowed values: {StandardName}, {DebugName}.");
    }

    /// <summary>Lowercase name for the startup log line.</summary>
    public static string ToConfigName(LoggingProfileKind profile) =>
        profile == LoggingProfileKind.Debug ? DebugName : StandardName;

    /// <summary>
    /// Applies minimum levels for the profile. Does not read Serilog or MEL config —
    /// the named profile is the only level source.
    /// </summary>
    public static LoggerConfiguration ApplyLevels(this LoggerConfiguration configuration, LoggingProfileKind profile)
    {
        configuration.MinimumLevel.Information();

        if (profile == LoggingProfileKind.Standard)
        {
            configuration
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information);
        }

        return configuration;
    }

    /// <summary>
    /// Request-middleware level for a completed request. Paths are compared without query;
    /// only exact <c>/health</c> and <c>/ready</c> are treated as probes.
    /// </summary>
    public static LogEventLevel GetRequestLogLevel(
        LoggingProfileKind profile,
        string? path,
        int statusCode,
        Exception? exception)
    {
        var isProbe = IsHealthOrReadyPath(path);

        if (exception is not null || statusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        if (isProbe && statusCode >= 200 && statusCode <= 299)
        {
            return LogEventLevel.Verbose;
        }

        if (profile == LoggingProfileKind.Standard)
        {
            return LogEventLevel.Verbose;
        }

        return LogEventLevel.Information;
    }

    private static bool IsHealthOrReadyPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        // PathString may include a trailing slash; strip for comparison.
        var p = path.TrimEnd('/');
        if (p.Length == 0)
        {
            p = "/";
        }

        return string.Equals(p, "/health", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p, "/ready", StringComparison.OrdinalIgnoreCase);
    }
}
