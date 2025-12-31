using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Tempo.Api.Models;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

/// <summary>
/// Unit tests for MediaService
/// </summary>
public class MediaServiceTests : IDisposable
{
    private readonly string _tempMediaDirectory;
    private readonly MediaStorageConfig _config;
    private readonly ILogger<MediaService> _logger;
    private readonly MediaService _service;

    public MediaServiceTests()
    {
        // Create temporary directory for media storage
        _tempMediaDirectory = Path.Combine(Path.GetTempPath(), $"tempo-test-media-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempMediaDirectory);

        _config = new MediaStorageConfig
        {
            RootPath = _tempMediaDirectory,
            MaxFileSizeBytes = 52_428_800 // 50MB
        };

        var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        _logger = loggerFactory.CreateLogger<MediaService>();

        _service = new MediaService(_config, _logger);
    }

    public void Dispose()
    {
        // Clean up temporary directory
        if (Directory.Exists(_tempMediaDirectory))
        {
            try
            {
                Directory.Delete(_tempMediaDirectory, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    [Theory]
    [InlineData("test.jpg")]
    [InlineData("test.jpeg")]
    [InlineData("test.png")]
    [InlineData("test.gif")]
    [InlineData("test.webp")]
    [InlineData("test.mp4")]
    [InlineData("test.mov")]
    [InlineData("test.avi")]
    public void IsSupportedFileType_WithValidExtensions_ReturnsTrue(string filename)
    {
        // Act
        var result = _service.IsSupportedFileType(filename);

        // Assert
        result.Should().BeTrue();
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.pdf")]
    [InlineData("test.doc")]
    [InlineData("test.exe")]
    [InlineData("test")]
    [InlineData("test.")]
    public void IsSupportedFileType_WithInvalidExtension_ReturnsFalse(string filename)
    {
        // Act
        var result = _service.IsSupportedFileType(filename);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("test.JPG")]
    [InlineData("test.JPEG")]
    [InlineData("test.PNG")]
    [InlineData("test.MP4")]
    public void IsSupportedFileType_WithCaseInsensitive_HandlesCorrectly(string filename)
    {
        // Act
        var result = _service.IsSupportedFileType(filename);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateFileSize_WithinLimit_ReturnsTrue()
    {
        // Arrange
        var fileSize = 10_000_000; // 10MB

        // Act
        var result = _service.ValidateFileSize(fileSize);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateFileSize_AtLimit_ReturnsTrue()
    {
        // Arrange
        var fileSize = _config.MaxFileSizeBytes; // Exactly at limit

        // Act
        var result = _service.ValidateFileSize(fileSize);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateFileSize_ExceedsLimit_ReturnsFalse()
    {
        // Arrange
        var fileSize = _config.MaxFileSizeBytes + 1; // Over limit

        // Act
        var result = _service.ValidateFileSize(fileSize);

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData("test.jpg", "image/jpeg")]
    [InlineData("test.jpeg", "image/jpeg")]
    [InlineData("test.png", "image/png")]
    [InlineData("test.gif", "image/gif")]
    [InlineData("test.webp", "image/webp")]
    [InlineData("test.mp4", "video/mp4")]
    [InlineData("test.mov", "video/quicktime")]
    [InlineData("test.avi", "video/x-msvideo")]
    public void GetMimeType_WithValidExtensions_ReturnsCorrectMimeType(string filename, string expectedMimeType)
    {
        // Act
        var result = _service.GetMimeType(filename);

        // Assert
        result.Should().Be(expectedMimeType);
    }

    [Theory]
    [InlineData("test.txt")]
    [InlineData("test.unknown")]
    [InlineData("test")]
    public void GetMimeType_WithUnknownExtension_ReturnsOctetStream(string filename)
    {
        // Act
        var result = _service.GetMimeType(filename);

        // Assert
        result.Should().Be("application/octet-stream");
    }

    [Fact]
    public void GenerateFilePath_CreatesWorkoutDirectory()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";

        // Act
        var result = _service.GenerateFilePath(workoutId, filename);

        // Assert
        var workoutDir = Path.Combine(_tempMediaDirectory, workoutId.ToString());
        Directory.Exists(workoutDir).Should().BeTrue();
        result.Should().StartWith(workoutDir);
    }

    [Fact]
    public void GenerateFilePath_SanitizesFilename()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        // Use characters that are definitely invalid on all platforms (path separators)
        var filename = "test/\\:*.jpg"; // Contains path separators and other invalid characters
        var invalidChars = Path.GetInvalidFileNameChars();

        // Act
        var result = _service.GenerateFilePath(workoutId, filename);

        // Assert
        var fileName = Path.GetFileName(result);
        fileName.Should().EndWith(".jpg");
        
        // Verify that forward slashes are removed (invalid on all platforms)
        fileName.Should().NotContain("/");
        
        // Verify it was sanitized (should be different from original if it had invalid chars)
        if (filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            fileName.Should().NotBe(filename);
        }
        
        // Verify the file path is within the workout directory (prevents directory traversal)
        result.Should().StartWith(Path.Combine(_tempMediaDirectory, workoutId.ToString()));
    }

    [Fact]
    public void GenerateFilePath_WithConflictingFilename_AddsGuid()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";
        var workoutDir = Path.Combine(_tempMediaDirectory, workoutId.ToString());
        Directory.CreateDirectory(workoutDir);
        var existingFile = Path.Combine(workoutDir, filename);
        File.WriteAllText(existingFile, "existing");

        // Act
        var result = _service.GenerateFilePath(workoutId, filename);

        // Assert
        result.Should().NotBe(existingFile);
        result.Should().Contain("test_");
        result.Should().EndWith(".jpg");
        Path.GetFileName(result).Should().MatchRegex(@"^test_[a-f0-9]{32}\.jpg$");
    }

    [Fact]
    public void GenerateFilePath_PreventsDirectoryTraversal()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "../../../etc/passwd.jpg"; // Path traversal attempt

        // Act
        var result = _service.GenerateFilePath(workoutId, filename);

        // Assert
        // Should sanitize the filename and prevent traversal
        // The path should be within the workout directory
        result.Should().StartWith(Path.Combine(_tempMediaDirectory, workoutId.ToString()));
        // After sanitization, "/" becomes "_", so ".." becomes "_" when split
        // The important thing is that the path doesn't allow directory traversal
        var fileName = Path.GetFileName(result);
        fileName.Should().NotContain("/");
        fileName.Should().NotContain("\\");
        // Verify the file is actually in the workout directory (not parent directories)
        var workoutDir = Path.Combine(_tempMediaDirectory, workoutId.ToString());
        Path.GetDirectoryName(result)!.Should().Be(workoutDir);
    }

    [Fact]
    public void GenerateFilePath_WithLongFilename_Truncates()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var longName = new string('a', 250); // 250 characters
        var filename = $"{longName}.jpg"; // Total > 200 chars

        // Act
        var result = _service.GenerateFilePath(workoutId, filename);

        // Assert
        var fileName = Path.GetFileName(result);
        fileName.Length.Should().BeLessThanOrEqualTo(200);
        fileName.Should().EndWith(".jpg");
    }

    [Fact]
    public void UploadMediaFile_WithValidFile_CreatesMediaRecord()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";
        var fileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // Minimal JPEG header
        var stream = new MemoryStream(fileContent);
        var formFile = new FormFile(stream, 0, fileContent.Length, "file", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        // Act
        var result = _service.UploadMediaFile(formFile, workoutId);

        // Assert
        result.Should().NotBeNull();
        result!.WorkoutId.Should().Be(workoutId);
        result.Filename.Should().Be(filename);
        result.FileSizeBytes.Should().Be(fileContent.Length);
        result.MimeType.Should().Be("image/jpeg");
        File.Exists(result.FilePath).Should().BeTrue();
    }

    [Fact]
    public void UploadMediaFile_WithInvalidFileType_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.txt";
        var fileContent = new byte[] { 0x01, 0x02, 0x03 };
        var stream = new MemoryStream(fileContent);
        var formFile = new FormFile(stream, 0, fileContent.Length, "file", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = "text/plain"
        };

        // Act
        var result = _service.UploadMediaFile(formFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void UploadMediaFile_WithOversizedFile_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";
        var oversizedSize = _config.MaxFileSizeBytes + 1;
        var stream = new MemoryStream(new byte[oversizedSize]);
        var formFile = new FormFile(stream, 0, oversizedSize, "file", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        // Act
        var result = _service.UploadMediaFile(formFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void UploadMediaFile_WithEmptyFile_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";
        var stream = new MemoryStream();
        var formFile = new FormFile(stream, 0, 0, "file", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };

        // Act
        var result = _service.UploadMediaFile(formFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void UploadMediaFile_WithNullFile_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();

        // Act
        var result = _service.UploadMediaFile(null!, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CopyMediaFile_WithValidFile_CopiesAndCreatesRecord()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var sourceDir = Path.Combine(_tempMediaDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "test.jpg");
        var fileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        File.WriteAllBytes(sourceFile, fileContent);

        // Act
        var result = _service.CopyMediaFile(sourceFile, workoutId, "Test caption");

        // Assert
        result.Should().NotBeNull();
        result!.WorkoutId.Should().Be(workoutId);
        result.Filename.Should().Be("test.jpg");
        result.FileSizeBytes.Should().Be(fileContent.Length);
        result.MimeType.Should().Be("image/jpeg");
        result.Caption.Should().Be("Test caption");
        File.Exists(result.FilePath).Should().BeTrue();
        File.ReadAllBytes(result.FilePath).Should().BeEquivalentTo(fileContent);
    }

    [Fact]
    public void CopyMediaFile_WithMissingFile_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var nonExistentFile = Path.Combine(_tempMediaDirectory, "nonexistent.jpg");

        // Act
        var result = _service.CopyMediaFile(nonExistentFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CopyMediaFile_WithInvalidFileType_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var sourceDir = Path.Combine(_tempMediaDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "test.txt");
        File.WriteAllText(sourceFile, "test content");

        // Act
        var result = _service.CopyMediaFile(sourceFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void CopyMediaFile_WithOversizedFile_ReturnsNull()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var sourceDir = Path.Combine(_tempMediaDirectory, "source");
        Directory.CreateDirectory(sourceDir);
        var sourceFile = Path.Combine(sourceDir, "test.jpg");
        // Create a file larger than the limit
        var oversizedContent = new byte[_config.MaxFileSizeBytes + 1];
        File.WriteAllBytes(sourceFile, oversizedContent);

        // Act
        var result = _service.CopyMediaFile(sourceFile, workoutId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void GenerateFilePath_WithPathTraversalInFilename_PreventsAttack()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var maliciousFilenames = new[]
        {
            "../../../etc/passwd.jpg",
            "..\\..\\..\\windows\\system32\\config\\sam.jpg",
            "....//....//etc/passwd.jpg",
            "..%2F..%2F..%2Fetc%2Fpasswd.jpg"
        };

        foreach (var filename in maliciousFilenames)
        {
            // Act
            var result = _service.GenerateFilePath(workoutId, filename);

            // Assert
            // The path should be within the workout directory (prevents traversal)
            var workoutDir = Path.Combine(_tempMediaDirectory, workoutId.ToString());
            result.Should().StartWith(workoutDir);
            
            // Verify the file is actually in the workout directory (not parent directories)
            var resultDir = Path.GetDirectoryName(result)!;
            resultDir.Should().Be(workoutDir);
            
            var fileName = Path.GetFileName(result);
            // Forward slashes should be removed (invalid on all platforms)
            fileName.Should().NotContain("/");
            fileName.Should().EndWith(".jpg");
            
            // The important security check: verify the file is in the correct directory
            // This prevents directory traversal regardless of which characters are in the filename
        }
    }

    [Fact]
    public void UploadMediaFile_WithCaption_SetsCaption()
    {
        // Arrange
        var workoutId = Guid.NewGuid();
        var filename = "test.jpg";
        var fileContent = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var stream = new MemoryStream(fileContent);
        var formFile = new FormFile(stream, 0, fileContent.Length, "file", filename)
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
        var caption = "My test photo";

        // Act
        var result = _service.UploadMediaFile(formFile, workoutId, caption);

        // Assert
        result.Should().NotBeNull();
        result!.Caption.Should().Be(caption);
    }
}

