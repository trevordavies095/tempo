'use client';

import { Suspense, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { getCoachWeekPrivacyReceipt } from '@/lib/api';
import { formatCoachDate, formatWeekRangeLabel } from '@/lib/coachWeek';

function ReceiptPageInner() {
  const params = useParams();
  const weekId = typeof params.id === 'string' ? params.id : '';
  const [inspectOpen, setInspectOpen] = useState(false);

  const receiptQuery = useQuery({
    queryKey: ['coach-privacy-receipt', weekId],
    queryFn: () => getCoachWeekPrivacyReceipt(weekId),
    enabled: Boolean(weekId),
  });

  const prettyPayload = useMemo(() => {
    if (!receiptQuery.data?.redactedPayload) return '';
    try {
      return JSON.stringify(receiptQuery.data.redactedPayload, null, 2);
    } catch {
      return String(receiptQuery.data.redactedPayload);
    }
  }, [receiptQuery.data]);

  return (
    <PageShell
      density="control"
      title="What was sent"
      subtitle="Privacy receipt for this coach week"
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted underline">
            ← Week
          </Link>
        </div>
      }
    >
      <div className="w-full space-y-6">
        {receiptQuery.isLoading && (
          <Card>
            <p className="text-sm text-muted">Loading receipt...</p>
          </Card>
        )}
        {receiptQuery.isError && (
          <Card>
            <p className="text-sm text-danger">
              {receiptQuery.error instanceof Error
                ? receiptQuery.error.message
                : 'Failed to load receipt'}
            </p>
          </Card>
        )}
        {receiptQuery.data && (
          <>
            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Week</h2>
              <p className="text-sm text-muted">
                {formatCoachDate(receiptQuery.data.weekStartDate)} · {receiptQuery.data.status}
              </p>
              <p className="text-xs text-muted mt-2 font-mono">
                hash {receiptQuery.data.payloadHash}
              </p>
            </Card>
            {receiptQuery.data.recentTraining && receiptQuery.data.recentTraining.length > 0 && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Recent training sent</h2>
                <ul className="space-y-1 text-sm text-muted">
                  {receiptQuery.data.recentTraining.map((w) => (
                    <li key={w.weekStartDate}>
                      {formatWeekRangeLabel(w.weekStartDate, w.weekEndDate)}: {w.runCount} run
                      {w.runCount === 1 ? '' : 's'}
                    </li>
                  ))}
                </ul>
              </Card>
            )}
            <Card>
              <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
                <h2 className="text-lg font-semibold text-ink">Redacted payload</h2>
                <Button type="button" variant="secondary" onClick={() => setInspectOpen((o) => !o)}>
                  {inspectOpen ? 'Hide JSON' : 'Inspect JSON'}
                </Button>
              </div>
              {inspectOpen && (
                <pre className="overflow-auto rounded-tempo border border-border bg-canvas p-3 text-xs text-ink max-h-[28rem]">
                  {prettyPayload}
                </pre>
              )}
            </Card>
          </>
        )}
      </div>
    </PageShell>
  );
}

export default function CoachPrivacyReceiptPage() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="What was sent">
          <Card>
            <p className="text-sm text-muted">Loading...</p>
          </Card>
        </PageShell>
      }
    >
      <ReceiptPageInner />
    </Suspense>
  );
}
