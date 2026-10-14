namespace Tempo.Api.Services;

/// <summary>
/// Layer 1 JSON document persisted on CoachMemoryLayer (layer = L1).
/// Assemble later overlays live Tempo shoes + HR zones; this is the onboarding snapshot.
/// </summary>
public sealed record CoachLayer1Document(
    CoachLayer1HrZones HrZones,
    CoachLayer1Paces Paces,
    IReadOnlyList<CoachLayer1Shoe> Shoes,
    Guid? DefaultShoeId,
    CoachLayer1RaceStatus RaceStatus,
    DateTime CompletedAt);

public sealed record CoachLayer1HrZones(
    string CalculationMethod,
    int? Age,
    int? RestingHeartRateBpm,
    int? MaxHeartRateBpm,
    IReadOnlyList<CoachLayer1HrZone> Zones);

public sealed record CoachLayer1HrZone(int ZoneNumber, int MinBpm, int MaxBpm);

public sealed record CoachLayer1Paces(
    int EasySecPerKm,
    int TempoSecPerKm,
    int ThresholdSecPerKm,
    int IntervalSecPerKm);

public sealed record CoachLayer1Shoe(Guid Id, string Brand, string Model);

public sealed record CoachLayer1RaceStatus(
    string GoalType,
    string Distance,
    double DistanceM,
    string? RaceDate,
    int? TargetTimeS,
    string? Notes);
