using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Draft edit + approve/apply for coach weeks (coach tables only).
/// </summary>
public class CoachWeekApplyService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly TempoDbContext _db;

    public CoachWeekApplyService(TempoDbContext db)
    {
        _db = db;
    }

    public async Task<(CoachWeek? Week, string? Error, int StatusCode)> UpdateDraftAsync(
        DateOnly weekStartMonday,
        string commitJson,
        CancellationToken cancellationToken = default)
    {
        var startedAdjust = await _db.CoachAdjustments.AsNoTracking()
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);

        var hasCommitted = await _db.CoachWeeks.AnyAsync(
            w => w.WeekStartDate == weekStartMonday && w.Status == CoachWeekStatuses.Committed,
            cancellationToken);
        if (hasCommitted && startedAdjust == null)
        {
            return (null, "Week is already committed and immutable", 409);
        }

        var draft = await _db.CoachWeeks
            .FirstOrDefaultAsync(
                w => w.WeekStartDate == weekStartMonday && w.Status == CoachWeekStatuses.Draft,
                cancellationToken);
        if (draft == null)
        {
            return (null, "Draft week not found", 404);
        }

        var (commit, validateError) = await ValidateCommitAgainstConstraintsAsync(
            commitJson,
            weekStartMonday,
            cancellationToken);
        if (validateError != null || commit == null)
        {
            return (null, validateError ?? "Invalid weekly commit", 400);
        }

        if (startedAdjust != null)
        {
            var lockError = await ValidateLockedAgainstSourceAsync(
                startedAdjust,
                weekStartMonday,
                commit,
                cancellationToken);
            if (lockError != null)
            {
                return (null, lockError, 400);
            }
        }

        draft.CommitJson = CoachWeeklyCommitSchema.Serialize(commit);
        draft.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return (draft, null, 200);
    }

    public async Task<(CoachWeek? Week, string? Error, int StatusCode)> ApplyAsync(
        DateOnly weekStartMonday,
        string? optionalCommitJson,
        IReadOnlyList<CoachSafetyAcknowledgment> safetyAcknowledgments,
        IReadOnlyList<int> acceptedMemoryProposalIndexes,
        CancellationToken cancellationToken = default)
    {
        var startedAdjust = await _db.CoachAdjustments
            .FirstOrDefaultAsync(
                a => a.WeekStartDate == weekStartMonday && a.Status == CoachAdjustmentStatuses.Started,
                cancellationToken);

        var priorCommitted = await _db.CoachWeeks
            .FirstOrDefaultAsync(
                w => w.WeekStartDate == weekStartMonday && w.Status == CoachWeekStatuses.Committed,
                cancellationToken);
        if (priorCommitted != null && startedAdjust == null)
        {
            return (null, "Week is already committed and immutable", 409);
        }

        var draft = await _db.CoachWeeks
            .FirstOrDefaultAsync(
                w => w.WeekStartDate == weekStartMonday && w.Status == CoachWeekStatuses.Draft,
                cancellationToken);
        if (draft == null)
        {
            return (null, "Draft week not found", 404);
        }

        if (!string.IsNullOrWhiteSpace(optionalCommitJson))
        {
            var (edited, validateError) = await ValidateCommitAgainstConstraintsAsync(
                optionalCommitJson,
                weekStartMonday,
                cancellationToken);
            if (validateError != null || edited == null)
            {
                return (null, validateError ?? "Invalid weekly commit", 400);
            }

            if (startedAdjust != null)
            {
                var lockError = await ValidateLockedAgainstSourceAsync(
                    startedAdjust,
                    weekStartMonday,
                    edited,
                    cancellationToken);
                if (lockError != null)
                {
                    return (null, lockError, 400);
                }
            }

            draft.CommitJson = CoachWeeklyCommitSchema.Serialize(edited);
            draft.UpdatedAt = DateTime.UtcNow;
        }

        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            draft.CommitJson,
            weekStartMonday,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return (null, schemaError ?? "Draft commit is invalid", 400);
        }

        if (startedAdjust != null)
        {
            var lockError = await ValidateLockedAgainstSourceAsync(
                startedAdjust,
                weekStartMonday,
                commit,
                cancellationToken);
            if (lockError != null)
            {
                return (null, lockError, 400);
            }
        }

        var ackError = ValidateSafetyAcknowledgments(commit.SafetyFlags, safetyAcknowledgments);
        if (ackError != null)
        {
            return (null, ackError, 400);
        }

        var acceptIndexes = acceptedMemoryProposalIndexes ?? Array.Empty<int>();
        var memoryError = ValidateMemoryIndexes(commit.MemoryProposals, acceptIndexes);
        if (memoryError != null)
        {
            return (null, memoryError, 400);
        }

        var now = DateTime.UtcNow;
        await AppendAcceptedMemoryAsync(commit, acceptIndexes, draft.Id, now, cancellationToken);

        var applyRecord = new CoachWeekApplyRecord(
            AppliedAt: now,
            SafetyAcknowledgments: safetyAcknowledgments
                .Select(a => new CoachSafetyAcknowledgmentRecord(
                    a.Index,
                    a.Acknowledged,
                    a.Response))
                .ToList(),
            AcceptedMemoryProposalIndexes: acceptIndexes.ToList());

        if (startedAdjust != null && priorCommitted != null)
        {
            priorCommitted.Status = CoachWeekStatuses.Archived;
            priorCommitted.UpdatedAt = now;
            startedAdjust.Status = CoachAdjustmentStatuses.Applied;
            startedAdjust.UpdatedAt = now;
        }

        draft.Status = CoachWeekStatuses.Committed;
        draft.ApplyRecordJson = JsonSerializer.Serialize(applyRecord, JsonOptions);
        draft.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        return (draft, null, 200);
    }

    private async Task<string?> ValidateLockedAgainstSourceAsync(
        CoachAdjustment adjustment,
        DateOnly weekStartMonday,
        CoachWeeklyCommit commit,
        CancellationToken cancellationToken)
    {
        var source = await _db.CoachWeeks.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == adjustment.SourceCommittedWeekId, cancellationToken);
        if (source == null)
        {
            return "Source committed week for adjust not found";
        }

        var sourceError = CoachWeeklyCommitSchema.TryParseAndValidate(
            source.CommitJson,
            weekStartMonday,
            out var sourceCommit);
        if (sourceError != null || sourceCommit == null)
        {
            return sourceError ?? "Source committed week commit is invalid";
        }

        return CoachWeekSplice.ValidateLockedDaysUnchanged(
            sourceCommit,
            commit,
            adjustment.LockedThroughDate);
    }

    private async Task<(CoachWeeklyCommit? Commit, string? Error)> ValidateCommitAgainstConstraintsAsync(
        string commitJson,
        DateOnly weekStartMonday,
        CancellationToken cancellationToken)
    {
        var schemaError = CoachWeeklyCommitSchema.TryParseAndValidate(
            commitJson,
            weekStartMonday,
            out var commit);
        if (schemaError != null || commit == null)
        {
            return (null, schemaError ?? "Invalid weekly commit");
        }

        var constraints = await _db.CoachConstraints.AsNoTracking()
            .Where(c => !c.IsRetired)
            .ToListAsync(cancellationToken);
        var constraintError = CoachCommitConstraintValidator.Validate(commit, constraints);
        return constraintError == null ? (commit, null) : (null, constraintError);
    }

    internal static string? ValidateSafetyAcknowledgments(
        IReadOnlyList<CoachWeeklyCommitSafetyFlag> flags,
        IReadOnlyList<CoachSafetyAcknowledgment> acknowledgments)
    {
        flags ??= [];
        acknowledgments ??= [];

        var byIndex = acknowledgments
            .GroupBy(a => a.Index)
            .ToDictionary(g => g.Key, g => g.Last());

        for (var i = 0; i < flags.Count; i++)
        {
            if (!byIndex.TryGetValue(i, out var ack) || !ack.Acknowledged)
            {
                return $"safetyFlags[{i}] must be acknowledged before apply";
            }

            if (flags[i].RequiresResponse && string.IsNullOrWhiteSpace(ack.Response))
            {
                return $"safetyFlags[{i}] requires a non-empty response";
            }
        }

        return null;
    }

    internal static string? ValidateMemoryIndexes(
        IReadOnlyList<CoachWeeklyCommitMemoryProposal> proposals,
        IReadOnlyList<int> acceptedIndexes)
    {
        proposals ??= [];
        foreach (var index in acceptedIndexes.Distinct())
        {
            if (index < 0 || index >= proposals.Count)
            {
                return $"acceptedMemoryProposalIndexes contains out-of-range index {index}";
            }
        }

        return null;
    }

    private async Task AppendAcceptedMemoryAsync(
        CoachWeeklyCommit commit,
        IReadOnlyList<int> acceptedIndexes,
        Guid weekId,
        DateTime acceptedAt,
        CancellationToken cancellationToken)
    {
        foreach (var index in acceptedIndexes.Distinct().OrderBy(i => i))
        {
            var proposal = commit.MemoryProposals[index];
            var layerKey = NormalizeLayerKey(proposal.Layer);
            if (layerKey == null)
            {
                continue;
            }

            var layer = await _db.CoachMemoryLayers
                .FirstOrDefaultAsync(l => l.Layer == layerKey, cancellationToken);
            if (layer == null)
            {
                layer = new CoachMemoryLayer
                {
                    Layer = layerKey,
                    Document = "{}",
                    CreatedAt = acceptedAt,
                    UpdatedAt = acceptedAt
                };
                _db.CoachMemoryLayers.Add(layer);
            }

            var root = ParseObject(layer.Document);
            if (root["acceptedProposals"] is not JsonArray arr)
            {
                arr = new JsonArray();
                root["acceptedProposals"] = arr;
            }

            arr.Add(new JsonObject
            {
                ["path"] = proposal.Path ?? string.Empty,
                ["summary"] = proposal.Summary ?? string.Empty,
                ["acceptedAt"] = acceptedAt.ToString("O", CultureInfo.InvariantCulture),
                ["weekId"] = weekId.ToString()
            });

            layer.Document = root.ToJsonString();
            layer.UpdatedAt = acceptedAt;
        }
    }

    internal static string? NormalizeLayerKey(string? layer)
    {
        var trimmed = (layer ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.Equals("L1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer1", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer_1", StringComparison.OrdinalIgnoreCase))
        {
            return "L1";
        }

        if (trimmed.Equals("L2", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("2", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer2", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer_2", StringComparison.OrdinalIgnoreCase))
        {
            return "L2";
        }

        if (trimmed.Equals("L3", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("3", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer3", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("layer_3", StringComparison.OrdinalIgnoreCase))
        {
            return "L3";
        }

        return null;
    }

    private static JsonObject ParseObject(string json)
    {
        try
        {
            var node = JsonNode.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (node is JsonObject obj)
            {
                return obj;
            }
        }
        catch (JsonException)
        {
            // Replace unusable document with a fresh object.
        }

        return new JsonObject();
    }
}

public sealed record CoachSafetyAcknowledgment(int Index, bool Acknowledged, string? Response);

public sealed record CoachWeekApplyRecord(
    DateTime AppliedAt,
    IReadOnlyList<CoachSafetyAcknowledgmentRecord> SafetyAcknowledgments,
    IReadOnlyList<int> AcceptedMemoryProposalIndexes);

public sealed record CoachSafetyAcknowledgmentRecord(int Index, bool Acknowledged, string? Response);
