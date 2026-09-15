import { formatDistance, formatDuration, formatPace, formatElevation } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';
import { overviewDurationDisplay } from '@/lib/workoutClocks';
import { WeatherDisplay } from '@/components/WeatherDisplay';

const ZONE_BAR_CLASSES = [
  'h-full bg-danger/20',
  'h-full bg-danger/40',
  'h-full bg-danger/60',
  'h-full bg-danger/80',
  'h-full bg-danger',
] as const;

/** Integer percents of sum(timeS) via largest remainder; timeS === 0 always gets 0%. */
function zoneTimePercents(times: Array<{ timeS: number }>): number[] {
  const sum = times.reduce((acc, z) => acc + z.timeS, 0);
  if (sum <= 0) {
    return times.map(() => 0);
  }

  const eligibleIdx: number[] = [];
  for (let i = 0; i < times.length; i++) {
    if (times[i].timeS > 0) eligibleIdx.push(i);
  }

  const floors = new Array(times.length).fill(0);
  const fracs: { i: number; frac: number }[] = [];
  for (const i of eligibleIdx) {
    const exact = (times[i].timeS / sum) * 100;
    floors[i] = Math.floor(exact);
    fracs.push({ i, frac: exact - floors[i] });
  }

  let rem = 100 - floors.reduce((a: number, b: number) => a + b, 0);
  fracs.sort((a, b) => b.frac - a.frac);
  for (const item of fracs) {
    if (rem <= 0) break;
    floors[item.i] += 1;
    rem -= 1;
  }
  return floors;
}

