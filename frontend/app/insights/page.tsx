'use client';

import { useQuery } from '@tanstack/react-query';
import { getInsights } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { ProgressBar } from '@/components/insights/ProgressBar';
import { WeatherExtremesCard } from '@/components/insights/WeatherExtremesCard';
import { PersonalRecordsCard } from '@/components/insights/PersonalRecordsCard';
import { DataCoverageCard } from '@/components/insights/DataCoverageCard';

function SkeletonCard({ rows }: { rows: number }) {
  return (
    <Card>
      <div aria-hidden="true">
        <div className="h-6 w-40 rounded bg-canvas animate-pulse mb-4" />
        <div className="space-y-2">
          {Array.from({ length: rows }).map((_, i) => (
            <div key={i} className="h-6 rounded bg-canvas animate-pulse" />
          ))}
        </div>
      </div>
    </Card>
  );
}

function UnlockInsightsCard({
  current,
  required,
  message,
}: {
  current: number;
  required: number;
  message?: string | null;
}) {
  const percent = required > 0 ? (current / required) * 100 : 0;
  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-2">Unlock Insights</h2>
      {message ? <p className="text-sm text-muted mb-4">{message}</p> : null}
      <div className="flex items-baseline justify-between text-sm mb-1">
        <span className="font-medium text-ink">Runs</span>
        <span className="text-muted tabular-nums">
          {current} of {required}
        </span>
      </div>
      <ProgressBar percent={percent} label="Runs toward insights" />
    </Card>
  );
}

function InsightsPageContent() {
  const { unitPreference } = useSettings();
  const { data, isLoading, isError, error, refetch, isFetching } = useQuery({
    queryKey: ['insights'],
    queryFn: getInsights,
  });

  return (
    <PageShell density="control" title="Insights">
      {isLoading ? (
        <div className="space-y-6" aria-busy="true" aria-label="Loading insights">
          <SkeletonCard rows={2} />
          <SkeletonCard rows={5} />
          <SkeletonCard rows={4} />
        </div>
      ) : isError || !data ? (
        <Card>
          <EmptyState
            title="Could not load insights"
            description={error instanceof Error ? error.message : 'Please try again.'}
            action={
              <Button variant="secondary" size="sm" onClick={() => refetch()} disabled={isFetching}>
                {isFetching ? 'Retrying…' : 'Retry'}
              </Button>
            }
          />
        </Card>
      ) : !data.sufficientData ? (
        <UnlockInsightsCard
          current={data.currentWorkouts ?? data.metadata.totalWorkouts}
          required={data.requiredWorkouts ?? data.metadata.minimumWorkoutsRequired}
          message={data.message}
        />
      ) : (
        <div className="space-y-6">
          <WeatherExtremesCard weather={data.weather} unitPreference={unitPreference} />
          <PersonalRecordsCard />
          <DataCoverageCard metadata={data.metadata} />
        </div>
      )}
    </PageShell>
  );
}

export default function InsightsPage() {
  return (
    <AuthGuard>
      <InsightsPageContent />
    </AuthGuard>
  );
}
