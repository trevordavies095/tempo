import { formatDistance, formatDuration, formatPace } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';
import { Card } from '@/components/ui/Card';

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
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-2">
        Splits ({splits.length})
      </h2>
      <div className="overflow-x-auto">
        <table className="w-full border-collapse">
          <thead>
            <tr className="border-b border-border">
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-ink">
                Split
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-ink">
                Distance
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-ink">
                Duration
              </th>
              <th className="text-left py-1.5 px-2.5 text-xs font-semibold text-ink">
                Pace
              </th>
            </tr>
          </thead>
          <tbody>
            {splits.map((split) => (
              <tr
                key={`split-${split.idx}`}
                className={`border-b border-border cursor-pointer hover:bg-canvas transition-colors ${
                  hoveredSplitIdx === split.idx ? 'bg-canvas' : ''
                }`}
                onMouseEnter={() => onSplitHover(split.idx)}
                onClick={() => onSplitHover(split.idx)}
                onMouseLeave={() => onSplitHover(null)}
              >
                <td className="py-1.5 px-2.5 text-xs text-ink">
                  {split.idx + 1}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-ink">
                  {formatDistance(split.distanceM, unitPreference)}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-ink">
                  {formatDuration(split.durationS)}
                </td>
                <td className="py-1.5 px-2.5 text-xs text-ink">
                  {formatPace(split.paceS, unitPreference)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </Card>
  );
}
