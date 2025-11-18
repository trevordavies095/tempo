'use client';

import Link from 'next/link';
import { type WorkoutListItem } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDateTime, getWorkoutDisplayName } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import WorkoutMap from './WorkoutMap';

interface WorkoutCardProps {
  workout: WorkoutListItem;
}

function getRunTypeBadgeColor(runType: string | null): string {
  switch (runType) {
    case 'Race':
      return 'bg-red-100 dark:bg-red-900/30 text-red-800 dark:text-red-200 border-red-200 dark:border-red-800';
    case 'Workout':
      return 'bg-orange-100 dark:bg-orange-900/30 text-orange-800 dark:text-orange-200 border-orange-200 dark:border-orange-800';
    case 'Long Run':
      return 'bg-blue-100 dark:bg-blue-900/30 text-blue-800 dark:text-blue-200 border-blue-200 dark:border-blue-800';
    default:
      return 'bg-gray-100 dark:bg-gray-800 text-gray-700 dark:text-gray-300 border-gray-200 dark:border-gray-700';
  }
}

function getRunTypeLabel(runType: string | null): string {
  return runType || 'None';
}

export default function WorkoutCard({ workout }: WorkoutCardProps) {
  const { unitPreference } = useSettings();

  return (
    <Link
      href={`/dashboard/${workout.id}`}
      className="block w-full bg-white dark:bg-gray-900 rounded-lg border border-gray-200 dark:border-gray-800 shadow-sm hover:shadow-md transition-shadow"
    >
      <div className="p-6">
        {/* Header: Name, Date/Time, Run Type */}
        <div className="flex items-start justify-between mb-4">
          <div className="flex-1 min-w-0">
            <h3 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-1">
              {getWorkoutDisplayName(workout.name, workout.startedAt)}
            </h3>
            <p className="text-sm text-gray-600 dark:text-gray-400">
              {formatDateTime(workout.startedAt)}
            </p>
          </div>
          <span
            className={`ml-4 px-3 py-1 text-xs font-medium rounded-full border ${getRunTypeBadgeColor(workout.runType)}`}
          >
            {getRunTypeLabel(workout.runType)}
          </span>
        </div>

        {/* Metrics: Distance, Pace, Time */}
        <div className="grid grid-cols-3 gap-4 mb-4">
          <div>
            <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {formatDistance(workout.distanceM, unitPreference)}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mt-1">
              Distance
            </div>
          </div>
          <div>
            <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {formatPace(workout.avgPaceS, unitPreference)}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mt-1">
              Pace
            </div>
          </div>
          <div>
            <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {formatDuration(workout.durationS)}
            </div>
            <div className="text-xs text-gray-500 dark:text-gray-400 uppercase tracking-wide mt-1">
              Time
            </div>
          </div>
        </div>

        {/* Map */}
        {workout.route ? (
          <div className="mt-4">
            <WorkoutMap route={workout.route} workoutId={workout.id} height="h-48" interactive={false} />
          </div>
        ) : (
          <div className="mt-4 flex items-center justify-center h-48 bg-gray-100 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
            <p className="text-gray-500 dark:text-gray-400">No route data available</p>
          </div>
        )}
      </div>
    </Link>
  );
}

