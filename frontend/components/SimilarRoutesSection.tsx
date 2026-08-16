'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDate, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { IconArrowUp, IconArrowDown } from '@tabler/icons-react';

interface SimilarRoutesSectionProps {
  workoutId: string;
  currentWorkout: WorkoutDetail;
}

/**
 * Format time difference in seconds to a readable string
 * @param differenceS Time difference in seconds (negative = faster, positive = slower)
 * @returns Formatted string like "+2:15 slower" or "-2:15 faster"
 */
function formatTimeDifference(differenceS: number): string {
  if (differenceS === 0) {
    return 'same time';
  }
  const absSeconds = Math.abs(differenceS);
  const minutes = Math.floor(absSeconds / 60);
  const seconds = absSeconds % 60;
  const sign = differenceS < 0 ? '-' : '+';
  const fasterSlower = differenceS < 0 ? 'faster' : 'slower';
  return `${sign}${minutes}:${seconds.toString().padStart(2, '0')} ${fasterSlower}`;
}

/**
 * Format pace difference in seconds per km to a readable string
 * @param differenceS Pace difference in seconds per km (negative = faster pace, positive = slower pace)
 * @param unitPreference Unit preference for formatting
 * @returns Formatted string like "+5s/km slower" or "-16s/mi faster"
 */
function formatPaceDifference(differenceS: number, unitPreference: 'metric' | 'imperial'): string {
  if (differenceS === 0) {
    return 'same pace';
  }
  let convertedDifference: number;
  let unitLabel: string;
  
  if (unitPreference === 'imperial') {
    // Convert from seconds/km to seconds/mile (same conversion as formatPace)
    convertedDifference = differenceS * 1.609344;
    unitLabel = 's/mi';
  } else {
    convertedDifference = differenceS;
    unitLabel = 's/km';
  }
  
  const absSeconds = Math.abs(convertedDifference);
  const sign = convertedDifference < 0 ? '-' : '+';
  const fasterSlower = convertedDifference < 0 ? 'faster' : 'slower';
  return `${sign}${Math.round(absSeconds)}${unitLabel} ${fasterSlower}`;
}

/**
 * Format relative effort difference to a readable string
 * @param previousEffort Previous workout's relative effort
 * @param currentEffort Current workout's relative effort
 * @returns Formatted string like "85 (-5)" if current is 90 and previous is 85
 */
function formatRelativeEffortDifference(previousEffort: number, currentEffort: number): string {
  const difference = previousEffort - currentEffort;
  const sign = difference < 0 ? '-' : '+';
  const absDiff = Math.abs(difference);
  return `${previousEffort} (${sign}${absDiff})`;
}

/**
 * Format elevation difference to a readable string
 * @param previousElev Previous workout's elevation gain in meters
 * @param currentElev Current workout's elevation gain in meters
 * @param unitPreference Unit preference for formatting
 * @returns Formatted string like "+150m (+50m more)" or "+150m (-20m less)"
 */
function formatElevationDifference(
  previousElev: number,
  currentElev: number,
  unitPreference: 'metric' | 'imperial'
): string {
  const difference = previousElev - currentElev;
  const absDiff = Math.abs(difference);
  const previousFormatted = formatElevation(previousElev, unitPreference);
  
  if (unitPreference === 'imperial') {
    const diffFeet = Math.round(absDiff * 3.28084);
    if (difference > 0) {
      return `${previousFormatted} (+${diffFeet}ft more)`;
    } else if (difference < 0) {
      return `${previousFormatted} (-${diffFeet}ft less)`;
    } else {
      return previousFormatted;
    }
  } else {
    if (difference > 0) {
      return `${previousFormatted} (+${Math.round(absDiff)}m more)`;
    } else if (difference < 0) {
      return `${previousFormatted} (-${Math.round(absDiff)}m less)`;
    } else {
      return previousFormatted;
    }
  }
}

