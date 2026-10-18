using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Endpoints;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// History list and week-by-id composition over committed/archived coach weeks.
/// </summary>
public sealed class CoachHistoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoDbContext _db;
    private readonly CoachPlanDaysService _planDays;
    private readonly CoachAdjustmentService _adjustmentService;

    public CoachHistoryService(
        TempoDbContext db,
        CoachPlanDaysService planDays,
        CoachAdjustmentService adjustmentService)
    {
        _db = db;
        _planDays = planDays;
        _adjustmentService = adjustmentService;
    }

    public async Task<(CoachWeeksListDocument? Document, string? Error, int StatusCode)> ListAsync(
        int page,
        int pageSize,
        int? timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1)
        {
            pageSize = 20;
        }

        if (pageSize > 100)
        {
            pageSize = 100;
        }

        var offset = timezoneOffsetMinutes ?? 0;

        var query = _db.CoachWeeks.AsNoTracking()
            .Where(w =>
                w.Status == CoachWeekStatuses.Committed
                || w.Status == CoachWeekStatuses.Archived);

        var totalCount = await query.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
        if (totalPages > 0 && page > totalPages)
        {
            return (null, "page is out of range", 400);
        }

        var weeks = await query
            .OrderByDescending(w => w.WeekStartDate)
            .ThenByDescending(w => w.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var jobIds = weeks.Select(w => w.JobId).Distinct().ToList();
        var jobs = await _db.CoachJobs.AsNoTracking()
            .Where(j => jobIds.Contains(j.Id))
            .Select(j => new { j.Id, j.AdjustmentId })
            .ToListAsync(cancellationToken);
        var adjustmentByJobId = jobs
            .Where(j => j.AdjustmentId.HasValue)
            .ToDictionary(j => j.Id, j => j.AdjustmentId!.Value);

        var committedMondays = weeks
            .Where(w => w.Status == CoachWeekStatuses.Committed)
            .Select(w => w.WeekStartDate)
            .Distinct()
            .ToList();
        var appliedAdjustments = committedMondays.Count == 0
            ? []
            : await _db.CoachAdjustments.AsNoTracking()
                .Where(a =>
                    committedMondays.Contains(a.WeekStartDate)
                    && a.Status == CoachAdjustmentStatuses.Applied)
                .Select(a => a.WeekStartDate)
                .Distinct()
                .ToListAsync(cancellationToken);
        var appliedMondays = appliedAdjustments.ToHashSet();

        var checkInMondays = weeks
            .Select(w => w.WeekStartDate.AddDays(-7))
            .Distinct()
            .ToList();
        var feelByCheckInMonday = await _db.CoachCheckIns.AsNoTracking()
            .Where(c => checkInMondays.Contains(c.WeekStartDate))
            .ToDictionaryAsync(c => c.WeekStartDate, c => c.FeelScore, cancellationToken);

        var items = new List<CoachWeekListItemDocument>(weeks.Count);
        foreach (var week in weeks)
        {
            var (planned, actual) = await _planDays.SumDistancesForWeekAsync(
                week,
                offset,
                cancellationToken);

            var isAdjustment = adjustmentByJobId.ContainsKey(week.JobId)
                || (week.Status == CoachWeekStatuses.Committed
                    && appliedMondays.Contains(week.WeekStartDate));

            feelByCheckInMonday.TryGetValue(week.WeekStartDate.AddDays(-7), out var feelScore);

            var weekEnd = week.WeekStartDate.AddDays(6);
            items.Add(new CoachWeekListItemDocument(
                Id: week.Id,
                WeekStartDate: week.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                WeekEndDate: weekEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Status: week.Status,
                IsAdjustment: isAdjustment,
                FeelScore: feelScore,
                PlannedDistanceM: planned,
                ActualDistanceM: actual));
        }

        return (new CoachWeeksListDocument(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages), null, 200);
    }

    public async Task<(CoachWeekHistoryDetailDocument? Document, string? Error, int StatusCode)> GetByIdAsync(
        Guid weekId,
        int? timezoneOffsetMinutes,
        CancellationToken cancellationToken = default)
    {
        var week = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == weekId, cancellationToken);
        if (week == null
            || week.Status == CoachWeekStatuses.Draft)
        {
            return (null, "Week not found", 404);
        }

        if (week.Status is not (CoachWeekStatuses.Committed or CoachWeekStatuses.Archived))
        {
            return (null, "Week not found", 404);
        }

        var offset = timezoneOffsetMinutes ?? 0;
        var adjustment = await _adjustmentService.FindForWeekDocumentAsync(
            week.WeekStartDate,
            week.JobId,
            week.Status,
            cancellationToken);

        var job = await _db.CoachJobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == week.JobId, cancellationToken);
        if (adjustment == null && job?.AdjustmentId is Guid aid)
        {
            adjustment = await _db.CoachAdjustments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == aid, cancellationToken);
        }

        var weekDoc = CoachWeekDocumentMapper.ToDocument(week, adjustment);
        // Job linked to an adjustment counts even when the adjustment row is gone / status odd.
        if (!weekDoc.IsAdjustment && job?.AdjustmentId is Guid linkedAid)
        {
            weekDoc = weekDoc with
            {
                IsAdjustment = true,
                AdjustmentId = weekDoc.AdjustmentId ?? linkedAid
            };
        }

        var days = await _planDays.BuildDaysForWeekAsync(week, offset, cancellationToken);
        var receipt = BuildReceipt(week);

        return (new CoachWeekHistoryDetailDocument(
            Id: weekDoc.Id,
            WeekStartDate: weekDoc.WeekStartDate,
            Status: weekDoc.Status,
            Commit: weekDoc.Commit,
            PreflightId: weekDoc.PreflightId,
            JobId: weekDoc.JobId,
            PayloadHash: weekDoc.PayloadHash,
            CreatedAt: weekDoc.CreatedAt,
            UpdatedAt: weekDoc.UpdatedAt,
            IsAdjustment: weekDoc.IsAdjustment,
            AdjustmentId: weekDoc.AdjustmentId,
            LockedThroughDate: weekDoc.LockedThroughDate,
            AdjustmentCategory: weekDoc.AdjustmentCategory,
            AdjustmentStatus: weekDoc.AdjustmentStatus,
            ApplyRecord: weekDoc.ApplyRecord,
            Days: days,
            PrivacyReceipt: receipt), null, 200);
    }

    private static CoachWeekPrivacyReceiptDocument BuildReceipt(CoachWeek week)
    {
        object? payload;
        try
        {
            payload = JsonSerializer.Deserialize<object>(week.RedactedPayloadJson, JsonOptions);
        }
        catch
        {
            payload = null;
        }

        var (episodeIds, ruleIds) = CoachPrivacyRedactor.ExtractSelectedIds(week.RedactedPayloadJson);
        var recentTraining = CoachPrivacyRedactor.ExtractRecentTrainingSummary(week.RedactedPayloadJson);

        return new CoachWeekPrivacyReceiptDocument(
            WeekId: week.Id,
            WeekStartDate: week.WeekStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Status: week.Status,
            PayloadHash: week.PayloadHash,
            RedactedPayload: payload,
            SelectedEpisodeIds: episodeIds,
            SelectedRuleIds: ruleIds,
            RecentTraining: recentTraining);
    }
}
