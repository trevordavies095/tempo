/**
 * Coach check-in week defaults (tempo-cli port; mirrors API WeeklyRecapWeekMath.ResolveDefaultCheckInWeek).
 * Sat–Sun and Tue–Fri → today; Monday → last week’s Monday so the picker lands in the last completed Mon–Sun.
 */

function localDateInputValue(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

/**
 * Default referenceDate (yyyy-MM-dd) when the check-in UI opens with no query param.
 */
export function resolveDefaultCheckInReferenceDate(now: Date = new Date()): string {
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  // Monday (getDay() === 1) → previous Monday
  if (d.getDay() === 1) {
    d.setDate(d.getDate() - 7);
  }
  return localDateInputValue(d);
}

/** Shift a yyyy-MM-dd calendar date by whole days (local). */
export function addDaysIso(iso: string, days: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  date.setDate(date.getDate() + days);
  return localDateInputValue(date);
}

/** Plan week Monday = check-in Monday + 7 (ritual N → N+1). */
export function planWeekStartFromCheckInMonday(checkInMonday: string): string {
  return addDaysIso(checkInMonday, 7);
}

/**
 * Short Mon–Sun label for ritual CTAs, e.g. "Oct 12–18".
 * Assumes start/end are yyyy-MM-dd in the same year for the end day number.
 */
export function formatWeekRangeLabel(startIso: string, endIso: string): string {
  const start = parseLocalIso(startIso);
  const end = parseLocalIso(endIso);
  const startLabel = start.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
  const endLabel = end.toLocaleDateString(undefined, {
    month: start.getMonth() === end.getMonth() ? undefined : 'short',
    day: 'numeric',
  });
  return `${startLabel}–${endLabel}`;
}

function parseLocalIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}
