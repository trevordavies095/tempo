import { formatDistance, formatDuration, formatPace, formatElevation } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';
import { WeatherDisplay } from '@/components/WeatherDisplay';

export function WorkoutOverviewMetrics({
  workout,
  unitPreference,
}: {
  workout: WorkoutDetail;
  unitPreference: UnitPreference;
}) {
  const hasAdditionalDetails =
    workout.elevGainM !== null ||
    workout.calories !== null ||
    workout.relativeEffort !== null ||
    workout.maxHeartRateBpm !== null ||
    workout.avgHeartRateBpm !== null ||
    workout.maxCadenceRpm !== null ||
    workout.avgCadenceRpm !== null ||
    workout.maxPowerWatts !== null ||
    workout.avgPowerWatts !== null;
  const hasWeather = !!workout.weather;
  const bothExist = hasAdditionalDetails && hasWeather;
  const showMovingTime =
    workout.movingTimeS !== null && workout.movingTimeS !== workout.durationS;

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
            <div className="text-xs text-muted mb-1">
              {showMovingTime ? 'Moving Time' : 'Duration'}
            </div>
            <div className="text-2xl font-bold text-ink">
              {showMovingTime
                ? formatDuration(workout.movingTimeS!)
                : formatDuration(workout.durationS)}
            </div>
            {showMovingTime && (
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
                {showMovingTime && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Elapsed Time</span>
                    <span className="text-sm font-semibold text-ink">
                      {formatDuration(workout.durationS)}
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
                {(workout.maxCadenceRpm !== null || workout.avgCadenceRpm !== null) && (
                  <div className="flex justify-between items-center">
                    <span className="text-xs text-muted">Cadence</span>
                    <span className="text-sm font-semibold text-ink">
                      {workout.maxCadenceRpm !== null && workout.avgCadenceRpm !== null
                        ? `${workout.maxCadenceRpm} / ${workout.avgCadenceRpm} rpm`
                        : workout.maxCadenceRpm !== null
                          ? `${workout.maxCadenceRpm} rpm`
                          : `${workout.avgCadenceRpm} rpm`}
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
