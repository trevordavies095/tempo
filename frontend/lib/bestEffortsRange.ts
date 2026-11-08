import type { BestEffortItem, BestEffortsParams } from '@/lib/api';

/**
 * Personal Records range on the Insights page. The rules match the iOS app (parity ticket 18):
 * - All time: no parameters, so the server returns all-time bests with no previous fields and no badges.
 * - This year: `from` and `since` = Jan 1 of the current local year (season bests and PRs set this year).
 * - Last 90 days: `from` and `since` = today − 90 days.
 * Dates are the user's local calendar date written as `yyyy-MM-dd`; the server compares them
 * inclusively against each Workout's UTC start date.
 */
export type BestEffortsRange = 'allTime' | 'thisYear' | 'last90Days';

export const BEST_EFFORTS_RANGES: { id: BestEffortsRange; label: string }[] = [
  { id: 'allTime', label: 'All time' },
  { id: 'thisYear', label: 'This year' },
  { id: 'last90Days', label: 'Last 90 days' },
];

/** Local calendar date as `yyyy-MM-dd` (no UTC conversion, so Jan 1 stays Jan 1). */
export function formatLocalDate(date: Date): string {
  const y = date.getFullYear();
  const m = (date.getMonth() + 1).toString().padStart(2, '0');
  const d = date.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Query parameters for a range. `today` is injectable so the mapping is deterministic. */
export function bestEffortsRangeParams(range: BestEffortsRange, today: Date = new Date()): BestEffortsParams {
  switch (range) {
    case 'allTime':
      return {};
    case 'thisYear': {
      const start = formatLocalDate(new Date(today.getFullYear(), 0, 1));
      return { from: start, since: start };
    }
    case 'last90Days': {
      const start = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 90);
      const value = formatLocalDate(start);
      return { from: value, since: value };
    }
  }
}

/**
 * "New" badge rule: a ranged result (never All time) whose Workout date is inside the range
 * and which beat a previous best (`previousTimeS` non-null).
 */
export function isNewRecord(item: BestEffortItem, range: BestEffortsRange, params: BestEffortsParams): boolean {
  if (range === 'allTime' || item.previousTimeS == null) {
    return false;
  }
  if (params.from && item.workoutDate < params.from) {
    return false;
  }
  if (params.to && item.workoutDate > params.to) {
    return false;
  }
  return true;
}
