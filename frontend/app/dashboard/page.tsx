'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useState } from 'react';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import { formatDate, formatDistance, formatDuration, formatPace, formatElevation } from '@/lib/format';
import WeeklyStatsWidget from '@/components/WeeklyStatsWidget';
import YearlyComparisonWidget from '@/components/YearlyComparisonWidget';

export default function DashboardPage() {
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workouts', { page, pageSize }],
    queryFn: () => getWorkouts({ page, pageSize }),
  });

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Dashboard
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400 mb-8">
              Loading workouts...
            </p>
          </div>
        </main>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Dashboard
            </h1>
            <div className="p-4 bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg">
              <p className="text-sm text-red-800 dark:text-red-200">
                Error: {error instanceof Error ? error.message : 'Failed to load workouts'}
              </p>
            </div>
          </div>
        </main>
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <div className="flex min-h-screen items-center justify-center bg-zinc-50 dark:bg-black">
        <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
          <div className="w-full">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Dashboard
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400 mb-8">
              No workouts found. <Link href="/import" className="text-blue-600 dark:text-blue-400 hover:underline">Import a GPX file</Link> to get started.
            </p>
          </div>
        </main>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <div className="flex items-center justify-between mb-4">
            <div>
              <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
                Dashboard
              </h1>
              <p className="text-lg text-gray-600 dark:text-gray-400">
                {data.totalCount} total workout{data.totalCount !== 1 ? 's' : ''}
              </p>
            </div>
            <Link
              href="/import"
              className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
            >
              ← Back to Import
            </Link>
          </div>
        </div>

        <div className="w-full grid grid-cols-1 md:grid-cols-2 gap-6 mb-8">
          <WeeklyStatsWidget />
          <YearlyComparisonWidget />
        </div>

        <div className="w-full overflow-x-auto">
          <table className="w-full border-collapse">
            <thead>
              <tr className="border-b border-gray-200 dark:border-gray-800">
                <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Date
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
                <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Elevation
                </th>
                <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Type
                </th>
                <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Splits
                </th>
                <th className="text-left py-3 px-4 text-sm font-semibold text-gray-700 dark:text-gray-300">
                  Route
                </th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((workout) => (
                <tr
                  key={workout.id}
                  className="border-b border-gray-100 dark:border-gray-900 hover:bg-gray-50 dark:hover:bg-gray-900/50 transition-colors"
                >
                  <td className="py-3 px-4">
                    <Link
                      href={`/dashboard/${workout.id}`}
                      className="text-blue-600 dark:text-blue-400 hover:underline font-medium"
                    >
                      {formatDate(workout.startedAt)}
                    </Link>
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {formatDistance(workout.distanceM)}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {formatDuration(workout.durationS)}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {formatPace(workout.avgPaceS)}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {workout.elevGainM !== null
                      ? formatElevation(workout.elevGainM)
                      : '—'}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {workout.runType || '—'}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {workout.splitsCount}
                  </td>
                  <td className="py-3 px-4 text-gray-700 dark:text-gray-300">
                    {workout.hasRoute ? '✓' : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {data.totalPages > 1 && (
          <div className="w-full mt-8 flex items-center justify-between">
            <div className="text-sm text-gray-600 dark:text-gray-400">
              Page {data.page} of {data.totalPages}
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                disabled={page === 1}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-700 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                Previous
              </button>
              <button
                onClick={() => setPage((p) => Math.min(data.totalPages, p + 1))}
                disabled={page === data.totalPages}
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-700 rounded-lg hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                Next
              </button>
            </div>
          </div>
        )}
      </main>
    </div>
  );
}

