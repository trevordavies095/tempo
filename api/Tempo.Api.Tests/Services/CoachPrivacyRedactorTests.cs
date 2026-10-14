using System.Text.Json;
using FluentAssertions;
using Tempo.Api.Services;
using Xunit;

namespace Tempo.Api.Tests.Services;

public class CoachPrivacyRedactorTests
{
    [Fact]
    public void TryRedact_StripsNameEmailAndRouteCoordinates()
    {
        var packet = new
        {
            meta = new { weekStartDate = "2026-10-12" },
            athlete = new { name = "Ada", email = "ada@example.com", username = "ada" },
            checkIn = new
            {
                runs = new[]
                {
                    new
                    {
                        distanceM = 5000,
                        name = "Morning jog",
                        route = new
                        {
                            coordinates = new[] { new[] { -71.1, 42.3 }, new[] { -71.11, 42.31 } }
                        }
                    }
                }
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out var report, out var error);

        ok.Should().BeTrue(error);
        error.Should().BeNull();
        json.Should().NotContain("\"name\":");
        json.Should().NotContain("Ada");
        json.Should().NotContain("ada@example.com");
        json.Should().NotContain("coordinates");
        json.Should().NotContain("42.3");
        report.Stripped.Should().Contain(s => s.Category == "athleteIdentity");
        report.Stripped.Should().Contain(s => s.Category == "gpsRoutes");
        report.Included.Should().NotBeEmpty();
    }

    [Fact]
    public void TryRedact_FailsClosed_WhenResidualGpsRemainsInNotes()
    {
        // Coordinates embedded in a free-text string (no forbidden key to strip).
        var packet = new
        {
            checkIn = new
            {
                notes = "Started near [42.3601, -71.0589]"
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out _, out var error);

        ok.Should().BeFalse();
        json.Should().BeEmpty();
        error.Should().Contain("residual GPS");
    }

    [Fact]
    public void LooksLikeResidualGps_DetectsLatProperty()
    {
        CoachPrivacyRedactor.LooksLikeResidualGps("{\"lat\":42.3}").Should().BeTrue();
        CoachPrivacyRedactor.LooksLikeResidualGps("{\"distanceM\":5000}").Should().BeFalse();
    }

    [Fact]
    public void TryRedact_CleanPacket_Succeeds()
    {
        var packet = new
        {
            meta = new { weekStartDate = "2026-10-12", unitPreference = "metric" },
            checkIn = new
            {
                runs = new[]
                {
                    new { workoutId = Guid.NewGuid(), distanceM = 10000.0, durationS = 3000, notes = "felt fine" }
                }
            }
        };

        var ok = CoachPrivacyRedactor.TryRedact(packet, out var json, out var report, out var error);

        ok.Should().BeTrue(error);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("checkIn").GetProperty("runs")[0].GetProperty("distanceM").GetDouble()
            .Should().Be(10000);
        report.Included.Should().Contain(i => i.Category == "checkIn");
    }
}
