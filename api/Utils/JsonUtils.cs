using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tempo.Api.Utils;

/// <summary>
/// Utility class for JSON serialization with shared options.
/// </summary>
public static class JsonUtils
{
    /// <summary>
    /// Default JSON serializer options used throughout the application.
    /// Uses compact formatting (no indentation) and ignores null values.
    /// </summary>
    public static readonly JsonSerializerOptions DefaultOptions = new JsonSerializerOptions
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

