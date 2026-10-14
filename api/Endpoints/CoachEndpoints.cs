using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
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
            .WithSummary("Get AI Coach availability, opt-in, and provider status")
            .WithDescription(
                "Returns whether the host has enabled AI Coach (Coach__Enabled), whether the athlete has opted in, " +
                "and Anthropic provider connection status. Never returns the API key. " +
                "When the host flag is off, available is false and optedIn/providerConnected are false.");

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
                "404 when Coach__Enabled is false. Does not wipe coach data or the provider key.");

        group.MapGet("/coach/provider", GetCoachProvider)
            .WithName("GetCoachProvider")
            .Produces<CoachProviderDocument>(200)
            .Produces(404)
            .WithSummary("Get Coach Anthropic provider status")
            .WithDescription(
                "Returns whether an Anthropic provider connection exists. Never returns the API key. " +
                "404 when Coach__Enabled is false.");

        group.MapPut("/coach/provider", PutCoachProvider)
            .WithName("PutCoachProvider")
            .Accepts<PutCoachProviderRequest>("application/json")
            .Produces<CoachProviderDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Save Coach Anthropic API key and model")
            .WithDescription(
                "Encrypts and stores the BYO Anthropic API key without probing. " +
                "modelId defaults to claude-opus-4-7; unknown model ids are rejected. " +
                "404 when Coach__Enabled is false.");

        group.MapPost("/coach/provider/test", PostCoachProviderTest)
            .WithName("PostCoachProviderTest")
            .Accepts<TestCoachProviderRequest>("application/json")
            .Produces<CoachProviderDocument>(200)
            .Produces(400)
            .Produces(404)
            .Produces(503)
            .WithSummary("Test Coach Anthropic connection")
            .WithDescription(
                "Athlete-data-free probe. If apiKey is provided, probes that key without saving. " +
                "Otherwise probes the stored key (404 if none). Updates lastProbeAt/lastError only for stored keys.");

        group.MapDelete("/coach/provider", DeleteCoachProvider)
            .WithName("DeleteCoachProvider")
            .Produces(204)
            .Produces(404)
            .WithSummary("Remove Coach Anthropic API key")
            .WithDescription(
                "Deletes the provider connection row. Does not touch CoachPreferences or opt-in. " +
                "404 when Coach__Enabled is false. 204 if already gone.");

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
                "Coach home when host-enabled and athlete opted in. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        CoachOnboardingEndpoints.Map(group);
        CoachConstraintEndpoints.Map(group);
        CoachCheckInEndpoints.Map(group);
        CoachPrivacyEndpoints.Map(group);
        CoachGenerateEndpoints.Map(group);
        CoachReviewEndpoints.Map(group);
        CoachAdjustEndpoints.Map(group);
    }

    private static async Task<IResult> GetCoachSettings(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.Ok(new CoachSettingsDocument(
                Available: false,
                OptedIn: false,
                ProviderConnected: false,
                ModelId: null));
        }

        var prefs = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        var provider = await db.CoachProviderConnections.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(ToSettingsDocument(optedIn: prefs?.OptedIn == true, provider));
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
        var provider = await db.CoachProviderConnections.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(ToSettingsDocument(optedIn: true, provider));
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
        var provider = await db.CoachProviderConnections.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(ToSettingsDocument(optedIn: false, provider));
    }

    private static async Task<IResult> GetCoachProvider(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var row = await db.CoachProviderConnections.AsNoTracking().FirstOrDefaultAsync();
        return Results.Ok(ToProviderDocument(row));
    }

    private static async Task<IResult> PutCoachProvider(
        [FromBody] PutCoachProviderRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAnthropicSecretProtector protector,
        ILoggerFactory loggerFactory)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var apiKey = request?.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            return Results.BadRequest(new { error = "apiKey is required" });
        }

        var modelId = CoachAnthropicModels.NormalizeOrReject(request?.ModelId);
        if (modelId == null)
        {
            return Results.BadRequest(new { error = "Unknown model id" });
        }

        var now = DateTime.UtcNow;
        var existing = await db.CoachProviderConnections.FirstOrDefaultAsync();
        if (existing != null)
        {
            existing.ApiKeyCiphertext = protector.Encrypt(apiKey);
            existing.ModelId = modelId;
            existing.LastError = null;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync();
            loggerFactory.CreateLogger("CoachProvider").LogInformation("Coach Anthropic API key replaced");
            return Results.Ok(ToProviderDocument(existing));
        }

        var row = new CoachProviderConnection
        {
            ApiKeyCiphertext = protector.Encrypt(apiKey),
            ModelId = modelId,
            ConnectedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.CoachProviderConnections.Add(row);
        await db.SaveChangesAsync();
        loggerFactory.CreateLogger("CoachProvider").LogInformation("Coach Anthropic connection saved");
        return Results.Ok(ToProviderDocument(row));
    }

    private static async Task<IResult> PostCoachProviderTest(
        [FromBody] TestCoachProviderRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAnthropicSecretProtector protector,
        IAnthropicCoachClient anthropic)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var requestKey = request?.ApiKey?.Trim();
        var testingStored = string.IsNullOrEmpty(requestKey);
        CoachProviderConnection? row = null;
        string apiKey;

        if (testingStored)
        {
            row = await db.CoachProviderConnections.FirstOrDefaultAsync();
            if (row == null)
            {
                return Results.NotFound();
            }

            try
            {
                apiKey = protector.Decrypt(row.ApiKeyCiphertext);
            }
            catch (CryptographicException)
            {
                row.LastError = "Stored key could not be decrypted";
                row.LastProbeAt = DateTime.UtcNow;
                row.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                return Results.BadRequest(new { error = "Stored key could not be decrypted" });
            }
        }
        else
        {
            apiKey = requestKey!;
        }

        var probe = await anthropic.ProbeApiKeyAsync(apiKey);
        if (testingStored && row != null)
        {
            row.LastProbeAt = DateTime.UtcNow;
            row.LastError = probe switch
            {
                AnthropicProbeResult.Ok => null,
                AnthropicProbeResult.Unauthorized => "Anthropic rejected the API key",
                AnthropicProbeResult.Billing => "Anthropic billing or payment required",
                _ => "Could not reach Anthropic. Try again."
            };
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return probe switch
        {
            AnthropicProbeResult.Ok => Results.Ok(
                testingStored && row != null
                    ? ToProviderDocument(row)
                    : new CoachProviderDocument(
                        Connected: false,
                        ModelId: null,
                        LastProbeAt: DateTime.UtcNow,
                        LastError: null)),
            AnthropicProbeResult.Unauthorized => Results.BadRequest(new { error = "Anthropic rejected the API key" }),
            AnthropicProbeResult.Billing => Results.BadRequest(new { error = "Anthropic billing or payment required" }),
            _ => Results.Json(
                new { error = "Could not reach Anthropic. Try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<IResult> DeleteCoachProvider(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return Results.NotFound();
        }

        var rows = await db.CoachProviderConnections.ToListAsync();
        if (rows.Count > 0)
        {
            db.CoachProviderConnections.RemoveRange(rows);
            await db.SaveChangesAsync();
        }

        return Results.NoContent();
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

        var providerConnected = await db.CoachProviderConnections.AsNoTracking().AnyAsync();
        var onboardingComplete =
            await db.CoachGoals.AsNoTracking().AnyAsync() &&
            await db.CoachMemoryLayers.AsNoTracking()
                .AnyAsync(l => l.Layer == CoachOnboardingEndpoints.Layer1Key);

        var nextSteps = new List<string>();
        if (!providerConnected)
        {
            nextSteps.Add("Connect your Anthropic API key");
        }

        if (!onboardingComplete)
        {
            nextSteps.Add("Complete coach onboarding");
        }

        CoachGoalDocument? goal = null;
        if (onboardingComplete)
        {
            var goalRow = await db.CoachGoals.AsNoTracking().FirstOrDefaultAsync();
            if (goalRow != null)
            {
                goal = CoachOnboardingEndpoints.ToGoalDocument(goalRow);
            }
        }

        return Results.Ok(new CoachHomeDocument(
            Title: "AI Coach",
            Disclaimer:
                "Tempo AI Coach is not a doctor and does not provide medical advice. " +
                "It offers training suggestions only. Seek a qualified professional for pain, injury, or health concerns.",
            NextSteps: nextSteps,
            OnboardingCompleted: onboardingComplete,
            Goal: goal));
    }

    private static CoachSettingsDocument ToSettingsDocument(bool optedIn, CoachProviderConnection? provider) =>
        new(
            Available: true,
            OptedIn: optedIn,
            ProviderConnected: provider != null,
            ModelId: provider?.ModelId);

    private static CoachProviderDocument ToProviderDocument(CoachProviderConnection? row) =>
        row == null
            ? new CoachProviderDocument(Connected: false, ModelId: null, LastProbeAt: null, LastError: null)
            : new CoachProviderDocument(
                Connected: true,
                ModelId: row.ModelId,
                LastProbeAt: row.LastProbeAt,
                LastError: row.LastError);
}

public record CoachSettingsDocument(
    bool Available,
    bool OptedIn,
    bool ProviderConnected,
    string? ModelId);

public record CoachProviderDocument(
    bool Connected,
    string? ModelId,
    DateTime? LastProbeAt,
    string? LastError);

public record PutCoachProviderRequest(string? ApiKey, string? ModelId);

public record TestCoachProviderRequest(string? ApiKey);

public record CoachHomeDocument(
    string Title,
    string Disclaimer,
    IReadOnlyList<string> NextSteps,
    bool OnboardingCompleted,
    CoachGoalDocument? Goal);
