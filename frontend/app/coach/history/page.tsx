'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { getCoachWeeks } from '@/lib/api';
import { formatCoachDistance } from '@/lib/coachDistance';
import { formatWeekRangeLabel } from '@/lib/coachWeek';
import { useSettings } from '@/lib/settings';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import Pagination from '@/components/Pagination';

const FEEL_LABELS: Record<number, string> = {
  1: 'tough',
  2: 'off',
  3: 'okay',
  4: 'good',
  5: 'great',
};

function feelClause(feelScore: number | null): string | null {
  if (feelScore == null) {
    return null;
  }
  const label = FEEL_LABELS[feelScore];
  return label ? `felt ${label}` : `felt ${feelScore}/5`;
}

export default function CoachHistoryPage() {
  const { unitPreference } = useSettings();
  const [page, setPage] = useState(1);
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['coach-weeks', page, timezoneOffsetMinutes],
    queryFn: () =>
      getCoachWeeks({
        page,
        pageSize: 20,
        timezoneOffsetMinutes,
      }),
  });

  return (
    <PageShell density="control" title="History" subtitle="Committed weeks and what was sent">
      {isLoading && (
        <Card>
          <p className="text-sm text-muted">Loading weeks…</p>
        </Card>
      )}
      {isError && (
        <Card>
          <EmptyState
            title="Could not load history"
            description={error instanceof Error ? error.message : 'Failed to load weeks'}
          />
        </Card>
      )}
      {data && data.items.length === 0 && (
        <Card>
          <EmptyState
            title="No committed weeks yet"
            description="When you accept a plan, it will show up here with planned vs actual and what was sent."
          />
        </Card>
      )}
      {data && data.items.length > 0 && (
        <div className="space-y-3">
          {data.items.map((week) => {
            const feel = feelClause(week.feelScore);
            const distance = `${formatCoachDistance(week.actualDistanceM, unitPreference)} of ${formatCoachDistance(week.plannedDistanceM, unitPreference)}`;
            const parts = [
              formatWeekRangeLabel(week.weekStartDate, week.weekEndDate),
              feel,
              distance,
              week.isAdjustment ? 'adjusted' : null,
            ].filter(Boolean);

            return (
              <Link
                key={week.id}
                href={`/coach/history/${week.id}`}
                className="block rounded-tempo border border-border bg-raised px-4 py-3 hover:border-ink/30 transition-colors"
              >
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <p className="text-sm text-ink">{parts.join(' · ')}</p>
                  {week.status === 'archived' && (
                    <span className="text-xs uppercase tracking-wide text-muted">Archived</span>
                  )}
                </div>
              </Link>
            );
          })}
          <Pagination
            currentPage={data.page}
            totalPages={data.totalPages}
            onPageChange={setPage}
            className="pt-2"
          />
        </div>
      )}
    </PageShell>
  );
}
