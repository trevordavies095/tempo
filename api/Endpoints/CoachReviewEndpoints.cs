using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachReviewEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static void Map(RouteGroupBuilder group)
    {
        group.MapPut("/weeks/draft", PutDraftWeek)
            .WithName("PutCoachDraftWeek")
            .Accepts<PutCoachDraftWeekRequest>("application/json")
            .Produces<CoachWeekDocument>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Update the draft weekly commit")
            .WithDescription(
                "Replaces draft CommitJson after schema + non-negotiable constraint validation. " +
                "404 if no draft; 409 if the week is already committed (unless a started Adjust is open).");

        group.MapPost("/weeks/draft/apply", ApplyDraftWeek)
            .WithName("ApplyCoachDraftWeek")
            .Accepts<ApplyCoachDraftWeekRequest>("application/json")
            .Produces<CoachWeekDocument>(200)
            .Produces(400)
            .Produces(404)
            .Produces(409)
            .WithSummary("Approve and apply a draft weekly commit")
            .WithDescription(
                "Requires every safety flag acknowledged (index-based). Flags with requiresResponse need a non-empty response. " +
                "Typed memory channels default to rejected; only acceptedLayer1PatchIndexes / acceptedEpisodeIndexes / acceptedRuleProposalIndexes apply. " +
                "Accepting an invalid stamped proposal returns 409. Optional archiveNotesAsRecap appends a recap episode from coachesNotes. " +
                "Transitions draft → committed (coach tables only). Mid-week Adjust apply archives the prior committed week and splices remaining days. " +
                "Optional commit edits then applies in one shot.");

        group.MapGet("/weeks/committed", GetCommittedWeek)
            .WithName("GetCoachCommittedWeek")
            .Produces<CoachWeekDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Get the committed weekly plan for a weekStartDate (yyyy-MM-dd)");
    }

    private static async Task<IResult> PutDraftWeek(
        [FromBody] PutCoachDraftWeekRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachWeekApplyService applyService,
        CoachAdjustmentService adjustmentService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "weekStartDate is required (yyyy-MM-dd)" });
        }

        if (!TryParseWeekStart(request.WeekStartDate, out var monday, out var dateError))
        {
            return Results.BadRequest(new { error = dateError ?? "weekStartDate is required (yyyy-MM-dd)" });
        }

        if (request.Commit == null || request.Commit.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return Results.BadRequest(new { error = "commit is required" });
        }

        string commitJson;
        try
        {
            commitJson = request.Commit.Value.GetRawText();
        }
        catch (Exception)
        {
            return Results.BadRequest(new { error = "commit must be a JSON object" });
        }

        var (week, error, status) = await applyService.UpdateDraftAsync(monday, commitJson);
        if (status != 200 || week == null)
        {
            return status switch
            {
                404 => Results.NotFound(new { error }),
                409 => Results.Conflict(new { error }),
                _ => Results.BadRequest(new { error })
            };
        }

        return Results.Ok(await MapWeekAsync(week, adjustmentService));
    }

    private static async Task<IResult> ApplyDraftWeek(
        [FromBody] ApplyCoachDraftWeekRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachWeekApplyService applyService,
        CoachAdjustmentService adjustmentService)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (request == null)
        {
            return Results.BadRequest(new { error = "weekStartDate is required (yyyy-MM-dd)" });
        }

        if (!TryParseWeekStart(request.WeekStartDate, out var monday, out var dateError))
        {
            return Results.BadRequest(new { error = dateError ?? "weekStartDate is required (yyyy-MM-dd)" });
        }

        string? optionalCommitJson = null;
        if (request.Commit is { } commitEl
            && commitEl.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null)
        {
            try
            {
                optionalCommitJson = commitEl.GetRawText();
            }
            catch (Exception)
            {
                return Results.BadRequest(new { error = "commit must be a JSON object" });
            }
        }

        var acks = (request.SafetyAcknowledgments ?? [])
            .Select(a => new CoachSafetyAcknowledgment(a.Index, a.Acknowledged, a.Response))
            .ToList();

        var (week, error, status) = await applyService.ApplyAsync(
            monday,
            optionalCommitJson,
            acks,
            request.AcceptedLayer1PatchIndexes ?? [],
            request.AcceptedEpisodeIndexes ?? [],
            request.AcceptedRuleProposalIndexes ?? [],
            request.ArchiveNotesAsRecap);
        if (status != 200 || week == null)
        {
            return status switch
            {
                404 => Results.NotFound(new { error }),
                409 => Results.Conflict(new { error }),
                _ => Results.BadRequest(new { error })
            };
        }

        return Results.Ok(await MapWeekAsync(week, adjustmentService));
    }

    private static async Task<IResult> GetCommittedWeek(
        TempoDbContext db,
        CoachConfig coachConfig,
        CoachAdjustmentService adjustmentService,
        [FromQuery] string? weekStartDate)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        if (!TryParseWeekStart(weekStartDate, out var monday, out var dateError))
        {
            return Results.BadRequest(new { error = dateError ?? "weekStartDate is required (yyyy-MM-dd)" });
        }

        var week = await db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.WeekStartDate == monday && w.Status == CoachWeekStatuses.Committed);
        if (week == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(await MapWeekAsync(week, adjustmentService));
    }

    private static bool TryParseWeekStart(string? weekStartDate, out DateOnly monday, out string? error)
    {
        monday = default;
        error = null;
        if (!DateOnly.TryParse(weekStartDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out monday))
        {
            error = "weekStartDate must be yyyy-MM-dd";
            return false;
        }

        return true;
    }

    private static async Task<CoachWeekDocument> MapWeekAsync(
        CoachWeek week,
        CoachAdjustmentService adjustmentService)
    {
        var adjustment = await adjustmentService.FindForWeekDocumentAsync(
            week.WeekStartDate,
            week.JobId,
            week.Status);
        return CoachWeekDocumentMapper.ToDocument(week, adjustment);
    }

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

public sealed record PutCoachDraftWeekRequest(string? WeekStartDate, JsonElement? Commit);

public sealed record ApplyCoachDraftWeekRequest(
    string? WeekStartDate,
    JsonElement? Commit,
    IReadOnlyList<ApplyCoachSafetyAckRequest>? SafetyAcknowledgments,
    IReadOnlyList<int>? AcceptedLayer1PatchIndexes,
    IReadOnlyList<int>? AcceptedEpisodeIndexes,
    IReadOnlyList<int>? AcceptedRuleProposalIndexes,
    bool ArchiveNotesAsRecap = false);

public sealed record ApplyCoachSafetyAckRequest(int Index, bool Acknowledged, string? Response);
