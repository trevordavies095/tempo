using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Tempo.Api.Tests.Infrastructure;
using Xunit;

namespace Tempo.Api.Tests.IntegrationTests;

/// <summary>
/// Integration tests for workout media endpoints
/// </summary>
[Collection("Integration Tests")]
public class WorkoutMediaTests : IClassFixture<TempoWebApplicationFactory>
{
    private readonly TempoWebApplicationFactory _factory;

    public WorkoutMediaTests(TempoWebApplicationFactory factory)
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
    /// Creates a minimal valid JPEG file content for testing
    /// </summary>
    private static byte[] CreateMinimalJpeg()
    {
        // Minimal valid JPEG header
        return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
    }

    /// <summary>
    /// Creates a minimal valid PNG file content for testing
    /// </summary>
    private static byte[] CreateMinimalPng()
    {
        // Minimal valid PNG header
        return new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithValidFile_UploadsSuccessfully()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var fileContent = CreateMinimalJpeg();
        var content = new MultipartFormDataContent();
        var fileStream = new MemoryStream(fileContent);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent, "files", "test.jpg");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // When no errors, the endpoint returns a list directly, not wrapped in an object
        var result = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        result[0].filename.Should().Be("test.jpg");
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithMultipleFiles_UploadsAll()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var content = new MultipartFormDataContent();
        var file1Content = CreateMinimalJpeg();
        var file1Stream = new MemoryStream(file1Content);
        var streamContent1 = new StreamContent(file1Stream);
        streamContent1.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent1, "files", "test1.jpg");

        var file2Content = CreateMinimalPng();
        var file2Stream = new MemoryStream(file2Content);
        var streamContent2 = new StreamContent(file2Stream);
        streamContent2.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(streamContent2, "files", "test2.png");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // When no errors, the endpoint returns a list directly
        var result = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result.Select(m => m.filename).Should().Contain(new[] { "test1.jpg", "test2.png" });
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithInvalidFileType_ReturnsError()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var fileContent = System.Text.Encoding.UTF8.GetBytes("This is not an image");
        var content = new MultipartFormDataContent();
        var fileStream = new MemoryStream(fileContent);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/plain");
        content.Add(streamContent, "files", "test.txt");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithOversizedFile_ReturnsError()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        // Create a file larger than 50MB
        var oversizedContent = new byte[52_428_801]; // 50MB + 1 byte
        var content = new MultipartFormDataContent();
        var fileStream = new MemoryStream(oversizedContent);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent, "files", "large.jpg");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithNonExistentWorkout_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentWorkoutId = Guid.NewGuid();

        var fileContent = CreateMinimalJpeg();
        var content = new MultipartFormDataContent();
        var fileStream = new MemoryStream(fileContent);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        content.Add(streamContent, "files", "test.jpg");

        // Act
        var response = await client.PostAsync($"/workouts/{nonExistentWorkoutId}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithoutFiles_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        // Create multipart form with no files (empty form causes exception, so we need to handle it)
        var content = new MultipartFormDataContent();
        // Add a dummy field to make it a valid multipart form
        content.Add(new StringContent("dummy"), "dummy");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithNonMultipartForm_ReturnsBadRequest()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var content = new StringContent("test", System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        // The endpoint checks HasFormContentType and returns BadRequest, but ASP.NET Core
        // may return UnsupportedMediaType (415) before the endpoint is reached
        response.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.UnsupportedMediaType);
    }

    [Fact]
    public async Task ListWorkoutMedia_WithMedia_ReturnsAllMedia()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            // Get media directory from config
            var config = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            await TestDataSeeder.SeedWorkoutWithMediaAsync(db, workout, config.RootPath, count: 2);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/media");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListWorkoutMedia_WithNoMedia_ReturnsEmptyList()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/media");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public async Task ListWorkoutMedia_WithNonExistentWorkout_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentWorkoutId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/workouts/{nonExistentWorkoutId}/media");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkoutMediaFile_WithValidMedia_ReturnsFile()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        WorkoutMedia media;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            var mediaService = scope.ServiceProvider.GetRequiredService<MediaService>();
            var config = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            await TestDataSeeder.SeedWorkoutWithMediaAsync(db, workout, config.RootPath, count: 1);
            
            media = await db.WorkoutMedia.FirstAsync(m => m.WorkoutId == workout.Id);
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/media/{media.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/jpeg");
        response.Content.Headers.ContentDisposition!.FileName.Should().Be(media.Filename);
    }

    [Fact]
    public async Task GetWorkoutMediaFile_WithNonExistentWorkout_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentWorkoutId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/workouts/{nonExistentWorkoutId}/media/{mediaId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkoutMediaFile_WithNonExistentMedia_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }
        var nonExistentMediaId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/media/{nonExistentMediaId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetWorkoutMediaFile_WithMissingFile_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        WorkoutMedia media;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            // Create media record with non-existent file path
            media = new WorkoutMedia
            {
                WorkoutId = workout.Id,
                Filename = "missing.jpg",
                FilePath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.jpg"),
                MimeType = "image/jpeg",
                FileSizeBytes = 1024,
                CreatedAt = DateTime.UtcNow
            };
            db.WorkoutMedia.Add(media);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.GetAsync($"/workouts/{workout.Id}/media/{media.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWorkoutMedia_WithValidMedia_DeletesFileAndRecord()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        WorkoutMedia media;
        string mediaFilePath;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            var config = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            await TestDataSeeder.SeedWorkoutWithMediaAsync(db, workout, config.RootPath, count: 1);
            
            media = await db.WorkoutMedia.FirstAsync(m => m.WorkoutId == workout.Id);
            mediaFilePath = media.FilePath;
            
            // Verify file exists before deletion
            File.Exists(mediaFilePath).Should().BeTrue();
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}/media/{media.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify file is deleted
        File.Exists(mediaFilePath).Should().BeFalse();
        
        // Verify database record is deleted
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var deletedMedia = await db.WorkoutMedia.FindAsync(media.Id);
            deletedMedia.Should().BeNull();
        }
    }

    [Fact]
    public async Task DeleteWorkoutMedia_WithNonExistentWorkout_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        var nonExistentWorkoutId = Guid.NewGuid();
        var mediaId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/workouts/{nonExistentWorkoutId}/media/{mediaId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWorkoutMedia_WithNonExistentMedia_ReturnsNotFound()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }
        var nonExistentMediaId = Guid.NewGuid();

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}/media/{nonExistentMediaId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWorkoutMedia_WithMissingFile_StillDeletesRecord()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        WorkoutMedia media;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
            
            // Create media record with non-existent file path (orphaned record)
            media = new WorkoutMedia
            {
                WorkoutId = workout.Id,
                Filename = "orphaned.jpg",
                FilePath = Path.Combine(Path.GetTempPath(), $"orphaned-{Guid.NewGuid()}.jpg"),
                MimeType = "image/jpeg",
                FileSizeBytes = 1024,
                CreatedAt = DateTime.UtcNow
            };
            db.WorkoutMedia.Add(media);
            await db.SaveChangesAsync();
        }

        // Act
        var response = await client.DeleteAsync($"/workouts/{workout.Id}/media/{media.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        
        // Verify database record is still deleted even though file was missing
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var deletedMedia = await db.WorkoutMedia.FindAsync(media.Id);
            deletedMedia.Should().BeNull();
        }
    }

    [Fact]
    public async Task UploadWorkoutMedia_WithPathTraversalAttempt_PreventsAttack()
    {
        // Arrange
        await EnsureCleanDatabaseAsync();
        var client = await TestHttpClientFactory.CreateAuthenticatedClientAsync(_factory);
        
        Workout workout;
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            workout = await TestDataSeeder.SeedWorkoutAsync(db);
        }

        var fileContent = CreateMinimalJpeg();
        var content = new MultipartFormDataContent();
        var fileStream = new MemoryStream(fileContent);
        var streamContent = new StreamContent(fileStream);
        streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        // Attempt path traversal attack
        content.Add(streamContent, "files", "../../../etc/passwd.jpg");

        // Act
        var response = await client.PostAsync($"/workouts/{workout.Id}/media", content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // When no errors, the endpoint returns a list directly
        var result = await response.Content.ReadFromJsonAsync<List<MediaResponse>>();
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
        
        // Verify the file was saved in correct directory (prevents directory traversal)
        using (var scope = _factory.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TempoDbContext>();
            var media = await db.WorkoutMedia.FirstOrDefaultAsync(m => m.WorkoutId == workout.Id);
            media.Should().NotBeNull();
            
            var config = scope.ServiceProvider.GetRequiredService<MediaStorageConfig>();
            var expectedDir = Path.Combine(config.RootPath, workout.Id.ToString());
            
            // Verify the file path doesn't allow directory traversal
            media!.FilePath.Should().StartWith(expectedDir);
            Path.GetDirectoryName(media.FilePath)!.Should().Be(expectedDir);
            
            // Verify the stored filename was sanitized (path separators removed)
            var storedFileName = Path.GetFileName(media.FilePath);
            storedFileName.Should().NotContain("/");
        }
    }

    private class MediaResponse
    {
        public Guid id { get; set; }
        public string filename { get; set; } = string.Empty;
        public string mimeType { get; set; } = string.Empty;
        public long fileSizeBytes { get; set; }
        public string? caption { get; set; }
        public DateTime createdAt { get; set; }
    }
}

