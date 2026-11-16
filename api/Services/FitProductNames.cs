using System.Reflection;
using System.Text.RegularExpressions;
using Dynastream.Fit;

namespace Tempo.Api.Services;

/// <summary>
/// Display names for FIT manufacturer and Garmin product IDs, built once from the constants in the
/// <c>Garmin.FIT.Sdk</c> package (<see cref="Manufacturer"/>, <see cref="GarminProduct"/>). New
/// devices arrive with each package bump. Constant names such as <c>Fr965</c> or
/// <c>Fenix7sProSolar</c> are humanised by rule; names the rules get wrong go in the override tables.
/// </summary>
public static partial class FitProductNames
{
    /// <summary>
    /// Manufacturers whose <c>product</c> field holds a <see cref="GarminProduct"/> ID (the FIT
    /// profile's <c>garmin_product</c> subfield): Garmin, Garmin FR405 ANTFS, Dynastream OEM,
    /// Dynastream and Tacx.
    /// </summary>
    private static readonly HashSet<ushort> GarminProductManufacturers =
    [
        Manufacturer.Garmin,
        Manufacturer.GarminFr405Antfs,
        Manufacturer.DynastreamOem,
        Manufacturer.Dynastream,
        Manufacturer.Tacx,
    ];

    /// <summary>Products whose display name is not "Garmin " + the humanised model.</summary>
    private static readonly string[] NonGarminBrandPrefixes = ["Tacx ", "TrainingPeaks "];

    /// <summary>Full display names (without the "Garmin " prefix) keyed by SDK constant name.</summary>
    private static readonly Dictionary<string, string> ProductOverrides = new(StringComparer.Ordinal)
    {
        ["OHR"] = "OHR",
        ["Hrm4Run"] = "HRM-Run",
        ["Hrm4RunSingleByteProductId"] = "HRM-Run",
        ["HrmRun"] = "HRM-Run",
        ["HrmRunSingleByteProductId"] = "HRM-Run",
        ["HrmTri"] = "HRM-Tri",
        ["HrmTriSingleByteProductId"] = "HRM-Tri",
        ["Bsm"] = "Speed Sensor",
        ["Bcm"] = "Cadence Sensor",
        ["Gen3Bsm"] = "Speed Sensor",
        ["Gen3Bcm"] = "Cadence Sensor",
        ["Gen3BsmSingleByteProductId"] = "Speed Sensor",
        ["Gen3BcmSingleByteProductId"] = "Cadence Sensor",
        ["Sdm4"] = "Foot Pod",
        ["Fr310xt4t"] = "Forerunner 310XT",
        ["Fr235lAsia"] = "Forerunner 235",
        ["Fr645m"] = "Forerunner 645 Music",
        ["Fr645mAsia"] = "Forerunner 645 Music",
        ["Fr245mAsia"] = "Forerunner 245 Music",
        ["Fr255Small"] = "Forerunner 255S",
        ["Fr255SmallMusic"] = "Forerunner 255S Music",
        ["Fr265Small"] = "Forerunner 265S",
        ["Fr702026"] = "Forerunner 70 (2026)", // Fr70 (1436) is the 2011 model
        ["Vivoactive3mW"] = "Vivoactive 3 Music",
        ["Vivoactive3mL"] = "Vivoactive 3 Music",
        ["VivoActive3mApac"] = "Vivoactive 3 Music",
        ["VivoActive3tChn"] = "Vivoactive 3",
        ["Vivoactive4Small"] = "Vivoactive 4S",
        ["VivoActive4SmallAsia"] = "Vivoactive 4S",
        ["VivoActive4OledAsia"] = "Venu",
        ["Venusq2music"] = "Venu Sq 2 Music",
        ["VenusqMusicV2"] = "Venu Sq Music",
        ["EpixGen2"] = "Epix (Gen 2)",
        ["EpixGen2Apac"] = "Epix (Gen 2)",
        ["EpixGen2Pro42"] = "Epix Pro (Gen 2)",
        ["EpixGen2Pro47"] = "Epix Pro (Gen 2)",
        ["EpixGen2Pro51"] = "Epix Pro (Gen 2)",
        ["MarqGen2"] = "MARQ (Gen 2)",
        ["MarqGen2Aviator"] = "MARQ Aviator (Gen 2)",
        ["MarqGen2Commander"] = "MARQ Commander (Gen 2)",
        ["D2airvenu"] = "D2 Air",
        ["D2charlie"] = "D2 Charlie",
        ["Virbx"] = "VIRB X",
        ["Virbxe"] = "VIRB XE",
        ["Truswing"] = "TruSwing",
        ["Foretrex601_701"] = "Foretrex 601/701",
        ["Oregon7xx"] = "Oregon 700 Series",
        ["Oregon7xxWw"] = "Oregon 700 Series",
        ["Rino7xx"] = "Rino 700 Series",
        ["TacxFluxFluxSSmart"] = "Tacx Flux / Flux S Smart",
        ["TacxTrainingAppWin"] = "Tacx Training App (Windows)",
        ["TacxTrainingAppMac"] = "Tacx Training App (Mac)",
        ["TacxTrainingAppMacCatalyst"] = "Tacx Training App (Mac)",
        ["TacxTrainingAppAndroid"] = "Tacx Training App (Android)",
        ["TacxTrainingAppIos"] = "Tacx Training App (iOS)",
        ["TacxTrainingAppLegacy"] = "Tacx Training App",
        ["TrainingPeaksVirtualIos"] = "TrainingPeaks Virtual (iOS)",
        ["TrainingPeaksVirtualAndroid"] = "TrainingPeaks Virtual (Android)",
        ["TrainingPeaksVirtualWindows"] = "TrainingPeaks Virtual (Windows)",
        ["TrainingPeaksVirtualMacOs"] = "TrainingPeaks Virtual (macOS)",
        ["TrainingPeaksVirtualAppleTv"] = "TrainingPeaks Virtual (Apple TV)",
        ["Connect"] = "Connect",
        ["ConnectiqSimulator"] = "Connect IQ Simulator",
        ["AndroidAntplusPlugin"] = "ANT+ Plugin (Android)",
    };

