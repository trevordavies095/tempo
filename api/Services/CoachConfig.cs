namespace Tempo.Api.Services;

/// <summary>
/// Host kill switch and coach packet knobs.
/// Env: Coach__Enabled (default false), Coach__L2TopK (default 8),
/// Coach__RecentTrainingPriorWeekCount (default 3),
/// Coach__ExchangeSeriesPoints (default 90),
/// Coach__ExchangeRecentTrainingPriorWeekCount (default 1).
/// </summary>
public class CoachConfig
{
    public bool Enabled { get; set; }

    /// <summary>Hard cap on retrieved Layer 2 episodes (prior-week is uncapped separately).</summary>
    public int L2TopK { get; set; } = 8;

    /// <summary>
    /// Number of full Mon–Sun weeks before the check-in week included in packet recentTraining.
    /// Total buckets = this value + 1 (check-in week). Not a UI setting.
    /// </summary>
    public int RecentTrainingPriorWeekCount { get; set; } = 3;

    /// <summary>Target points for exchange attachedRun series downsampling (issue 30).</summary>
    public int ExchangeSeriesPoints { get; set; } = 90;

    /// <summary>
    /// Prior full weeks before the exchange week in exchange recentTraining.
    /// Total buckets = this value + 1 (current week). Default 1 → W−1…current.
    /// </summary>
    public int ExchangeRecentTrainingPriorWeekCount { get; set; } = 1;
}
