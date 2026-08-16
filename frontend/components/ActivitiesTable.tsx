import Link from 'next/link';
import { type WorkoutListItem } from '@/lib/api';
import { formatDuration, formatDistance, formatElevation, getWorkoutDisplayName } from '@/lib/format';
import { formatActivityDate } from '@/utils/dateUtils';
import { type SortColumn, type SortOrder } from '@/hooks/useActivitiesFilters';
import { IconArrowDown, IconArrowUp } from '@tabler/icons-react';

interface ActivitiesTableProps {
  workouts: WorkoutListItem[];
  unitPreference: 'metric' | 'imperial';
  sortBy: SortColumn;
  sortOrder: SortOrder;
  onSort: (column: SortColumn) => void;
}

function getSortIcon(column: SortColumn, currentSortBy: SortColumn, currentSortOrder: SortOrder) {
  if (currentSortBy !== column) {
    return <IconArrowDown className="inline-block w-3 h-3 ml-1 text-muted" />;
  }
  return currentSortOrder === 'desc' ? (
    <IconArrowDown className="inline-block w-3 h-3 ml-1 text-ink" />
  ) : (
    <IconArrowUp className="inline-block w-3 h-3 ml-1 text-ink" />
  );
}

export default function ActivitiesTable({
  workouts,
  unitPreference,
  sortBy,
  sortOrder,
  onSort,
}: ActivitiesTableProps) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full">
        <thead className="bg-canvas">
          <tr>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('startedAt')}
            >
              <div className="flex items-center">
                Date
                {getSortIcon('startedAt', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('name')}
            >
              <div className="flex items-center">
                Title
                {getSortIcon('name', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('durationS')}
            >
              <div className="flex items-center">
                Time
                {getSortIcon('durationS', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('distanceM')}
            >
              <div className="flex items-center">
                Distance
                {getSortIcon('distanceM', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('elevGainM')}
            >
              <div className="flex items-center">
                Elevation
                {getSortIcon('elevGainM', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-muted uppercase tracking-wider cursor-pointer hover:bg-raised"
              onClick={() => onSort('relativeEffort')}
            >
              <div className="flex items-center">
                Relative Effort
                {getSortIcon('relativeEffort', sortBy, sortOrder)}
              </div>
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {workouts.map((workout) => (
            <tr key={workout.id} className="hover:bg-canvas">
              <td className="px-6 py-4 whitespace-nowrap text-sm text-ink">
                {formatActivityDate(workout.startedAt)}
              </td>
              <td className="px-6 py-4 text-sm">
                <Link
                  href={`/dashboard/${workout.id}`}
                  className="text-ink underline decoration-border hover:decoration-ink"
                >
                  {getWorkoutDisplayName(workout.name, workout.startedAt)}
                </Link>
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-ink">
                {formatDuration(workout.durationS)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-ink">
                {formatDistance(workout.distanceM, unitPreference)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-ink">
                {workout.elevGainM !== null && workout.elevGainM !== undefined
                  ? formatElevation(workout.elevGainM, unitPreference)
                  : '—'}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-ink">
                {workout.relativeEffort !== null && workout.relativeEffort !== undefined
                  ? workout.relativeEffort
                  : '—'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
