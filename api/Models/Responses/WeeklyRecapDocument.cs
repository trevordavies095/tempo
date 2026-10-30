using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Tempo.Api.Models.Responses;

/// <summary>
/// Body of <c>GET /stats/weekly-recap</c>.
/// </summary>
public class WeeklyRecapDocument
{
    [Required]
    [JsonPropertyName("weekStart")]
    public string WeekStart { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("weekEnd")]
    public string WeekEnd { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("referenceDate")]
    public string ReferenceDate { get; set; } = string.Empty;

    [JsonPropertyName("timezoneOffsetMinutes")]
    public int? TimezoneOffsetMinutes { get; set; }

    [Required]
    [JsonPropertyName("currentWeekIsPartial")]
    public bool CurrentWeekIsPartial { get; set; }

    [Required]
    [JsonPropertyName("generatedAtUtc")]
    public string GeneratedAtUtc { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("metrics")]
    public WeeklyRecapMetricsDocument Metrics { get; set; } = new();
}

public class WeeklyRecapMetricsDocument
{
    [Required]
    [JsonPropertyName("runs")]
    public WeeklyRecapMetricIntDocument Runs { get; set; } = new();

    [Required]
    [JsonPropertyName("distanceM")]
    public WeeklyRecapMetricDoubleDocument DistanceM { get; set; } = new();

    [Required]
    [JsonPropertyName("durationS")]
    public WeeklyRecapMetricLongDocument DurationS { get; set; } = new();

    [Required]
    [JsonPropertyName("elevationGainM")]
    public WeeklyRecapMetricDoubleDocument ElevationGainM { get; set; } = new();

    [Required]
    [JsonPropertyName("relativeEffortSum")]
    public WeeklyRecapMetricIntDocument RelativeEffortSum { get; set; } = new();

    /// <summary>
    /// Duration-weighted average heart rate of runs typed <c>Easy Run</c> or <c>Recovery</c>. <c>Walk-Run</c> and other
    /// run types are excluded. Null for a bucket with no qualifying run.
    /// </summary>
    [Required]
    [JsonPropertyName("easyRunAvgHeartRateBpm")]
    public WeeklyRecapMetricNullableDoubleDocument EasyRunAvgHeartRateBpm { get; set; } = new();

    /// <summary>
    /// Sum of moving time, falling back to elapsed duration for workouts without moving time.
    /// </summary>
    [Required]
    [JsonPropertyName("movingTimeS")]
    public WeeklyRecapMetricLongDocument MovingTimeS { get; set; } = new();

    /// <summary>
    /// Longest single run by distance in the bucket; 0 when the bucket has no runs.
    /// </summary>
    [Required]
    [JsonPropertyName("longestRunDistanceM")]
    public WeeklyRecapMetricDoubleDocument LongestRunDistanceM { get; set; } = new();

    /// <summary>
    /// Longest single run by moving time (elapsed when moving time is missing) in the bucket; 0 when the bucket has
    /// no runs. Independent of <see cref="LongestRunDistanceM"/>, so it may be a different workout.
    /// </summary>
    [Required]
    [JsonPropertyName("longestRunDurationS")]
    public WeeklyRecapMetricIntDocument LongestRunDurationS { get; set; } = new();

    /// <summary>
    /// Current week only: run counts keyed by exact <c>runType</c>. Types with no runs are omitted; workouts without a
    /// run type are counted under <c>Unspecified</c>. No previous, trailing average, or delta.
    /// </summary>
    [Required]
    [JsonPropertyName("runsByType")]
    public Dictionary<string, int> RunsByType { get; set; } = new();

    // Time in HR zone 1–5 (seconds), summed from each workout's stored zone seconds (the same values as
    // heartRateZoneTimes on workout detail). Null for a bucket with no HR series; trailing average covers only
    // buckets with a value; delta is null when either week is null.
    [Required]
    [JsonPropertyName("zoneSecondsZ1")]
    public WeeklyRecapMetricNullableLongDocument ZoneSecondsZ1 { get; set; } = new();

    [Required]
    [JsonPropertyName("zoneSecondsZ2")]
    public WeeklyRecapMetricNullableLongDocument ZoneSecondsZ2 { get; set; } = new();

    [Required]
    [JsonPropertyName("zoneSecondsZ3")]
    public WeeklyRecapMetricNullableLongDocument ZoneSecondsZ3 { get; set; } = new();

    [Required]
    [JsonPropertyName("zoneSecondsZ4")]
    public WeeklyRecapMetricNullableLongDocument ZoneSecondsZ4 { get; set; } = new();

    [Required]
    [JsonPropertyName("zoneSecondsZ5")]
    public WeeklyRecapMetricNullableLongDocument ZoneSecondsZ5 { get; set; } = new();
}

public class WeeklyRecapMetricIntDocument
{
    [Required]
    [JsonPropertyName("current")]
    public int Current { get; set; }

    [Required]
    [JsonPropertyName("previous")]
    public int Previous { get; set; }

    [Required]
    [JsonPropertyName("trailingAvg")]
    public double TrailingAvg { get; set; }

    [JsonPropertyName("deltaVsPrevious")]
    public int? DeltaVsPrevious { get; set; }
}

public class WeeklyRecapMetricLongDocument
{
    [Required]
    [JsonPropertyName("current")]
    public long Current { get; set; }

    [Required]
    [JsonPropertyName("previous")]
    public long Previous { get; set; }

    [Required]
    [JsonPropertyName("trailingAvg")]
    public double TrailingAvg { get; set; }

    [JsonPropertyName("deltaVsPrevious")]
    public long? DeltaVsPrevious { get; set; }
}

public class WeeklyRecapMetricDoubleDocument
{
    [Required]
    [JsonPropertyName("current")]
    public double Current { get; set; }

    [Required]
    [JsonPropertyName("previous")]
    public double Previous { get; set; }

    [Required]
    [JsonPropertyName("trailingAvg")]
    public double TrailingAvg { get; set; }

    [JsonPropertyName("deltaVsPrevious")]
    public double? DeltaVsPrevious { get; set; }
}

public class WeeklyRecapMetricNullableDoubleDocument
{
    [JsonPropertyName("current")]
    public double? Current { get; set; }

    [JsonPropertyName("previous")]
    public double? Previous { get; set; }

    [JsonPropertyName("trailingAvg")]
    public double? TrailingAvg { get; set; }

    [JsonPropertyName("deltaVsPrevious")]
    public double? DeltaVsPrevious { get; set; }
}

public class WeeklyRecapMetricNullableLongDocument
{
    [JsonPropertyName("current")]
    public long? Current { get; set; }

    [JsonPropertyName("previous")]
    public long? Previous { get; set; }

    [JsonPropertyName("trailingAvg")]
    public double? TrailingAvg { get; set; }

    [JsonPropertyName("deltaVsPrevious")]
    public long? DeltaVsPrevious { get; set; }
}
