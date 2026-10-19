using System.Text.Json;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachLayer1PatcherTests
{
    private static CoachLayer1Document BaseDoc() =>
        new(
            HrZones: new CoachLayer1HrZones("AgeBased", 30, null, 190, []),
            Paces: new CoachLayer1Paces(360, 310, 280, 265),
            Shoes: [],
            DefaultShoeId: null,
            RaceStatus: new CoachLayer1RaceStatus("race", "5K", 5000, null, null, null),
            CompletedAt: DateTime.UtcNow,
            TrainingPhase: null,
            RunDaysPerWeek: 3);

    [Fact]
    public void Apply_RunDaysPerWeek_UpdatesDocument()
    {
        var patched = CoachLayer1Patcher.Apply(
            BaseDoc(),
            new CoachLayer1Patch
            {
                FieldPath = "runDaysPerWeek",
                OldValue = JsonSerializer.SerializeToElement(3),
                NewValue = JsonSerializer.SerializeToElement(5),
                Rationale = "Consistent five-day weeks"
            });

        patched.RunDaysPerWeek.Should().Be(5);
        patched.Paces.EasySecPerKm.Should().Be(360);
    }
}
