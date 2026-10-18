using System.Text.Json;

namespace Tempo.Api.Services;

/// <summary>
/// Applies allowlisted Layer 1 patches onto a typed L1 document.
/// </summary>
public static class CoachLayer1Patcher
{
    public static CoachLayer1Document Apply(CoachLayer1Document document, CoachLayer1Patch patch)
    {
        var path = patch.FieldPath.Trim();
        var value = patch.NewValue;

        return path switch
        {
            "paces.easySecPerKm" => document with
            {
                Paces = document.Paces with { EasySecPerKm = ReadInt(value) }
            },
            "paces.tempoSecPerKm" => document with
            {
                Paces = document.Paces with { TempoSecPerKm = ReadInt(value) }
            },
            "paces.thresholdSecPerKm" => document with
            {
                Paces = document.Paces with { ThresholdSecPerKm = ReadInt(value) }
            },
            "paces.intervalSecPerKm" => document with
            {
                Paces = document.Paces with { IntervalSecPerKm = ReadInt(value) }
            },
            "hrZones.maxHeartRateBpm" => document with
            {
                HrZones = document.HrZones with { MaxHeartRateBpm = ReadInt(value) }
            },
            "hrZones.restingHeartRateBpm" => document with
            {
                HrZones = document.HrZones with { RestingHeartRateBpm = ReadInt(value) }
            },
            "raceStatus.targetTimeS" => document with
            {
                RaceStatus = document.RaceStatus with { TargetTimeS = ReadInt(value) }
            },
            "raceStatus.notes" => document with
            {
                RaceStatus = document.RaceStatus with { Notes = ReadNullableString(value) }
            },
            "trainingPhase" => document with
            {
                TrainingPhase = NormalizeTrainingPhase(ReadNullableString(value))
            },
            _ => throw new InvalidOperationException($"Unsupported L1 patch path '{path}'")
        };
    }

    private static int ReadInt(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind != JsonValueKind.Number || !value.Value.TryGetInt32(out var n))
        {
            throw new InvalidOperationException("Expected positive integer JsonElement");
        }

        return n;
    }

    private static string? ReadNullableString(JsonElement? value)
    {
        if (value is null || value.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.Value.ValueKind == JsonValueKind.String ? value.Value.GetString() : value.Value.ToString();
    }

    private static string? NormalizeTrainingPhase(string? phase)
    {
        if (string.IsNullOrWhiteSpace(phase))
        {
            return null;
        }

        return phase.Trim().ToLowerInvariant();
    }
}
