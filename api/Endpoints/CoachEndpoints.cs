using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachEndpoints
{
    public static RouteGroupBuilder MapCoachSettings(this RouteGroupBuilder group)
    {
        group.MapGet("/coach", GetCoachSettings)
            .WithName("GetCoachSettings")
            .Produces<CoachSettingsDocument>(200)
            .WithSummary("Get AI Coach availability and opt-in status")
            .WithDescription(
                "Returns whether the host has enabled AI Coach (Coach__Enabled) and whether the athlete has opted in. " +
                "When the host flag is off, available is false and optedIn is always false.");

        group.MapPost("/coach/opt-in", PostCoachOptIn)
            .WithName("PostCoachOptIn")
            .Produces<CoachSettingsDocument>(200)
            .Produces(404)
            .WithSummary("Opt into AI Coach")
            .WithDescription(
                "Athlete opt-in when the host has enabled Coach. 404 when Coach__Enabled is false. Idempotent.");

        group.MapPost("/coach/opt-out", PostCoachOptOut)
            .WithName("PostCoachOptOut")
            .Produces<CoachSettingsDocument>(200)
            .Produces(404)
            .WithSummary("Opt out of AI Coach")
            .WithDescription(
                "Non-destructive disable: sets OptedIn false and keeps the preferences row. " +
                "404 when Coach__Enabled is false. Does not wipe coach data.");

        return group;
    }

    public static void MapCoachEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/coach")
            .WithTags("Coach")
            .RequireAuthorization();

        group.MapGet("", GetCoachHome)
            .WithName("GetCoachHome")
            .Produces<CoachHomeDocument>(200)
            .Produces(404)
            .WithSummary("Get Coach home shell")
            .WithDescription(
                "Empty Coach home when host-enabled and athlete opted in. " +
                "404 when Coach is unavailable or the athlete has not opted in.");
    }

    private static async Task<IResult> GetCoachSettings(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.Ok(new CoachSettingsDocument(Available: false, OptedIn: false));
        }

        var row = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(new CoachSettingsDocument(Available: true, OptedIn: row?.OptedIn == true));
    }

    private static async Task<IResult> PostCoachOptIn(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var now = DateTime.UtcNow;
        var row = await db.CoachPreferences.FirstOrDefaultAsync();
        if (row == null)
        {
            row = new CoachPreferences
            {
                OptedIn = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CoachPreferences.Add(row);
        }
        else
        {
            row.OptedIn = true;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
        return Results.Ok(new CoachSettingsDocument(Available: true, OptedIn: true));
    }

    private static async Task<IResult> PostCoachOptOut(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var now = DateTime.UtcNow;
        var row = await db.CoachPreferences.FirstOrDefaultAsync();
        if (row == null)
        {
            row = new CoachPreferences
            {
                OptedIn = false,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CoachPreferences.Add(row);
        }
        else
        {
            row.OptedIn = false;
            row.UpdatedAt = now;
        }

        await db.SaveChangesAsync();
        return Results.Ok(new CoachSettingsDocument(Available: true, OptedIn: false));
    }

    private static async Task<IResult> GetCoachHome(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var row = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        if (row?.OptedIn != true)
        {
            return Results.NotFound();
        }

        return Results.Ok(new CoachHomeDocument(
            Title: "AI Coach",
            Disclaimer:
                "Tempo AI Coach is not a doctor and does not provide medical advice. " +
                "It offers training suggestions only. Seek a qualified professional for pain, injury, or health concerns.",
            NextSteps: new[]
            {
                "Connect your Anthropic API key",
                "Complete coach onboarding"
            }));
    }
}

public record CoachSettingsDocument(bool Available, bool OptedIn);

public record CoachHomeDocument(string Title, string Disclaimer, IReadOnlyList<string> NextSteps);
