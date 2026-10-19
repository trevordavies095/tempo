using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachExchangeReplySchemaTests
{
    [Fact]
    public void TryParseAndValidate_AcceptsValidReply()
    {
        var error = CoachExchangeReplySchema.TryParseAndValidate(BuildReplyJson(), out var reply);
        error.Should().BeNull();
        reply!.Verdict.Should().Be("on_track");
        reply.WhatHappened.Should().Contain("easy");
    }

    [Fact]
    public void TryParseAndValidate_RequiresSections()
    {
        var error = CoachExchangeReplySchema.TryParseAndValidate(
            BuildReplyJson(whatHappened: ""),
            out _);
        error.Should().Contain("whatHappened");
    }

    [Fact]
    public void TryParseAndValidate_RejectsBadVerdict()
    {
        var error = CoachExchangeReplySchema.TryParseAndValidate(
            BuildReplyJson(verdict: "great"),
            out _);
        error.Should().Contain("verdict");
    }

    [Fact]
    public void TryParseAndValidate_RejectsMemoryChannels()
    {
        var json = """
            {
              "whatHappened": "ok",
              "whyItLooksLikeThat": "ok",
              "whatItMeansNext": "ok",
              "verdict": "on_track",
              "safetyFlags": [],
              "suggestedChange": null,
              "layer1Patches": [{ "fieldPath": "paces.easySecPerKm", "oldValue": 1, "newValue": 2, "rationale": "x" }]
            }
            """;
        var error = CoachExchangeReplySchema.TryParseAndValidate(json, out _);
        error.Should().Contain("memory");
    }

    [Fact]
    public void StripSuggestedChangeIfNeeded_ClearsWithoutPlan()
    {
        var reply = new CoachExchangeReply
        {
            WhatHappened = "a",
            WhyItLooksLikeThat = "b",
            WhatItMeansNext = "c",
            Verdict = "on_track",
            SuggestedChange = new CoachExchangeSuggestedChange
            {
                Summary = "Ease Thursday",
                Note = "Make Thursday easy",
                AffectedDates = ["2026-10-22"]
            }
        };

        CoachExchangeReplySchema.StripSuggestedChangeIfNeeded(reply, hasCommittedPlan: false, remainingDates: null);
        reply.SuggestedChange.Should().BeNull();
    }

    [Fact]
    public void StripSuggestedChangeIfNeeded_FiltersToRemainingDays()
    {
        var reply = new CoachExchangeReply
        {
            WhatHappened = "a",
            WhyItLooksLikeThat = "b",
            WhatItMeansNext = "c",
            Verdict = "watch",
            SuggestedChange = new CoachExchangeSuggestedChange
            {
                Summary = "Ease Thu",
                Note = "Ease Thursday",
                AffectedDates = ["2026-10-20", "2026-10-22"]
            }
        };

        CoachExchangeReplySchema.StripSuggestedChangeIfNeeded(
            reply,
            hasCommittedPlan: true,
            remainingDates: ["2026-10-22", "2026-10-23"]);
        reply.SuggestedChange!.AffectedDates.Should().Equal("2026-10-22");
    }

    internal static string BuildReplyJson(
        string whatHappened = "You ran an easy aerobic session as planned.",
        string whyItLooksLikeThat = "Pace sat under easy HR with stable cadence.",
        string whatItMeansNext = "Keep tomorrow easy and protect the quality day.",
        string verdict = "on_track",
        object? suggestedChange = null,
        object[]? safetyFlags = null)
    {
        var sc = suggestedChange == null ? "null" : System.Text.Json.JsonSerializer.Serialize(suggestedChange);
        var flags = System.Text.Json.JsonSerializer.Serialize(safetyFlags ?? Array.Empty<object>());
        return $$"""
            {
              "whatHappened": {{System.Text.Json.JsonSerializer.Serialize(whatHappened)}},
              "whyItLooksLikeThat": {{System.Text.Json.JsonSerializer.Serialize(whyItLooksLikeThat)}},
              "whatItMeansNext": {{System.Text.Json.JsonSerializer.Serialize(whatItMeansNext)}},
              "verdict": {{System.Text.Json.JsonSerializer.Serialize(verdict)}},
              "safetyFlags": {{flags}},
              "suggestedChange": {{sc}}
            }
            """;
    }
}
