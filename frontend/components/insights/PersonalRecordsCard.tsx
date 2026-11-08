'use client';

import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getBestEfforts } from '@/lib/api';
import {
  BEST_EFFORTS_RANGES,
  bestEffortsRangeRequests,
  joinPersonalRecords,
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
  const requests = useMemo(() => bestEffortsRangeRequests(range), [range]);

  // Request (1): the list. A failure here is the card's error state.
  const { data, isLoading, isError, error, refetch } = useQuery({
    queryKey: ['bestEfforts', 'list', requests.list],
    queryFn: () => getBestEfforts(requests.list),
  });

  // Request (2), ranged choices only, in parallel with (1): PRs set since the start, for New and "Was".
  // A failure here is silent; the list shows without badges.
  const prsParams = requests.prs;
  const prs = useQuery({
    queryKey: ['bestEfforts', 'prs', prsParams],
    queryFn: () => getBestEfforts(prsParams ?? {}),
    enabled: prsParams != null,
  });
  const prRows = prsParams != null && prs.isSuccess ? prs.data.distances : null;

  const rows = useMemo(
    () => (data ? joinPersonalRecords(data.distances, prRows) : []),
    [data, prRows]
  );
  const newDistances = useMemo(
    () => new Set(rows.filter((row) => row.isNew).map((row) => row.distance)),
    [rows]
  );

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
              <Button variant="secondary" size="sm" onClick={() => {
                  refetch();
                  if (prsParams != null) prs.refetch();
                }}>
                Retry
              </Button>
            }
          />
        ) : rows.length === 0 ? (
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
            efforts={rows}
            isNew={(item) => newDistances.has(item.distance)}
          />
        )}
      </Tabs>
    </Card>
  );
}
