'use client';

import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { ComposedChart, Scatter, Line, XAxis, YAxis, ResponsiveContainer, Tooltip, CartesianGrid } from 'recharts';
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

interface TrendLinePoint {
  date: number;
  paceS: number;
  isTrend: true;
}

/**
 * Ordinary least squares line y = slope * x + intercept (x = epoch ms, y = paceS).
 * Returns two endpoints for min/max date, or null if a line is not meaningful.
 */
/** X-axis tooltip label (tick value) as epoch ms, if parseable. */
function axisLabelToEpochMs(label: unknown): number | undefined {
  if (label == null) {
    return undefined;
  }
  if (typeof label === 'number' && Number.isFinite(label)) {
    return label;
  }
  if (typeof label === 'string') {
    const asNum = Number(label);
    if (Number.isFinite(asNum)) {
      return asNum;
    }
    const parsed = Date.parse(label);
    if (Number.isFinite(parsed)) {
      return parsed;
    }
    return undefined;
  }
  if (label instanceof Date) {
    return label.getTime();
  }
  return undefined;
}

/**
 * ComposedChart axis tooltips resolve Scatter by tick index, which can disagree with the
 * active axis label; the Line trend still matches by `date`. Fall back to label → workout.
 */
function findWorkoutByAxisLabel(label: unknown, chartData: ChartDataPoint[]): ChartDataPoint | undefined {
  const ms = axisLabelToEpochMs(label);
  if (ms === undefined || chartData.length === 0) {
    return undefined;
  }
  const exact = chartData.find((p) => p.date === ms);
  if (exact) {
    return exact;
  }
  return chartData.find((p) => Math.abs(p.date - ms) < 1000);
}

function computePaceTrendLine(points: ChartDataPoint[]): TrendLinePoint[] | null {
  if (points.length < 2) {
    return null;
  }
  const dates = points.map((p) => p.date);
  const minDate = Math.min(...dates);
  const maxDate = Math.max(...dates);
  if (minDate === maxDate) {
    return null;
  }

  const n = points.length;
  const meanX = dates.reduce((a, b) => a + b, 0) / n;
  const meanY = points.reduce((s, p) => s + p.paceS, 0) / n;
  let numer = 0;
  let denom = 0;
  for (let i = 0; i < n; i++) {
    const xc = dates[i] - meanX;
    const yc = points[i].paceS - meanY;
    numer += xc * yc;
    denom += xc * xc;
  }
  if (denom === 0 || !Number.isFinite(denom) || Math.abs(denom) < 1e-9) {
    return null;
  }

  const slope = numer / denom;
  const intercept = meanY - slope * meanX;
  const yAt = (d: number) => slope * d + intercept;

  return [
    { date: minDate, paceS: yAt(minDate), isTrend: true },
    { date: maxDate, paceS: yAt(maxDate), isTrend: true },
  ];
}

