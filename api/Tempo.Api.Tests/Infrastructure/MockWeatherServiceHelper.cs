using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Tempo.Api.Services;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Helper class for mocking WeatherService HTTP calls in integration tests
/// </summary>
public static class MockWeatherServiceHelper
{
    /// <summary>
    /// Configures a successful weather API response
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    /// <param name="temperature">Temperature in Celsius (default: 20.0)</param>
    /// <param name="humidity">Humidity percentage (default: 60.0)</param>
    /// <param name="precipitation">Precipitation in mm (default: 0.0)</param>
    /// <param name="weatherCode">WMO weather code (default: 0 - clear sky)</param>
    /// <param name="windSpeed">Wind speed in m/s (default: 5.0)</param>
    /// <param name="windDirection">Wind direction in degrees (default: 180)</param>
    /// <param name="pressure">Surface pressure in hPa (default: 1013.25)</param>
    public static void ConfigureSuccessfulWeatherResponse(
        IServiceCollection services,
        double temperature = 20.0,
        double humidity = 60.0,
        double precipitation = 0.0,
        int weatherCode = 0,
        double windSpeed = 5.0,
        int windDirection = 180,
        double pressure = 1013.25)
    {
        var mockResponse = new
        {
            hourly = new
            {
                time = new[] { DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm") },
                temperature_2m = new[] { temperature },
                relative_humidity_2m = new[] { humidity },
                precipitation = new[] { precipitation },
                weather_code = new[] { weatherCode },
                wind_speed_10m = new[] { windSpeed * 3.6 }, // Convert m/s to km/h (Open-Meteo uses km/h)
                wind_direction_10m = new[] { windDirection },
                surface_pressure = new[] { pressure }
            }
        };

        var jsonResponse = JsonSerializer.Serialize(mockResponse);
        ConfigureWeatherHttpClient(services, HttpStatusCode.OK, jsonResponse);
    }

    /// <summary>
    /// Configures a weather API failure (HTTP error)
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    /// <param name="statusCode">HTTP status code (default: 500)</param>
    public static void ConfigureWeatherApiFailure(
        IServiceCollection services,
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError)
    {
        ConfigureWeatherHttpClient(services, statusCode, "Internal Server Error");
    }

    /// <summary>
    /// Configures a weather API timeout
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    public static void ConfigureWeatherApiTimeout(IServiceCollection services)
    {
        ConfigureWeatherHttpClient(services, HttpStatusCode.RequestTimeout, null, shouldTimeout: true);
    }

    /// <summary>
    /// Configures a weather API response with invalid JSON
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    public static void ConfigureInvalidJsonResponse(IServiceCollection services)
    {
        ConfigureWeatherHttpClient(services, HttpStatusCode.OK, "{ invalid json }");
    }

    /// <summary>
    /// Configures a weather API response with missing hourly data
    /// </summary>
    /// <param name="services">Service collection to configure</param>
    public static void ConfigureMissingHourlyDataResponse(IServiceCollection services)
    {
        var mockResponse = new { error = "No data available" };
        var jsonResponse = JsonSerializer.Serialize(mockResponse);
        ConfigureWeatherHttpClient(services, HttpStatusCode.OK, jsonResponse);
    }

    /// <summary>
    /// Configures the HttpClient for WeatherService with a mock handler
    /// </summary>
    private static void ConfigureWeatherHttpClient(
        IServiceCollection services,
        HttpStatusCode statusCode,
        string? responseContent,
        bool shouldTimeout = false)
    {
        // Remove existing WeatherService registrations (AddHttpClient may register multiple)
        services.RemoveAll(typeof(WeatherService));

        // Create a mock HttpMessageHandler
        var mockHandler = new Mock<HttpMessageHandler>();
        
        if (shouldTimeout)
        {
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(() => throw new TaskCanceledException("Request timeout"));
        }
        else
        {
            var response = new HttpResponseMessage(statusCode);
            if (responseContent != null)
            {
                response.Content = new StringContent(responseContent, Encoding.UTF8, "application/json");
            }

            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(response);
        }

        var httpClient = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://archive-api.open-meteo.com")
        };

        // Register WeatherService with the mocked HttpClient
        services.AddScoped<WeatherService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<WeatherService>>();
            return new WeatherService(httpClient, logger);
        });
    }
}

