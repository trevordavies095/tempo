using FluentAssertions;
using Tempo.Api.Utils;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class WeeklyRecapWeekMathTests
{
    [Fact]
    public void ResolveMondaySundayWeek_UsesMondayThroughSunday()
    {
        // Wednesday 2026-10-14 local with offset 0
        var utcNow = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        var week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(utcNow, timezoneOffsetMinutes: 0, referenceLocalDate: null);

        week.WeekStartLocal.Date.Should().Be(new DateTime(2026, 10, 12));
        week.WeekEndLocal.Date.Should().Be(new DateTime(2026, 10, 18));
        week.WeekStartLocal.DayOfWeek.Should().Be(DayOfWeek.Monday);
        week.FullWeekRange.StartUtc.Should().Be(new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc));
        week.FullWeekRange.EndUtc.Should().Be(new DateTime(2026, 10, 18, 23, 59, 59, DateTimeKind.Utc).AddTicks(9999999));
    }

    [Fact]
    public void ResolveMondaySundayWeek_HonorsReferenceDate()
    {
        var utcNow = new DateTime(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);
        var week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            utcNow,
            timezoneOffsetMinutes: 0,
            referenceLocalDate: new DateTime(2026, 9, 30));

        week.ReferenceLocalDate.Should().Be(new DateTime(2026, 9, 30));
        week.WeekStartLocal.Date.Should().Be(new DateTime(2026, 9, 28));
        week.WeekEndLocal.Date.Should().Be(new DateTime(2026, 10, 4));
    }

    [Fact]
    public void BuildWeeklyRecapUtcContext_CapsCurrentRangeAtUtcNow()
    {
        var utcNow = new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);
        var ctx = WeeklyRecapWeekMath.BuildWeeklyRecapUtcContext(utcNow, timezoneOffsetMinutes: 0, referenceLocalDate: null);
        var full = WeeklyRecapWeekMath.ResolveMondaySundayWeek(utcNow, 0, null);

        ctx.CurrentWeekIsPartial.Should().BeTrue();
        ctx.CurrentRange.StartUtc.Should().Be(full.FullWeekRange.StartUtc);
        ctx.CurrentRange.EndUtc.Should().Be(utcNow);
        ctx.CurrentRange.EndUtc.Should().BeBefore(full.FullWeekRange.EndUtc);
    }
}
