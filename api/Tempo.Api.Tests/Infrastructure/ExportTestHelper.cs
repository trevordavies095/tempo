using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Tempo.Api.Tests.Infrastructure;

/// <summary>
/// Helper class for creating test ZIP files for import/export testing
/// </summary>
public static class ExportTestHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Creates a valid export ZIP with all required files
    /// </summary>
    public static MemoryStream CreateValidExportZipAsync()
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 1,
                    shoes = 2,
                    workouts = 3,
                    routes = 3,
                    splits = 15,
                    timeSeries = 180,
                    mediaFiles = 0,
                    bestEfforts = 5,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = "data/settings.json",
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory with all required JSON files (empty arrays for minimal valid files)
            CreateJsonFile(archive, "data/settings.json", new { });
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with path traversal attack in manifest paths
    /// </summary>
    public static MemoryStream CreateMaliciousZipWithManifestPathTraversalAsync(string maliciousPath)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest with malicious path
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = maliciousPath, // Malicious path
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with path traversal attack in ZIP entry names
    /// </summary>
    public static MemoryStream CreateMaliciousZipWithEntryPathTraversalAsync(string maliciousEntryName)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create valid manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create malicious ZIP entry
            var maliciousEntry = archive.CreateEntry(maliciousEntryName);
            using (var writer = new StreamWriter(maliciousEntry.Open(), Encoding.UTF8))
            {
                writer.Write("malicious content");
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP without manifest.json
    /// </summary>
    public static MemoryStream CreateZipWithMissingManifestAsync()
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create data directory but no manifest
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with malformed/invalid manifest JSON
    /// </summary>
    public static MemoryStream CreateZipWithInvalidManifestAsync()
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest with invalid JSON
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write("{ invalid json }");
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with manifest missing required fields
    /// </summary>
    public static MemoryStream CreateZipWithIncompleteManifestAsync(string? version = null, bool includeStatistics = true, bool includeDataFormat = true)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestObj = new Dictionary<string, object?>
            {
                ["exportDate"] = DateTime.UtcNow,
                ["exportedBy"] = "test"
            };

            if (version != null)
            {
                manifestObj["version"] = version;
            }

            if (includeStatistics)
            {
                manifestObj["statistics"] = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                };
            }

            if (includeDataFormat)
            {
                manifestObj["dataFormat"] = new
                {
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                };
            }

            var manifestJson = JsonSerializer.Serialize(manifestObj, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with unsupported version
    /// </summary>
    public static MemoryStream CreateZipWithUnsupportedVersionAsync(string version = "2.0.0")
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = new
            {
                version = version, // Unsupported version
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP without data/ directory
    /// </summary>
    public static MemoryStream CreateZipWithMissingDataDirectoryAsync()
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // No data directory created
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with missing required JSON files
    /// </summary>
    public static MemoryStream CreateZipWithMissingFilesAsync(bool missingShoes = false, bool missingWorkouts = false, bool missingRoutes = false)
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = missingShoes ? null : "data/shoes.json",
                    workouts = missingWorkouts ? null : "data/workouts.json",
                    routes = missingRoutes ? null : "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory with only some files
            if (!missingShoes)
            {
                CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            }
            if (!missingWorkouts)
            {
                CreateJsonFile(archive, "data/workouts.json", Array.Empty<object>());
            }
            if (!missingRoutes)
            {
                CreateJsonFile(archive, "data/routes.json", Array.Empty<object>());
            }
            CreateJsonFile(archive, "data/splits.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/time-series.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/media-metadata.json", Array.Empty<object>());
            CreateJsonFile(archive, "data/best-efforts.json", Array.Empty<object>());
        }

        zipStream.Position = 0;
        return zipStream;
    }

    /// <summary>
    /// Creates a ZIP with manifest referencing files that don't exist
    /// </summary>
    public static MemoryStream CreateZipWithMissingReferencedFilesAsync()
    {
        var zipStream = new MemoryStream();
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Create manifest referencing files that don't exist
            var manifest = new
            {
                version = "1.0.0",
                tempoVersion = "1.0.0",
                exportDate = DateTime.UtcNow,
                exportedBy = "test",
                statistics = new
                {
                    settings = 0,
                    shoes = 0,
                    workouts = 0,
                    routes = 0,
                    splits = 0,
                    timeSeries = 0,
                    mediaFiles = 0,
                    bestEfforts = 0,
                    totalSizeBytes = 0L
                },
                dataFormat = new
                {
                    settings = (string?)null,
                    shoes = "data/shoes.json",
                    workouts = "data/workouts.json",
                    routes = "data/routes.json",
                    splits = "data/splits.json",
                    timeSeries = "data/time-series.json",
                    mediaMetadata = "data/media-metadata.json",
                    bestEfforts = "data/best-efforts.json"
                }
            };

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            var manifestEntry = archive.CreateEntry("manifest.json");
            using (var writer = new StreamWriter(manifestEntry.Open(), Encoding.UTF8))
            {
                writer.Write(manifestJson);
            }

            // Create data directory but missing some required files
            CreateJsonFile(archive, "data/shoes.json", Array.Empty<object>());
            // Missing workouts.json, routes.json, etc.
        }

        zipStream.Position = 0;
        return zipStream;
    }

    private static void CreateJsonFile(ZipArchive archive, string entryName, object data)
    {
        var entry = archive.CreateEntry(entryName);
        using (var stream = entry.Open())
        using (var writer = new StreamWriter(stream, Encoding.UTF8))
        {
            var json = JsonSerializer.Serialize(data, JsonOptions);
            writer.Write(json);
        }
    }
}

