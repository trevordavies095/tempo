using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public interface IBestEffortService
{
    Task UpdateBestEffortsForNewWorkoutAsync(TempoDbContext db, Workout workout);
}
