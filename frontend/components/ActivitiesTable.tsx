import Link from 'next/link';
import { type WorkoutListItem } from '@/lib/api';
import { formatDuration, formatDistance, formatElevation, getWorkoutDisplayName } from '@/lib/format';
import { formatActivityDate } from '@/utils/dateUtils';
import { type SortColumn, type SortOrder } from '@/hooks/useActivitiesFilters';

interface ActivitiesTableProps {
  workouts: WorkoutListItem[];
  unitPreference: 'metric' | 'imperial';
  sortBy: SortColumn;
  sortOrder: SortOrder;
  onSort: (column: SortColumn) => void;
}

function getSortIcon(column: SortColumn, currentSortBy: SortColumn, currentSortOrder: SortOrder) {
  if (currentSortBy !== column) {
    return <span className="text-gray-400 dark:text-gray-500 ml-1">↓</span>;
  }
  return currentSortOrder === 'desc' ? (
    <span className="text-gray-900 dark:text-gray-100 ml-1">↓</span>
  ) : (
    <span className="text-gray-900 dark:text-gray-100 ml-1">↑</span>
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
        <thead className="bg-gray-50 dark:bg-gray-800">
          <tr>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('startedAt')}
            >
              <div className="flex items-center">
                Date
                {getSortIcon('startedAt', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('name')}
            >
              <div className="flex items-center">
                Title
                {getSortIcon('name', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('durationS')}
            >
              <div className="flex items-center">
                Time
                {getSortIcon('durationS', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('distanceM')}
            >
              <div className="flex items-center">
                Distance
                {getSortIcon('distanceM', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('elevGainM')}
            >
              <div className="flex items-center">
                Elevation
                {getSortIcon('elevGainM', sortBy, sortOrder)}
              </div>
            </th>
            <th
              className="px-6 py-3 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wider cursor-pointer hover:bg-gray-100 dark:hover:bg-gray-700"
              onClick={() => onSort('relativeEffort')}
            >
              <div className="flex items-center">
                Relative Effort
                {getSortIcon('relativeEffort', sortBy, sortOrder)}
              </div>
            </th>
          </tr>
        </thead>
        <tbody className="bg-white dark:bg-gray-900 divide-y divide-gray-200 dark:divide-gray-800">
          {workouts.map((workout) => (
            <tr key={workout.id} className="hover:bg-gray-50 dark:hover:bg-gray-800">
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                {formatActivityDate(workout.startedAt)}
              </td>
              <td className="px-6 py-4 text-sm">
                <Link
                  href={`/dashboard/${workout.id}`}
                  className="text-blue-600 dark:text-blue-400 hover:text-blue-800 dark:hover:text-blue-300 hover:underline"
                >
                  {getWorkoutDisplayName(workout.name, workout.startedAt)}
                </Link>
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                {formatDuration(workout.durationS)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                {formatDistance(workout.distanceM, unitPreference)}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
                {workout.elevGainM !== null && workout.elevGainM !== undefined
                  ? formatElevation(workout.elevGainM, unitPreference)
                  : '—'}
              </td>
              <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-900 dark:text-gray-100">
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

