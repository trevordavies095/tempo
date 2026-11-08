/** Direction of a week's value against its trailing three-week average. */
export type RecapTrend = 'up' | 'down' | 'flat';

/** Within this fraction of the trailing average either side, a week counts as flat. */
export const RECAP_FLAT_BAND = 0.05;

/**
 * Classifies `current` against `trailingAvg`: flat within ±5% of the trailing average, otherwise up or down.
 * Returns null when there is no baseline (trailing average zero or missing), so no arrow is shown.
 */
export function recapTrend(current: number, trailingAvg: number | null | undefined): RecapTrend | null {
  if (trailingAvg == null || !Number.isFinite(trailingAvg) || trailingAvg <= 0) {
    return null;
  }
  const band = trailingAvg * RECAP_FLAT_BAND;
  if (current > trailingAvg + band) return 'up';
  if (current < trailingAvg - band) return 'down';
  return 'flat';
}
