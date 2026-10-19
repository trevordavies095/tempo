using System.Globalization;
using System.Text.Json;
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

        await StampMemoryValidityAsync(commit, weekStartMonday, cancellationToken);

        draft.CommitJson = CoachWeeklyCommitSchema.Serialize(commit);
        draft.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return (draft, null, 200);
    }

    public async Task<(CoachWeek? Week, string? Error, int StatusCode)> ApplyAsync(
        DateOnly weekStartMonday,
        string? optionalCommitJson,
        IReadOnlyList<CoachSafetyAcknowledgment> safetyAcknowledgments,
        IReadOnlyList<int> acceptedLayer1PatchIndexes,
        IReadOnlyList<int> acceptedEpisodeIndexes,
        IReadOnlyList<int> acceptedRuleProposalIndexes,
        bool archiveNotesAsRecap,
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

            await StampMemoryValidityAsync(edited, weekStartMonday, cancellationToken);
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

        // Authoritative re-stamp against current DB before accepting indexes.
        await StampMemoryValidityAsync(commit, weekStartMonday, cancellationToken);
        draft.CommitJson = CoachWeeklyCommitSchema.Serialize(commit);

        var ackError = ValidateSafetyAcknowledgments(commit.SafetyFlags, safetyAcknowledgments);
        if (ackError != null)
        {
            return (null, ackError, 400);
        }

        var l1Indexes = acceptedLayer1PatchIndexes ?? Array.Empty<int>();
        var episodeIndexes = acceptedEpisodeIndexes ?? Array.Empty<int>();
        var ruleIndexes = acceptedRuleProposalIndexes ?? Array.Empty<int>();

        var memoryError = ValidateAcceptedIndexes(
            "acceptedLayer1PatchIndexes", commit.Layer1Patches, l1Indexes, p => p.Validity);
        if (memoryError != null)
        {
            return (null, memoryError.Error, memoryError.StatusCode);
        }

        memoryError = ValidateAcceptedIndexes(
            "acceptedEpisodeIndexes", commit.Layer2EpisodeDrafts, episodeIndexes, p => p.Validity);
        if (memoryError != null)
        {
            return (null, memoryError.Error, memoryError.StatusCode);
        }

        memoryError = ValidateAcceptedIndexes(
            "acceptedRuleProposalIndexes", commit.Layer3RuleProposals, ruleIndexes, p => p.Validity);
        if (memoryError != null)
        {
            return (null, memoryError.Error, memoryError.StatusCode);
        }

        var now = DateTime.UtcNow;
        var weekEnding = weekStartMonday.AddDays(6);
        var applied = await ApplyAcceptedMemoryAsync(
            commit,
            l1Indexes,
            episodeIndexes,
            ruleIndexes,
            archiveNotesAsRecap,
            draft.Id,
            weekEnding,
            now,
            cancellationToken);

        var applyRecord = new CoachWeekApplyRecord(
            AppliedAt: now,
            SafetyAcknowledgments: safetyAcknowledgments
                .Select(a => new CoachSafetyAcknowledgmentRecord(
                    a.Index,
                    a.Acknowledged,
                    a.Response))
                .ToList(),
            AcceptedLayer1PatchIndexes: l1Indexes.Distinct().OrderBy(i => i).ToList(),
            AcceptedEpisodeIndexes: episodeIndexes.Distinct().OrderBy(i => i).ToList(),
            AcceptedRuleProposalIndexes: ruleIndexes.Distinct().OrderBy(i => i).ToList(),
            ArchiveNotesAsRecap: archiveNotesAsRecap,
            AppliedLayer1Paths: applied.Layer1Paths,
            AppliedEpisodeIds: applied.EpisodeIds,
            AppliedRuleIds: applied.RuleIds,
            RecapEpisodeId: applied.RecapEpisodeId);

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

        // Accept of week W locks exchanges for week W-7 (hard lock; no escape hatch).
        await CoachExchangeWeekLock.StampPriorWeekLockedAsync(
            _db,
            weekStartMonday,
            now,
            cancellationToken);

        return (draft, null, 200);
    }

    private async Task StampMemoryValidityAsync(
        CoachWeeklyCommit commit,
        DateOnly weekStartMonday,
        CancellationToken cancellationToken)
    {
        var episodeIds = await _db.CoachEpisodes.AsNoTracking()
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);
        var activeRules = await _db.CoachRules.AsNoTracking()
            .Where(r => r.Status == CoachRuleStatuses.Active)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        CoachMemoryProposalValidator.Stamp(
            commit,
            weekStartMonday,
            episodeIds.ToHashSet(StringComparer.Ordinal),
            activeRules);
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

    internal static MemoryIndexError? ValidateAcceptedIndexes<T>(
        string fieldName,
        IReadOnlyList<T> proposals,
        IReadOnlyList<int> acceptedIndexes,
        Func<T, CoachProposalValidity?> validitySelector)
    {
        proposals ??= [];
        foreach (var index in acceptedIndexes.Distinct())
        {
            if (index < 0 || index >= proposals.Count)
            {
                return new MemoryIndexError($"{fieldName} contains out-of-range index {index}", 400);
            }

            var validity = validitySelector(proposals[index]);
            if (validity is { Ok: false })
            {
                return new MemoryIndexError(
                    $"{fieldName} index {index} is invalid and cannot be accepted: {validity.Reason}",
                    409);
            }
        }

        return null;
    }

    private async Task<(
        List<string> Layer1Paths,
        List<string> EpisodeIds,
        List<string> RuleIds,
        string? RecapEpisodeId)> ApplyAcceptedMemoryAsync(
        CoachWeeklyCommit commit,
        IReadOnlyList<int> l1Indexes,
        IReadOnlyList<int> episodeIndexes,
        IReadOnlyList<int> ruleIndexes,
        bool archiveNotesAsRecap,
        Guid weekId,
        DateOnly weekEnding,
        DateTime acceptedAt,
        CancellationToken cancellationToken)
    {
        var appliedPaths = new List<string>();
        var appliedEpisodeIds = new List<string>();
        var appliedRuleIds = new List<string>();
        string? recapId = null;

        if (l1Indexes.Count > 0)
        {
            var layer = await _db.CoachMemoryLayers
                .FirstOrDefaultAsync(l => l.Layer == "L1", cancellationToken);
            if (layer == null)
            {
                throw new InvalidOperationException("Layer 1 document is required before applying L1 patches");
            }

            var document = CoachLayer1Json.Deserialize(layer.Document)
                ?? throw new InvalidOperationException("Layer 1 document could not be deserialized");

            foreach (var index in l1Indexes.Distinct().OrderBy(i => i))
            {
                var patch = commit.Layer1Patches[index];
                document = CoachLayer1Patcher.Apply(document, patch);
                appliedPaths.Add(patch.FieldPath);
            }

            layer.Document = CoachLayer1Json.Serialize(document);
            layer.UpdatedAt = acceptedAt;
        }

        var existingEpisodeIds = (await _db.CoachEpisodes.Select(e => e.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var index in episodeIndexes.Distinct().OrderBy(i => i))
        {
            var draft = commit.Layer2EpisodeDrafts[index];
            var date = DateOnly.Parse(draft.Date, CultureInfo.InvariantCulture);
            var id = CoachMemoryIds.EpisodeId(date, draft.Category, draft.Summary, existingEpisodeIds);
            existingEpisodeIds.Add(id);

            DateOnly? sourceWeekEnding = null;
            if (!string.IsNullOrWhiteSpace(draft.SourceWeekEnding)
                && DateOnly.TryParse(draft.SourceWeekEnding, CultureInfo.InvariantCulture, DateTimeStyles.None, out var swe))
            {
                sourceWeekEnding = swe;
            }
            else
            {
                sourceWeekEnding = weekEnding;
            }

            _db.CoachEpisodes.Add(new CoachEpisode
            {
                Id = id,
                Date = date,
                Category = draft.Category,
                Summary = draft.Summary.Length > CoachMemoryProposalValidator.MaxEpisodeSummaryLength
                    ? draft.Summary[..CoachMemoryProposalValidator.MaxEpisodeSummaryLength]
                    : draft.Summary,
                SourceWeekEnding = sourceWeekEnding,
                SourceWeekId = weekId,
                Source = CoachEpisodeSources.Coach,
                Retired = false,
                CreatedAt = acceptedAt
            });
            appliedEpisodeIds.Add(id);
        }

        var existingRuleIds = (await _db.CoachRules.Select(r => r.Id).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);
        var rulesById = await _db.CoachRules.ToDictionaryAsync(r => r.Id, cancellationToken);

        foreach (var index in ruleIndexes.Distinct().OrderBy(i => i))
        {
            var proposal = commit.Layer3RuleProposals[index];
            var operation = proposal.Operation.Trim().ToLowerInvariant();
            if (operation == "promote")
            {
                var id = CoachMemoryIds.RuleId(proposal.RuleText!, existingRuleIds);
                existingRuleIds.Add(id);
                var evidence = proposal.Evidence
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Select(e => e.Trim())
                    .ToList();
                _db.CoachRules.Add(new CoachRule
                {
                    Id = id,
                    TargetLayer = proposal.TargetLayer,
                    RuleText = proposal.RuleText!.Trim(),
                    Status = CoachRuleStatuses.Active,
                    Evidence = evidence,
                    Source = CoachRuleSources.Coach,
                    CreatedAt = acceptedAt,
                    UpdatedAt = acceptedAt
                });
                appliedRuleIds.Add(id);
            }
            else if (operation == "revise")
            {
                var rule = rulesById[proposal.TargetRuleId!.Trim()];
                rule.RuleText = proposal.RuleText!.Trim();
                rule.TargetLayer = proposal.TargetLayer;
                var merged = rule.Evidence.ToList();
                foreach (var item in proposal.Evidence.Where(e => !string.IsNullOrWhiteSpace(e)))
                {
                    var trimmed = item.Trim();
                    if (!merged.Contains(trimmed, StringComparer.Ordinal))
                    {
                        merged.Add(trimmed);
                    }
                }

                rule.Evidence = merged;
                rule.UpdatedAt = acceptedAt;
                appliedRuleIds.Add(rule.Id);
            }
            else if (operation == "retire")
            {
                var rule = rulesById[proposal.TargetRuleId!.Trim()];
                rule.Status = CoachRuleStatuses.Retired;
                rule.UpdatedAt = acceptedAt;
                appliedRuleIds.Add(rule.Id);
            }
        }

        if (archiveNotesAsRecap)
        {
            var summary = commit.CoachesNotes.Trim();
            if (summary.Length > CoachMemoryProposalValidator.MaxEpisodeSummaryLength)
            {
                summary = summary[..CoachMemoryProposalValidator.MaxEpisodeSummaryLength];
            }

            recapId = CoachMemoryIds.EpisodeId(
                weekEnding,
                CoachEpisodeCategories.Recap,
                summary,
                existingEpisodeIds);
            existingEpisodeIds.Add(recapId);
            _db.CoachEpisodes.Add(new CoachEpisode
            {
                Id = recapId,
                Date = weekEnding,
                Category = CoachEpisodeCategories.Recap,
                Summary = summary,
                SourceWeekEnding = weekEnding,
                SourceWeekId = weekId,
                Source = CoachEpisodeSources.Coach,
                Retired = false,
                CreatedAt = acceptedAt
            });
            appliedEpisodeIds.Add(recapId);
        }

        return (appliedPaths, appliedEpisodeIds, appliedRuleIds, recapId);
    }
}

public sealed record CoachSafetyAcknowledgment(int Index, bool Acknowledged, string? Response);

public sealed record MemoryIndexError(string Error, int StatusCode);

public sealed record CoachWeekApplyRecord(
    DateTime AppliedAt,
    IReadOnlyList<CoachSafetyAcknowledgmentRecord> SafetyAcknowledgments,
    IReadOnlyList<int> AcceptedLayer1PatchIndexes,
    IReadOnlyList<int> AcceptedEpisodeIndexes,
    IReadOnlyList<int> AcceptedRuleProposalIndexes,
    bool ArchiveNotesAsRecap,
    IReadOnlyList<string> AppliedLayer1Paths,
    IReadOnlyList<string> AppliedEpisodeIds,
    IReadOnlyList<string> AppliedRuleIds,
    string? RecapEpisodeId);

public sealed record CoachSafetyAcknowledgmentRecord(int Index, bool Acknowledged, string? Response);
