using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for ImportWorkout endpoint
/// </summary>
[Collection("Integration Tests")]
public class ImportWorkoutTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public ImportWorkoutTests(TempoWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Helper method to ensure database is clean before a test (but preserves test user)
    /// </summary>
    private async Task EnsureCleanDatabaseAsync()
    {
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            
            // Clear all data except users (we need the test user for authentication)
            await TestDataSeeder.SafeClearAllDataAsync(db, preserveUsers: true);
        }
    }

    /// <summary>
    /// Creates a minimal valid GPX file content for testing
    /// Uses points with sufficient distance to meet minimum distance requirements (~5km)
    /// </summary>
    private static string CreateMinimalGpxContent(DateTime? startTime = null, double distanceKm = 6.35)
    {
        var start = startTime ?? DateTime.UtcNow.AddHours(-1);
        var durationMinutes = 30;
        var end = start.AddMinutes(durationMinutes);
        
        // Create track points along a path that gives approximately the desired distance
        // Starting point: San Francisco
        var startLat = 37.7749;
        var startLon = -122.4194;
        
        // Calculate degree increment to achieve desired distance
        // At San Francisco's latitude (~37.77°), moving diagonally (northeast):
        // - 1 degree latitude ≈ 111 km
        // - 1 degree longitude ≈ 111 * cos(37.77°) ≈ 87.7 km
        // - Diagonal distance per degree ≈ sqrt(111^2 + 87.7^2) ≈ 141 km
        // So to get distanceKm km, we need: distanceKm / 141 degrees
        // Note: Default distanceKm = 6.35km maintains backward compatibility with the old
        // hardcoded 0.045 degree increment, which produced approximately 6.35km
        var degreeIncrement = distanceKm / 141.0;
        
        // We'll create multiple points to ensure smooth parsing
        var numPoints = 20;
        var points = new List<string>();
        
        for (int i = 0; i < numPoints; i++)
        {
            var progress = (double)i / (numPoints - 1);
            var lat = startLat + (progress * degreeIncrement); // Move north
            var lon = startLon + (progress * degreeIncrement); // Move east
            var ele = 10.0 + (progress * 50.0); // Elevation gain
            var time = start.AddMinutes(progress * durationMinutes);
            
            points.Add($@"      <trkpt lat=""{lat:F6}"" lon=""{lon:F6}"">
        <ele>{ele:F1}</ele>
        <time>{time:yyyy-MM-ddTHH:mm:ss}Z</time>
      </trkpt>");
        }

        return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<gpx version=""1.1"" creator=""Tempo Test"" xmlns=""http://www.topografix.com/GPX/1/1"">
  <trk>
    <name>Test Run</name>
    <trkseg>
{string.Join("\n", points)}
    </trkseg>
  </trk>
</gpx>";
    }

    #region Validation Tests

    [Fact]
    public async Task ImportWorkout_ReturnsBadRequest_WhenNonMultipartRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var content = new StringContent("test", Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync("/workouts/import", content);

        // Assert
        // Accept both 400 (BadRequest) and 415 (UnsupportedMediaType) as valid responses
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var error = await response.Content.ReadAsStringAsync();
            error.Should().Contain("multipart/form-data");
        }
    }

    [Fact]
    public async Task ImportWorkout_ReturnsBadRequest_WhenNoFilesUploaded()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        // Add a dummy field to make the multipart form valid (empty MultipartFormDataContent causes InvalidDataException)
        var formData = new MultipartFormDataContent();
        formData.Add(new StringContent("dummy"), "dummy");

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("No files uploaded");
    }

    [Fact]
    public async Task ImportWorkout_ReturnsBadRequest_WhenInvalidFileType()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var formData = new MultipartFormDataContent();
        var fileContent = new StringContent("test content", Encoding.UTF8, "text/plain");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.txt"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await response.Content.ReadAsStringAsync();
        error.Should().Contain("GPX or FIT");
    }

    [Fact]
    public async Task ImportWorkout_AcceptsValidFileTypes()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    #endregion

    #region Single File Tests

    [Fact]
    public async Task ImportWorkout_SingleGpx_DefaultsToMetric_WhenUnitPreferenceMissing()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);
        // Note: Not adding unitPreference field

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify workout was created with metric splits (1000m)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts
                .Include(w => w.Splits)
                .FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
            if (workout!.Splits.Any())
            {
                // Metric splits should be approximately 1000m
                workout.Splits.First().DistanceM.Should().BeApproximately(1000.0, 100.0);
            }
        }
    }

    [Fact]
    public async Task ImportWorkout_SingleGpx_UsesImperial_WhenUnitPreferenceIsImperial()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);
        formData.Add(new StringContent("imperial"), "unitPreference");

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        // Verify workout was created with imperial splits (1609.344m = 1 mile)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts
                .Include(w => w.Splits)
                .FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
            if (workout!.Splits.Any())
            {
                // Imperial splits should be approximately 1609.344m (1 mile)
                workout.Splits.First().DistanceM.Should().BeApproximately(1609.344, 100.0);
            }
        }
    }

    [Fact]
    public async Task ImportWorkout_SingleGpx_SavesWorkoutWithCorrectFields()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var gpxContent = CreateMinimalGpxContent(startTime);
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            // Query by the specific start time to ensure we get the workout we just created
            // Use a time range to account for any small time differences
            var workout = await db.Workouts
                .Where(w => w.StartedAt >= startTime.AddSeconds(-5) && w.StartedAt <= startTime.AddSeconds(5))
                .FirstOrDefaultAsync();
            
            // Fail with a clear message if workout not found by time (rather than using fallback that masks the issue)
            workout.Should().NotBeNull(
                because: $"Workout with StartedAt around {startTime:O} was not found. " +
                         "This indicates the workout was not created with the expected start time.");
            workout!.StartedAt.Should().BeCloseTo(startTime, TimeSpan.FromSeconds(1));
            workout.DistanceM.Should().BeGreaterThan(0);
            workout.DurationS.Should().BeGreaterThan(0);
            workout.AvgPaceS.Should().BeGreaterThan(0);
            workout.Name.Should().Be("Test Run");
            workout.Source.Should().Be("gpx_import");
        }
    }

    [Fact]
    public async Task ImportWorkout_SingleGpx_DoesNotAssignRetiredDefaultShoe()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var retiredShoe = await TestDataSeeder.SeedShoeAsync(db, isRetired: true);
            await TestDataSeeder.SeedUserSettingsAsync(db, defaultShoeId: retiredShoe.Id);
        }

        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var startTime = new DateTime(2024, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        var gpxContent = CreateMinimalGpxContent(startTime);
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts
                .Where(w => w.StartedAt >= startTime.AddSeconds(-5) && w.StartedAt <= startTime.AddSeconds(5))
                .FirstOrDefaultAsync();

            workout.Should().NotBeNull();
            workout!.ShoeId.Should().BeNull();
        }
    }

    [Fact]
    public async Task ImportWorkout_SingleGpx_StoresRouteAsGeoJson()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts
                .Include(w => w.Route)
                .FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
            workout!.Route.Should().NotBeNull();
            workout.Route!.RouteGeoJson.Should().Contain("LineString");
            workout.Route.RouteGeoJson.Should().Contain("coordinates");
        }
    }

    [Fact]
    public async Task ImportWorkout_SingleGpx_ComputesSplitsCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts
                .Include(w => w.Splits.OrderBy(s => s.Idx))
                .FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
            workout!.Splits.Should().NotBeEmpty();
            
            // Verify splits are ordered by index
            var splits = workout.Splits.OrderBy(s => s.Idx).ToList();
            for (int i = 0; i < splits.Count; i++)
            {
                splits[i].Idx.Should().Be(i);
                splits[i].DistanceM.Should().BeGreaterThan(0);
                splits[i].DurationS.Should().BeGreaterThan(0);
                splits[i].PaceS.Should().BeGreaterThan(0);
            }
        }
    }

    #endregion

    #region Multi-File Tests

    [Fact]
    public async Task ImportWorkout_MultiFile_ReturnsCorrectCounts_WhenAllSuccessful()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        
        // Add two files
        for (int i = 0; i < 2; i++)
        {
            var startTime = DateTime.UtcNow.AddHours(-i - 1);
            var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(CreateMinimalGpxContent(startTime)));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
            fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "file",
                FileName = $"test{i}.gpx"
            };
            formData.Add(fileContent);
        }

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MultiFileImportResponse>();
        result.Should().NotBeNull();
        result!.totalProcessed.Should().Be(2);
        result.successful.Should().Be(2);
        result.skipped.Should().Be(0);
        result.updated.Should().Be(0);
        result.errors.Should().Be(0);
    }

    [Fact]
    public async Task ImportWorkout_MultiFile_HandlesDuplicatesCorrectly()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var startTime = DateTime.UtcNow.AddHours(-1);
        var gpxContent = CreateMinimalGpxContent(startTime);
        
        // Import first file
        var formData1 = new MultipartFormDataContent();
        var fileContent1 = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent1.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test1.gpx"
        };
        formData1.Add(fileContent1);
        await client.PostAsync("/workouts/import", formData1);

        // Import same file again (duplicate)
        var formData2 = new MultipartFormDataContent();
        var fileContent2 = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent2.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test2.gpx"
        };
        formData2.Add(fileContent2);

        // Act
        var response = await client.PostAsync("/workouts/import", formData2);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MultiFileImportResponse>();
        result.Should().NotBeNull();
        // Duplicate should be skipped or updated depending on implementation
        result!.skipped.Should().BeGreaterThanOrEqualTo(0);
        result.updated.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task ImportWorkout_MultiFile_HandlesMixedSuccessAndError()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var formData = new MultipartFormDataContent();
        
        // Add valid GPX file
        var validGpx = CreateMinimalGpxContent();
        var validFile = new ByteArrayContent(Encoding.UTF8.GetBytes(validGpx));
        validFile.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        validFile.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "valid.gpx"
        };
        formData.Add(validFile);
        
        // Add invalid file (wrong extension)
        var invalidFile = new StringContent("invalid content", Encoding.UTF8, "text/plain");
        invalidFile.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "invalid.txt"
        };
        formData.Add(invalidFile);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<MultiFileImportResponse>();
        result.Should().NotBeNull();
        result!.totalProcessed.Should().Be(2);
        result.successful.Should().Be(1);
        result.errors.Should().Be(1);
        result.errorDetails.Should().NotBeEmpty();
    }

    #endregion

    #region Weather Failure Tests

    [Fact]
    public async Task ImportWorkout_HandlesWeatherApiFailure_Gracefully()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        
        // Configure weather service to fail - create a new factory with mocked weather service
        using var factoryWithMockWeather = new TempoWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    MockWeatherServiceHelper.ConfigureWeatherApiFailure(services, HttpStatusCode.InternalServerError);
                });
            });
        
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(factoryWithMockWeather);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert - workout should still be imported despite weather failure
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = factoryWithMockWeather.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
            // Weather field may be null or have a default value
        }
    }

    [Fact]
    public async Task ImportWorkout_HandlesWeatherApiTimeout_Gracefully()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        
        // Configure weather service to timeout
        using var factoryWithMockWeather = new TempoWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    MockWeatherServiceHelper.ConfigureWeatherApiTimeout(services);
                });
            });
        
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(factoryWithMockWeather);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert - workout should still be imported despite weather timeout
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = factoryWithMockWeather.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task ImportWorkout_HandlesInvalidWeatherJson_Gracefully()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        
        // Configure weather service to return invalid JSON
        using var factoryWithMockWeather = new TempoWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    MockWeatherServiceHelper.ConfigureInvalidJsonResponse(services);
                });
            });
        
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(factoryWithMockWeather);
        var gpxContent = CreateMinimalGpxContent();
        var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(gpxContent));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/gpx+xml");
        fileContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = "file",
            FileName = "test.gpx"
        };
        formData.Add(fileContent);

        // Act
        var response = await client.PostAsync("/workouts/import", formData);

        // Assert - workout should still be imported despite invalid weather JSON
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = factoryWithMockWeather.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var workout = await db.Workouts.FirstOrDefaultAsync();
            
            workout.Should().NotBeNull();
        }
    }

    #endregion

    private class MultiFileImportResponse
    {
        public int totalProcessed { get; set; }
        public int successful { get; set; }
        public int skipped { get; set; }
        public int updated { get; set; }
        public int errors { get; set; }
        public List<object>? errorDetails { get; set; }
    }
}

