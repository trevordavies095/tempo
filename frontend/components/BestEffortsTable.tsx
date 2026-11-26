import Link from 'next/link';
import type { BestEffortItem } from '@/lib/api';
import { parseLocalDate } from '@/lib/localCalendar';
import type { UnitPreference } from '@/lib/settings';
import { formatPace } from '@/lib/format';

/** Format seconds as M:SS or H:MM:SS. */
export function formatEffortTime(seconds: number): string {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;

  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${secs.toString().padStart(2, '0')}`;
}

/** `yyyy-MM-dd` as a short local date ("Aug 3, 2025") without a UTC shift. */
function formatShortDate(value: string): string {
  const date = parseLocalDate(value);
  if (!date) return value;
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

function NewPill() {
  return (
    <span className="rounded-full border border-ink/25 px-1.5 py-px text-[10px] font-semibold uppercase leading-tight tracking-wide text-ink dark:border-volt/40 dark:text-volt">
      New
    </span>
  );
}

function PreviousBest({ item }: { item: BestEffortItem }) {
  const previous = item.previousTimeS;
  if (previous == null) return null;
  const text = `Was ${formatEffortTime(previous)}${
    item.previousWorkoutDate ? ` · ${formatShortDate(item.previousWorkoutDate)}` : ''
  }`;
  return item.previousWorkoutId ? (
    <Link
      href={`/dashboard/${item.previousWorkoutId}`}
      className="underline decoration-border hover:text-ink hover:decoration-ink"
    >
      {text}
    </Link>
  ) : (
    <>{text}</>
  );
}

function Improvement({ item }: { item: BestEffortItem }) {
  const previous = item.previousTimeS;
  if (previous == null || previous <= item.timeS) return null;
  return (
    <span aria-label={`Improved by ${formatEffortTime(previous - item.timeS)}`}>
      −{formatEffortTime(previous - item.timeS)}
    </span>
  );
}

const HEADER_CELL = 'px-3 pb-2 text-xs font-medium uppercase tracking-wide text-muted';

/**
 * Distance → time table of best efforts, shortest distance first. Each time links to its Workout.
 * Shared by the dashboard Best Efforts card and the Insights Personal Records card. When rows carry a
 * previous best (Insights joins it in from the `since` request), each shows "Was m:ss on <date>" linking
 * to that Workout and the improvement; `isNew` marks rows with a New pill.
 *
 * `detailed` is the wide Insights layout: column headers, plus Pace and Date columns. Its columns are
 * fixed-width and the improvement sits on the "Was" line, so nothing shifts when the range changes.
 */
export function BestEffortsTable({
  efforts,
  isNew,
  detailed = false,
  unitPreference = 'metric',
}: {
  efforts: BestEffortItem[];
  isNew?: (item: BestEffortItem) => boolean;
  detailed?: boolean;
  unitPreference?: UnitPreference;
}) {
  const sorted = [...efforts].sort((a, b) => a.distanceM - b.distanceM);

  if (detailed) {
    return (
      <div className="overflow-x-auto">
        <table className="w-full table-fixed">
          <thead>
            <tr className="border-b border-border">
              <th scope="col" className={`${HEADER_CELL} w-[30%] text-left`}>Distance</th>
              <th scope="col" className={`${HEADER_CELL} w-[36%] text-left`}>Time</th>
              <th scope="col" className={`${HEADER_CELL} hidden w-[15%] text-right sm:table-cell`}>Pace</th>
              <th scope="col" className={`${HEADER_CELL} w-[19%] text-right`}>Date</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {sorted.map((item) => (
              <tr key={item.distance}>
                <th scope="row" className="px-3 py-2 text-left text-sm font-medium text-ink align-top">
                  <span className="inline-flex flex-wrap items-center gap-x-2 gap-y-1">
                    {item.distance}
                    {isNew?.(item) ? <NewPill /> : null}
                  </span>
                </th>
                <td className="px-3 py-2 text-sm align-top">
                  <span className="inline-flex flex-wrap items-baseline gap-x-2">
                    <Link
                      href={`/dashboard/${item.workoutId}`}
                      className="font-semibold text-ink tabular-nums underline decoration-border hover:decoration-ink"
                    >
                      {formatEffortTime(item.timeS)}
                    </Link>
                    <span className="text-xs font-medium tabular-nums text-hr-zone-1">
                      <Improvement item={item} />
                    </span>
                  </span>
                  {item.previousTimeS != null ? (
                    <div className="mt-0.5 whitespace-nowrap text-xs text-muted tabular-nums">
                      <PreviousBest item={item} />
                    </div>
                  ) : null}
                </td>
                <td className="hidden px-3 py-2 text-right text-sm tabular-nums text-muted align-top whitespace-nowrap sm:table-cell">
                  {item.distanceM > 0 ? formatPace(item.timeS / (item.distanceM / 1000), unitPreference) : '—'}
                </td>
                <td className="px-3 py-2 text-right text-sm tabular-nums text-muted align-top whitespace-nowrap">
                  {formatShortDate(item.workoutDate)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  const hasHistory = sorted.some((item) => item.previousTimeS != null);

  return (
    <div className="overflow-x-auto">
      <table className="w-full">
        <tbody className="divide-y divide-border">
          {sorted.map((item) => (
            <tr key={item.distance}>
              <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-ink align-top">
                <span className="inline-flex items-center gap-2">
                  {item.distance}
                  {isNew?.(item) ? <NewPill /> : null}
                </span>
              </td>
              <td className="px-3 py-2 whitespace-nowrap text-sm align-top">
                <Link
                  href={`/dashboard/${item.workoutId}`}
                  className="text-ink tabular-nums underline decoration-border hover:decoration-ink"
                >
                  {formatEffortTime(item.timeS)}
                </Link>
                {item.previousTimeS != null ? (
                  <div className="mt-0.5 text-xs text-muted">
                    <PreviousBest item={item} />
                  </div>
                ) : null}
              </td>
              {hasHistory ? (
                <td className="px-3 py-2 whitespace-nowrap text-sm text-right tabular-nums text-muted align-top">
                  <Improvement item={item} />
                </td>
              ) : null}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
