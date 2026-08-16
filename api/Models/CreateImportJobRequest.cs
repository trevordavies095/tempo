namespace Tempo.Api.Models;

public class CreateImportJobRequest
{
    public string Filename { get; set; } = string.Empty;

    public long ByteSize { get; set; }

    public string? UnitPreference { get; set; }
}
