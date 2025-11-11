'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import dynamic from 'next/dynamic';
import { getWorkout } from '@/lib/api';
import { formatDate, formatDateTime, formatDistance, formatDuration, formatPace } from '@/lib/format';

// Dynamically import WorkoutMap to avoid SSR issues with Leaflet
const WorkoutMap = dynamic(() => import('@/components/WorkoutMap'), {
  ssr: false,
  loading: () => (
    <div className="flex items-center justify-center h-96 bg-gray-100 dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700">
      <p className="text-gray-500 dark:text-gray-400">Loading map...</p>
    </div>
  ),
});

export default function WorkoutDetailPage() {
  const params = useParams();
  const id = params.id as string;

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workout', id],
    queryFn: () => getWorkout(id),
  });

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Workout Details
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400 mb-8">
              Loading workout...
            </p>
          </div>
        </main>
      </div>
    );
  }

  if (isError) {
    const isNotFound = error instanceof Error && error.message === 'Workout not found';
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <Link
              href="/workouts"
              className="text-blue-600 dark:text-blue-400 hover:underline mb-4 inline-block"
            >
              ← Back to Workouts
            </Link>
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Workout Details
            </h1>
            <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
              <p className="text-sm text-red-800 dark:text-red-200">
                {isNotFound ? 'Workout not found' : `Error: ${error instanceof Error ? error.message : 'Failed to load workout'}`}
              </p>
            </div>
          </div>
        </main>
      </div>
    );
  }

  if (!data) {
    return null;
  }

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <Link
            href="/workouts"
            className="text-blue-600 dark:text-blue-400 hover:underline mb-4 inline-block"
          >
            ← Back to Workouts
          </Link>
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
            Workout Details
          </h1>
          <p className="text-lg text-gray-600 dark:text-gray-400">
            {formatDateTime(data.startedAt)}
          </p>
        </div>

        <div className="w-full space-y-8">
          {/* Main Stats */}
          <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Distance</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatDistance(data.distanceM)}
              </div>
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Duration</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatDuration(data.durationS)}
              </div>
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Pace</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {formatPace(data.avgPaceS)}
              </div>
            </div>
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <div className="text-sm text-gray-600 dark:text-gray-400 mb-1">Elevation</div>
              <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
                {data.elevGainM !== null
                  ? `${(data.elevGainM / 1000).toFixed(1)} km`
                  : '—'}
              </div>
            </div>
          </div>

          {/* Additional Info */}
          <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
            <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Workout Information
            </h2>
            <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Run Type</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {data.runType || '—'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Source</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {data.source || '—'}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Started At</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {formatDateTime(data.startedAt)}
                </dd>
              </div>
              <div>
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400">Created At</dt>
                <dd className="mt-1 text-sm text-gray-900 dark:text-gray-100">
                  {formatDateTime(data.createdAt)}
                </dd>
              </div>
            </dl>
            {data.notes && (
              <div className="mt-4">
                <dt className="text-sm font-medium text-gray-600 dark:text-gray-400 mb-1">Notes</dt>
                <dd className="text-sm text-gray-900 dark:text-gray-100 whitespace-pre-wrap">
                  {data.notes}
                </dd>
              </div>
            )}
          </div>

          {/* Splits Table */}
          {data.splits && data.splits.length > 0 && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Splits ({data.splits.length})
              </h2>
              <div className="overflow-x-auto">
                <table className="w-full border-collapse">
                  <thead>
                    <tr className="border-b border-gray-200 dark:border-gray-800">
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Split
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Distance
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Duration
                      </th>
                      <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                        Pace
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {data.splits.map((split) => (
                      <tr
                        key={`split-${split.idx}`}
                        className="border-b border-gray-100 dark:border-gray-900"
                      >
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {split.idx + 1}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatDistance(split.distanceM)}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatDuration(split.durationS)}
                        </td>
                        <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                          {formatPace(split.paceS)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Weather Data */}
          {data.weather && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Weather
              </h2>
              <pre className="text-sm text-gray-700 dark:text-gray-300 bg-gray-50 dark:bg-gray-800 p-4 rounded overflow-x-auto">
                {JSON.stringify(data.weather, null, 2)}
              </pre>
            </div>
          )}

          {/* Route Map */}
          {data.route && (
            <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
              <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
                Route Map
              </h2>
              <WorkoutMap key={data.id} route={data.route} workoutId={data.id} />
            </div>
          )}
        </div>
      </main>
    </div>
  );
}

