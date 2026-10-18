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
  const startLabel = start.toLocaleDateString('en-GB', { month: 'short', day: 'numeric' });
  const endLabel = end.toLocaleDateString('en-GB', {
    month: start.getMonth() === end.getMonth() ? undefined : 'short',
    day: 'numeric',
  });
  return `${startLabel}–${endLabel}`;
}

/** Athlete-facing day label, e.g. "Mon 19 Oct". */
export function formatCoachDate(iso: string): string {
  const date = parseLocalIso(iso);
  return date.toLocaleDateString('en-GB', {
    weekday: 'short',
    day: 'numeric',
    month: 'short',
  });
}

/** Map coach job error codes to plain sentences for the UI. */
export function formatCoachJobError(
  errorCode: string | null | undefined,
  errorMessage: string | null | undefined
): string {
  const message = errorMessage?.trim();
  if (message) {
    return message;
  }
  switch (errorCode) {
    case 'provider_error':
      return 'The coach could not reach Anthropic. Check your API key and try again.';
    case 'redaction_failed':
      return 'The privacy packet could not be built. Nothing was sent.';
    case 'validation_failed':
      return 'The draft failed validation. Try again or ask for changes.';
    case 'cancelled':
      return 'This job was cancelled.';
    default:
      return errorCode ? 'Something went wrong. Try again.' : 'Something went wrong. Try again.';
  }
}

/** Poll a coach job until ready/failed or timeout (used outside React render). */
export async function pollCoachJobUntilSettled<T extends { status: string }>(
  started: T,
  fetchJob: (job: T) => Promise<T>,
  options?: {
    timeoutMs?: number;
    intervalMs?: number;
    onUpdate?: (job: T) => void;
  }
): Promise<T> {
  const timeoutMs = options?.timeoutMs ?? 180_000;
  const intervalMs = options?.intervalMs ?? 500;
  const deadline = Date.now() + timeoutMs;
  let current = started;
  options?.onUpdate?.(current);
  while (current.status !== 'ready' && current.status !== 'failed') {
    if (Date.now() > deadline) {
      throw new Error('Timed out while waiting for your coach.');
    }
    await new Promise((r) => setTimeout(r, intervalMs));
    current = await fetchJob(current);
    options?.onUpdate?.(current);
  }
  return current;
}

function parseLocalIso(iso: string): Date {
  const [y, m, d] = iso.split('-').map(Number);
  return new Date(y, m - 1, d);
}
