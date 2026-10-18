using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachMemoryEndpoints
{
    private static readonly HashSet<string> HumanEpisodeCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        CoachEpisodeCategories.Race,
        CoachEpisodeCategories.Breakthrough,
        CoachEpisodeCategories.Issue,
        CoachEpisodeCategories.Equipment,
        CoachEpisodeCategories.Milestone,
        CoachEpisodeCategories.ProtocolChange,
        CoachEpisodeCategories.Recap
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/memory/episodes", ListEpisodes)
            .WithName("ListCoachEpisodes")
            .Produces<IReadOnlyList<CoachEpisodeDocument>>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("List Coach Layer 2 episodes")
            .WithDescription(
                "Lists CoachEpisodes newest-first. includeRetired=false by default. " +
                "Optional category filter (incl. recap). 404 when Coach unavailable or not opted in.");

        group.MapPost("/memory/episodes", CreateEpisode)
            .WithName("CreateCoachEpisode")
            .Accepts<CreateCoachEpisodeRequest>("application/json")
            .Produces<CoachEpisodeDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Add an athlete-authored episode")
            .WithDescription(
                "Creates a Layer 2 episode with Source=athlete. Category may include recap. " +
                "Summary max 400 chars. Id via CoachMemoryIds.");

        group.MapPost("/memory/episodes/{id}/retire", RetireEpisode)
            .WithName("RetireCoachEpisode")
            .Produces<CoachEpisodeDocument>(200)
            .Produces(404)
            .WithSummary("Retire a Coach episode")
            .WithDescription("Soft-retires the episode. Idempotent if already retired.");

        group.MapPost("/memory/episodes/{id}/unretire", UnretireEpisode)
            .WithName("UnretireCoachEpisode")
            .Produces<CoachEpisodeDocument>(200)
            .Produces(404)
            .WithSummary("Unretire a Coach episode")
            .WithDescription("Clears Retired. Idempotent if already active.");

        group.MapGet("/memory/rules", ListRules)
            .WithName("ListCoachRules")
            .Produces<IReadOnlyList<CoachRuleDocument>>(200)
            .Produces(404)
            .WithSummary("List Coach Layer 3 rules")
            .WithDescription(
                "Lists CoachRules: active first, then retired when includeRetired=true. " +
                "404 when Coach unavailable or not opted in.");

        group.MapPost("/memory/rules", CreateRule)
            .WithName("CreateCoachRule")
            .Accepts<CreateCoachRuleRequest>("application/json")
            .Produces<CoachRuleDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Add an athlete-authored standing rule")
            .WithDescription(
                "Creates a Layer 3 rule with Source=athlete and Status=active. " +
                "Id via CoachMemoryIds.");

        group.MapPost("/memory/rules/{id}/retire", RetireRule)
            .WithName("RetireCoachRule")
            .Produces<CoachRuleDocument>(200)
            .Produces(404)
            .WithSummary("Retire a Coach rule")
            .WithDescription("Sets Status=retired. Idempotent if already retired.");

        group.MapPost("/memory/rules/{id}/unretire", UnretireRule)
            .WithName("UnretireCoachRule")
            .Produces<CoachRuleDocument>(200)
            .Produces(404)
            .WithSummary("Unretire a Coach rule")
            .WithDescription("Sets Status=active. Idempotent if already active.");

        group.MapPost("/memory/notes", CreateMemoryNote)
            .WithName("CreateCoachMemoryNote")
            .Accepts<CreateCoachMemoryNoteRequest>("application/json")
            .Produces<CoachMemoryNoteDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Tell your coach something")
            .WithDescription(
                "Dated text becomes an athlete episode (milestone). " +
                "Undated text becomes an athlete rule in coaching_preferences.");
    }

    private static async Task<IResult> ListEpisodes(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] bool includeRetired = false,
        [FromQuery] string? category = null)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var query = db.CoachEpisodes.AsNoTracking().AsQueryable();
        if (!includeRetired)
        {
            query = query.Where(e => !e.Retired);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var cat = category.Trim().ToLowerInvariant();
            if (!HumanEpisodeCategories.Contains(cat))
            {
                return Results.BadRequest(new
                {
                    error =
                        "category must be race|breakthrough|issue|equipment|milestone|protocol_change|recap"
                });
            }

            query = query.Where(e => e.Category == cat);
        }

        var rows = await query
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Date)
            .ThenBy(e => e.Id)
            .ToListAsync();

        return Results.Ok(rows.Select(ToEpisodeDocument).ToList());
    }

    private static async Task<IResult> CreateEpisode(
        [FromBody] CreateCoachEpisodeRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        if (!DateOnly.TryParse(
                request.Date,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            return Results.BadRequest(new { error = "date must be yyyy-MM-dd" });
        }

        var category = (request.Category ?? string.Empty).Trim().ToLowerInvariant();
        if (!HumanEpisodeCategories.Contains(category))
        {
            return Results.BadRequest(new
            {
                error =
                    "category must be race|breakthrough|issue|equipment|milestone|protocol_change|recap"
            });
        }

        var summary = (request.Summary ?? string.Empty).Trim();
        if (summary.Length == 0)
        {
            return Results.BadRequest(new { error = "summary is required" });
        }

        if (summary.Length > CoachMemoryProposalValidator.MaxEpisodeSummaryLength)
        {
            return Results.BadRequest(new
            {
                error = $"summary must be at most {CoachMemoryProposalValidator.MaxEpisodeSummaryLength} characters"
            });
        }

        DateOnly? sourceWeekEnding = null;
        if (!string.IsNullOrWhiteSpace(request.SourceWeekEnding))
        {
            if (!DateOnly.TryParse(
                    request.SourceWeekEnding,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var swe))
            {
                return Results.BadRequest(new { error = "sourceWeekEnding must be yyyy-MM-dd" });
            }

            sourceWeekEnding = swe;
        }

        var existingIds = (await db.CoachEpisodes.AsNoTracking().Select(e => e.Id).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var id = CoachMemoryIds.EpisodeId(date, category, summary, existingIds);
        var now = DateTime.UtcNow;
        var row = new CoachEpisode
        {
            Id = id,
            Date = date,
            Category = category,
            Summary = summary,
            SourceWeekEnding = sourceWeekEnding,
            SourceWeekId = null,
            Source = CoachEpisodeSources.Athlete,
            Retired = false,
            CreatedAt = now
        };
        db.CoachEpisodes.Add(row);
        await db.SaveChangesAsync();
        return Results.Ok(ToEpisodeDocument(row));
    }

    private static async Task<IResult> RetireEpisode(
        string id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachEpisodes.FirstOrDefaultAsync(e => e.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        if (!row.Retired)
        {
            row.Retired = true;
            await db.SaveChangesAsync();
        }

        return Results.Ok(ToEpisodeDocument(row));
    }

    private static async Task<IResult> UnretireEpisode(
        string id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachEpisodes.FirstOrDefaultAsync(e => e.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        if (row.Retired)
        {
            row.Retired = false;
            await db.SaveChangesAsync();
        }

        return Results.Ok(ToEpisodeDocument(row));
    }

    private static async Task<IResult> ListRules(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] bool includeRetired = false)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var query = db.CoachRules.AsNoTracking().AsQueryable();
        if (!includeRetired)
        {
            query = query.Where(r => r.Status == CoachRuleStatuses.Active);
        }

        var rows = await query
            .OrderBy(r => r.Status == CoachRuleStatuses.Active ? 0 : 1)
            .ThenBy(r => r.TargetLayer)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .ToListAsync();

        return Results.Ok(rows.Select(ToRuleDocument).ToList());
    }

    private static async Task<IResult> CreateRule(
        [FromBody] CreateCoachRuleRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        var targetLayer = (request.TargetLayer ?? string.Empty).Trim().ToLowerInvariant();
        if (!CoachWeeklyCommitSchema.RuleTargetLayers.Contains(targetLayer))
        {
            return Results.BadRequest(new
            {
                error =
                    "targetLayer must be data_interpretation_notes|key_learnings|coaching_preferences"
            });
        }

        var ruleText = (request.RuleText ?? string.Empty).Trim();
        if (ruleText.Length == 0)
        {
            return Results.BadRequest(new { error = "ruleText is required" });
        }

        var evidence = (request.Evidence ?? [])
            .Select(e => (e ?? string.Empty).Trim())
            .Where(e => e.Length > 0)
            .ToList();

        var existingIds = (await db.CoachRules.AsNoTracking().Select(r => r.Id).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var id = CoachMemoryIds.RuleId(ruleText, existingIds);
        var now = DateTime.UtcNow;
        var row = new CoachRule
        {
            Id = id,
            TargetLayer = targetLayer,
            RuleText = ruleText,
            Status = CoachRuleStatuses.Active,
            Evidence = evidence,
            Source = CoachRuleSources.Athlete,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.CoachRules.Add(row);
        await db.SaveChangesAsync();
        return Results.Ok(ToRuleDocument(row));
    }

    private static async Task<IResult> RetireRule(
        string id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachRules.FirstOrDefaultAsync(r => r.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        if (row.Status != CoachRuleStatuses.Retired)
        {
            row.Status = CoachRuleStatuses.Retired;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return Results.Ok(ToRuleDocument(row));
    }

    private static async Task<IResult> UnretireRule(
        string id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachRules.FirstOrDefaultAsync(r => r.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        if (row.Status != CoachRuleStatuses.Active)
        {
            row.Status = CoachRuleStatuses.Active;
            row.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return Results.Ok(ToRuleDocument(row));
    }

    private static async Task<IResult> CreateMemoryNote(
        [FromBody] CreateCoachMemoryNoteRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "Request body is required" });
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Results.BadRequest(new { error = "text is required" });
        }

        if (text.Length > CoachMemoryProposalValidator.MaxEpisodeSummaryLength)
        {
            return Results.BadRequest(new
            {
                error = $"text must be at most {CoachMemoryProposalValidator.MaxEpisodeSummaryLength} characters"
            });
        }

        DateOnly? date = null;
        if (!string.IsNullOrWhiteSpace(request.Date))
        {
            if (!DateOnly.TryParse(
                    request.Date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsed))
            {
                return Results.BadRequest(new { error = "date must be yyyy-MM-dd" });
            }

            date = parsed;
        }

        if (date is { } episodeDate)
        {
            var existingIds = (await db.CoachEpisodes.AsNoTracking().Select(e => e.Id).ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
            var id = CoachMemoryIds.EpisodeId(
                episodeDate,
                CoachEpisodeCategories.Milestone,
                text,
                existingIds);
            var now = DateTime.UtcNow;
            var row = new CoachEpisode
            {
                Id = id,
                Date = episodeDate,
                Category = CoachEpisodeCategories.Milestone,
                Summary = text,
                SourceWeekEnding = null,
                SourceWeekId = null,
                Source = CoachEpisodeSources.Athlete,
                Retired = false,
                CreatedAt = now
            };
            db.CoachEpisodes.Add(row);
            await db.SaveChangesAsync();
            return Results.Ok(new CoachMemoryNoteDocument(
                Kind: "episode",
                Episode: ToEpisodeDocument(row),
                Rule: null));
        }

        {
            var existingIds = (await db.CoachRules.AsNoTracking().Select(r => r.Id).ToListAsync())
                .ToHashSet(StringComparer.Ordinal);
            var id = CoachMemoryIds.RuleId(text, existingIds);
            var now = DateTime.UtcNow;
            var row = new CoachRule
            {
                Id = id,
                TargetLayer = CoachRuleTargetLayers.CoachingPreferences,
                RuleText = text,
                Status = CoachRuleStatuses.Active,
                Evidence = [],
                Source = CoachRuleSources.Athlete,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.CoachRules.Add(row);
            await db.SaveChangesAsync();
            return Results.Ok(new CoachMemoryNoteDocument(
                Kind: "rule",
                Episode: null,
                Rule: ToRuleDocument(row)));
        }
    }

    internal static CoachEpisodeDocument ToEpisodeDocument(CoachEpisode row) =>
        new(
            Id: row.Id,
            Date: row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Category: row.Category,
            Summary: row.Summary,
            SourceWeekEnding: row.SourceWeekEnding?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Source: row.Source,
            Retired: row.Retired,
            CreatedAt: row.CreatedAt);

    internal static CoachRuleDocument ToRuleDocument(CoachRule row) =>
        new(
            Id: row.Id,
            TargetLayer: row.TargetLayer,
            RuleText: row.RuleText,
            Status: row.Status,
            Evidence: row.Evidence,
            Source: row.Source,
            CreatedAt: row.CreatedAt,
            UpdatedAt: row.UpdatedAt);

    private static async Task<bool> IsCoachAvailableAsync(TempoDbContext db, CoachConfig coachConfig)
    {
        if (!coachConfig.Enabled)
        {
            return false;
        }

        var prefs = await db.CoachPreferences.AsNoTracking().FirstOrDefaultAsync();
        return prefs?.OptedIn == true;
    }
}

public record CoachEpisodeDocument(
    string Id,
    string Date,
    string Category,
    string Summary,
    string? SourceWeekEnding,
    string Source,
    bool Retired,
    DateTime CreatedAt);

public record CreateCoachEpisodeRequest(
    string? Date,
    string? Category,
    string? Summary,
    string? SourceWeekEnding);

public record CoachRuleDocument(
    string Id,
    string TargetLayer,
    string RuleText,
    string Status,
    IReadOnlyList<string> Evidence,
    string Source,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CreateCoachRuleRequest(
    string? TargetLayer,
    string? RuleText,
    IReadOnlyList<string>? Evidence);

public record CreateCoachMemoryNoteRequest(string? Text, string? Date);

public record CoachMemoryNoteDocument(
    string Kind,
    CoachEpisodeDocument? Episode,
    CoachRuleDocument? Rule);
