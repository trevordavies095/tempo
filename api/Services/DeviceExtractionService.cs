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
    public static string? MapAppleWatchIdentifier(string identifier)
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
    /// According to FIT spec: ProductName is most reliable, then manufacturer+product codes.
    /// </summary>
    public static string? ExtractDeviceName(JsonElement deviceElement, ILogger? logger = null)
    {
        // 1. Check ProductName first (most reliable - actual device name string)
        var productName = ExtractProductName(deviceElement, logger);
        if (productName != null)
        {
            return productName;
        }

        // 2. Extract manufacturer and product code
        var manufacturer = ExtractManufacturerCode(deviceElement, logger);
        var productCode = ExtractProductCode(deviceElement, manufacturer, out var productString);

        // 3. If product is a string, combine with manufacturer
        if (productString != null)
        {
            return CombineDeviceInfo(manufacturer, productString);
        }

        // 4. Combine manufacturer and product code
        return CombineDeviceInfo(manufacturer, productCode, logger);
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

    /// <summary>
    /// Extracts manufacturer name from device element.
    /// </summary>
    private static string? ExtractManufacturerCode(JsonElement deviceElement, ILogger? logger)
    {
        if (!deviceElement.TryGetProperty("manufacturer", out var manufacturerElement))
        {
            return null;
        }

        if (manufacturerElement.ValueKind == JsonValueKind.String)
        {
            return manufacturerElement.GetString();
        }

        if (manufacturerElement.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        var manufacturerCode = manufacturerElement.GetInt32();
        
        // Handle extended manufacturer codes (>255 are manufacturer-specific)
        if (manufacturerCode > 255)
        {
            return HandleExtendedManufacturerCode(deviceElement, manufacturerCode, logger);
        }

        // Standard manufacturer codes (0-255)
        var manufacturer = manufacturerCode switch
        {
            1 => "Garmin",
            2 => "Garmin", // GarminFr405Antfs
            23 => "Suunto",
            32 => "Wahoo Fitness",
            71 => "TomTom",
            73 => "Wattbike",
            94 => "Stryd",
            123 => "Polar",
            129 => "Coros",
            142 => "Tag Heuer",
            144 => "Zwift",
            182 => "Strava",
            265 => "Strava",
            294 => "Coros",
            // 255 is "Development" in FIT spec but not helpful for end users
            255 => null,
            _ => null
        };
        
        if (manufacturer == null)
        {
            logger?.LogDebug("Unknown manufacturer code: {Code}", manufacturerCode);
        }

        return manufacturer;
    }

    /// <summary>
    /// Handles extended manufacturer codes (>255).
    /// </summary>
    private static string? HandleExtendedManufacturerCode(JsonElement deviceElement, int manufacturerCode, ILogger? logger)
    {
        // Extended manufacturer codes: check if it might be Garmin
        // Many Garmin devices use extended codes, but we can't definitively identify them
        // For now, if we have a product code that looks like a Garmin product, assume Garmin
        if (!deviceElement.TryGetProperty("product", out var prodCheck) || 
            prodCheck.ValueKind != JsonValueKind.Number)
        {
            logger?.LogDebug("Extended manufacturer code: {Code} (unknown manufacturer)", manufacturerCode);
            return null;
        }

        var prod = prodCheck.GetInt32();
        // Garmin products typically have specific ranges
        // If product code is reasonable (< 10000), might be Garmin
        if (prod < 10000)
        {
            logger?.LogDebug("Extended manufacturer code {Code} with product {Product} - assuming Garmin", 
                manufacturerCode, prod);
            return "Garmin";
        }

        logger?.LogDebug("Extended manufacturer code: {Code} (unknown manufacturer)", manufacturerCode);
        return null;
    }

    /// <summary>
    /// Extracts product code from device element.
    /// </summary>
    /// <returns>Product code as ushort, or null if not found or is a string</returns>
    /// <param name="productString">Output parameter for product string if found</param>
    private static ushort? ExtractProductCode(JsonElement deviceElement, string? manufacturer, out string? productString)
    {
        productString = null;

        if (!deviceElement.TryGetProperty("product", out var productElement))
        {
            return null;
        }

        if (productElement.ValueKind == JsonValueKind.Number)
        {
            return (ushort)productElement.GetInt32();
        }

        if (productElement.ValueKind == JsonValueKind.String)
        {
            // Product as string - use directly
            productString = productElement.GetString();
            return null;
        }

        return null;
    }

    /// <summary>
    /// Combines manufacturer and product information into a device name.
    /// </summary>
    private static string? CombineDeviceInfo(string? manufacturer, string? productString)
    {
        if (!string.IsNullOrWhiteSpace(productString))
        {
            if (!string.IsNullOrWhiteSpace(manufacturer))
            {
                return $"{manufacturer} {productString}".Trim();
            }
            return productString;
        }

        return null;
    }

    /// <summary>
    /// Combines manufacturer and product code into a device name.
    /// </summary>
    private static string? CombineDeviceInfo(string? manufacturer, ushort? productCode, ILogger? logger)
    {
        // Combine manufacturer and product code
        if (!string.IsNullOrWhiteSpace(manufacturer) && productCode.HasValue)
        {
            // For known manufacturers, show manufacturer name
            // For Garmin, we could map product codes, but that's extensive
            // For now, show manufacturer name (cleaner than "Garmin 108")
            return manufacturer;
        }

        // Fallback: show product code if available
        if (productCode.HasValue)
        {
            return $"Product {productCode.Value}";
        }

        // Fallback: show manufacturer if available
        if (!string.IsNullOrWhiteSpace(manufacturer))
        {
            return manufacturer;
        }

        logger?.LogDebug("No device information extracted from FIT file");
        return null;
    }
}

