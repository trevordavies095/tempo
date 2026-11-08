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

/**
 * Weekday for the recap header's "through <weekday>" while the week is in progress.
 * Returns null when the week is complete, and on Sunday (the week's last day), where the range alone says it.
 */
export function recapThroughWeekday(currentWeekIsPartial: boolean | null | undefined, now: Date = new Date()): string | null {
  if (!currentWeekIsPartial || now.getDay() === 0) return null;
  return now.toLocaleDateString(undefined, { weekday: 'long' });
}
