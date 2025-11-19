'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useState, useEffect } from 'react';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import WeeklyStatsWidget from '@/components/WeeklyStatsWidget';
import WorkoutCard from '@/components/WorkoutCard';
import YearlyWeeklyChart from '@/components/YearlyWeeklyChart';

export default function DashboardPage() {
  const [page, setPage] = useState(1);
  const [pageSize] = useState(20);
  const [selectedPeriodEndDate, setSelectedPeriodEndDate] = useState<string | null>(null);
  const [selectedWeek, setSelectedWeek] = useState<{ weekStart: string; weekEnd: string } | null>(null);

  // Parse URL hash on mount
  useEffect(() => {
    if (typeof window !== 'undefined') {
      const hash = window.location.hash;
      if (hash.startsWith('#interval')) {
        const params = new URLSearchParams(hash.substring(10)); // Remove '#interval?'
        const interval = params.get('interval');
        const yearOffset = parseInt(params.get('year_offset') || '0', 10);

        if (interval && interval.length === 6) {
          // Format: YYYYWW (e.g., 202516)
          // Calculate the week from the interval (for backwards compatibility)
          const year = parseInt(interval.substring(0, 4), 10) - yearOffset;
          const weekNum = parseInt(interval.substring(4, 6), 10);

          // Calculate week boundaries from the last 52 weeks
          // Week 52 is the most recent complete week
          const today = new Date();
          const daysSinceMonday = ((today.getDay() - 1 + 7) % 7);
          const mostRecentMonday = new Date(today);
          mostRecentMonday.setDate(today.getDate() - daysSinceMonday);
          
          let week52End = new Date(mostRecentMonday);
          week52End.setDate(mostRecentMonday.getDate() + 6);
          if (daysSinceMonday === 0) {
            week52End.setDate(mostRecentMonday.getDate() - 1);
          }
          
          const week52Start = new Date(week52End);
          week52Start.setDate(week52End.getDate() - 6);
          
          // Calculate the week start for the given week number
          const weekStart = new Date(week52Start);
          weekStart.setDate(week52Start.getDate() - (52 - weekNum) * 7);
          const weekEnd = new Date(weekStart);
          weekEnd.setDate(weekStart.getDate() + 6);

          setSelectedWeek({
            weekStart: weekStart.toISOString().split('T')[0],
            weekEnd: weekEnd.toISOString().split('T')[0],
          });
        }
      }
    }
  }, []);

  // Reset page and clear week selection when period changes
  useEffect(() => {
    setPage(1);
    setSelectedWeek(null);
  }, [selectedPeriodEndDate]);

  // Reset page when week selection changes
  useEffect(() => {
    setPage(1);
  }, [selectedWeek]);

  // Update URL hash when week is selected
  useEffect(() => {
    if (selectedWeek && typeof window !== 'undefined') {
      const weekStartDate = new Date(selectedWeek.weekStart);
      const year = weekStartDate.getFullYear();
      
      // Calculate week number (1-52) for the last 52 weeks
      // Match backend logic: Week 52 is the most recent complete week
      const today = new Date();
      today.setHours(0, 0, 0, 0);
      const daysSinceMonday = ((today.getDay() - 1 + 7) % 7);
      const mostRecentMonday = new Date(today);
      mostRecentMonday.setDate(today.getDate() - daysSinceMonday);
      
      let week52End = new Date(mostRecentMonday);
      week52End.setDate(mostRecentMonday.getDate() + 6);
      if (daysSinceMonday === 0) {
        // If today is Monday, week 52 ended yesterday (last Sunday)
        week52End.setDate(mostRecentMonday.getDate() - 1);
      }
      
      const week52Start = new Date(week52End);
      week52Start.setDate(week52End.getDate() - 6);
      
      // Week 1 starts 51 weeks before week 52
      const week1Start = new Date(week52Start);
      week1Start.setDate(week52Start.getDate() - 51 * 7);
      
      // Calculate which week number (1-52) the selected week is
      const diffTime = weekStartDate.getTime() - week1Start.getTime();
      const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));
      const weekNum = Math.floor(diffDays / 7) + 1;

      const interval = `${year}${Math.min(52, Math.max(1, weekNum)).toString().padStart(2, '0')}`;
      const yearOffset = new Date().getFullYear() - year;
      window.location.hash = `interval?interval=${interval}&interval_type=week&year_offset=${yearOffset}`;
    } else if (!selectedWeek && typeof window !== 'undefined') {
      // Clear hash when no week is selected
      window.location.hash = '';
    }
  }, [selectedWeek]);

  // Build query params based on selected week
  const queryParams: WorkoutsListParams = {
    page,
    pageSize,
  };

  // If a week is selected, filter by that week; otherwise use default 7-day filter
  if (selectedWeek) {
    queryParams.startDate = selectedWeek.weekStart;
    queryParams.endDate = selectedWeek.weekEnd;
  }
  // If no week selected, backend will apply default 7-day filter

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workouts', queryParams],
    queryFn: () => getWorkouts(queryParams),
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
                {selectedWeek
                  ? `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} for selected week`
                  : `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} in the last 7 days`}
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

        <div className="w-full mb-8">
          <YearlyWeeklyChart
            selectedPeriodEndDate={selectedPeriodEndDate}
            onPeriodChange={setSelectedPeriodEndDate}
            selectedWeek={selectedWeek}
            onWeekSelect={setSelectedWeek}
          />
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

