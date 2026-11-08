import Link from 'next/link';
import type { BestEffortItem } from '@/lib/api';

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
  const [y, m, d] = value.split('-').map(Number);
  if (!y || !m || !d) return value;
  return new Date(y, m - 1, d).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

/**
 * Distance → time table of best efforts, shortest distance first. Each time links to its Workout.
 * Shared by the dashboard Best Efforts card and the Insights Personal Records card. When rows carry a
 * previous best (ranged requests with `since`), each shows "Was m:ss on <date>" linking to that
 * Workout and the improvement; `isNew` marks rows with a New pill.
 */
export function BestEffortsTable({
  efforts,
  isNew,
}: {
  efforts: BestEffortItem[];
  isNew?: (item: BestEffortItem) => boolean;
}) {
  const sorted = [...efforts].sort((a, b) => a.distanceM - b.distanceM);
  const hasHistory = sorted.some((item) => item.previousTimeS != null);

  return (
    <div className="overflow-x-auto">
      <table className="w-full">
        <tbody className="divide-y divide-border">
          {sorted.map((item) => {
            const previous = item.previousTimeS;
            return (
              <tr key={item.distance}>
                <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-ink align-top">
                  <span className="inline-flex items-center gap-2">
                    {item.distance}
                    {isNew?.(item) ? (
                      <span className="rounded-full bg-ink px-2 py-0.5 text-xs font-semibold text-inverse dark:bg-volt dark:text-on-volt">
                        New
                      </span>
                    ) : null}
                  </span>
                </td>
                <td className="px-3 py-2 whitespace-nowrap text-sm align-top">
                  <Link
                    href={`/dashboard/${item.workoutId}`}
                    className="text-ink tabular-nums underline decoration-border hover:decoration-ink"
                  >
                    {formatEffortTime(item.timeS)}
                  </Link>
                  {previous != null ? (
                    <div className="mt-0.5 text-xs text-muted">
                      {item.previousWorkoutId ? (
                        <Link
                          href={`/dashboard/${item.previousWorkoutId}`}
                          className="underline decoration-border hover:text-ink hover:decoration-ink"
                        >
                          Was {formatEffortTime(previous)}
                          {item.previousWorkoutDate ? ` on ${formatShortDate(item.previousWorkoutDate)}` : ''}
                        </Link>
                      ) : (
                        <>Was {formatEffortTime(previous)}</>
                      )}
                    </div>
                  ) : null}
                </td>
                {hasHistory ? (
                  <td className="px-3 py-2 whitespace-nowrap text-sm text-right tabular-nums text-muted align-top">
                    {previous != null && previous > item.timeS ? (
                      <span aria-label={`Improved by ${formatEffortTime(previous - item.timeS)}`}>
                        −{formatEffortTime(previous - item.timeS)}
                      </span>
                    ) : null}
                  </td>
                ) : null}
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
