namespace Tempo.Api.Services;

/// <summary>
/// Threshold constants for insights endpoint data requirements.
/// These thresholds determine when specific insights become available to users.
/// </summary>
public static class InsightsThresholds
{
    /// <summary>
    /// Minimum number of workouts required to show any insights.
    /// Below this threshold, users see a message encouraging them to complete more runs.
    /// </summary>
    public const int MINIMUM_WORKOUTS = 5;

    /// <summary>
    /// Minimum number of workouts required to calculate weather-based statistics.
    /// Weather stats require fewer workouts since they're often available for most outdoor runs.
    /// </summary>
    public const int MINIMUM_FOR_WEATHER_STATS = 3;

    /// <summary>
    /// Minimum percentage of workouts with weather data to show weather insights.
    /// Weather stats are omitted if less than 25% of workouts have weather data.
    /// </summary>
    public const double MINIMUM_WEATHER_COVERAGE = 0.25; // 25%

    /// <summary>
    /// Minimum percentage of workouts with heart rate data to show HR insights.
    /// Heart rate stats are omitted if less than 25% of workouts have HR data.
    /// </summary>
    public const double MINIMUM_HR_COVERAGE = 0.25; // 25%
}
