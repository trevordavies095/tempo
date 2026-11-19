/**
 * The route comparison Trend, matching the iOS Similar Runs screen (`SimilarRunsAnalysis.trend`).
 *
 * Take every run on the route (the matches plus this run), keep the last five by date, and fit
 * pace against run order (0, 1, 2, …) by ordinary least squares. A slope below −1 s/km per run is
 * Improving (getting faster), above +1 s/km per run is Declining, anything else is Stable. Fewer
 * than three runs: Not enough runs.
 */

export type RouteTrend = 'improving' | 'declining' | 'stable' | 'notEnoughRuns';

/** Runs needed before a trend is called. */
export const MINIMUM_TREND_RUNS = 3;
/** The trend looks at this many most recent runs. */
export const TREND_WINDOW = 5;
/** Seconds per kilometre per run inside which the trend is Stable. */
export const TREND_TOLERANCE_S_PER_KM_PER_RUN = 1;

export interface RouteTrendRun {
  /** ISO date string or epoch milliseconds. */
  startedAt: string | number;
  /** Seconds per kilometre. */
  avgPaceS: number;
}

export interface RouteTrendDisplay {
  /** "Improving", "Declining", "Stable", "Not enough runs". */
  title: string;
  /** Arrow mirroring the iOS SF Symbol (arrow.up.right, arrow.down.right, arrow.right, minus). */
  symbol: string;
  /** Text colour class: green Improving, red Declining, muted otherwise. */
  className: string;
}

const DISPLAY: Record<RouteTrend, RouteTrendDisplay> = {
  improving: { title: 'Improving', symbol: '↗', className: 'text-hr-zone-1' },
  declining: { title: 'Declining', symbol: '↘', className: 'text-danger' },
  stable: { title: 'Stable', symbol: '→', className: 'text-muted' },
  notEnoughRuns: { title: 'Not enough runs', symbol: '−', className: 'text-muted' },
};

/** Ordinary least-squares slope; null for fewer than two points or when every x is the same. */
export function leastSquaresSlope(points: ReadonlyArray<{ x: number; y: number }>): number | null {
  if (points.length < 2) {
    return null;
  }
  const n = points.length;
  const meanX = points.reduce((sum, p) => sum + p.x, 0) / n;
  const meanY = points.reduce((sum, p) => sum + p.y, 0) / n;
  let sxx = 0;
  let sxy = 0;
  for (const p of points) {
    const dx = p.x - meanX;
    sxx += dx * dx;
    sxy += dx * (p.y - meanY);
  }
  if (!(sxx > 0)) {
    return null;
  }
  return sxy / sxx;
}

function epochMs(startedAt: string | number): number {
  return typeof startedAt === 'number' ? startedAt : new Date(startedAt).getTime();
}

/** The Trend over every run on the route, this run included. */
export function routeTrend(
  runs: ReadonlyArray<RouteTrendRun>,
  tolerance: number = TREND_TOLERANCE_S_PER_KM_PER_RUN
): RouteTrend {
  if (runs.length < MINIMUM_TREND_RUNS) {
    return 'notEnoughRuns';
  }
  const recent = [...runs]
    .sort((a, b) => epochMs(a.startedAt) - epochMs(b.startedAt))
    .slice(-TREND_WINDOW);
  const slope = leastSquaresSlope(recent.map((run, index) => ({ x: index, y: run.avgPaceS })));
  if (slope === null) {
    return 'stable';
  }
  if (slope < -tolerance) {
    return 'improving';
  }
  if (slope > tolerance) {
    return 'declining';
  }
  return 'stable';
}

/** How many runs the Trend looked at ("Last 5 runs"). */
export function routeTrendRunCount(runCount: number): number {
  return Math.min(runCount, TREND_WINDOW);
}

export function routeTrendDisplay(trend: RouteTrend): RouteTrendDisplay {
  return DISPLAY[trend];
}
