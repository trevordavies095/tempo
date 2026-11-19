'use client';

import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useRouter, usePathname } from 'next/navigation';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDate } from '@/lib/format';
import { routeTimeDelta, type RouteDelta } from '@/lib/routeDelta';
import { IconChevronRight } from '@tabler/icons-react';
import { RouteDeltaText } from '@/components/RouteDeltaText';

interface RouteMatchesSummaryProps {
  workoutId: string;
  currentWorkout: WorkoutDetail;
}

interface Highlight {
  type: 'best' | 'latest' | 'fastest';
  route: SimilarRoute;
  label: string;
  /** This run − the highlighted match; null when the server sent no difference. */
  delta: RouteDelta | null;
}

/**
 * Calculate highlights from matched routes
 * @param routes Array of similar routes
 * @returns Array of highlights (best time, most recent, fastest pace)
 */
function calculateHighlights(routes: SimilarRoute[]): Highlight[] {
  if (routes.length === 0) {
    return [];
  }

  const highlights: Highlight[] = [];

  // Best Time: the match with the most negative timeDifferenceS (match − current), i.e. the fastest match
  const bestTime = routes.reduce((best, route) => {
    if (route.timeDifferenceS === undefined || route.timeDifferenceS === null) {
      return best;
    }
    if (best === null) {
      return route;
    }
    if (best.timeDifferenceS === undefined || best.timeDifferenceS === null) {
      return route;
    }
    return route.timeDifferenceS < best.timeDifferenceS ? route : best;
  }, null as SimilarRoute | null);

  if (bestTime && bestTime.timeDifferenceS !== undefined && bestTime.timeDifferenceS !== null) {
    highlights.push({
      type: 'best',
      route: bestTime,
      label: 'Best',
      delta: routeTimeDelta(bestTime.timeDifferenceS),
    });
  }

  // Most Recent: Route with latest startedAt date
  const mostRecent = routes.reduce((latest, route) => {
    return new Date(route.startedAt) > new Date(latest.startedAt) ? route : latest;
  }, routes[0]);

  // Only add if different from best time
  if (!bestTime || mostRecent.workoutId !== bestTime.workoutId) {
    highlights.push({
      type: 'latest',
      route: mostRecent,
      label: 'Latest',
      delta: routeTimeDelta(mostRecent.timeDifferenceS),
    });
  }

  // Fastest Pace: Route with lowest avgPaceS (optional, only if different from best time)
  const fastestPace = routes.reduce((fastest, route) => {
    return route.avgPaceS < fastest.avgPaceS ? route : fastest;
  }, routes[0]);

  // Only add if different from both best time and most recent
  if (
    (!bestTime || fastestPace.workoutId !== bestTime.workoutId) &&
    fastestPace.workoutId !== mostRecent.workoutId
  ) {
    highlights.push({
      type: 'fastest',
      route: fastestPace,
      label: 'Fastest Pace',
      delta: routeTimeDelta(fastestPace.timeDifferenceS),
    });
  }

  // Limit to 3 highlights
  return highlights.slice(0, 3);
}

export function RouteMatchesSummary({ workoutId, currentWorkout }: RouteMatchesSummaryProps) {
  const router = useRouter();
  const pathname = usePathname();

  const { data, isLoading, isError } = useQuery({
    queryKey: ['similar-routes', workoutId],
    queryFn: () => getSimilarRoutes(workoutId),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes (formerly cacheTime in v4)
    retry: 1,
    refetchOnWindowFocus: false,
    enabled: !!workoutId && !!currentWorkout.route,
  });

  // Memoize highlights calculation to avoid unnecessary recalculations
  const highlights = useMemo(() => {
    if (!data || data.length === 0) {
      return [];
    }
    return calculateHighlights(data);
  }, [data]);

  const handleViewAll = () => {
    router.push(`${pathname}?tab=comparison`);
  };

  // Don't render anything if loading, error, or no data
  // This ensures the section is completely hidden when:
  // - Query is still loading (no loading state shown)
  // - Query errors (no error state shown)
  // - No matches found (data.length === 0)
  // - No data returned (!data)
  // The parent component also checks for route existence before rendering this component
  if (isLoading || isError || !data || data.length === 0) {
    return null;
  }

  const matchCount = data.length;
  const matchText = matchCount === 1 ? 'run' : 'runs';

  return (
    <div className="bg-raised p-3 rounded-lg border border-border overflow-hidden">
      <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">
        Previous Efforts
      </h3>
      <div className="space-y-3">
        {/* Match count */}
        <div className="text-sm text-ink">
          {matchCount} {matchText} on similar route
        </div>

        {/* Highlights */}
        {highlights.length > 0 && (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {highlights.map((highlight) => (
              <div key={`${highlight.type}-${highlight.route.workoutId}`} className="space-y-0.5 min-w-0">
                <div className="text-xs font-medium text-ink break-words">
                  {highlight.label}: {formatDate(highlight.route.startedAt)}
                </div>
                <RouteDeltaText delta={highlight.delta} className="block text-xs break-words" />
              </div>
            ))}
          </div>
        )}

        {/* View All Matches button */}
        <button
          onClick={handleViewAll}
          className="w-full mt-3 px-3 py-2 text-sm font-medium text-ink hover:bg-canvas rounded-tempo transition-colors flex items-center justify-center gap-1"
        >
          <span>View All Matches</span>
          <IconChevronRight className="w-4 h-4" />
        </button>
      </div>
    </div>
  );
}

