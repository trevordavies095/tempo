using Microsoft.EntityFrameworkCore;
using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

/// <summary>
/// Resets peelable coach setup while keeping opt-in preferences and the Anthropic provider key.
/// </summary>
public sealed class CoachSetupService
{
    private readonly TempoDbContext _db;
    private readonly CoachJobService _jobs;

    public CoachSetupService(TempoDbContext db, CoachJobService jobs)
    {
        _db = db;
        _jobs = jobs;
    }

    /// <summary>
    /// Interrupts active coach jobs, then deletes all peelable coach rows except
    /// <c>CoachPreferences</c> and <c>CoachProviderConnections</c>.
    /// Clears SeedMemoryNudgeDismissed so a wiped instance can see the seed nudge again.
    /// </summary>
    public async Task ResetSetupAsync(CancellationToken cancellationToken = default)
    {
        await WipePeelableAsync(cancellationToken);

        var prefs = await _db.CoachPreferences.FirstOrDefaultAsync(cancellationToken);
        if (prefs != null && prefs.SeedMemoryNudgeDismissed)
        {
            prefs.SeedMemoryNudgeDismissed = false;
            prefs.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Full peel: interrupts jobs, deletes every coach table row including provider ciphertext
    /// and preferences (opt-in). Core workouts, shoes, and UserSettings are untouched.
    /// </summary>
    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await WipePeelableAsync(cancellationToken);
        await _db.CoachProviderConnections.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachPreferences.ExecuteDeleteAsync(cancellationToken);
    }

    private async Task WipePeelableAsync(CancellationToken cancellationToken)
    {
        await _jobs.InterruptIncompleteJobsAsync(cancellationToken);

        await _db.CoachJobs.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachWeeks.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachAdjustments.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachPrivacyPreflights.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachCheckIns.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachConstraints.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachEpisodes.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachRules.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachMemoryLayers.ExecuteDeleteAsync(cancellationToken);
        await _db.CoachGoals.ExecuteDeleteAsync(cancellationToken);
    }
}
