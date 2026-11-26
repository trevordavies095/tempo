/**
 * Local calendar days for the API's `yyyy-MM-dd` dates and `timezoneOffsetMinutes` parameter (the server
 * counterpart is `LocalCalendar`). `new Date('yyyy-MM-dd')` and `toISOString()` read and write UTC days, which
 * land a day off west or east of UTC; these stay in the browser's local day.
 */

/** Browser offset as the API expects it: minutes ahead of UTC (EST = -300). */
export function browserTimezoneOffsetMinutes(): number {
  return -new Date().getTimezoneOffset();
}

/** Parses a `yyyy-MM-dd` date as local midnight of that day, or null when it doesn't match. */
export function parseLocalDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return null;
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
}

/** Local calendar date as `yyyy-MM-dd` (no UTC conversion, so Jan 1 stays Jan 1). */
export function formatLocalDate(date: Date): string {
  const y = date.getFullYear();
  const m = String(date.getMonth() + 1).padStart(2, '0');
  const d = String(date.getDate()).padStart(2, '0');
  return `${y}-${m}-${d}`;
}