    /// <summary>
    /// Trailing parts of a product constant that name a region, edition or case size rather than a
    /// model. Stripped repeatedly, longest first, before tokenising.
    /// </summary>
    private static readonly string[] ProductSuffixes =
    [
        "SingleByteProductId", "ChinaNfc", "Daimler", "NoWifi",
        "Taiwan", "Russia", "Hebrew", "Japan", "China", "Korea", "Large", "Small",
        "Asia", "Apac", "Emea", "Thai",
        "Jpn", "Chn", "Kor", "Twn", "Sea", "Ww",
    ];

    /// <summary>Word tokens shown differently from title case.</summary>
    private static readonly Dictionary<string, string> WordMap = new(StringComparer.Ordinal)
    {
        ["Fr"] = "Forerunner",
        ["Hrm"] = "HRM",
        ["Hr"] = "HR",
        ["Gps"] = "GPS",
        ["Gpsmap"] = "GPSMAP",
        ["Gnss"] = "GNSS",
        ["Marq"] = "MARQ",
        ["Virb"] = "VIRB",
        ["Etrex"] = "eTrex",
        ["Amoled"] = "AMOLED",
        ["Mtb"] = "MTB",
        ["Rct"] = "RCT",
        ["Ut"] = "UT",
        ["Lte"] = "LTE",
        ["Cp"] = "CP",
        ["Ss"] = "SS",
        ["Venusq"] = "Venu Sq",
        ["Vivoactive"] = "Vivoactive",
        ["Vivosmart"] = "Vivosmart",
        ["Vivomove"] = "Vivomove",
        ["Vivofit"] = "Vivofit",
        ["Vivosport"] = "Vivosport",
        ["Vivoki"] = "Vivoki",
    };

