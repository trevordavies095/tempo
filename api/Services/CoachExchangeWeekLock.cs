using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Hard lock for mid-week exchanges: after Accept of plan week N+1, no new Sends for week N.
/// </summary>
public static class CoachExchangeWeekLock
{
    public const string LockedErrorMessage =
        "Exchanges for this week are locked after you accepted next week's plan";

    public static DateOnly NextPlanMonday(DateOnly weekStartMonday) => weekStartMonday.AddDays(7);

    public static DateOnly PreviousPlanMonday(DateOnly weekStartMonday) => weekStartMonday.AddDays(-7);

    /// <summary>
    /// Week W is locked when a committed or archived plan exists for W+7 (Accept of N+1).
    /// Draft-only next week does not lock.
    /// </summary>
    public static async Task<bool> IsLockedAsync(
        TempoDbContext db,
        DateOnly weekStartMonday,
        CancellationToken cancellationToken = default)
    {
        var nextMonday = NextPlanMonday(weekStartMonday);
        return await db.CoachWeeks.AsNoTracking().AnyAsync(
            w => w.WeekStartDate == nextMonday
                 && (w.Status == CoachWeekStatuses.Committed || w.Status == CoachWeekStatuses.Archived),
            cancellationToken);
    }

    /// <summary>
    /// After Accept of week W, stamp Locked on all exchanges for week W-7.
    /// </summary>
    public static async Task StampPriorWeekLockedAsync(
        TempoDbContext db,
        DateOnly acceptedWeekMonday,
        DateTime updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        var priorMonday = PreviousPlanMonday(acceptedWeekMonday);
        await db.CoachExchanges
            .Where(e => e.WeekStartDate == priorMonday && !e.Locked)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.Locked, true)
                    .SetProperty(e => e.UpdatedAt, updatedAtUtc),
                cancellationToken);
    }
}
