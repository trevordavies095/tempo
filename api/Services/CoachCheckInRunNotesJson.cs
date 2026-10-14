using System.Text.Json;
using System.Text.Json.Serialization;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public static class CoachCheckInRunNotesJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string Serialize(IReadOnlyList<CoachCheckInRunNote> notes) =>
        JsonSerializer.Serialize(notes, Options);

    public static IReadOnlyList<CoachCheckInRunNote> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<CoachCheckInRunNote>>(json, Options) ?? [];
    }
}
