namespace Tempo.Api.Services;

/// <summary>Sectioned mid-week exchange reply (never a weekly commit / memory channels).</summary>
public sealed class CoachExchangeReply
{
    public string WhatHappened { get; set; } = string.Empty;

    public string WhyItLooksLikeThat { get; set; } = string.Empty;

    public string WhatItMeansNext { get; set; } = string.Empty;

    public string Verdict { get; set; } = string.Empty;

    public List<string> SafetyFlags { get; set; } = [];

    public CoachExchangeSuggestedChange? SuggestedChange { get; set; }
}

public sealed class CoachExchangeSuggestedChange
{
    public string Summary { get; set; } = string.Empty;

    public string Note { get; set; } = string.Empty;

    public List<string> AffectedDates { get; set; } = [];
}

public static class CoachExchangeVerdicts
{
    public const string OnTrack = "on_track";
    public const string Noise = "noise";
    public const string Watch = "watch";
    public const string Concern = "concern";

    public static readonly HashSet<string> All = new(StringComparer.OrdinalIgnoreCase)
    {
        OnTrack,
        Noise,
        Watch,
        Concern
    };
}
