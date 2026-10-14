using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachWeeklyCommitSchemaTests
{
    private static readonly DateOnly WeekStart = new(2026, 10, 12);

    [Fact]
    public void TryParseAndValidate_RequiresCoachesNotes()
    {
        var json = BuildCommitJson(notes: "");
        var error = CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out _);
        error.Should().Contain("coachesNotes");
    }

    [Fact]
    public void TryParseAndValidate_RejectsOverlongNotes()
    {
        var json = BuildCommitJson(notes: new string('x', 801));
        var error = CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out _);
        error.Should().Contain("800");
    }

    [Fact]
    public void TryParseAndValidate_AcceptsValidCommit()
    {
        var json = BuildCommitJson(notes: "Solid base week. Keep easy days easy.");
        var error = CoachWeeklyCommitSchema.TryParseAndValidate(json, WeekStart, out var commit);
        error.Should().BeNull();
        commit!.Days.Should().HaveCount(7);
        commit.CoachesNotes.Should().Contain("Solid");
    }

    internal static string BuildCommitJson(
        string notes = "Keep the long easy and protect Thursday.",
        Func<int, string>? dayTypeForIndex = null,
        Func<int, string?>? runTypeForIndex = null,
        object[]? safetyFlags = null,
        object[]? memoryProposals = null,
        Func<int, double>? distanceMForIndex = null)
    {
        dayTypeForIndex ??= i => i == 6 ? "long" : i == 3 ? "quality" : i is 1 or 4 ? "rest" : "easy";
        runTypeForIndex ??= i =>
        {
            var dt = dayTypeForIndex(i);
            return dt == "rest" ? null : dt;
        };
        distanceMForIndex ??= i =>
        {
            var runType = runTypeForIndex(i);
            return runType == "long" ? 16000 : 8000;
        };

        var days = new List<object>();
        for (var i = 0; i < 7; i++)
        {
            var date = WeekStart.AddDays(i).ToString("yyyy-MM-dd");
            var dayType = dayTypeForIndex(i);
            var runType = runTypeForIndex(i);
            object? run = runType == null
                ? null
                : new
                {
                    type = runType,
                    distanceM = distanceMForIndex(i),
                    structure = (string?)null,
                    paceOrHr = (string?)null,
                    shoeId = (string?)null,
                    notes = (string?)null
                };
            days.Add(new { date, dayType, run, strength = (object?)null });
        }

        return System.Text.Json.JsonSerializer.Serialize(new
        {
            coachesNotes = notes,
            days,
            safetyFlags = safetyFlags ?? Array.Empty<object>(),
            memoryProposals = memoryProposals ?? Array.Empty<object>(),
            clarifyingQuestion = (string?)null,
            callouts = Array.Empty<string>()
        });
    }
}
