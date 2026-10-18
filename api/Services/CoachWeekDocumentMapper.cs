using System.Globalization;
using System.Text.Json;
using Tempo.Api.Endpoints;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public static class CoachWeekDocumentMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static CoachWeekDocument ToDocument(CoachWeek week, CoachAdjustment? adjustment = null)
    {
        object? commit;
        try
        {
            commit = JsonSerializer.Deserialize<object>(week.CommitJson, JsonOptions);
        }
        catch
        {
            commit = null;
        }

        var applyRecord = DeserializeApplyRecord(week.ApplyRecordJson);

        var isAdjustment = adjustment != null
            && (adjustment.Status == CoachAdjustmentStatuses.Started
                || adjustment.Status == CoachAdjustmentStatuses.Applied);

        return new CoachWeekDocument(
            Id: week.Id,
            WeekStartDate: week.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status: week.Status,
            Commit: commit,
            PreflightId: week.PreflightId,
            JobId: week.JobId,
            PayloadHash: week.PayloadHash,
            CreatedAt: week.CreatedAt,
            UpdatedAt: week.UpdatedAt,
            IsAdjustment: isAdjustment,
            AdjustmentId: adjustment?.Id,
            LockedThroughDate: adjustment?.LockedThroughDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            AdjustmentCategory: adjustment?.Category,
            AdjustmentStatus: adjustment?.Status,
            ApplyRecord: applyRecord);
    }

    private static object? DeserializeApplyRecord(string? applyRecordJson)
    {
        if (string.IsNullOrWhiteSpace(applyRecordJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<object>(applyRecordJson, JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
