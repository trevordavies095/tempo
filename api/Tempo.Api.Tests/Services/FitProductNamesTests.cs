using Dynastream.Fit;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class FitProductNamesTests
{
    [Theory]
    [InlineData("Fr965", "Garmin Forerunner 965")]
    [InlineData("Fr945Lte", "Garmin Forerunner 945 LTE")]
    [InlineData("Fr945Asia", "Garmin Forerunner 945")]
    [InlineData("Fr235ChinaNfc", "Garmin Forerunner 235")]
    [InlineData("Fr735xtJapan", "Garmin Forerunner 735XT")]
    [InlineData("Fr265Large", "Garmin Forerunner 265")]
    [InlineData("Fr265Small", "Garmin Forerunner 265S")]
    [InlineData("Fr570Small", "Garmin Forerunner 570")]
    [InlineData("Fr245Music", "Garmin Forerunner 245 Music")]
    [InlineData("Fenix7s", "Garmin Fenix 7S")]
    [InlineData("Fenix7sProSolar", "Garmin Fenix 7S Pro Solar")]
    [InlineData("Fenix7ProSolarNoWifi", "Garmin Fenix 7 Pro Solar")]
    [InlineData("Fenix6SSport", "Garmin Fenix 6S Sport")]
    [InlineData("Fenix8SolarLarge", "Garmin Fenix 8 Solar")]
    [InlineData("Fenix3HrSea", "Garmin Fenix 3 HR")]
    [InlineData("Fenix943mm", "Garmin Fenix 9")]
    [InlineData("ApproachS7243mm", "Garmin Approach S72")]
    [InlineData("Instinct3Amoled45mm", "Garmin Instinct 3 AMOLED")]
    [InlineData("DescentMk2s", "Garmin Descent Mk2S")]
    [InlineData("DescentMk3i", "Garmin Descent Mk3i")]
    [InlineData("Gpsmap66i", "Garmin GPSMAP 66i")]
    [InlineData("D2Mach1Pro", "Garmin D2 Mach 1 Pro")]
    [InlineData("D2AirX10", "Garmin D2 Air X10")]
    [InlineData("VivoSmartHrAsia", "Garmin Vivosmart HR")]
    [InlineData("VivoActive4SmallAsia", "Garmin Vivoactive 4S")]
    [InlineData("Vivoactive4Large", "Garmin Vivoactive 4")]
    [InlineData("VenusqMusic", "Garmin Venu Sq Music")]
    [InlineData("Venu4s", "Garmin Venu 4S")]
    [InlineData("EpixGen2", "Garmin Epix (Gen 2)")]
    [InlineData("EpixGen2Pro47", "Garmin Epix Pro (Gen 2)")]
    [InlineData("Edge1030Plus", "Garmin Edge 1030 Plus")]
    [InlineData("HrmProPlus", "Garmin HRM Pro Plus")]
    [InlineData("TacxNeo2TSmart", "Tacx Neo 2T Smart")]
    [InlineData("TrainingPeaksVirtualIos", "TrainingPeaks Virtual (iOS)")]
    public void HumaniseGarminProduct_ProducesMarketingName(string constant, string expected)
    {
        FitProductNames.HumaniseGarminProduct(constant).Should().Be(expected);
    }

    [Theory]
    [InlineData(3113, "Garmin Forerunner 945")]
    [InlineData(3652, "Garmin Forerunner 945 LTE")]
    [InlineData(4024, "Garmin Forerunner 955")]
    [InlineData(4315, "Garmin Forerunner 965")]
    [InlineData(3905, "Garmin Fenix 7S")]
    [InlineData(3906, "Garmin Fenix 7")]
    [InlineData(3907, "Garmin Fenix 7X")]
    public void GarminProductName_KeepsNamesFromTheOldHandWrittenMap(int product, string expected)
    {
        FitProductNames.GarminProductName((ushort)product).Should().Be(expected);
    }

    [Fact]
    public void AllGarminProducts_CoversEverySdkConstantExceptInvalid()
    {
        var sdkCount = typeof(GarminProduct).GetFields()
            .Count(f => f.IsLiteral && f.FieldType == typeof(ushort));

        FitProductNames.AllGarminProducts.Should().HaveCount(sdkCount - 1);
        FitProductNames.AllGarminProducts.Should().NotContainKey(GarminProduct.Invalid);
    }

    [Fact]
    public void AllGarminProducts_HaveNoRegionOrSizeLeftovers()
    {
        string[] leftovers =
        [
            "Asia", "Apac", "Japan", "Jpn", "China", "Chn", "Korea", "Kor", "Taiwan", "Twn",
            "Russia", "Thai", "Emea", "Hebrew", "Large", "Small", "Daimler", "SingleByte",
        ];

        foreach (var name in FitProductNames.AllGarminProducts.Values)
        {
            name.Should().MatchRegex("^(Garmin|Tacx|TrainingPeaks) ");
            name.Should().NotMatchRegex(@"\d+mm\b");
            foreach (var word in leftovers)
            {
                name.Should().NotContain(word, because: $"'{name}' should not keep a region or size suffix");
            }
        }
    }

    [Theory]
    [InlineData(1, "Garmin")]
    [InlineData(2, "Garmin")]
    [InlineData(23, "Suunto")]
    [InlineData(32, "Wahoo Fitness")]
    [InlineData(94, "Sound Of Motion")]
    [InlineData(95, "Stryd")]
    [InlineData(123, "Polar")]
    [InlineData(129, "Coros")]
    [InlineData(260, "Zwift")]
    [InlineData(265, "Strava")]
    [InlineData(294, "Coros")]
    [InlineData(348, "Huawei")]
    public void ManufacturerName_UsesSdkConstants(int code, string expected)
    {
        FitProductNames.ManufacturerName((ushort)code).Should().Be(expected);
    }

    [Theory]
    [InlineData(255)] // development
    [InlineData(65535)] // invalid
    [InlineData(182)] // not assigned
    public void ManufacturerName_ReturnsNull_WhenNoUsefulName(int code)
    {
        FitProductNames.ManufacturerName((ushort)code).Should().BeNull();
    }
}
