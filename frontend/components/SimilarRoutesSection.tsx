'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDate, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';

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
 * @returns Formatted string like "+5s/km slower" or "-5s/km faster"
 */
function formatPaceDifference(differenceS: number): string {
  const absSeconds = Math.abs(differenceS);
  const sign = differenceS < 0 ? '-' : '+';
  const fasterSlower = differenceS < 0 ? 'faster' : 'slower';
  return `${sign}${Math.round(absSeconds)}s/km ${fasterSlower}`;
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
        <h3 className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="flex items-center justify-center py-4">
          <p className="text-sm text-gray-600 dark:text-gray-400">Loading...</p>
        </div>
      </div>
    );
  }

  // Error state
  if (isError) {
    return (
      <div>
        <h3 className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="p-3 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
          <p className="text-sm text-red-800 dark:text-red-200">
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
        <h3 className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2 uppercase tracking-wide">
          Previous Efforts
        </h3>
        <div className="p-3 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700">
          <p className="text-sm text-gray-500 dark:text-gray-400">
            No previous efforts found on this route.
          </p>
        </div>
      </div>
    );
  }

  // Sort by date (most recent first)
  const sortedRoutes = [...data].sort((a, b) => 
    new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
  );

  return (
    <div>
      <h3 className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2 uppercase tracking-wide">
        Previous Efforts
      </h3>
      <div className="space-y-2">
        {sortedRoutes.map((route) => (
          <Link
            key={route.workoutId}
            href={`/dashboard/${route.workoutId}`}
            className="block p-3 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700 hover:bg-gray-100 dark:hover:bg-gray-800 hover:border-gray-300 dark:hover:border-gray-600 transition-colors"
          >
            <div className="space-y-1.5">
              {/* Date */}
              <div className="text-xs font-medium text-gray-900 dark:text-gray-100">
                {formatDate(route.startedAt)}
              </div>

              {/* Duration with time difference */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-gray-500 dark:text-gray-400">Duration</div>
                <div className="flex items-center gap-1.5">
                  <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatDuration(route.durationS)}
                  </span>
                  {route.timeDifferenceS !== undefined && route.timeDifferenceS !== null && (
                    <span
                      className={`text-xs ${
                        route.timeDifferenceS < 0
                          ? 'text-green-600 dark:text-green-400'
                          : 'text-red-600 dark:text-red-400'
                      }`}
                    >
                      {formatTimeDifference(route.timeDifferenceS)}
                      {route.timeDifferenceS < 0 ? (
                        <svg
                          className="inline-block w-3 h-3 ml-0.5"
                          fill="none"
                          stroke="currentColor"
                          viewBox="0 0 24 24"
                          xmlns="http://www.w3.org/2000/svg"
                        >
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M5 10l7-7m0 0l7 7m-7-7v18"
                          />
                        </svg>
                      ) : (
                        <svg
                          className="inline-block w-3 h-3 ml-0.5"
                          fill="none"
                          stroke="currentColor"
                          viewBox="0 0 24 24"
                          xmlns="http://www.w3.org/2000/svg"
                        >
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M19 14l-7 7m0 0l-7-7m7 7V3"
                          />
                        </svg>
                      )}
                    </span>
                  )}
                </div>
              </div>

              {/* Pace with pace difference */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-gray-500 dark:text-gray-400">Pace</div>
                <div className="flex items-center gap-1.5">
                  <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatPace(route.avgPaceS, unitPreference)}
                  </span>
                  {route.paceDifferenceS !== undefined && route.paceDifferenceS !== null && (
                    <span
                      className={`text-xs ${
                        route.paceDifferenceS < 0
                          ? 'text-green-600 dark:text-green-400'
                          : 'text-red-600 dark:text-red-400'
                      }`}
                    >
                      {formatPaceDifference(route.paceDifferenceS)}
                      {route.paceDifferenceS < 0 ? (
                        <svg
                          className="inline-block w-3 h-3 ml-0.5"
                          fill="none"
                          stroke="currentColor"
                          viewBox="0 0 24 24"
                          xmlns="http://www.w3.org/2000/svg"
                        >
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M5 10l7-7m0 0l7 7m-7-7v18"
                          />
                        </svg>
                      ) : (
                        <svg
                          className="inline-block w-3 h-3 ml-0.5"
                          fill="none"
                          stroke="currentColor"
                          viewBox="0 0 24 24"
                          xmlns="http://www.w3.org/2000/svg"
                        >
                          <path
                            strokeLinecap="round"
                            strokeLinejoin="round"
                            strokeWidth={2}
                            d="M19 14l-7 7m0 0l-7-7m7 7V3"
                          />
                        </svg>
                      )}
                    </span>
                  )}
                </div>
              </div>

              {/* Distance */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-gray-500 dark:text-gray-400">Distance</div>
                <div className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                  {formatDistance(route.distanceM, unitPreference)}
                </div>
              </div>

              {/* Relative Effort (if available) */}
              {route.relativeEffort !== null && route.relativeEffort !== undefined && (
                <div className="flex items-center justify-between">
                  <div className="text-xs text-gray-500 dark:text-gray-400">Relative Effort</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
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
                                  ? 'text-green-600 dark:text-green-400'
                                  : effortDiff > 0
                                  ? 'text-red-600 dark:text-red-400'
                                  : 'text-gray-500 dark:text-gray-400'
                              }`}
                            >
                              ({sign}{absDiff})
                              {effortDiff < 0 ? (
                                <svg
                                  className="inline-block w-3 h-3 ml-0.5"
                                  fill="none"
                                  stroke="currentColor"
                                  viewBox="0 0 24 24"
                                  xmlns="http://www.w3.org/2000/svg"
                                >
                                  <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M5 10l7-7m0 0l7 7m-7-7v18"
                                  />
                                </svg>
                              ) : effortDiff > 0 ? (
                                <svg
                                  className="inline-block w-3 h-3 ml-0.5"
                                  fill="none"
                                  stroke="currentColor"
                                  viewBox="0 0 24 24"
                                  xmlns="http://www.w3.org/2000/svg"
                                >
                                  <path
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeWidth={2}
                                    d="M19 14l-7 7m0 0l-7-7m7 7V3"
                                  />
                                </svg>
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
                  <div className="text-xs text-gray-500 dark:text-gray-400">Elevation</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatElevation(route.elevGainM, unitPreference)}
                    </span>
                    {currentWorkout.elevGainM !== null &&
                      currentWorkout.elevGainM !== undefined && (
                        (() => {
                          const elevDiff = route.elevGainM! - currentWorkout.elevGainM;
                          const absDiff = Math.abs(elevDiff);
                          const percentDiff = Math.abs(elevDiff / currentWorkout.elevGainM) * 100;
                          // Only show difference if significant (>50m or >10%)
                          const isSignificant = absDiff > 50 || percentDiff > 10;
                          
                          if (!isSignificant) {
                            return null;
                          }
                          
                          const sign = elevDiff > 0 ? '+' : '-';
                          const moreLess = elevDiff > 0 ? 'more' : 'less';
                          const diffFormatted = unitPreference === 'imperial' 
                            ? `${Math.round(absDiff * 3.28084)}ft`
                            : `${Math.round(absDiff)}m`;
                          
                          return (
                            <span className="text-xs text-gray-500 dark:text-gray-400">
                              ({sign}{diffFormatted} {moreLess})
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

