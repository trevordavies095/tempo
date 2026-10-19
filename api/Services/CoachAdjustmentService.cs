using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Utils;

namespace Tempo.Api.Services;

public class CoachAdjustmentService
{
    private readonly TempoDbContext _db;

    public CoachAdjustmentService(TempoDbContext db)
    {
        _db = db;
    }

    public async Task<(CoachAdjustment? Adjustment, string? Error, int StatusCode)> StartAsync(
        DateOnly weekStartMonday,
        string reason,
        string? category,
        int timezoneOffsetMinutes,
        DateOnly? referenceDate,
        CancellationToken cancellationToken = default)
    {
        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length == 0)
        {
            return (null, "reason is required", 400);
        }

        if (trimmedReason.Length > 2000)
        {
            return (null, "reason must be at most 2000 characters", 400);
        }

        string? normalizedCategory = null;
        if (!string.IsNullOrWhiteSpace(category))
        {
            normalizedCategory = category.Trim().ToLowerInvariant();
            if (!CoachAdjustmentCategories.All.Contains(normalizedCategory))
            {
                return (null, "category must be niggle, illness, life, or other", 400);
            }
        }

        DateTime? referenceLocal = referenceDate?.ToDateTime(TimeOnly.MinValue);
        var week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            DateTime.UtcNow,
            timezoneOffsetMinutes,
            referenceLocal);
        // Current calendar Mon–Sun containing "today", plus the upcoming plan week (ritual N+1).
        // Matches coach UI: Ask for changes on thisMonday or nextMonday.
        var currentMonday = DateOnly.FromDateTime(week.WeekStartLocal.Date);
        var upcomingPlanMonday = currentMonday.AddDays(7);
        var localToday = DateOnly.FromDateTime(week.ReferenceLocalDate.Date);

        if (weekStartMonday != currentMonday && weekStartMonday != upcomingPlanMonday)
        {
            return (null, "Adjust is only available for the athlete's current or upcoming Mon–Sun week", 400);
        }

        var weekEnd = weekStartMonday.AddDays(6);
        // Locked through = yesterday (elapsed). Remaining = today through Sunday.
        var lockedThrough = localToday.AddDays(-1);
        if (lockedThrough >= weekEnd)
        {
            return (null, "No remaining days left to adjust in this week", 409);
        }

        var committed = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.WeekStartDate == weekStartMonday && w.Status == CoachWeekStatuses.Committed,
                cancellationToken);
        if (committed == null)
        {
            return (null, "Committed week not found", 404);
        }

        var inFlight = await _db.CoachAdjustments.AsNoTracking()
            .AnyAsync(
                a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);
        if (inFlight)
        {
            return (null, "An adjust session is already in progress for this week", 409);
        }

        var now = DateTime.UtcNow;
        var adjustment = new CoachAdjustment
        {
            WeekStartDate = weekStartMonday,
            SourceCommittedWeekId = committed.Id,
            Reason = trimmedReason,
            Category = normalizedCategory,
            LockedThroughDate = lockedThrough,
            Status = CoachAdjustmentStatuses.Started,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.CoachAdjustments.Add(adjustment);
        await _db.SaveChangesAsync(cancellationToken);
        return (adjustment, null, 200);
    }

    public Task<CoachAdjustment?> GetStartedAsync(
        DateOnly weekStartMonday,
        CancellationToken cancellationToken = default) =>
        _db.CoachAdjustments
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);

    public Task<CoachAdjustment?> GetStartedAsNoTrackingAsync(
        DateOnly weekStartMonday,
        CancellationToken cancellationToken = default) =>
        _db.CoachAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);

    public async Task<CoachAdjustment?> FindForWeekDocumentAsync(
        DateOnly weekStartMonday,
        Guid? jobId,
        string weekStatus,
        CancellationToken cancellationToken = default)
    {
        if (jobId is Guid jid && jid != Guid.Empty)
        {
            var job = await _db.CoachJobs.AsNoTracking()
                .FirstOrDefaultAsync(j => j.Id == jid, cancellationToken);
            if (job?.AdjustmentId is Guid aid)
            {
                var byJob = await _db.CoachAdjustments.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == aid, cancellationToken);
                if (byJob != null)
                {
                    return byJob;
                }
            }
        }

        if (weekStatus == CoachWeekStatuses.Draft)
        {
            return await GetStartedAsNoTrackingAsync(weekStartMonday, cancellationToken);
        }

        if (weekStatus == CoachWeekStatuses.Committed)
        {
            return await _db.CoachAdjustments.AsNoTracking()
                .Where(a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Applied)
                .OrderByDescending(a => a.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return null;
    }
}
