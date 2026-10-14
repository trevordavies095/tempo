using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tempo.Api.Services;

public static class CoachLayer1Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string Serialize(CoachLayer1Document document) =>
        JsonSerializer.Serialize(document, Options);

    public static CoachLayer1Document? Deserialize(string json) =>
        JsonSerializer.Deserialize<CoachLayer1Document>(json, Options);
}
