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
import { calculateWeekFromInterval, generateIntervalFromWeek } from '@/utils/weekUtils';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

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

  const subtitle = !data || data.items.length === 0
    ? selectedWeek
      ? 'No workouts found for selected week'
      : 'No workouts found in the last 7 days'
    : selectedWeek
      ? `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} for selected week`
      : `${data.totalCount} workout${data.totalCount !== 1 ? 's' : ''} in the last 7 days`;

  if (isLoading) {
    return (
      <PageShell density="control" title="Dashboard">
        <EmptyState title="Loading workouts..." />
      </PageShell>
    );
  }

  if (isError) {
    return (
      <PageShell density="control" title="Dashboard">
        <Card>
          <EmptyState
            title="Failed to load workouts"
            description={error instanceof Error ? error.message : 'The dashboard could not load your recent Workouts.'}
          />
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell density="control" title="Dashboard" subtitle={subtitle}>
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
            <Card>
              <EmptyState
                title={
                  selectedWeek
                    ? 'No workouts found for the selected week'
                    : 'No workouts found in the last 7 days'
                }
                description="Import a GPX file to get started, or select a different week from the chart above."
                action={
                  <Link href="/import">
                    <Button>Import a GPX file</Button>
                  </Link>
                }
              />
            </Card>
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
    </PageShell>
  );
}

export default function DashboardPage() {
  return (
    <AuthGuard>
      <DashboardPageContent />
    </AuthGuard>
  );
}
