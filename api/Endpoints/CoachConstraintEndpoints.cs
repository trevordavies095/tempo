using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;
using Tempo.Api.Services;

namespace Tempo.Api.Endpoints;

public static class CoachConstraintEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/constraints", ListConstraints)
            .WithName("ListCoachConstraints")
            .Produces<IReadOnlyList<CoachConstraintDocument>>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("List Coach non-negotiables")
            .WithDescription(
                "Lists CoachConstraints. status=active (default), retired, or all. " +
                "404 when Coach is unavailable or the athlete has not opted in.");

        group.MapPost("/constraints", CreateConstraint)
            .WithName("CreateCoachConstraint")
            .Accepts<CoachConstraintRequest>("application/json")
            .Produces<CoachConstraintDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Create a Coach non-negotiable")
            .WithDescription(
                "Creates secondary_race, blackout, or standing_schedule. " +
                "secondary_race intent defaults to tune_up. Unknown standing presets are rejected.");

        group.MapPut("/constraints/{id:guid}", UpdateConstraint)
            .WithName("UpdateCoachConstraint")
            .Accepts<CoachConstraintRequest>("application/json")
            .Produces<CoachConstraintDocument>(200)
            .Produces(400)
            .Produces(404)
            .WithSummary("Update an active Coach non-negotiable")
            .WithDescription("Edits an active constraint. 404 if missing or retired.");

        group.MapPost("/constraints/{id:guid}/retire", RetireConstraint)
            .WithName("RetireCoachConstraint")
            .Produces<CoachConstraintDocument>(200)
            .Produces(404)
            .WithSummary("Retire a Coach non-negotiable")
            .WithDescription("Soft-retires the constraint. Idempotent if already retired.");
    }

    private static async Task<IResult> ListConstraints(
        TempoDbContext db,
        CoachConfig coachConfig,
        [FromQuery] string? status)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var filter = (status ?? "active").Trim().ToLowerInvariant();
        if (filter is not ("active" or "retired" or "all"))
        {
            return Results.BadRequest(new { error = "status must be active, retired, or all" });
        }

        var query = db.CoachConstraints.AsNoTracking().AsQueryable();
        query = filter switch
        {
            "active" => query.Where(c => !c.IsRetired),
            "retired" => query.Where(c => c.IsRetired),
            _ => query
        };

        var rows = await query
            .OrderBy(c => c.Kind)
            .ThenBy(c => c.StartDate)
            .ThenBy(c => c.CreatedAt)
            .ToListAsync();

        return Results.Ok(rows.Select(ToDocument).ToList());
    }

    private static async Task<IResult> CreateConstraint(
        [FromBody] CoachConstraintRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var error = CoachConstraintValidation.TryNormalize(
            request?.Kind,
            request?.StartDate,
            request?.EndDate,
            request?.Distance,
            request?.Intent,
            request?.Preset,
            request?.Weekdays,
            request?.Note,
            out var normalized);
        if (error != null || normalized == null)
        {
            return Results.BadRequest(new { error });
        }

        var now = DateTime.UtcNow;
        var row = new CoachConstraint();
        CoachConstraintValidation.Apply(row, normalized, now, isCreate: true);
        db.CoachConstraints.Add(row);
        await db.SaveChangesAsync();
        return Results.Ok(ToDocument(row));
    }

    private static async Task<IResult> UpdateConstraint(
        Guid id,
        [FromBody] CoachConstraintRequest? request,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachConstraints.FirstOrDefaultAsync(c => c.Id == id);
        if (row == null || row.IsRetired)
        {
            return Results.NotFound();
        }

        var error = CoachConstraintValidation.TryNormalize(
            request?.Kind,
            request?.StartDate,
            request?.EndDate,
            request?.Distance,
            request?.Intent,
            request?.Preset,
            request?.Weekdays,
            request?.Note,
            out var normalized);
        if (error != null || normalized == null)
        {
            return Results.BadRequest(new { error });
        }

        CoachConstraintValidation.Apply(row, normalized, DateTime.UtcNow, isCreate: false);
        await db.SaveChangesAsync();
        return Results.Ok(ToDocument(row));
    }

    private static async Task<IResult> RetireConstraint(
        Guid id,
        TempoDbContext db,
        CoachConfig coachConfig)
    {
        if (!await IsCoachAvailableAsync(db, coachConfig))
        {
            return Results.NotFound();
        }

        var row = await db.CoachConstraints.FirstOrDefaultAsync(c => c.Id == id);
        if (row == null)
        {
            return Results.NotFound();
        }

        if (!row.IsRetired)
        {
            var now = DateTime.UtcNow;
            row.IsRetired = true;
            row.RetiredAt = now;
            row.UpdatedAt = now;
            await db.SaveChangesAsync();
        }

        return Results.Ok(ToDocument(row));
    }

    internal static CoachConstraintDocument ToDocument(CoachConstraint row) =>
        new(
            Id: row.Id,
            Kind: row.Kind,
            StartDate: row.StartDate?.ToString("yyyy-MM-dd"),
            EndDate: row.EndDate?.ToString("yyyy-MM-dd"),
            Distance: row.Distance,
            DistanceM: row.DistanceM,
            Intent: row.Intent,
            Preset: row.Preset,
            Weekdays: row.Weekdays,
            Note: row.Note,
            IsRetired: row.IsRetired,
            RetiredAt: row.RetiredAt,
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

public record CoachConstraintDocument(
    Guid Id,
    string Kind,
    string? StartDate,
    string? EndDate,
    string? Distance,
    double? DistanceM,
    string? Intent,
    string? Preset,
    IReadOnlyList<int>? Weekdays,
    string? Note,
    bool IsRetired,
    DateTime? RetiredAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public record CoachConstraintRequest(
    string? Kind,
    string? StartDate,
    string? EndDate,
    string? Distance,
    string? Intent,
    string? Preset,
    IReadOnlyList<int>? Weekdays,
    string? Note);
