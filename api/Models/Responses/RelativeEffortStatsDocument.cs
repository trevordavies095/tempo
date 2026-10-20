using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /stats/relative-effort</c>.
/// </summary>
public class RelativeEffortStatsDocument
{
    [Required]
    [JsonPropertyName("weekStart")]
    public string WeekStart { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("weekEnd")]
    public string WeekEnd { get; set; } = string.Empty;

    /// <summary>Cumulative Mon–Sun relative effort (length 7).</summary>
    [Required]
    [JsonPropertyName("currentWeek")]
    public int[] CurrentWeek { get; set; } = [];

    /// <summary>Totals for the three prior full weeks.</summary>
    [Required]
    [JsonPropertyName("previousWeeks")]
    public List<int> PreviousWeeks { get; set; } = [];

    [Required]
    [JsonPropertyName("threeWeekAverage")]
    public double ThreeWeekAverage { get; set; }

    [Required]
    [JsonPropertyName("rangeMin")]
    public int RangeMin { get; set; }

    [Required]
    [JsonPropertyName("rangeMax")]
    public int RangeMax { get; set; }

    [Required]
    [JsonPropertyName("currentWeekTotal")]
    public int CurrentWeekTotal { get; set; }
}
