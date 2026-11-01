using Tempo.Api.Data;

namespace Tempo.Api.Services;

/// <summary>
/// Suggests a threshold pace from the athlete's recent 5K and 10K best efforts (read-only; never saved)
/// </summary>
public class ThresholdSuggestionService
{
    /// <summary>
    /// How far back a best effort may be to qualify
    /// </summary>
    public const int WindowDays = 90;

    private static readonly string[] QualifyingDistances = ["5K", "10K"];

    private readonly BestEffortService _bestEffortService;

    public ThresholdSuggestionService(BestEffortService bestEffortService)
    {
        _bestEffortService = bestEffortService;
    }

    /// <summary>
    /// Daniels T-pace from the 5K or 10K best effort (including splits of longer runs) among workouts started in
    /// the <see cref="WindowDays"/> days up to <paramref name="nowUtc"/>. When both qualify, the faster T-pace wins.
    /// Null when neither distance has an effort in the window.
    /// </summary>
    public async Task<ThresholdSuggestion?> GetSuggestionAsync(TempoDbContext db, DateTime nowUtc)
    {
        var from = DateOnly.FromDateTime(nowUtc).AddDays(-WindowDays);
        var efforts = await _bestEffortService.GetBestEffortsAsync(db, from);

        return efforts
            .Where(e => QualifyingDistances.Contains(e.Distance))
            .Select(e => new ThresholdSuggestion
            {
                ThresholdPaceS = (int)Math.Round(
                    DanielsVdot.ThresholdPaceSecondsPerKm(DanielsVdot.Vdot(e.DistanceM, e.TimeS))),
                Distance = e.Distance,
                TimeS = e.TimeS,
                WorkoutDate = e.WorkoutDate,
                WorkoutId = e.WorkoutId
            })
            .OrderBy(s => s.ThresholdPaceS)
            .FirstOrDefault();
    }

    public class ThresholdSuggestion
    {
        public int ThresholdPaceS { get; set; }
        public string Distance { get; set; } = string.Empty;
        public int TimeS { get; set; }
        public string WorkoutDate { get; set; } = string.Empty;
        public string WorkoutId { get; set; } = string.Empty;
    }
}
