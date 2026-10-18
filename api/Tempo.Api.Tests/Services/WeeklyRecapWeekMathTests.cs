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

    [Theory]
    [InlineData(2026, 8, 1, 2026, 7, 27)] // Sat → current Mon–Sun
    [InlineData(2026, 8, 2, 2026, 7, 27)] // Sun → current Mon–Sun
    [InlineData(2026, 8, 3, 2026, 7, 27)] // Mon → last completed Mon–Sun
    [InlineData(2026, 8, 5, 2026, 8, 3)]  // Wed → current Mon–Sun
    public void ResolveDefaultCheckInWeek_MatchesTempoCliWeekdayRules(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var utcNow = new DateTime(year, month, day, 15, 0, 0, DateTimeKind.Utc);
        var week = WeeklyRecapWeekMath.ResolveDefaultCheckInWeek(utcNow, timezoneOffsetMinutes: 0);

        week.WeekStartLocal.Date.Should().Be(new DateTime(expectedYear, expectedMonth, expectedDay));
        week.WeekStartLocal.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void ResolveMondaySundayWeek_NullReferenceOnMonday_StillCurrentWeek()
    {
        // Regression: weekly-recap null semantics stay "week containing today" even on Monday.
        var utcNow = new DateTime(2026, 8, 3, 15, 0, 0, DateTimeKind.Utc); // Monday
        var week = WeeklyRecapWeekMath.ResolveMondaySundayWeek(utcNow, timezoneOffsetMinutes: 0, referenceLocalDate: null);

        week.WeekStartLocal.Date.Should().Be(new DateTime(2026, 8, 3));
        week.WeekEndLocal.Date.Should().Be(new DateTime(2026, 8, 9));
    }

    [Fact]
    public void ResolvePlanWeekFromCheckIn_IsCheckInMondayPlusSeven()
    {
        var checkIn = WeeklyRecapWeekMath.ResolveMondaySundayWeek(
            new DateTime(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc),
            timezoneOffsetMinutes: 0,
            referenceLocalDate: new DateTime(2026, 10, 12));

        var plan = WeeklyRecapWeekMath.ResolvePlanWeekFromCheckIn(checkIn, timezoneOffsetMinutes: 0);

        plan.WeekStartLocal.Date.Should().Be(new DateTime(2026, 10, 19));
        plan.WeekEndLocal.Date.Should().Be(new DateTime(2026, 10, 25));
    }
}
