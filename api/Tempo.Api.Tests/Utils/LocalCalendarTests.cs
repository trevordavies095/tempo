using FluentAssertions;
using Tempo.Api.Utils;
using Xunit;

namespace Tempo.Api.Tests.Utils;

public class LocalCalendarTests
{
    private const int Est = -300;
    private const int Ist = 330;

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) =>
        new(y, m, d, h, min, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(Est, 2026, 1, 15, 2, 14)]   // 02:00 UTC is still the 14th in EST
    [InlineData(Est, 2026, 1, 15, 5, 15)]   // midnight EST
    [InlineData(Ist, 2026, 1, 14, 19, 15)]  // 19:00 UTC is already the 15th in IST
    [InlineData(0, 2026, 1, 15, 23, 15)]
    public void LocalDate_UsesTheOffsetFrame(int offset, int y, int m, int d, int h, int expectedDay)
    {
        new LocalCalendar(offset).LocalDate(Utc(y, m, d, h)).Should().Be(new DateTime(2026, 1, expectedDay));
    }

    [Fact]
    public void NoOffset_IsTheUtcFrame()
    {
        var calendar = new LocalCalendar(null);
        var date = new DateTime(2026, 3, 9);

        calendar.Should().Be(LocalCalendar.Utc);
        calendar.Days(date, date).Should().Be(new UtcRange(Utc(2026, 3, 9), Utc(2026, 3, 10).AddTicks(-1)));
        calendar.LocalDate(Utc(2026, 3, 9, 23, 30)).Should().Be(date);
    }

    [Fact]
    public void Days_CoversWholeLocalDaysAsUtc()
    {
        var range = new LocalCalendar(Est).Days(new DateTime(2026, 6, 1), new DateTime(2026, 6, 30));

        range.StartUtc.Should().Be(Utc(2026, 6, 1, 5));
        range.EndUtc.Should().Be(Utc(2026, 7, 1, 5).AddTicks(-1));
        range.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        range.EndUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Days_IgnoresTheTimeOfDayOnLocalDates()
    {
        var calendar = new LocalCalendar(Est);

        calendar.Days(new DateTime(2026, 6, 1, 18, 45, 0), new DateTime(2026, 6, 1, 7, 0, 0))
            .Should().Be(calendar.Days(new DateTime(2026, 6, 1), new DateTime(2026, 6, 1)));
    }

    [Fact]
    public void Days_SpansTheLocalYearBoundary()
    {
        var range = new LocalCalendar(Est).Days(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        range.StartUtc.Should().Be(Utc(2026, 1, 1, 5));
        range.EndUtc.Should().Be(Utc(2027, 1, 1, 5).AddTicks(-1));
    }

    [Theory]
    [InlineData("2026-11-23", "2026-11-23")] // Monday
    [InlineData("2026-11-25", "2026-11-23")] // Wednesday
    [InlineData("2026-11-29", "2026-11-23")] // Sunday
    [InlineData("2026-11-30", "2026-11-30")] // next Monday
    [InlineData("2027-01-01", "2026-12-28")] // Friday whose week starts in the previous year
    public void WeekStart_IsTheMondayOnOrBefore(string date, string expectedMonday)
    {
        LocalCalendar.WeekStart(DateTime.Parse(date).AddHours(15))
            .Should().Be(DateTime.Parse(expectedMonday));
    }

    [Fact]
    public void Week_IsMondayThroughSunday()
    {
        var range = new LocalCalendar(Est).Week(new DateTime(2026, 11, 23));

        range.StartUtc.Should().Be(Utc(2026, 11, 23, 5));
        range.EndUtc.Should().Be(Utc(2026, 11, 30, 5).AddTicks(-1));
    }

    [Theory]
    [InlineData(Est, 2026, 11, 24, 1, 0)] // Tuesday 01:00 UTC is Monday evening in EST
    [InlineData(0, 2026, 11, 24, 1, 1)]
    [InlineData(Ist, 2026, 11, 29, 20, 0)] // Sunday 20:00 UTC is Monday in IST
    [InlineData(Est, 2026, 11, 30, 3, 6)]  // Monday 03:00 UTC is Sunday in EST
    public void MondayIndex_FollowsTheLocalDay(int offset, int y, int m, int d, int h, int expected)
    {
        new LocalCalendar(offset).MondayIndex(Utc(y, m, d, h)).Should().Be(expected);
    }

    [Fact]
    public void ToUtc_AndToLocal_RoundTrip()
    {
        var calendar = new LocalCalendar(Ist);
        var utc = Utc(2026, 5, 4, 12, 15);

        calendar.ToUtc(calendar.ToLocal(utc)).Should().Be(utc);
        calendar.Today(utc).Should().Be(new DateTime(2026, 5, 4));
    }
}
