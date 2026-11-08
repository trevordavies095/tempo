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

/**
 * Distance → time table of best efforts, shortest distance first. Each time links to its Workout.
 * Shared by the dashboard Best Efforts card and the Insights Personal Records card.
 */
export function BestEffortsTable({ efforts }: { efforts: BestEffortItem[] }) {
  const sorted = [...efforts].sort((a, b) => a.distanceM - b.distanceM);

  return (
    <div className="overflow-x-auto">
      <table className="w-full">
        <tbody className="divide-y divide-border">
          {sorted.map((item) => (
            <tr key={item.distance}>
              <td className="px-3 py-2 whitespace-nowrap text-sm font-medium text-ink">
                {item.distance}
              </td>
              <td className="px-3 py-2 whitespace-nowrap text-sm">
                <Link
                  href={`/dashboard/${item.workoutId}`}
                  className="text-ink underline decoration-border hover:decoration-ink"
                >
                  {formatEffortTime(item.timeS)}
                </Link>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