    /// <summary>Display names keyed by SDK manufacturer constant name; null hides the manufacturer.</summary>
    private static readonly Dictionary<string, string?> ManufacturerOverrides = new(StringComparer.Ordinal)
    {
        ["Development"] = null,
        ["Invalid"] = null,
        ["GarminFr405Antfs"] = "Garmin",
        ["DynastreamOem"] = "Dynastream",
        ["SeikoEpsonOem"] = "Seiko Epson",
        ["PolarElectro"] = "Polar",
        ["Tomtom"] = "TomTom",
        ["TagHeuer"] = "TAG Heuer",
        ["_4iiiis"] = "4iiii",
        ["_1partcarbon"] = "1partcarbon",
        ["AAndD"] = "A&D",
        ["Srm"] = "SRM",
        ["Sram"] = "SRAM",
        ["Idt"] = "IDT",
        ["Icg"] = "ICG",
        ["Vdo"] = "VDO",
        ["Ibike"] = "iBike",
        ["Igpsport"] = "iGPSPORT",
        ["IfitCom"] = "iFit",
        ["Nurvv"] = "NURVV",
        ["Whoop"] = "WHOOP",
        ["Mywhoosh"] = "MyWhoosh",
        ["TrainerRoad"] = "TrainerRoad",
        ["RGTCycling"] = "RGT Cycling",
        ["Zone5cloud"] = "Zone5 Cloud",
        ["MioMagellan"] = "Mio Magellan",
    };

    private static readonly Lazy<IReadOnlyDictionary<ushort, string>> GarminProducts = new(BuildGarminProducts);
    private static readonly Lazy<IReadOnlyDictionary<ushort, string?>> Manufacturers = new(BuildManufacturers);

    /// <summary>Every known Garmin product ID with its display name, e.g. 4315 → "Garmin Forerunner 965".</summary>
    public static IReadOnlyDictionary<ushort, string> AllGarminProducts => GarminProducts.Value;

    /// <summary>True when this manufacturer numbers its products with <see cref="GarminProduct"/> IDs.</summary>
    public static bool UsesGarminProductIds(ushort manufacturer) => GarminProductManufacturers.Contains(manufacturer);

    /// <summary>Display name for a Garmin product ID, or null when the SDK does not know it.</summary>
    public static string? GarminProductName(ushort product) =>
        GarminProducts.Value.TryGetValue(product, out var name) ? name : null;

    /// <summary>
    /// Display name for a FIT manufacturer ID, or null when the SDK does not know it or it carries no
    /// useful name (<c>development</c>).
    /// </summary>
    public static string? ManufacturerName(ushort manufacturer) =>
        Manufacturers.Value.TryGetValue(manufacturer, out var name) ? name : null;

    /// <summary>Humanises one <see cref="GarminProduct"/> constant name, e.g. <c>Fr965</c> → "Garmin Forerunner 965".</summary>
    public static string HumaniseGarminProduct(string constantName)
    {
        var model = ProductOverrides.TryGetValue(constantName, out var overridden)
            ? overridden
            : HumaniseModel(StripProductSuffixes(constantName));

        return NonGarminBrandPrefixes.Any(p => model.StartsWith(p, StringComparison.Ordinal))
            ? model
            : $"Garmin {model}";
    }

    /// <summary>Humanises one <see cref="Manufacturer"/> constant name, e.g. <c>WahooFitness</c> → "Wahoo Fitness".</summary>
    public static string? HumaniseManufacturer(string constantName)
    {
        if (ManufacturerOverrides.TryGetValue(constantName, out var overridden))
        {
            return overridden;
        }

        var name = constantName.TrimStart('_');
        if (name.EndsWith("Byte", StringComparison.Ordinal) && name.Length > 4)
        {
            name = name[..^4];
        }

        return CamelBoundary().Replace(name, " ");
    }

