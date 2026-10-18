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
