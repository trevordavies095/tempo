namespace Tempo.Api.Services;

/// <summary>
/// Curated Anthropic model allowlist for AI Coach (v1).
/// </summary>
public static class CoachAnthropicModels
{
    public const string DefaultModelId = "claude-opus-4-7";

    public const string SonnetModelId = "claude-sonnet-4-6";

    private static readonly HashSet<string> Allowlist = new(StringComparer.Ordinal)
    {
        DefaultModelId,
        SonnetModelId
    };

    public static IReadOnlyCollection<string> All => Allowlist;

    /// <summary>
    /// Returns the normalized model id, or null if unknown.
    /// Null/empty input resolves to <see cref="DefaultModelId"/>.
    /// </summary>
    public static string? NormalizeOrReject(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return DefaultModelId;
        }

        var trimmed = modelId.Trim();
        return Allowlist.Contains(trimmed) ? trimmed : null;
    }
}
