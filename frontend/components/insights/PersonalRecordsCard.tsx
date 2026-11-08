'use client';

import { useQuery } from '@tanstack/react-query';
import { getBestEfforts } from '@/lib/api';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { BestEffortsTable } from '@/components/BestEffortsTable';

export function PersonalRecordsCard() {
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['bestEfforts'],
    queryFn: () => getBestEfforts(),
  });

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Personal Records</h2>
      {isLoading ? (
        <div className="space-y-2" aria-hidden="true">
          {Array.from({ length: 5 }).map((_, i) => (
            <div key={i} className="h-6 rounded bg-canvas animate-pulse" />
          ))}
        </div>
      ) : isError ? (
        <EmptyState
          title="Could not load personal records"
          description={error instanceof Error ? error.message : 'Please try again.'}
          action={
            <Button variant="secondary" size="sm" onClick={() => refetch()}>
              Retry
            </Button>
          }
        />
      ) : !data || data.distances.length === 0 ? (
        <EmptyState
          title="No personal records yet"
          description="Recalculate best efforts from the dashboard's Best Efforts card."
        />
      ) : (
        <BestEffortsTable efforts={data.distances} />
      )}
    </Card>
  );
}
