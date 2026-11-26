'use client';

import { useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { type InsightsMetadata } from '@/lib/api';
import { statsQueries } from '@/lib/queries';
import { bestEffortsRangeRequests } from '@/lib/bestEffortsRange';
import { formatDate } from '@/lib/format';
import { Card } from '@/components/ui/Card';

function formatMonthYear(dateString: string): string {
  return new Date(dateString).toLocaleDateString('en-US', { year: 'numeric', month: 'short' });
}

function Stat({ label, value, detail }: { label: string; value: string; detail?: string }) {
  return (
    <div className="flex items-baseline justify-between gap-4 py-3 first:pt-0 last:pb-0 sm:block sm:px-6 sm:py-0 sm:first:pl-0 sm:last:pr-0">
      <dt className="text-xs font-medium uppercase tracking-wide text-muted">{label}</dt>
      <div className="text-right sm:mt-1 sm:text-left">
        <dd className="text-2xl font-bold text-ink tabular-nums sm:text-3xl">{value}</dd>
        {detail ? <dd className="text-xs text-muted">{detail}</dd> : null}
      </div>
    </div>
  );
}

/** The page's headline: how much running there is, since when, and how many records fell this year. */
export function SummaryStats({ metadata }: { metadata: InsightsMetadata }) {
  // Same request (and query key) as the Personal Records card's "This year" badges, so it is shared.
  const prsParams = useMemo(() => bestEffortsRangeRequests('thisYear').prs ?? {}, []);
  const prs = useQuery(statsQueries.bestEffortPrs(prsParams));
  const year = new Date().getFullYear();

  return (
    <Card>
      <dl className="grid grid-cols-1 divide-y divide-border sm:grid-cols-3 sm:divide-x sm:divide-y-0">
        <Stat label="Total runs" value={metadata.totalWorkouts.toLocaleString()} />
        <Stat
          label="Running since"
          value={metadata.firstWorkoutDate ? formatMonthYear(metadata.firstWorkoutDate) : '—'}
          detail={metadata.latestWorkoutDate ? `Latest run ${formatDate(metadata.latestWorkoutDate)}` : undefined}
        />
        <Stat
          label={`Records in ${year}`}
          value={prs.isSuccess ? prs.data.distances.length.toLocaleString() : '—'}
          detail={prs.isSuccess ? 'All-time bests set this year' : undefined}
        />
      </dl>
    </Card>
  );
}
