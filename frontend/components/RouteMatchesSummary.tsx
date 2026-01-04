'use client';

import { useMemo, useEffect, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useRouter, usePathname } from 'next/navigation';
import { ScatterChart, Scatter, XAxis, YAxis, ResponsiveContainer, Tooltip, Cell } from 'recharts';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDate, formatPace } from '@/lib/format';
import { useSettings } from '@/lib/settings';

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

interface DotPlotDataPoint {
  date: number; // Timestamp for X-axis
  paceS: number; // Pace in seconds for Y-axis
  workoutId: string;
  isCurrent: boolean;
  dateDisplay: string; // For tooltip
  paceDisplay: string; // For tooltip
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

/**
 * Prepare data for dot plot chart
 * @param routes Array of similar routes
 * @param currentWorkout Current workout details
 * @param unitPreference Unit preference for formatting pace
 * @param maxPoints Maximum number of points to display (default: 5)
 * @returns Array of DotPlotDataPoint or empty array if insufficient data
 */
function prepareDotPlotData(
  routes: SimilarRoute[],
  currentWorkout: WorkoutDetail,
  unitPreference: 'metric' | 'imperial',
  maxPoints: number = 5
): DotPlotDataPoint[] {
  // Filter valid routes (must have pace and date)
  const validRoutes = routes.filter(
    (route) =>
      route.avgPaceS !== undefined &&
      route.avgPaceS !== null &&
      route.startedAt &&
      !isNaN(route.avgPaceS)
  );

  // Need at least 2 valid points
  if (validRoutes.length < 2) {
    return [];
  }

  // Sort by date (most recent first) and take last maxPoints
  const sortedRoutes = [...validRoutes].sort(
    (a, b) => new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
  );

  const recentRoutes = sortedRoutes.slice(0, maxPoints);

  // Check if current workout should be included
  const currentDate = new Date(currentWorkout.startedAt).getTime();
  const recentDates = recentRoutes.map((r) => new Date(r.startedAt).getTime());
  const includeCurrent = recentDates.includes(currentDate);

  // Prepare data points
  const dataPoints: DotPlotDataPoint[] = recentRoutes.map((route) => ({
    date: new Date(route.startedAt).getTime(),
    paceS: route.avgPaceS,
    workoutId: route.workoutId,
    isCurrent: includeCurrent && new Date(route.startedAt).getTime() === currentDate,
    dateDisplay: formatDate(route.startedAt),
    paceDisplay: formatPace(route.avgPaceS, unitPreference),
  }));

  // Sort by date for chart (oldest to newest)
  return dataPoints.sort((a, b) => a.date - b.date);
}

/**
 * Custom tooltip component for dot plot chart
 */
function DotPlotTooltip({ active, payload }: any) {
  if (!active || !payload || !payload[0]) {
    return null;
  }

  const data = payload[0].payload as DotPlotDataPoint;

  return (
    <div className="bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg p-2 shadow-lg">
      <p className="text-xs font-medium text-gray-900 dark:text-gray-100">{data.dateDisplay}</p>
      <p className="text-xs text-gray-600 dark:text-gray-400">Pace: {data.paceDisplay}</p>
    </div>
  );
}

export function RouteMatchesSummary({ workoutId, currentWorkout }: RouteMatchesSummaryProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { unitPreference } = useSettings();
  const [isDarkMode, setIsDarkMode] = useState(false);

  // Detect dark mode
  useEffect(() => {
    const checkDarkMode = () => {
      if (typeof window !== 'undefined') {
        const isDark =
          document.documentElement.classList.contains('dark') ||
          window.matchMedia('(prefers-color-scheme: dark)').matches;
        setIsDarkMode(isDark);
      }
    };

    checkDarkMode();

    // Watch for class changes
    const observer = new MutationObserver(checkDarkMode);
    if (typeof window !== 'undefined') {
      observer.observe(document.documentElement, {
        attributes: true,
        attributeFilter: ['class'],
      });

      // Also listen to media query changes
      const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
      const handleChange = () => checkDarkMode();
      mediaQuery.addEventListener('change', handleChange);

      return () => {
        observer.disconnect();
        mediaQuery.removeEventListener('change', handleChange);
      };
    }
  }, []);

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
  // This ensures the section is completely hidden when:
  // - Query is still loading (no loading state shown)
  // - Query errors (no error state shown)
  // - No matches found (data.length === 0)
  // - No data returned (!data)
  // The parent component also checks for route existence before rendering this component
  if (isLoading || isError || !data || data.length === 0) {
    return null;
  }

  const highlights = calculateHighlights(data);
  const matchCount = data.length;
  const matchText = matchCount === 1 ? 'run' : 'runs';

  // Prepare dot plot data
  const dotPlotData = useMemo(() => {
    if (!data || data.length < 2) {
      return [];
    }
    return prepareDotPlotData(data, currentWorkout, unitPreference, 5);
  }, [data, currentWorkout, unitPreference]);

  return (
    <div className="bg-white dark:bg-gray-900 p-3 rounded-lg border border-gray-200 dark:border-gray-800 overflow-hidden">
      <h3 className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-2 uppercase tracking-wide">
        Previous Efforts
      </h3>
      <div className="space-y-3">
        {/* Match count */}
        <div className="text-sm text-gray-900 dark:text-gray-100">
          {matchCount} {matchText} on similar route
        </div>

        {/* Highlights */}
        {highlights.length > 0 && (
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
            {highlights.map((highlight) => (
              <div key={`${highlight.type}-${highlight.route.workoutId}`} className="space-y-0.5 min-w-0">
                <div className="text-xs font-medium text-gray-900 dark:text-gray-100 break-words">
                  {highlight.label}: {formatDate(highlight.route.startedAt)}
                </div>
                {highlight.value && (
                  <div
                    className={`text-xs break-words ${
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

        {/* Dot Plot Chart */}
        {dotPlotData.length >= 2 && (
          <div
            className="mt-3 mb-3"
            style={{ height: '80px' }}
            aria-label="Pace trend for recent efforts on similar route"
            role="img"
          >
            <ResponsiveContainer width="100%" height="100%">
              <ScatterChart data={dotPlotData} margin={{ top: 5, right: 5, left: 0, bottom: 5 }}>
                <XAxis
                  dataKey="date"
                  type="number"
                  scale="time"
                  domain={['dataMin', 'dataMax']}
                  hide
                />
                <YAxis dataKey="paceS" domain={['auto', 'auto']} hide />
                <Tooltip content={<DotPlotTooltip />} />
                <Scatter dataKey="paceS">
                  {dotPlotData.map((entry, index) => (
                    <Cell
                      key={`cell-${index}`}
                      fill={
                        entry.isCurrent
                          ? isDarkMode
                            ? '#60a5fa'
                            : '#3b82f6'
                          : isDarkMode
                          ? '#9ca3af'
                          : '#6b7280'
                      }
                      r={entry.isCurrent ? 5 : 3}
                    />
                  ))}
                </Scatter>
              </ScatterChart>
            </ResponsiveContainer>
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

