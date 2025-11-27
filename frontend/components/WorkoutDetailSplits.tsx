import { formatDistance, formatDuration, formatPace } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';

interface WorkoutDetailSplitsProps {
  splits: WorkoutDetail['splits'];
  unitPreference: UnitPreference;
  hoveredSplitIdx: number | null;
  onSplitHover: (idx: number | null) => void;
}

export default function WorkoutDetailSplits({
  splits,
  unitPreference,
  hoveredSplitIdx,
  onSplitHover,
}: WorkoutDetailSplitsProps) {
  if (!splits || splits.length === 0) {
    return null;
  }

  return (
    <div className="bg-white dark:bg-gray-900 p-3 rounded-lg border border-gray-200 dark:border-gray-800">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-2">
        Splits ({splits.length})
      </h2>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-gray-200 dark:border-gray-800">
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-gray-700 dark:text-gray-300">
                Split
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-gray-700 dark:text-gray-300">
                Distance
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-gray-700 dark:text-gray-300">
                Duration
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-gray-700 dark:text-gray-300">
                Pace
              </th>
            </tr>
          </thead>
          <tbody>
            {splits.map((split) => (
              <tr
                key={`split-${split.idx}`}
                className="border-b border-gray-100 dark:border-gray-900 cursor-pointer hover:bg-gray-50 dark:hover:bg-gray-800 transition-colors"
                onMouseEnter={() => onSplitHover(split.idx)}
                onMouseLeave={() => onSplitHover(null)}
              >
                <td className="py-1.5 px-2.5 text-xs text-gray-700 dark:text-gray-300">
                  {split.idx + 1}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-gray-700 dark:text-gray-300">
                  {formatDistance(split.distanceM, unitPreference)}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-gray-700 dark:text-gray-300">
                  {formatDuration(split.durationS)}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-gray-700 dark:text-gray-300">
                  {formatPace(split.paceS, unitPreference)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

