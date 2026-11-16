using System.Text.Json;

namespace Tempo.Api.Services;

/// <summary>
/// Service for extracting device information from FIT files.
/// </summary>
public static class DeviceExtractionService
{
    /// <summary>
    /// Maps Apple Watch identifiers to friendly device names.
    /// Based on AppleDB device information: https://appledb.dev/device-selection/Apple-Watch.html
    /// </summary>
    public static string? MapAppleWatchIdentifier(string? identifier)
    {
        // Normalize identifier (remove any extra whitespace, case-insensitive)
        var normalized = identifier?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        // Apple Watch identifier pattern: "Watch" followed by number, comma, number
        // Try exact match first, then partial match
        return normalized switch
        {
            // Watch 7 series (S10/S9)
            "Watch7,12" => "Apple Watch Ultra 3",
            "Watch7,17" or "Watch7,18" or "Watch7,19" => "Apple Watch Series 11",
            "Watch7,13" or "Watch7,14" or "Watch7,15" => "Apple Watch SE 3",
            "Watch7,8" or "Watch7,9" or "Watch7,10" => "Apple Watch Series 10",
            "Watch7,5" => "Apple Watch Ultra 2",
            "Watch7,1" or "Watch7,2" or "Watch7,3" => "Apple Watch Series 9",
            
            // Watch 6 series (S8/S7/S6)
            "Watch6,18" => "Apple Watch Ultra",
            "Watch6,14" or "Watch6,15" or "Watch6,16" => "Apple Watch Series 8",
            "Watch6,10" or "Watch6,11" or "Watch6,12" => "Apple Watch SE (2nd generation)",
            "Watch6,6" or "Watch6,7" or "Watch6,8" => "Apple Watch Series 7",
            "Watch6,1" or "Watch6,2" or "Watch6,3" => "Apple Watch Series 6",
            
            // Watch 5 series (S5)
            "Watch5,9" or "Watch5,10" or "Watch5,11" => "Apple Watch SE (1st generation)",
            "Watch5,1" or "Watch5,2" or "Watch5,3" => "Apple Watch Series 5",
            
            // Watch 4 series (S4)
            "Watch4,1" or "Watch4,2" or "Watch4,3" => "Apple Watch Series 4",
            
            // Watch 3 series (S3)
            "Watch3,1" or "Watch3,2" or "Watch3,3" => "Apple Watch Series 3",
            
            // Watch 2 series (S2/S1P)
            "Watch2,3" or "Watch2,4" => "Apple Watch Series 2",
            "Watch2,6" or "Watch2,7" => "Apple Watch Series 1",
            
            // Watch 1 series (S1)
            "Watch1,1" or "Watch1,2" => "Apple Watch (1st generation)",
            
            _ => null
        };
    }

    /// <summary>
    /// Extracts device name from FIT device JSON element.
    /// A product name string wins; otherwise manufacturer and product IDs are looked up in the FIT
    /// SDK (<see cref="FitProductNames"/>).
    /// </summary>
    public static string? ExtractDeviceName(JsonElement deviceElement, ILogger? logger = null)
    {
        var productName = ExtractProductName(deviceElement, logger);
        if (productName != null)
        {
            return productName;
        }

        ushort? manufacturerCode = null;
        string? manufacturer = null;
        if (deviceElement.TryGetProperty("manufacturer", out var manufacturerElement))
        {
            if (manufacturerElement.ValueKind == JsonValueKind.String)
            {
                manufacturer = NullIfBlank(manufacturerElement.GetString());
            }
            else if (TryGetUShort(manufacturerElement, out var code))
            {
                manufacturerCode = code;
                manufacturer = FitProductNames.ManufacturerName(code);
                if (manufacturer == null)
                {
                    logger?.LogDebug("Unknown manufacturer code: {Code}", code);
                }
            }
        }

        ushort? productCode = null;
        if (deviceElement.TryGetProperty("product", out var productElement))
        {
            if (productElement.ValueKind == JsonValueKind.String)
            {
                var productString = NullIfBlank(productElement.GetString());
                if (productString != null)
                {
                    return manufacturer != null ? $"{manufacturer} {productString}" : productString;
                }
            }
            else if (TryGetUShort(productElement, out var code))
            {
                productCode = code;
            }
        }

        if (productCode is { } product)
        {
            if (manufacturerCode is { } mfr && FitProductNames.UsesGarminProductIds(mfr))
            {
                return FitProductNames.GarminProductName(product)
                       ?? $"{manufacturer ?? "Garmin"} (product {product})";
            }

            return manufacturer ?? $"Product {product}";
        }

        if (manufacturer == null)
        {
            logger?.LogDebug("No device information extracted from FIT file");
        }

        return manufacturer;
    }

    /// <summary>
    /// Extracts product name from device element, mapping Apple Watch identifiers if needed.
    /// </summary>
    private static string? ExtractProductName(JsonElement deviceElement, ILogger? logger)
    {
        if (!deviceElement.TryGetProperty("productName", out var productNameElement) || 
            productNameElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var productName = productNameElement.GetString();
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        // Check if ProductName is an Apple Watch identifier and map it to friendly name
        var mappedName = MapAppleWatchIdentifier(productName);
        if (mappedName != null)
        {
            logger?.LogDebug("Mapped Apple Watch identifier {Identifier} to {FriendlyName}", productName, mappedName);
            return mappedName;
        }
        
        logger?.LogDebug("Using ProductName from FIT file: {ProductName}", productName);
        return productName;
    }

    private static bool TryGetUShort(JsonElement element, out ushort value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetUInt16(out value);
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
