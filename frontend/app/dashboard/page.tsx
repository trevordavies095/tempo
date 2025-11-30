'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useState, useEffect } from 'react';
import { getWorkouts, type WorkoutsListParams } from '@/lib/api';
import WeeklyStatsWidget from '@/components/WeeklyStatsWidget';
import RelativeEffortGraph from '@/components/RelativeEffortGraph';
import BestEffortsChart from '@/components/BestEffortsChart';
import WorkoutCard from '@/components/WorkoutCard';
import YearlyWeeklyChart from '@/components/YearlyWeeklyChart';
import Pagination from '@/components/Pagination';
import LoadingState from '@/components/LoadingState';
import ErrorState from '@/components/ErrorState';
import { calculateWeekFromInterval, generateIntervalFromWeek } from '@/utils/weekUtils';
import { AuthGuard } from '@/components/AuthGuard';

function DashboardPageContent() {
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

        if (interval) {
          const weekRange = calculateWeekFromInterval(interval, yearOffset);
          if (weekRange) {
            setSelectedWeek(weekRange);
          }
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
      const result = generateIntervalFromWeek(selectedWeek.weekStart);
      if (result) {
        window.location.hash = `interval?interval=${result.interval}&interval_type=week&year_offset=${result.yearOffset}`;
      }
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
  } else {
    // Apply default 7-day filter when no week is selected
    const now = new Date();
    const sevenDaysAgo = new Date(now);
    sevenDaysAgo.setDate(now.getDate() - 7);
    queryParams.startDate = sevenDaysAgo.toISOString().split('T')[0];
    queryParams.endDate = now.toISOString().split('T')[0];
  }

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
            <LoadingState message="Loading workouts..." className="mb-8" />
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
              <ErrorState error={error} message="Failed to load workouts" />
            </div>
          </div>
        </main>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-6xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <div className="mb-4">
            <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
              Dashboard
            </h1>
            <p className="text-lg text-gray-600 dark:text-gray-400">
              {!data || data.items.length === 0
                ? selectedWeek
                  ? 'No workouts found for selected week'
                  : 'No workouts found in the last 7 days'
                : selectedWeek
                  ? `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} for selected week`
                  : `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} in the last 7 days`}
            </p>
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
            <RelativeEffortGraph />
            <BestEffortsChart />
          </div>
          <div className="flex-1 min-w-0">
            {!data || data.items.length === 0 ? (
              <div className="p-8 text-center text-gray-600 dark:text-gray-400">
                <p className="mb-4">
                  {selectedWeek
                    ? 'No workouts found for the selected week.'
                    : 'No workouts found in the last 7 days.'}
                </p>
                <p>
                  <Link href="/import" className="text-blue-600 dark:text-blue-400 hover:underline">
                    Import a GPX file
                  </Link>{' '}
                  to get started, or select a different week from the chart above.
                </p>
              </div>
            ) : (
              <>
                <div className="flex flex-col gap-4">
                  {data.items.map((workout) => (
                    <WorkoutCard key={workout.id} workout={workout} />
                  ))}
                </div>
                <Pagination
                  currentPage={data.page}
                  totalPages={data.totalPages}
                  onPageChange={setPage}
                  className="w-full mt-8"
                />
              </>
            )}
          </div>
        </div>
      </main>
    </div>
  );
}

export default function DashboardPage() {
  return (
    <AuthGuard>
      <DashboardPageContent />
    </AuthGuard>
  );
}
