namespace Tempo.Api.Services;

/// <summary>
/// Host kill switch and coach packet knobs.
/// Env: Coach__Enabled (default false), Coach__L2TopK (default 8).
/// </summary>
public class CoachConfig
{
    public bool Enabled { get; set; }

    /// <summary>Hard cap on retrieved Layer 2 episodes (prior-week is uncapped separately).</summary>
    public int L2TopK { get; set; } = 8;
}
