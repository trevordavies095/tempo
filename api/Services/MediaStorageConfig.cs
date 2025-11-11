namespace Tempo.Api.Services;

public class MediaStorageConfig
{
    public string RootPath { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; }
}