    private static IReadOnlyDictionary<ushort, string> BuildGarminProducts()
    {
        var map = new Dictionary<ushort, string>();
        foreach (var (value, name) in Constants(typeof(GarminProduct)))
        {
            if (value != GarminProduct.Invalid)
            {
                map[value] = HumaniseGarminProduct(name);
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<ushort, string?> BuildManufacturers()
    {
        var map = new Dictionary<ushort, string?>();
        foreach (var (value, name) in Constants(typeof(Manufacturer)))
        {
            map[value] = HumaniseManufacturer(name);
        }

        return map;
    }

    private static IEnumerable<(ushort Value, string Name)> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(ushort))
            .Select(f => ((ushort)f.GetRawConstantValue()!, f.Name));

    private static string StripProductSuffixes(string name)
    {
        var stripped = true;
        while (stripped)
        {
            stripped = false;
            var mm = CaseSizeSuffix().Match(name);
            if (mm.Success && mm.Index > 0)
            {
                name = name[..mm.Index];
                stripped = true;
                continue;
            }

            foreach (var suffix in ProductSuffixes)
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    name = name[..^suffix.Length];
                    stripped = true;
                    break;
                }
            }
        }

        return name;
    }

    /// <summary>
    /// <c>Fenix7sProSolar</c> → "Fenix 7S Pro Solar", <c>ApproachS70</c> → "Approach S70",
    /// <c>DescentMk2s</c> → "Descent Mk2S", <c>VivoSmartHr</c> → "Vivosmart HR".
    /// </summary>
    private static string HumaniseModel(string name)
    {
        var tokens = ModelToken().Matches(name).Select(m => m.Value).ToList();

        // "Vivo" + "Active" → "Vivoactive"
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            if (tokens[i] == "Vivo" && char.IsLetter(tokens[i + 1][0]))
            {
                tokens[i] = "Vivo" + tokens[i + 1].ToLowerInvariant();
                tokens.RemoveAt(i + 1);
            }
        }

        var words = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            var next = i + 1 < tokens.Count ? tokens[i + 1] : null;

            if (char.IsDigit(token[0]))
            {
                // "7s" → "7S", "735xt" → "735XT"; inReach "i" stays lower ("66i", "Mk3i")
                var digits = new string(token.TakeWhile(char.IsDigit).ToArray());
                var suffix = token[digits.Length..];
                token = digits + (suffix == "i" ? suffix : suffix.ToUpperInvariant());

                // "Fenix 6" + "S" (from Fenix6SSport) → "6S"; "Neo 2" + "T" → "2T"
                if (next is { Length: 1 } && char.IsUpper(next[0]) && (i + 2 >= tokens.Count || !char.IsDigit(tokens[i + 2][0])))
                {
                    token += next;
                    i++;
                }

                // "Mk" + "2S" → "Mk2S"; "Gen" + "2" → "(Gen 2)"
                if (words.Count > 0 && words[^1] == "Mk")
                {
                    words[^1] = "Mk" + token;
                    continue;
                }

                if (words.Count > 0 && words[^1] == "Gen")
                {
                    words[^1] = $"(Gen {token})";
                    continue;
                }

                words.Add(token);
                continue;
            }

            // single letter before a number: "S" + "70" → "S70", "D" + "2" → "D2", "X" + "10" → "X10"
            if (token.Length == 1 && char.IsUpper(token[0]) && next != null && char.IsDigit(next[0]))
            {
                words.Add(token + next.ToUpperInvariant());
                i++;
                continue;
            }

            words.Add(WordMap.TryGetValue(token, out var mapped) ? mapped : char.ToUpperInvariant(token[0]) + token[1..]);
        }

        return string.Join(' ', words);
    }

    [GeneratedRegex(@"[A-Z][a-z]*|[0-9]+[a-z]*|[a-z]+")]
    private static partial Regex ModelToken();

    [GeneratedRegex(@"[0-9]{2}mm$")]
    private static partial Regex CaseSizeSuffix();

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex CamelBoundary();
}
