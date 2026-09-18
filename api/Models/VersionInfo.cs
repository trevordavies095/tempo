using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models;

/// <summary>
/// Public support snapshot from GET /version. Identity only — no secrets, no Postgres.
/// </summary>
public class VersionInfo
{
    [Required]
    [JsonPropertyName("version")]
    public string Version { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("gitCommit")]
    public string GitCommit { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("buildDate")]
    public string BuildDate { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("logProfile")]
    public string LogProfile { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("environment")]
    public string Environment { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("apiImage")]
    public string ApiImage { get; set; } = "unknown";

    [Required]
    [JsonPropertyName("frontendImage")]
    public string FrontendImage { get; set; } = "unknown";

    /// <summary>
    /// Frozen plain-text projection of the other fields for copy-paste into bug reports.
    /// </summary>
    [Required]
    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = string.Empty;

    /// <summary>
    /// Builds the frozen support-snapshot text block (Unix newlines, trailing newline).
    /// </summary>
    public static string FormatSnapshot(
        string version,
        string gitCommit,
        string buildDate,
        string logProfile,
        string environment,
        string apiImage,
        string frontendImage)
    {
        var sb = new StringBuilder();
        sb.Append("Tempo support snapshot\n");
        sb.Append('\n');
        sb.Append("version: ").Append(version).Append('\n');
        sb.Append("gitCommit: ").Append(gitCommit).Append('\n');
        sb.Append("buildDate: ").Append(buildDate).Append('\n');
        sb.Append("logProfile: ").Append(logProfile).Append('\n');
        sb.Append("environment: ").Append(environment).Append('\n');
        sb.Append("apiImage: ").Append(apiImage).Append('\n');
        sb.Append("frontendImage: ").Append(frontendImage).Append('\n');
        return sb.ToString();
    }

    public void ApplySnapshot()
    {
        Snapshot = FormatSnapshot(
            Version,
            GitCommit,
            BuildDate,
            LogProfile,
            Environment,
            ApiImage,
            FrontendImage);
    }
}
