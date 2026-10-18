using System.Globalization;
using System.Text.RegularExpressions;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Pure Layer 2 episode selection: prior-week always included, then keyword top-k.
/// Ported from running-distillation assemble.select_layer2 (Tempo check-in fields).
/// </summary>
public static partial class CoachEpisodeSelector
{
    private static readonly Dictionary<string, double> CategoryBoost = new(StringComparer.OrdinalIgnoreCase)
    {
        [CoachEpisodeCategories.Issue] = 2.0,
        [CoachEpisodeCategories.Race] = 1.5,
        [CoachEpisodeCategories.Breakthrough] = 1.5,
        [CoachEpisodeCategories.Equipment] = 0.5,
        [CoachEpisodeCategories.Milestone] = 0.5,
        [CoachEpisodeCategories.ProtocolChange] = 0.5,
        [CoachEpisodeCategories.Recap] = 0.0
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "or", "to", "of", "in", "on", "for", "with", "after",
        "only", "was", "were", "is", "are", "be", "if", "as", "at", "my", "me", "i"
    };

    public static CoachEpisodeSelectionResult Select(
        IReadOnlyList<CoachEpisode> episodes,
        DateOnly weekEnding,
        CoachEpisodeQueryContext query,
        int l2TopK)
    {
        var priorWeekEnding = weekEnding.AddDays(-7);
        var queryTerms = ExtractQueryTerms(query);
        var boost = !string.IsNullOrWhiteSpace(query.Niggles)
                    || !string.IsNullOrWhiteSpace(query.LifeConstraints);

        var active = episodes.Where(e => !e.Retired).ToList();
        var prior = active.Where(e => InPriorWeek(e, priorWeekEnding)).ToList();
        var priorIds = prior.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);

        var scored = new List<(double Score, CoachEpisode Episode)>();
        foreach (var ep in active)
        {
            if (priorIds.Contains(ep.Id))
            {
                continue;
            }

            scored.Add((ScoreEpisode(ep, queryTerms, boost), ep));
        }

        scored.Sort((a, b) =>
        {
            var byScore = b.Score.CompareTo(a.Score);
            return byScore != 0 ? byScore : b.Episode.Date.CompareTo(a.Episode.Date);
        });

        var cap = Math.Max(0, l2TopK);
        var retrieved = scored.Take(cap).Select(s => s.Episode).ToList();

        var scoresById = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (score, episode) in scored.Take(cap))
        {
            scoresById[episode.Id] = score;
        }

        foreach (var ep in prior)
        {
            scoresById.TryAdd(ep.Id, ScoreEpisode(ep, queryTerms, boost));
        }

        return new CoachEpisodeSelectionResult(
            PriorWeek: prior,
            Retrieved: retrieved,
            PriorWeekEnding: priorWeekEnding,
            L2TopK: cap,
            ScoresById: scoresById,
            QueryTerms: queryTerms);
    }

    public static IReadOnlyList<string> ExtractQueryTerms(CoachEpisodeQueryContext query)
    {
        var blobs = new List<string?>
        {
            query.OverallFeel,
            query.Niggles,
            query.LifeConstraints
        };
        blobs.AddRange(query.RunNotes);
        blobs.AddRange(query.ConstraintNotes);

        var terms = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var blob in blobs)
        {
            if (string.IsNullOrWhiteSpace(blob))
            {
                continue;
            }

            foreach (Match match in TokenRegex().Matches(blob.ToLowerInvariant()))
            {
                var tok = match.Value;
                if (tok.Length < 3 || StopWords.Contains(tok) || !seen.Add(tok))
                {
                    continue;
                }

                terms.Add(tok);
            }
        }

        return terms;
    }

    public static double ScoreEpisode(
        CoachEpisode episode,
        IReadOnlyList<string> queryTerms,
        bool boostSignalCategories)
    {
        var summary = (episode.Summary ?? string.Empty).ToLowerInvariant();
        var tokens = TokenRegex().Matches(summary).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        var score = 0.0;
        foreach (var term in queryTerms)
        {
            if (tokens.Contains(term) || summary.Contains(term, StringComparison.Ordinal))
            {
                score += 1.0;
            }
        }

        if (boostSignalCategories
            && CategoryBoost.TryGetValue(episode.Category ?? string.Empty, out var boost))
        {
            score += boost;
        }

        return score;
    }

    public static bool InPriorWeek(CoachEpisode episode, DateOnly priorWeekEnding)
    {
        if (episode.SourceWeekEnding is DateOnly sourceWeekEnding)
        {
            return sourceWeekEnding == priorWeekEnding;
        }

        var start = priorWeekEnding.AddDays(-6);
        return episode.Date >= start && episode.Date <= priorWeekEnding;
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex TokenRegex();
}

public sealed record CoachEpisodeQueryContext(
    string? OverallFeel,
    string? Niggles,
    string? LifeConstraints,
    IReadOnlyList<string> RunNotes,
    IReadOnlyList<string> ConstraintNotes);

public sealed record CoachEpisodeSelectionResult(
    IReadOnlyList<CoachEpisode> PriorWeek,
    IReadOnlyList<CoachEpisode> Retrieved,
    DateOnly PriorWeekEnding,
    int L2TopK,
    IReadOnlyDictionary<string, double> ScoresById,
    IReadOnlyList<string> QueryTerms);
