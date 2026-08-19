using Tempo.Api.Data;
using Tempo.Api.Models;

namespace Tempo.Api.Services;

public interface IRelativeEffortService
{
    int? CalculateRelativeEffort(Workout workout, List<HeartRateZone> zones, TempoDbContext db);
}
