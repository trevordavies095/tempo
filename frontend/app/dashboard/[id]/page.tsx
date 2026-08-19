'use client';

import { useQuery } from '@tanstack/react-query';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { getWorkout, getSimilarRoutes } from '@/lib/api';
import WorkoutDetailHeader from '@/components/WorkoutDetailHeader';
import { ActivityDetailTabs } from '@/components/ActivityDetailTabs';
import { RouteComparisonTab } from '@/components/RouteComparisonTab';
import { WorkoutOverviewPanel } from '@/components/WorkoutOverviewPanel';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { EmptyState } from '@/components/ui/EmptyState';

function WorkoutOverviewPageContent() {
  const params = useParams();
  const id = params.id as string;

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workout', id],
    queryFn: () => getWorkout(id),
  });

  const { data: similarRoutes, isLoading: isLoadingSimilarRoutes } = useQuery({
    queryKey: ['similar-routes', id],
    queryFn: () => getSimilarRoutes(id),
    staleTime: 5 * 60 * 1000,
    gcTime: 10 * 60 * 1000,
    retry: 1,
    refetchOnWindowFocus: false,
    enabled: !!id && !!data?.route,
  });

  const hasMatchedRuns = !!(similarRoutes && similarRoutes.length > 0);
  const backLink = (
    <Link href="/dashboard" className="text-sm text-muted hover:text-ink">
      ← Back to Dashboard
    </Link>
  );

  if (isLoading) {
    return (
      <PageShell density="overview" title="Workout overview" subtitle="Loading workout…">
        <p className="text-muted">Loading workout…</p>
      </PageShell>
    );
  }

  if (isError) {
    const isNotFound = error instanceof Error && error.message === 'Workout not found';
    return (
      <PageShell density="overview" title="Workout overview" leading={backLink}>
        <EmptyState
          title={isNotFound ? 'Workout not found' : 'Could not load workout'}
          description={
            isNotFound
              ? 'This Workout may have been deleted.'
              : error instanceof Error
                ? error.message
                : 'Failed to load workout'
          }
        />
      </PageShell>
    );
  }

  if (!data) {
    return null;
  }

  return (
    <PageShell density="overview" title="Workout overview" leading={backLink}>
      <WorkoutDetailHeader workout={data} />

      <ActivityDetailTabs
        showComparisonTab={hasMatchedRuns}
        isLoadingSimilarRoutes={isLoadingSimilarRoutes}
        overviewContent={<WorkoutOverviewPanel workout={data} />}
        comparisonContent={<RouteComparisonTab workoutId={id} currentWorkout={data} />}
      />
    </PageShell>
  );
}

export default function WorkoutOverviewPage() {
  return (
    <AuthGuard>
      <WorkoutOverviewPageContent />
    </AuthGuard>
  );
}
