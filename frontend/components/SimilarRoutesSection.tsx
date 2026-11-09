'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { getSimilarRoutes, type WorkoutDetail } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDate, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { IconArrowUp, IconArrowDown } from '@tabler/icons-react';
import { routePaceDelta, routeTimeDelta, type RouteDelta } from '@/lib/routeDelta';
import { RouteDeltaText } from '@/components/RouteDeltaText';

/** Up when this run was faster than the match, down when slower. */
function DeltaArrow({ delta }: { delta: RouteDelta | null }) {
  if (delta?.tone === 'faster') return <IconArrowUp className="inline-block w-3 h-3 ml-0.5" />;
  if (delta?.tone === 'slower') return <IconArrowDown className="inline-block w-3 h-3 ml-0.5" />;
  return null;
}

interface SimilarRoutesSectionProps {
  workoutId: string;
  currentWorkout: WorkoutDetail;
}

export function SimilarRoutesSection({ workoutId, currentWorkout }: SimilarRoutesSectionProps) {
  const { unitPreference } = useSettings();

  const { data, isLoading, isError } = useQuery({
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
                  <RouteDeltaText delta={routeTimeDelta(route.timeDifferenceS)} className="text-xs">
                    <DeltaArrow delta={routeTimeDelta(route.timeDifferenceS)} />
                  </RouteDeltaText>
                </div>
              </div>

              {/* Pace with pace difference */}
              <div className="flex items-center justify-between">
                <div className="text-xs text-muted">Pace</div>
                <div className="flex items-center gap-1.5">
                  <span className="text-sm font-semibold text-ink">
                    {formatPace(route.avgPaceS, unitPreference)}
                  </span>
                  <RouteDeltaText delta={routePaceDelta(route.paceDifferenceS, unitPreference)} className="text-xs">
                    <DeltaArrow delta={routePaceDelta(route.paceDifferenceS, unitPreference)} />
                  </RouteDeltaText>
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

