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

    [Required]
    [JsonPropertyName("easyRunAvgHeartRateBpm")]
    public WeeklyRecapMetricNullableDoubleDocument EasyRunAvgHeartRateBpm { get; set; } = new();
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