export function WorkoutOverviewMetrics({
  workout,
  unitPreference,
}: {
  workout: WorkoutDetail;
  unitPreference: UnitPreference;
}) {
  const durationDisplay = overviewDurationDisplay(workout);
  const zoneTimes =
    workout.heartRateZoneTimes && workout.heartRateZoneTimes.length === 5
      ? workout.heartRateZoneTimes
      : null;
  const zonePercents = zoneTimes ? zoneTimePercents(zoneTimes) : null;

  const hasAdditionalDetails =
    workout.elevGainM !== null ||
    workout.calories !== null ||
    workout.relativeEffort !== null ||
    workout.maxHeartRateBpm !== null ||
    workout.avgHeartRateBpm !== null ||
    zoneTimes !== null ||
    workout.maxCadenceRpm !== null ||
    workout.avgCadenceRpm !== null ||
    workout.maxPowerWatts !== null ||
    workout.avgPowerWatts !== null ||
    durationDisplay.showElapsedSubtitle ||
    durationDisplay.showMovingInAdditionalDetails;
  const hasWeather = !!workout.weather;
  const bothExist = hasAdditionalDetails && hasWeather;

  return (
    <div className="space-y-2.5">
      <div>
        <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
          Key Metrics
        </h3>
        <div
          className={`grid gap-3 min-w-0 ${
            workout.relativeEffort !== null
              ? 'grid-cols-2 sm:grid-cols-4'
              : 'grid-cols-2 sm:grid-cols-3'
          }`}
        >
          <div className="min-w-0">
            <div className="text-xs text-muted mb-1">Distance</div>
            <div className="text-2xl font-bold text-ink">
              {formatDistance(workout.distanceM, unitPreference)}
            </div>
          </div>
          <div className="min-w-0">
            <div className="text-xs text-muted mb-1">{durationDisplay.heroLabel}</div>
            <div className="text-2xl font-bold text-ink">
              {formatDuration(durationDisplay.heroSeconds)}
            </div>
            {durationDisplay.showElapsedSubtitle && (
              <div className="text-xs text-muted mt-1">
                Elapsed: {formatDuration(workout.durationS)}
              </div>
            )}
          </div>
          <div className="min-w-0">
            <div className="text-xs text-muted mb-1">Pace</div>
            <div className="text-2xl font-bold text-ink">
              {formatPace(workout.avgPaceS, unitPreference)}
            </div>
          </div>
          {workout.relativeEffort !== null && (
            <div className="min-w-0">
              <div className="text-xs text-muted mb-1">Relative Effort</div>
              <div className="text-2xl font-bold text-ink">{workout.relativeEffort}</div>
            </div>
          )}
        </div>
      </div>

      {(hasAdditionalDetails || hasWeather) && (
        <div className={bothExist ? 'grid grid-cols-1 lg:grid-cols-2 gap-4 min-w-0' : ''}>
          {hasAdditionalDetails && (
            <div>
              <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
                Additional Details
              </h3>
              <div className="space-y-1.5">
                {workout.elevGainM !== null && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Elevation</span>
                    <span className="text-sm font-semibold text-ink">
                      {formatElevation(workout.elevGainM, unitPreference)}
                    </span>
                  </div>
                )}
                {durationDisplay.showElapsedSubtitle && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Elapsed Time</span>
                    <span className="text-sm font-semibold text-ink">
                      {formatDuration(workout.durationS)}
                    </span>
                  </div>
                )}
                {durationDisplay.showMovingInAdditionalDetails && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Moving Time</span>
                    <span className="text-sm font-semibold text-ink">
                      {formatDuration(workout.movingTimeS!)}
                    </span>
                  </div>
                )}
                {workout.calories !== null && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Calories</span>
                    <span className="text-sm font-semibold text-ink">{workout.calories}</span>
                  </div>
                )}
                {workout.relativeEffort !== null && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Relative Effort</span>
                    <span className="text-sm font-semibold text-ink">
                      {workout.relativeEffort}
                    </span>
                  </div>
                )}
                {(workout.maxHeartRateBpm !== null || workout.avgHeartRateBpm !== null) && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Heart Rate</span>
                    <span className="text-sm font-semibold text-ink">
                      {workout.maxHeartRateBpm !== null && workout.avgHeartRateBpm !== null
                        ? `${workout.maxHeartRateBpm} / ${workout.avgHeartRateBpm} bpm`
                        : workout.maxHeartRateBpm !== null
                          ? `${workout.maxHeartRateBpm} bpm`
                          : `${workout.avgHeartRateBpm} bpm`}
                    </span>
                  </div>
                )}
                {zoneTimes && zonePercents && (
                  <div className="space-y-1.5 pt-0.5">
                    <div className="flex h-2 w-full overflow-hidden rounded-sm bg-canvas">
                      {zoneTimes.map((z, i) => {
                        const pct = zonePercents[i];
                        if (pct <= 0) return null;
                        return (
                          <div
                            key={z.zone}
                            className={ZONE_BAR_CLASSES[i]}
                            style={{ width: `${pct}%` }}
                            title={`Zone ${z.zone}`}
                          />
                        );
                      })}
                    </div>
                    {zoneTimes.map((z, i) => (
                      <div key={z.zone} className="flex justify-between items-center gap-2">
                        <span className="text-xs text-muted">Zone {z.zone}</span>
                        <span className="text-sm font-semibold text-ink tabular-nums">
                          {formatDuration(z.timeS)}
                          <span className="text-muted font-normal ml-2">{zonePercents[i]}%</span>
                        </span>
                      </div>
                    ))}
                  </div>
                )}
                {(workout.maxCadenceRpm !== null || workout.avgCadenceRpm !== null) && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Cadence</span>
                    <span className="text-sm font-semibold text-ink">
                      {workout.maxCadenceRpm !== null && workout.avgCadenceRpm !== null
                        ? `${workout.maxCadenceRpm} / ${workout.avgCadenceRpm} spm`
                        : workout.maxCadenceRpm !== null
                          ? `${workout.maxCadenceRpm} spm`
                          : `${workout.avgCadenceRpm} spm`}
                    </span>
                  </div>
                )}
                {(workout.maxPowerWatts !== null || workout.avgPowerWatts !== null) && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Power</span>
                    <span className="text-sm font-semibold text-ink">
                      {workout.maxPowerWatts !== null && workout.avgPowerWatts !== null
                        ? `${workout.maxPowerWatts} / ${workout.avgPowerWatts} W`
                        : workout.maxPowerWatts !== null
                          ? `${workout.maxPowerWatts} W`
                          : `${workout.avgPowerWatts} W`}
                    </span>
                  </div>
                )}
              </div>
            </div>
          )}

          {hasWeather && (
            <WeatherDisplay
              weather={workout.weather}
              workoutStartTime={workout.startedAt}
              embedded
            />
          )}
        </div>
      )}
    </div>
  );
}
