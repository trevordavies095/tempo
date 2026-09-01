using System.Text.Json;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class RoutePreviewTests
{
    [Fact]
    public void BuildRoutePreviewGeoJson_ReturnsVerbatim_WhenAtMost100Points()
    {
        var geoJson = LineStringWithCount(100);

        var preview = TrackGeometry.BuildRoutePreviewGeoJson(geoJson);

        preview.Should().Be(geoJson);
    }

    [Fact]
    public void BuildRoutePreviewGeoJson_ReturnsVerbatim_WhenFewerThan100Points()
    {
        var geoJson = LineStringWithCount(50);

        var preview = TrackGeometry.BuildRoutePreviewGeoJson(geoJson);

        preview.Should().Be(geoJson);
    }

    [Fact]
    public void BuildRoutePreviewGeoJson_ReducesToAtMost100_AndKeepsEndpoints()
    {
        var geoJson = LineStringWithCount(250, wavy: true);
        var original = ParseCoordinates(geoJson);

        var preview = TrackGeometry.BuildRoutePreviewGeoJson(geoJson);
        var coords = ParseCoordinates(preview);

        coords.Count.Should().BeLessThanOrEqualTo(TrackGeometry.RoutePreviewMaxPoints);
        coords.Count.Should().BeGreaterThan(2);
        coords[0][0].Should().Be(original[0][0]);
        coords[0][1].Should().Be(original[0][1]);
        coords[^1][0].Should().Be(original[^1][0]);
        coords[^1][1].Should().Be(original[^1][1]);
    }

    [Fact]
    public void BuildRoutePreviewGeoJson_IsDeterministic()
    {
        var geoJson = LineStringWithCount(180, wavy: true);

        var first = TrackGeometry.BuildRoutePreviewGeoJson(geoJson);
        var second = TrackGeometry.BuildRoutePreviewGeoJson(geoJson);

        first.Should().Be(second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"type\":\"LineString\"}")]
    [InlineData("{\"type\":\"LineString\",\"coordinates\":[[1]]}")]
    public void BuildRoutePreviewGeoJson_ReturnsSentinel_WhenEmptyOrUnparseable(string? geoJson)
    {
        TrackGeometry.BuildRoutePreviewGeoJson(geoJson).Should().Be(TrackGeometry.EmptyRoutePreviewSentinel);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void IsUnusableListPreview_IsTrue_ForMissingOrSentinel(string? preview)
    {
        TrackGeometry.IsUnusableListPreview(preview).Should().BeTrue();
    }

    [Fact]
    public void IsUnusableListPreview_IsFalse_ForLineString()
    {
        TrackGeometry.IsUnusableListPreview(LineStringWithCount(3)).Should().BeFalse();
    }

    private static string LineStringWithCount(int count, bool wavy = false)
    {
        var coordinates = new List<double[]>(count);
        for (var i = 0; i < count; i++)
        {
            var lon = -122.4194 + i * 0.0008;
            var lat = 37.7749 + (wavy ? Math.Sin(i / 2.5) * 0.012 : i * 0.0003);
            coordinates.Add(new[] { lon, lat });
        }

        return JsonSerializer.Serialize(new { type = "LineString", coordinates });
    }

    private static List<double[]> ParseCoordinates(string geoJson)
    {
        using var doc = JsonDocument.Parse(geoJson);
        var coords = new List<double[]>();
        foreach (var pt in doc.RootElement.GetProperty("coordinates").EnumerateArray())
        {
            coords.Add(new[] { pt[0].GetDouble(), pt[1].GetDouble() });
        }

        return coords;
    }
}
