import type { BestEffortItem, BestEffortsParams } from '@/lib/api';

/**
 * Personal Records range on the Insights page. The rules match the iOS app (parity tickets 18 and 28,
 * amendment 2026-11-08):
 * - All time: one request with no parameters. All-time bests, no New badges, no "Was" lines.
 * - This year / Last 90 days: two requests in parallel.
 *   (1) `from=<start>` → the list: the fastest effort per distance on or after the start (season bests).
 *       Distances with no effort in the range are left out.
 *   (2) `since=<start>` only → the all-time PRs set on or after the start, each carrying the previous best
 *       it beat (`previousTimeS`, `previousWorkoutId`, `previousWorkoutDate`).
 *   A list row is New when response (2) has the same distance with the same `workoutId`; its "Was" line
 *   uses the previous fields from (2). If (2) fails the list shows without badges.
 * Sending `from` and `since` together cannot work: the server takes the previous best from
 * `[from, since − 1]`, which is empty when both are the same date.
 *
 * Start dates: Jan 1 of the current local year; local today − 90 days. Written as `yyyy-MM-dd` and
 * compared inclusively by the server against each Workout's UTC start date.
 */
export type BestEffortsRange = 'allTime' | 'thisYear' | 'last90Days';

export const BEST_EFFORTS_RANGES: { id: BestEffortsRange; label: string }[] = [
  { id: 'allTime', label: 'All time' },
  { id: 'thisYear', label: 'This year' },
  { id: 'last90Days', label: 'Last 90 days' },
];

/** The requests one range needs: the list, and for ranged choices the PRs used for badges. */
export interface BestEffortsRangeRequests {
  /** Request (1): the rows shown in the table. */
  list: BestEffortsParams;
  /** Request (2): PRs set since the start, for New and "Was". Null for All time (no second request). */
  prs: BestEffortsParams | null;
}

/** One table row: the list effort, with previous fields only when it is a PR set in the range. */
export interface PersonalRecordRow extends BestEffortItem {
  isNew: boolean;
}

/** Local calendar date as `yyyy-MM-dd` (no UTC conversion, so Jan 1 stays Jan 1). */
export function formatLocalDate(date: Date): string {
  const y = date.getFullYear();
  const m = (date.getMonth() + 1).toString().padStart(2, '0');
  const d = date.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${d}`;
}

/** Start date of a ranged choice, or null for All time. `today` is injectable for determinism. */
export function bestEffortsRangeStart(range: BestEffortsRange, today: Date = new Date()): string | null {
  switch (range) {
    case 'allTime':
      return null;
    case 'thisYear':
      return formatLocalDate(new Date(today.getFullYear(), 0, 1));
    case 'last90Days':
      return formatLocalDate(new Date(today.getFullYear(), today.getMonth(), today.getDate() - 90));
  }
}

/** Request parameters for a range: `{ list: {}, prs: null }` for All time, else `from` and `since` apart. */
export function bestEffortsRangeRequests(
  range: BestEffortsRange,
  today: Date = new Date()
): BestEffortsRangeRequests {
  const start = bestEffortsRangeStart(range, today);
  if (start == null) {
    return { list: {}, prs: null };
  }
  return { list: { from: start }, prs: { since: start } };
}

/**
 * Joins the list (request 1) with the PRs (request 2) by distance and `workoutId`. A matching row is New
 * and takes `previousTimeS`, `previousWorkoutId`, `previousWorkoutDate` from the PR; every other row has
 * them cleared. Pass `prs` as null/undefined for All time, while request (2) is loading, or when it
 * failed: the list then shows without badges or "Was" lines.
 */
export function joinPersonalRecords(
  list: BestEffortItem[],
  prs: BestEffortItem[] | null | undefined
): PersonalRecordRow[] {
  const prByDistance = new Map((prs ?? []).map((pr) => [pr.distance, pr]));
  return list.map((item) => {
    const pr = prByDistance.get(item.distance);
    if (pr && pr.workoutId === item.workoutId) {
      return {
        ...item,
        previousTimeS: pr.previousTimeS ?? null,
        previousWorkoutId: pr.previousWorkoutId ?? null,
        previousWorkoutDate: pr.previousWorkoutDate ?? null,
        isNew: true,
      };
    }
    return { ...item, previousTimeS: null, previousWorkoutId: null, previousWorkoutDate: null, isNew: false };
  });
}
