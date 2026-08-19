namespace Tempo.Api.Services;

public interface IWeatherService
{
    Task<string?> GetWeatherForWorkoutAsync(
        string? rawStravaDataJson,
        string? rawFitDataJson,
        double? latitude,
        double? longitude,
        DateTime startTime);
}
