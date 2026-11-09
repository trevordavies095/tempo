/** Direction of a week's value against its trailing three-week average. */
export type RecapTrend = 'up' | 'down' | 'flat';

/**
 * Classifies `current` against `trailingAvg` the same way as iOS: up when above the trailing average,
 * down when below, flat only when equal. Returns null when there is no baseline (trailing average zero
 * or missing), so no arrow is shown.
 */
export function recapTrend(current: number, trailingAvg: number | null | undefined): RecapTrend | null {
  if (trailingAvg == null || !Number.isFinite(trailingAvg) || trailingAvg <= 0) {
    return null;
  }
  if (current > trailingAvg) return 'up';
  if (current < trailingAvg) return 'down';
  return 'flat';
}

/** The week a recap shows: whether it is still in progress, and how many runs it has so far. */
export interface RecapWeek {
  inProgress: boolean;
  runs: number;
}

/**
 * `recapTrend` for a metric in `week`, the same rule as iOS. An in-progress week with no runs yet shows no
 * arrow on any metric, so the start of every week does not read as falling short. A finished week with no
 * runs keeps its arrows (a real comparison), and once the week has a run the plain rule applies.
 */
export function recapWeekTrend(
  week: RecapWeek,
  current: number,
  trailingAvg: number | null | undefined
): RecapTrend | null {
  if (week.inProgress && week.runs <= 0) return null;
  return recapTrend(current, trailingAvg);
}

/**
 * Weekday for the recap header's "through <weekday>" while the week is in progress.
 * Returns null when the week is complete, and on Sunday (the week's last day), where the range alone says it.
 */
export function recapThroughWeekday(currentWeekIsPartial: boolean | null | undefined, now: Date = new Date()): string | null {
  if (!currentWeekIsPartial || now.getDay() === 0) return null;
  return now.toLocaleDateString(undefined, { weekday: 'long' });
}