export function SimilarRoutesSection({ workoutId, currentWorkout }: SimilarRoutesSectionProps) {
  const { unitPreference } = useSettings();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['similar-routes', workoutId],
    queryFn: () => getSimilarRoutes(workoutId),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes (formerly cacheTime in v4)
    retry: 1,
    refetchOnWindowFocus: false,
    enabled: !!workoutId && !!currentWorkout.route,
  });

  // Loading state
  if (isLoading) {
    return (
      <div>
        <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="flex items-center justify-center py-4">
          <p className="text-sm text-muted">Loading...</p>
        </div>
      </div>
    );
  }

  // Error state
  if (isError) {
    return (
      <div>
        <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="p-3 bg-canvas border border-danger rounded-tempo">
          <p className="text-sm text-danger">
            Unable to load previous efforts. Please try again.
          </p>
        </div>
      </div>
    );
  }

  // Empty state
  if (!data || data.length === 0) {
    return (
      <div>
        <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="p-3 bg-canvas rounded-lg border border-border">
          <p className="text-sm text-muted">
            No previous efforts found on this route.
          </p>
        </div>
      </div>
    );
  }

  // Use data directly - backend already sorts by similarity score (highest first),
  // then by date (most recent first), then by distance similarity
  return (
    <div>
      <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
        Previous Efforts
      </h3>
      <div className="space-y-2">
        {data.map((route) => (
          <Link
            key={route.workoutId}
            href={`/dashboard/${route.workoutId}`}
            className="block p-3 bg-canvas rounded-lg border border-border hover:bg-canvas hover:border-ink transition-colors"
          >
            <div className="space-y-1.5">
              {/* Date */}
              <div className="text-xs font-medium text-ink">
                {formatDate(route.startedAt)}
              </div>

              {/* Duration with time difference */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-muted">Duration</div>
                <div className="flex items-center gap-1.5">
                  <span className="text-sm font-semibold text-ink">
                    {formatDuration(route.durationS)}
                  </span>
                  {route.timeDifferenceS !== undefined && route.timeDifferenceS !== null && (
                    <span
                      className={`text-xs ${
                        route.timeDifferenceS < 0
                          ? 'text-ink'
                          : route.timeDifferenceS > 0
                          ? 'text-danger'
                          : 'text-muted'
                      }`}
                    >
                      {formatTimeDifference(route.timeDifferenceS)}
                      {route.timeDifferenceS < 0 ? (
                        <IconArrowUp className="inline-block w-3 h-3 ml-0.5" />
                      ) : route.timeDifferenceS > 0 ? (
                        <IconArrowDown className="inline-block w-3 h-3 ml-0.5" />
                      ) : null}
                    </span>
                  )}
                </div>
              </div>

              {/* Pace with pace difference */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-muted">Pace</div>
                <div className="flex items-center gap-1.5">
                  <span className="text-sm font-semibold text-ink">
                    {formatPace(route.avgPaceS, unitPreference)}
                  </span>
                  {route.paceDifferenceS !== undefined && route.paceDifferenceS !== null && (
                    <span
                      className={`text-xs ${
                        route.paceDifferenceS < 0
                          ? 'text-ink'
                          : route.paceDifferenceS > 0
                          ? 'text-danger'
                          : 'text-muted'
                      }`}
                    >
                      {formatPaceDifference(route.paceDifferenceS, unitPreference)}
                      {route.paceDifferenceS < 0 ? (
                        <IconArrowUp className="inline-block w-3 h-3 ml-0.5" />
                      ) : route.paceDifferenceS > 0 ? (
                        <IconArrowDown className="inline-block w-3 h-3 ml-0.5" />
                      ) : null}
                    </span>
                  )}
                </div>
              </div>

              {/* Distance */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-muted">Distance</div>
                <div className="text-sm font-semibold text-ink">
                  {formatDistance(route.distanceM, unitPreference)}
                </div>
              </div>

              {/* Relative Effort (if available) */}
              {route.relativeEffort !== null && route.relativeEffort !== undefined && (
                <div className="flex items-center justify-between">
                  <div className="text-xs text-muted">Relative Effort</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-ink">
                      {route.relativeEffort}
                    </span>
                    {currentWorkout.relativeEffort !== null &&
                      currentWorkout.relativeEffort !== undefined && (
                        (() => {
                          const effortDiff = route.relativeEffort! - currentWorkout.relativeEffort;
                          const absDiff = Math.abs(effortDiff);
                          const sign = effortDiff < 0 ? '-' : '+';
                          return (
                            <span
                              className={`text-xs ${
                                effortDiff < 0
                                  ? 'text-ink'
                                  : effortDiff > 0
                                  ? 'text-danger'
                                  : 'text-muted'
                              }`}
                            >
                              ({sign}{absDiff})
                              {effortDiff < 0 ? (
                                <IconArrowUp className="inline-block w-3 h-3 ml-0.5" />
                              ) : effortDiff > 0 ? (
                                <IconArrowDown className="inline-block w-3 h-3 ml-0.5" />
                              ) : null}
                            </span>
                          );
                        })()
                      )}
                  </div>
                </div>
              )}

              {/* Elevation Gain (if available) */}
              {route.elevGainM !== null && route.elevGainM !== undefined && (
                <div className="flex items-center justify-between">
                  <div className="text-xs text-muted">Elevation</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-ink">
                      {formatElevation(route.elevGainM, unitPreference)}
                    </span>
                    {currentWorkout.elevGainM !== null &&
                      currentWorkout.elevGainM !== undefined && (
                        (() => {
                          const elevDiff = route.elevGainM! - currentWorkout.elevGainM;
                          const absDiff = Math.abs(elevDiff);
                          // Only calculate percentDiff if currentWorkout.elevGainM is not 0 to avoid division by zero
                          const percentDiff = currentWorkout.elevGainM > 0
                            ? Math.abs(elevDiff / currentWorkout.elevGainM) * 100
                            : 0;
                          // Only show difference if significant (>50m or >10% when elevation > 0)
                          const isSignificant = absDiff > 50 || (currentWorkout.elevGainM > 0 && percentDiff > 10);
                          
                          if (!isSignificant) {
                            return null;
                          }
                          
                          const moreLess = elevDiff > 0 ? 'more' : 'less';
                          const diffFormatted = unitPreference === 'imperial' 
                            ? `${Math.round(absDiff * 3.28084)}ft`
                            : `${Math.round(absDiff)}m`;
                          
                          return (
                            <span className="text-xs text-muted">
                              ({diffFormatted} {moreLess})
                            </span>
                          );
                        })()
                      )}
                  </div>
                </div>
              )}
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}

