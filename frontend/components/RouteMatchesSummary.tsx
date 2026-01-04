'use client';

import { useQuery } from '@tanstack/react-query';
import { useRouter, usePathname } from 'next/navigation';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDate } from '@/lib/format';

interface RouteMatchesSummaryProps {
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

interface Highlight {
  type: 'best' | 'latest' | 'fastest';
  route: SimilarRoute;
  label: string;
  value: string;
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

  // Best Time: Route with most negative timeDifferenceS (fastest compared to current)
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
      value: formatTimeDifference(bestTime.timeDifferenceS),
    });
  }

  // Most Recent: Route with latest startedAt date
  const mostRecent = routes.reduce((latest, route) => {
    return new Date(route.startedAt) > new Date(latest.startedAt) ? route : latest;
  }, routes[0]);

  // Only add if different from best time
  if (!bestTime || mostRecent.workoutId !== bestTime.workoutId) {
    const timeDiff = mostRecent.timeDifferenceS;
    highlights.push({
      type: 'latest',
      route: mostRecent,
      label: 'Latest',
      value: timeDiff !== undefined && timeDiff !== null ? formatTimeDifference(timeDiff) : '',
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
    const timeDiff = fastestPace.timeDifferenceS;
    highlights.push({
      type: 'fastest',
      route: fastestPace,
      label: 'Fastest Pace',
      value: timeDiff !== undefined && timeDiff !== null ? formatTimeDifference(timeDiff) : '',
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

  const handleViewAll = () => {
    router.push(`${pathname}?tab=comparison`);
  };

  // Don't render anything if loading, error, or no data
  if (isLoading || isError || !data || data.length === 0) {
    return null;
  }

  const highlights = calculateHighlights(data);
  const matchCount = data.length;
  const matchText = matchCount === 1 ? 'run' : 'runs';

  return (
    <div className="bg-white dark:bg-gray-900 p-3 rounded-lg border border-gray-200 dark:border-gray-800">
      <h3 className="text-xs font-medium text-gray-600 dark:text-gray-400 mb-2 uppercase tracking-wide">
        Previous Efforts
      </h3>
      <div className="space-y-3">
        {/* Match count */}
        <div className="text-sm text-gray-900 dark:text-gray-100">
          {matchCount} {matchText} on similar route
        </div>

        {/* Highlights */}
        {highlights.length > 0 && (
          <div className="space-y-2">
            {highlights.map((highlight) => (
              <div key={`${highlight.type}-${highlight.route.workoutId}`} className="space-y-0.5">
                <div className="text-xs font-medium text-gray-900 dark:text-gray-100">
                  {highlight.label}: {formatDate(highlight.route.startedAt)}
                </div>
                {highlight.value && (
                  <div
                    className={`text-xs ${
                      highlight.route.timeDifferenceS !== undefined &&
                      highlight.route.timeDifferenceS !== null
                        ? highlight.route.timeDifferenceS < 0
                          ? 'text-green-600 dark:text-green-400'
                          : highlight.route.timeDifferenceS > 0
                          ? 'text-red-600 dark:text-red-400'
                          : 'text-gray-500 dark:text-gray-400'
                        : 'text-gray-500 dark:text-gray-400'
                    }`}
                  >
                    {highlight.value}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}

        {/* View All Matches button */}
        <button
          onClick={handleViewAll}
          className="w-full mt-3 px-3 py-2 text-sm font-medium text-blue-600 dark:text-blue-400 hover:text-blue-700 dark:hover:text-blue-300 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-md transition-colors flex items-center justify-center gap-1"
        >
          <span>View All Matches</span>
          <svg
            className="w-4 h-4"
            fill="none"
            stroke="currentColor"
            viewBox="0 0 24 24"
            xmlns="http://www.w3.org/2000/svg"
          >
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={2}
              d="M9 5l7 7-7 7"
            />
          </svg>
        </button>
      </div>
    </div>
  );
}