function PaceComparisonTooltip({
  active,
  payload,
  label,
  chartData,
  unitPreference,
}: {
  active?: boolean;
  payload?: ReadonlyArray<{ payload?: unknown }>;
  label?: string | number;
  chartData: ChartDataPoint[];
  unitPreference: 'metric' | 'imperial';
}) {
  if (!active || !payload?.length) {
    return null;
  }
  // Prefer a real workout row when the payload mixes series (e.g. trend + scatter near the same spot).
  const data = payload.find((item) => {
    const p = item.payload as (ChartDataPoint & { isTrend?: boolean }) | undefined;
    return typeof p?.workoutId === 'string' && p.isTrend !== true;
  })?.payload as (ChartDataPoint & { dateISO?: string }) | undefined;

  const dataFromLabel = data ?? findWorkoutByAxisLabel(label, chartData);

  if (dataFromLabel) {
    return (
      <div className="bg-canvas border border-border rounded-lg shadow-lg p-3">
        <p className="text-sm font-medium text-ink mb-1">
          {dataFromLabel.dateISO ? formatDate(dataFromLabel.dateISO) : dataFromLabel.dateDisplay}
        </p>
        <p className="text-xs text-muted">
          Pace: {dataFromLabel.paceDisplay}
        </p>
        <p className="text-xs text-muted">
          Duration: {formatDuration(dataFromLabel.durationS)}
        </p>
        {dataFromLabel.timeDifferenceS !== undefined && dataFromLabel.timeDifferenceS !== null && (
          <p
            className={`text-xs ${
              dataFromLabel.timeDifferenceS < 0
                ? 'text-ink'
                : dataFromLabel.timeDifferenceS > 0
                  ? 'text-danger'
                  : 'text-muted'
            }`}
          >
            {formatTimeDifference(dataFromLabel.timeDifferenceS)}
          </p>
        )}
      </div>
    );
  }

  const trendPayload = payload.find((item) => (item.payload as TrendLinePoint | undefined)?.isTrend === true)
    ?.payload as TrendLinePoint | undefined;
  if (trendPayload) {
    return (
      <div className="bg-canvas border border-border rounded-lg shadow-lg p-3">
        <p className="text-sm font-medium text-ink mb-1">Linear trend</p>
        <p className="text-xs text-muted">
          Pace: {formatPace(trendPayload.paceS, unitPreference)}
        </p>
      </div>
    );
  }

  return null;
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

  const trendLineData = useMemo(() => computePaceTrendLine(chartData), [chartData]);

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
  }, [data, currentWorkout.avgPaceS, currentWorkout.startedAt]);

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

  // Loading state
  if (isLoading) {
    return (
      <div className="w-full space-y-3">
        <div className="bg-raised p-6 rounded-lg border border-border">
          <div className="h-64 flex items-center justify-center">
            <p className="text-sm text-muted">Loading route comparison...</p>
          </div>
        </div>
      </div>
    );
  }

  // Error state
  if (isError) {
    return (
      <div className="w-full space-y-3">
        <div className="bg-raised p-6 rounded-lg border border-border">
          <div className="p-4 bg-canvas border border-danger rounded-tempo">
            <p className="text-sm text-danger">
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
        <div className="bg-raised p-6 rounded-lg border border-border">
          <div className="p-4 bg-canvas rounded-lg border border-border">
            <p className="text-sm text-muted">
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
      <div className="bg-raised p-6 rounded-lg border border-border">
        <h2 className="text-lg font-semibold text-ink mb-4">
          Pace Over Time
        </h2>
        <div style={{ height: '300px' }}>
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chartData} margin={{ top: 5, right: 20, left: 0, bottom: 20 }}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--line)" />
              <XAxis 
                dataKey="date" 
                type="number"
                scale="time"
                domain={['dataMin', 'dataMax']}
                tick={{ fill: 'var(--muted)', fontSize: 12 }}
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
                tick={{ fill: 'var(--muted)', fontSize: 12 }}
                tickFormatter={(value) => formatPace(value, unitPreference)}
                domain={['auto', 'auto']}
                width={80}
              />
              <Tooltip
                shared={false}
                content={(props) => (
                  <PaceComparisonTooltip
                    active={props.active}
                    label={props.label}
                    payload={props.payload}
                    chartData={chartData}
                    unitPreference={unitPreference}
                  />
                )}
              />
              <Scatter
                name="Efforts"
                data={chartData}
                dataKey="paceS"
                fill="var(--muted)"
                isAnimationActive={false}
                shape={(props: unknown) => {
                  const { cx, cy, payload } = props as {
                    cx?: number;
                    cy?: number;
                    payload?: ChartDataPoint;
                  };
                  if (cx == null || cy == null || !payload) {
                    return <g />;
                  }
                  const isCurrent = payload.isCurrent;
                  const id = payload.workoutId;
                  return (
                    <circle
                      cx={cx}
                      cy={cy}
                      r={isCurrent ? 6 : 4}
                      fill={isCurrent ? 'var(--volt)' : 'var(--muted)'}
                      stroke={isCurrent ? 'var(--ink)' : 'none'}
                      strokeWidth={isCurrent ? 2 : 0}
                      style={{ cursor: 'pointer' }}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (id) {
                          router.push(`/dashboard/${id}`);
                        }
                      }}
                    />
                  );
                }}
                activeShape={(props: unknown) => {
                  const { cx, cy, payload } = props as {
                    cx?: number;
                    cy?: number;
                    payload?: ChartDataPoint;
                  };
                  if (cx == null || cy == null || !payload) {
                    return <g />;
                  }
                  const id = payload.workoutId;
                  return (
                    <circle
                      cx={cx}
                      cy={cy}
                      r={8}
                      fill="var(--volt)"
                      stroke="var(--ink)"
                      strokeWidth={2}
                      style={{ cursor: 'pointer' }}
                      onClick={(e) => {
                        e.stopPropagation();
                        if (id) {
                          router.push(`/dashboard/${id}`);
                        }
                      }}
                    />
                  );
                }}
              />
              {trendLineData && (
                <Line
                  type="linear"
                  data={trendLineData}
                  dataKey="paceS"
                  stroke="var(--muted)"
                  className="[&_path]:pointer-events-none"
                  strokeWidth={2}
                  strokeDasharray="6 4"
                  dot={false}
                  activeDot={false}
                  isAnimationActive={false}
                  name="Trend"
                />
              )}
            </ComposedChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Quick Stats Section */}
      {quickStats && (
        <div className="bg-raised p-6 rounded-lg border border-border">
          <h2 className="text-lg font-semibold text-ink mb-4">
            Quick Stats
          </h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {/* Best Time */}
            {quickStats.bestTime && quickStats.bestTime.timeDifferenceS !== undefined && quickStats.bestTime.timeDifferenceS !== null && (
              <div className="p-4 bg-canvas rounded-lg border border-border">
                <div className="text-xs font-medium text-muted mb-1">Best Time</div>
                <div className="text-sm font-semibold text-ink mb-1">
                  {formatDate(quickStats.bestTime.startedAt)}
                </div>
                <div className={`text-xs ${
                  quickStats.bestTime.timeDifferenceS < 0
                    ? 'text-ink'
                    : quickStats.bestTime.timeDifferenceS > 0
                    ? 'text-danger'
                    : 'text-muted'
                }`}>
                  {formatTimeDifference(quickStats.bestTime.timeDifferenceS)}
                </div>
              </div>
            )}

            {/* Average Pace */}
            <div className="p-4 bg-canvas rounded-lg border border-border">
              <div className="text-xs font-medium text-muted mb-1">Average Pace</div>
              <div className="text-sm font-semibold text-ink">
                {formatPace(quickStats.avgPace, unitPreference)}
              </div>
            </div>

            {/* Improvement Trend */}
            <div className="p-4 bg-canvas rounded-lg border border-border">
              <div className="text-xs font-medium text-muted mb-1">Trend</div>
              <div className={`text-sm font-semibold flex items-center gap-1 ${
                quickStats.trend === 'improving'
                  ? 'text-ink'
                  : quickStats.trend === 'declining'
                  ? 'text-danger'
                  : 'text-muted'
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
      <div className="bg-raised p-6 rounded-lg border border-border">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-ink">
            All Matches ({sortedRoutes.length})
          </h2>
          <div className="flex items-center gap-2">
            <select
              value={sortBy}
              onChange={(e) => setSortBy(e.target.value as SortBy)}
              className="px-3 py-1.5 text-sm border border-border rounded-md bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt"
            >
              <option value="date">Date</option>
              <option value="pace">Pace</option>
              <option value="timeDiff">Time Difference</option>
            </select>
            <button
              onClick={() => setSortOrder(sortOrder === 'asc' ? 'desc' : 'asc')}
              className="px-3 py-1.5 text-sm border border-border rounded-md bg-canvas text-ink hover:bg-canvas focus:outline-none focus:ring-2 focus:ring-volt"
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
              className="block p-4 bg-canvas rounded-lg border border-border hover:bg-canvas hover:border-ink transition-colors"
            >
              <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3">
                {/* Date */}
                <div>
                  <div className="text-xs text-muted mb-1">Date</div>
                  <div className="text-sm font-semibold text-ink">
                    {formatDate(route.startedAt)}
                  </div>
                </div>

                {/* Duration with time difference */}
                <div>
                  <div className="text-xs text-muted mb-1">Duration</div>
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
                      </span>
                    )}
                  </div>
                </div>

                {/* Pace with pace difference */}
                <div>
                  <div className="text-xs text-muted mb-1">Pace</div>
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
                      </span>
                    )}
                  </div>
                </div>

                {/* Distance and Additional Info */}
                <div>
                  <div className="text-xs text-muted mb-1">Distance</div>
                  <div className="text-sm font-semibold text-ink mb-2">
                    {formatDistance(route.distanceM, unitPreference)}
                  </div>
                  {route.relativeEffort !== null && route.relativeEffort !== undefined && (
                    <div className="text-xs text-muted">
                      Effort: {route.relativeEffort}
                    </div>
                  )}
                  {route.elevGainM !== null && route.elevGainM !== undefined && (
                    <div className="text-xs text-muted">
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

