using Microsoft.AspNetCore.Http;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public class MediaService
{
    private readonly MediaStorageConfig _config;
    private readonly ILogger<MediaService> _logger;

    private static readonly Dictionary<string, string> MimeTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".jpg", "image/jpeg" },
        { ".jpeg", "image/jpeg" },
        { ".png", "image/png" },
        { ".gif", "image/gif" },
        { ".webp", "image/webp" },
        { ".mp4", "video/mp4" },
        { ".mov", "video/quicktime" },
        { ".avi", "video/x-msvideo" }
    };

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".mov", ".avi"
    };

    public MediaService(MediaStorageConfig config, ILogger<MediaService> logger)
    {
        _config = config;
        _logger = logger;
    }

    public string GetMimeType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return MimeTypeMap.TryGetValue(extension, out var mimeType) 
            ? mimeType 
            : "application/octet-stream";
    }

    public bool IsSupportedFileType(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(extension);
    }

    public bool ValidateFileSize(long fileSizeBytes)
    {
        return fileSizeBytes <= _config.MaxFileSizeBytes;
    }

    public string GenerateFilePath(Guid workoutId, string originalFilename)
    {
        // Create workout-specific directory
        var workoutDir = Path.Combine(_config.RootPath, workoutId.ToString());
        Directory.CreateDirectory(workoutDir);

        // Sanitize filename
        var safeFilename = SanitizeFilename(originalFilename);
        
        // Check for conflicts and add GUID if needed
        var filePath = Path.Combine(workoutDir, safeFilename);
        if (File.Exists(filePath))
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(safeFilename);
            var extension = Path.GetExtension(safeFilename);
            safeFilename = $"{nameWithoutExt}_{Guid.NewGuid():N}{extension}";
            filePath = Path.Combine(workoutDir, safeFilename);
        }

        return filePath;
    }

    public WorkoutMedia? CopyMediaFile(
        string sourceFilePath,
        Guid workoutId,
        string? caption = null)
    {
        try
        {
            // Validate file exists
            if (!File.Exists(sourceFilePath))
            {
                _logger.LogWarning("Media file not found: {FilePath}", sourceFilePath);
                return null;
            }

            // Get file info
            var fileInfo = new FileInfo(sourceFilePath);
            var originalFilename = Path.GetFileName(sourceFilePath);

            // Validate file type
            if (!IsSupportedFileType(originalFilename))
            {
                _logger.LogWarning("Unsupported media file type: {FilePath}", sourceFilePath);
                return null;
            }

            // Validate file size
            if (!ValidateFileSize(fileInfo.Length))
            {
                _logger.LogWarning("Media file exceeds size limit: {FilePath} ({Size} bytes)", 
                    sourceFilePath, fileInfo.Length);
                return null;
            }

            // Generate destination path
            var destinationPath = GenerateFilePath(workoutId, originalFilename);
            var mimeType = GetMimeType(originalFilename);

            // Copy file
            File.Copy(sourceFilePath, destinationPath, overwrite: false);

            _logger.LogInformation("Copied media file: {Source} -> {Destination}", 
                sourceFilePath, destinationPath);

            // Create WorkoutMedia record
            return new WorkoutMedia
            {
                Id = Guid.NewGuid(),
                WorkoutId = workoutId,
                Filename = originalFilename,
                FilePath = destinationPath,
                MimeType = mimeType,
                FileSizeBytes = fileInfo.Length,
                Caption = caption,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error copying media file: {FilePath}", sourceFilePath);
            return null;
        }
    }

    public WorkoutMedia? UploadMediaFile(
        IFormFile file,
        Guid workoutId,
        string? caption = null)
    {
        try
        {
            // Validate file is provided
            if (file == null || file.Length == 0)
            {
                _logger.LogWarning("No file provided for upload");
                return null;
            }

            var originalFilename = file.FileName;

            // Validate file type
            if (!IsSupportedFileType(originalFilename))
            {
                _logger.LogWarning("Unsupported media file type: {FileName}", originalFilename);
                return null;
            }

            // Validate file size
            if (!ValidateFileSize(file.Length))
            {
                _logger.LogWarning("Media file exceeds size limit: {FileName} ({Size} bytes)", 
                    originalFilename, file.Length);
                return null;
            }

            // Generate destination path
            var destinationPath = GenerateFilePath(workoutId, originalFilename);
            var mimeType = GetMimeType(originalFilename);

            // Save file from stream
            using (var fileStream = new FileStream(destinationPath, FileMode.Create))
            {
                file.CopyTo(fileStream);
            }

            _logger.LogInformation("Uploaded media file: {FileName} -> {Destination}", 
                originalFilename, destinationPath);

            // Create WorkoutMedia record
            return new WorkoutMedia
            {
                Id = Guid.NewGuid(),
                WorkoutId = workoutId,
                Filename = originalFilename,
                FilePath = destinationPath,
                MimeType = mimeType,
                FileSizeBytes = file.Length,
                Caption = caption,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading media file: {FileName}", file?.FileName);
            return null;
        }
    }

    private static string SanitizeFilename(string filename)
    {
        // Remove path separators and other dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", filename.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        
        // Limit length
        if (sanitized.Length > 200)
        {
            var extension = Path.GetExtension(sanitized);
            var nameWithoutExt = Path.GetFileNameWithoutExtension(sanitized);
            sanitized = nameWithoutExt.Substring(0, 200 - extension.Length) + extension;
        }

        return sanitized;
    }
}

