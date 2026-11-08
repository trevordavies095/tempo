'use client';

import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getBestEfforts } from '@/lib/api';
import {
  BEST_EFFORTS_RANGES,
  bestEffortsRangeParams,
  isNewRecord,
  type BestEffortsRange,
} from '@/lib/bestEffortsRange';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Tabs } from '@/components/ui/Tabs';
import { BestEffortsTable } from '@/components/BestEffortsTable';

export function PersonalRecordsCard() {
  // Local to the card: not persisted, not in the URL.
  const [range, setRange] = useState<BestEffortsRange>('allTime');
  const params = useMemo(() => bestEffortsRangeParams(range), [range]);

  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['bestEfforts', range, params],
    queryFn: () => getBestEfforts(params),
  });

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Personal Records</h2>
      <Tabs items={BEST_EFFORTS_RANGES} value={range} onChange={setRange} aria-label="Personal records range">
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
          range === 'allTime' ? (
            <EmptyState
              title="No personal records yet"
              description="Recalculate best efforts from the dashboard's Best Efforts card."
            />
          ) : (
            <EmptyState title="No records in this range" />
          )
        ) : (
          <BestEffortsTable
            efforts={data.distances}
            isNew={(item) => isNewRecord(item, range, params)}
          />
        )}
      </Tabs>
    </Card>
  );
}
