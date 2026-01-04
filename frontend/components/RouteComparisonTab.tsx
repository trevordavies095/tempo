'use client';

import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { LineChart, Line, XAxis, YAxis, ResponsiveContainer, Tooltip, CartesianGrid } from 'recharts';
import { getSimilarRoutes, type SimilarRoute, type WorkoutDetail } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDate, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';

interface RouteComparisonTabProps {
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

interface ChartDataPoint {
  date: number; // Timestamp for X-axis
  dateDisplay: string; // Formatted date for display
  dateISO?: string; // ISO date string for tooltip
  paceS: number; // Pace in seconds
  paceDisplay: string; // Formatted pace
  workoutId: string;
  durationS: number;
  timeDifferenceS?: number;
  isCurrent: boolean;
}

type SortBy = 'date' | 'pace' | 'timeDiff';
type SortOrder = 'asc' | 'desc';

export function RouteComparisonTab({ workoutId, currentWorkout }: RouteComparisonTabProps) {
  const { unitPreference } = useSettings();
  const router = useRouter();
  const [sortBy, setSortBy] = useState<SortBy>('date');
  const [sortOrder, setSortOrder] = useState<SortOrder>('desc');

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['similar-routes', workoutId],
    queryFn: () => getSimilarRoutes(workoutId),
    staleTime: 5 * 60 * 1000, // 5 minutes
    gcTime: 10 * 60 * 1000, // 10 minutes (formerly cacheTime in v4)
    retry: 1,
    refetchOnWindowFocus: false,
    enabled: !!workoutId && !!currentWorkout.route,
  });

  // Prepare chart data - always includes current workout
  const chartData = useMemo((): ChartDataPoint[] => {
    // Ensure we have current workout data
    if (!currentWorkout || !currentWorkout.startedAt || currentWorkout.avgPaceS === undefined) {
      return [];
    }

    // Start with similar routes (if any)
    const routes: Array<SimilarRoute & { isCurrent?: boolean }> = data ? [...data] : [];
    
    // Always add current workout to the data (backend doesn't include it in similar routes)
    // This ensures the current workout is always visible on the chart
    const currentWorkoutInData = routes.some(r => r.workoutId === workoutId);
    if (!currentWorkoutInData) {
      routes.push({
        workoutId: workoutId,
        startedAt: currentWorkout.startedAt,
        durationS: currentWorkout.durationS,
        distanceM: currentWorkout.distanceM,
        avgPaceS: currentWorkout.avgPaceS,
        elevGainM: currentWorkout.elevGainM ?? null,
        isCurrent: true,
        // No timeDifferenceS or paceDifferenceS for current workout (it's the baseline)
      });
    }

    // Sort by date for chart
    const sortedRoutes = routes.sort((a, b) => 
      new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime()
    );

    return sortedRoutes.map((route) => ({
      date: new Date(route.startedAt).getTime(), // Convert to timestamp for X-axis
      dateDisplay: formatDate(route.startedAt),
      dateISO: route.startedAt, // Keep ISO string for tooltip
      paceS: route.avgPaceS,
      paceDisplay: formatPace(route.avgPaceS, unitPreference),
      workoutId: route.workoutId,
      durationS: route.durationS,
      timeDifferenceS: route.timeDifferenceS,
      isCurrent: route.workoutId === workoutId,
    }));
  }, [data, unitPreference, workoutId, currentWorkout]);

  // Calculate quick stats
  const quickStats = useMemo(() => {
    if (!data || data.length === 0) {
      return null;
    }

    // Best Time: Route with most negative timeDifferenceS
    const bestTime = data.reduce((best, route) => {
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

    // Average Pace: Mean of all avgPaceS values
    const avgPace = data.reduce((sum, route) => sum + route.avgPaceS, 0) / data.length;

    // Improvement Trend: Compare current pace to average of last 5 runs
    // Filter to only include past workouts (before current workout date) to ensure
    // the trend compares current performance to past performance, not future workouts
    const currentWorkoutDate = new Date(currentWorkout.startedAt).getTime();
    const pastRoutes = data.filter(route => 
      new Date(route.startedAt).getTime() < currentWorkoutDate
    );
    const sortedByDate = [...pastRoutes].sort((a, b) => 
      new Date(b.startedAt).getTime() - new Date(a.startedAt).getTime()
    );
    const last5Runs = sortedByDate.slice(0, 5);
    const avgLast5Pace = last5Runs.length > 0
      ? last5Runs.reduce((sum, route) => sum + route.avgPaceS, 0) / last5Runs.length
      : currentWorkout.avgPaceS;
    
    const currentPace = currentWorkout.avgPaceS;
    const percentDiff = Math.abs((currentPace - avgLast5Pace) / avgLast5Pace) * 100;
    let trend: 'improving' | 'declining' | 'stable';
    if (currentPace < avgLast5Pace) {
      trend = 'improving';
    } else if (currentPace > avgLast5Pace) {
      trend = 'declining';
    } else {
      trend = 'stable';
    }
    // Override to stable if within 2%
    if (percentDiff <= 2) {
      trend = 'stable';
    }

    return {
      bestTime,
      avgPace,
      trend,
    };
  }, [data, currentWorkout.avgPaceS]);

  // Sort routes for list display
  const sortedRoutes = useMemo(() => {
    if (!data || data.length === 0) {
      return [];
    }

    const sorted = [...data].sort((a, b) => {
      let comparison = 0;

      switch (sortBy) {
        case 'date':
          comparison = new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime();
          break;
        case 'pace':
          comparison = a.avgPaceS - b.avgPaceS;
          break;
        case 'timeDiff':
          const aDiff = a.timeDifferenceS ?? 0;
          const bDiff = b.timeDifferenceS ?? 0;
          comparison = aDiff - bDiff;
          break;
      }

      return sortOrder === 'asc' ? comparison : -comparison;
    });

    return sorted;
  }, [data, sortBy, sortOrder]);

  // Custom tooltip for chart
  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload as ChartDataPoint & { dateISO?: string };
      return (
        <div className="bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg p-3">
          <p className="text-sm font-medium text-gray-900 dark:text-gray-100 mb-1">
            {data.dateISO ? formatDate(data.dateISO) : data.dateDisplay}
          </p>
          <p className="text-xs text-gray-600 dark:text-gray-400">
            Pace: {data.paceDisplay}
          </p>
          <p className="text-xs text-gray-600 dark:text-gray-400">
            Duration: {formatDuration(data.durationS)}
          </p>
          {data.timeDifferenceS !== undefined && data.timeDifferenceS !== null && (
            <p className={`text-xs ${
              data.timeDifferenceS < 0
                ? 'text-green-600 dark:text-green-400'
                : data.timeDifferenceS > 0
                ? 'text-red-600 dark:text-red-400'
                : 'text-gray-500 dark:text-gray-400'
            }`}>
              {formatTimeDifference(data.timeDifferenceS)}
            </p>
          )}
        </div>
      );
    }
    return null;
  };

  // Loading state
  if (isLoading) {
    return (
      <div className="w-full space-y-3">
        <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
          <div className="h-64 flex items-center justify-center">
            <p className="text-sm text-gray-600 dark:text-gray-400">Loading route comparison...</p>
          </div>
        </div>
      </div>
    );
  }

  // Error state
  if (isError) {
    return (
      <div className="w-full space-y-3">
        <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
          <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
            <p className="text-sm text-red-800 dark:text-red-200">
              {error instanceof Error ? error.message : 'Unable to load route comparison. Please try again.'}
            </p>
          </div>
        </div>
      </div>
    );
  }

  // Empty state - only show if we have no chart data at all (including current workout)
  if (chartData.length === 0) {
    return (
      <div className="w-full space-y-3">
        <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
          <div className="p-4 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700">
            <p className="text-sm text-gray-500 dark:text-gray-400">
              No previous efforts found on this route.
            </p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="w-full space-y-3">
      {/* Chart Section */}
      <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Pace Over Time
        </h2>
        <div style={{ height: '300px' }}>
          <ResponsiveContainer width="100%" height="100%">
            <LineChart data={chartData} margin={{ top: 5, right: 20, left: 0, bottom: 20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="#e5e7eb" className="dark:stroke-gray-700" />
              <XAxis 
                dataKey="date" 
                type="number"
                scale="time"
                domain={['dataMin', 'dataMax']}
                tick={{ fill: '#6b7280', fontSize: 12 }}
                className="dark:text-gray-400"
                angle={-45}
                textAnchor="end"
                height={80}
                tickFormatter={(value) => {
                  // Convert timestamp back to formatted date
                  const date = new Date(value);
                  return formatDate(date.toISOString());
                }}
              />
              <YAxis 
                tick={{ fill: '#6b7280', fontSize: 12 }}
                className="dark:text-gray-400"
                tickFormatter={(value) => formatPace(value, unitPreference)}
                domain={['auto', 'auto']}
                width={80}
              />
              <Tooltip content={<CustomTooltip />} />
              <Line 
                type="monotone" 
                dataKey="paceS" 
                stroke="#8884d8" 
                strokeWidth={2}
                dot={(props: any) => {
                  const isCurrent = props.payload?.isCurrent;
                  const workoutId = props.payload?.workoutId;
                  return (
                    <circle 
                      cx={props.cx} 
                      cy={props.cy} 
                      r={isCurrent ? 6 : 4} 
                      fill={isCurrent ? '#3b82f6' : '#8884d8'}
                      stroke={isCurrent ? '#1e40af' : 'none'}
                      strokeWidth={isCurrent ? 2 : 0}
                      style={{ cursor: 'pointer' }}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (workoutId) {
                          router.push(`/dashboard/${workoutId}`);
                        }
                      }}
                    />
                  );
                }}
                activeDot={(props: any) => {
                  const workoutId = props.payload?.workoutId;
                  return (
                    <circle
                      cx={props.cx}
                      cy={props.cy}
                      r={8}
                      fill="#8884d8"
                      stroke="#fff"
                      strokeWidth={2}
                      style={{ cursor: 'pointer' }}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (workoutId) {
                          router.push(`/dashboard/${workoutId}`);
                        }
                      }}
                    />
                  );
                }}
              />
            </LineChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Quick Stats Section */}
      {quickStats && (
        <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
            Quick Stats
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* Best Time */}
            {quickStats.bestTime && quickStats.bestTime.timeDifferenceS !== undefined && quickStats.bestTime.timeDifferenceS !== null && (
              <div className="p-4 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700">
                <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Best Time</div>
                <div className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-1">
                  {formatDate(quickStats.bestTime.startedAt)}
                </div>
                <div className={`text-xs ${
                  quickStats.bestTime.timeDifferenceS < 0
                    ? 'text-green-600 dark:text-green-400'
                    : quickStats.bestTime.timeDifferenceS > 0
                    ? 'text-red-600 dark:text-red-400'
                    : 'text-gray-500 dark:text-gray-400'
                }`}>
                  {formatTimeDifference(quickStats.bestTime.timeDifferenceS)}
                </div>
              </div>
            )}

            {/* Average Pace */}
            <div className="p-4 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700">
              <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Average Pace</div>
              <div className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                {formatPace(quickStats.avgPace, unitPreference)}
              </div>
            </div>

            {/* Improvement Trend */}
            <div className="p-4 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700">
              <div className="text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">Trend</div>
              <div className={`text-sm font-semibold flex items-center gap-1 ${
                quickStats.trend === 'improving'
                  ? 'text-green-600 dark:text-green-400'
                  : quickStats.trend === 'declining'
                  ? 'text-red-600 dark:text-red-400'
                  : 'text-gray-600 dark:text-gray-400'
              }`}>
                {quickStats.trend === 'improving' && '↑ Improving'}
                {quickStats.trend === 'declining' && '↓ Declining'}
                {quickStats.trend === 'stable' && '→ Stable'}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Sortable List Section */}
      <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100">
            All Matches ({sortedRoutes.length})
          </h2>
          <div className="flex items-center gap-2">
            <select
              value={sortBy}
              onChange={(e) => setSortBy(e.target.value as SortBy)}
              className="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="date">Date</option>
              <option value="pace">Pace</option>
              <option value="timeDiff">Time Difference</option>
            </select>
            <button
              onClick={() => setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc')}
              className="px-3 py-1.5 text-sm border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 hover:bg-gray-50 dark:hover:bg-gray-700 focus:outline-none focus:ring-2 focus:ring-blue-500"
              aria-label={`Sort ${sortOrder === 'asc' ? 'descending' : 'ascending'}`}
            >
              {sortOrder === 'asc' ? '↑' : '↓'}
            </button>
          </div>
        </div>
        <div className="space-y-2 max-h-[600px] overflow-y-auto">
          {sortedRoutes.map((route) => (
            <Link
              key={route.workoutId}
              href={`/dashboard/${route.workoutId}`}
              className="block p-4 bg-gray-50 dark:bg-gray-800/50 rounded-lg border border-gray-200 dark:border-gray-700 hover:bg-gray-100 dark:hover:bg-gray-800 hover:border-gray-300 dark:hover:border-gray-600 transition-colors"
            >
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                {/* Date */}
                <div>
                  <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Date</div>
                  <div className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                    {formatDate(route.startedAt)}
                  </div>
                </div>

                {/* Duration with time difference */}
                <div>
                  <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Duration</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatDuration(route.durationS)}
                    </span>
                    {route.timeDifferenceS !== undefined && route.timeDifferenceS !== null && (
                      <span
                        className={`text-xs ${
                          route.timeDifferenceS < 0
                            ? 'text-green-600 dark:text-green-400'
                            : route.timeDifferenceS > 0
                            ? 'text-red-600 dark:text-red-400'
                            : 'text-gray-500 dark:text-gray-400'
                        }`}
                      >
                        {formatTimeDifference(route.timeDifferenceS)}
                      </span>
                    )}
                  </div>
                </div>

                {/* Pace with pace difference */}
                <div>
                  <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Pace</div>
                  <div className="flex items-center gap-1.5">
                    <span className="text-sm font-semibold text-gray-900 dark:text-gray-100">
                      {formatPace(route.avgPaceS, unitPreference)}
                    </span>
                    {route.paceDifferenceS !== undefined && route.paceDifferenceS !== null && (
                      <span
                        className={`text-xs ${
                          route.paceDifferenceS < 0
                            ? 'text-green-600 dark:text-green-400'
                            : route.paceDifferenceS > 0
                            ? 'text-red-600 dark:text-red-400'
                            : 'text-gray-500 dark:text-gray-400'
                        }`}
                      >
                        {formatPaceDifference(route.paceDifferenceS, unitPreference)}
                      </span>
                    )}
                  </div>
                </div>

                {/* Distance and Additional Info */}
                <div>
                  <div className="text-xs text-gray-500 dark:text-gray-400 mb-1">Distance</div>
                  <div className="text-sm font-semibold text-gray-900 dark:text-gray-100 mb-2">
                    {formatDistance(route.distanceM, unitPreference)}
                  </div>
                  {route.relativeEffort !== null && route.relativeEffort !== undefined && (
                    <div className="text-xs text-gray-500 dark:text-gray-400">
                      Effort: {route.relativeEffort}
                    </div>
                  )}
                  {route.elevGainM !== null && route.elevGainM !== undefined && (
                    <div className="text-xs text-gray-500 dark:text-gray-400">
                      Elev: {formatElevation(route.elevGainM, unitPreference)}
                    </div>
                  )}
                </div>
              </div>
            </Link>
          ))}
        </div>
      </div>
    </div>
  );
}

