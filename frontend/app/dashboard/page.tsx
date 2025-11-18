'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useState } from 'react';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import WeeklyStatsWidget from '@/components/WeeklyStatsWidget';
import WorkoutCard from '@/components/WorkoutCard';

export default function DashboardPage() {
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);

  // Backend applies default 7-day filter when no dates provided
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
              No workouts found in the last 7 days. <Link href="/import" className="text-blue-600 dark:text-blue-400 hover:underline">Import a GPX file</Link> to get started.
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
                {data.totalCount} workout{data.totalCount !== 1 ? 's' : ''} in the last 7 days
              </p>
            </div>
            <div className="flex gap-4">
              <Link
                href="/settings"
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Settings
              </Link>
              <Link
                href="/import"
                className="px-4 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 hover:text-gray-900 dark:hover:text-gray-100"
              >
                Import
              </Link>
            </div>
          </div>
        </div>

        <div className="w-full flex flex-col md:flex-row gap-6 mb-8">
          <div className="flex flex-col gap-6 md:w-80 flex-shrink-0">
            <WeeklyStatsWidget />
          </div>
          <div className="flex-1 min-w-0">
            <div className="flex flex-col gap-4">
              {data.items.map((workout) => (
                <WorkoutCard key={workout.id} workout={workout} />
              ))}
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
          </div>
        </div>
      </main>
    </div>
  );
}

