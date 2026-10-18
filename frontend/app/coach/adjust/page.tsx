'use client';

import { Suspense, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { startCoachAdjust, type CoachAdjustCategory } from '@/lib/api';
import { formatCoachDate } from '@/lib/coachWeek';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const CATEGORIES: { value: CoachAdjustCategory | ''; label: string }[] = [
  { value: '', label: 'No category' },
  { value: 'niggle', label: 'Niggle' },
  { value: 'illness', label: 'Illness' },
  { value: 'life', label: 'Life' },
  { value: 'other', label: 'Other' },
];

function localDateIso(d = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function AdjustPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const weekStartDate = searchParams.get('weekStartDate') ?? '';
  const [reason, setReason] = useState('');
  const [category, setCategory] = useState<CoachAdjustCategory | ''>('');
  const [error, setError] = useState<string | null>(null);

  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const referenceDate = localDateIso();

  const startMutation = useMutation({
    mutationFn: () =>
      startCoachAdjust({
        weekStartDate,
        reason,
        category: category || null,
        timezoneOffsetMinutes,
        referenceDate,
      }),
    onSuccess: (doc) => {
      setError(null);
      const params = new URLSearchParams({
        mode: 'adjust',
        weekStartDate: doc.weekStartDate,
        referenceDate,
      });
      router.push(`/coach/preflight?${params.toString()}`);
    },
    onError: (err: Error) => setError(err.message),
  });

  if (!weekStartDate) {
    return (
      <PageShell density="control" title="Ask for changes">
        <Card>
          <p className="text-sm text-danger">Missing week.</p>
          <Link href="/coach" className="text-sm underline mt-2 inline-block">
            Back to Week
          </Link>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell
      density="control"
      title="Ask for changes"
      subtitle={`Week of ${formatCoachDate(weekStartDate)}`}
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted underline">
            ← Week
          </Link>
        </div>
      }
    >
      <div className="w-full space-y-6">
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">What changed?</h2>
          <p className="text-sm text-muted mb-4">
            Required free-text reason. Only remaining days will be replanned; elapsed days stay
            historical. Nothing is sent until you confirm what your coach will see.
          </p>
          <label className="block text-sm text-muted space-y-1 mb-4">
            <span>Reason</span>
            <textarea
              className={fieldClass}
              rows={4}
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="e.g. Calf niggle flared after Tuesday — need easier remaining week"
              required
            />
          </label>
          <label className="block text-sm text-muted space-y-1 mb-4">
            <span>Category (optional)</span>
            <select
              className={fieldClass}
              value={category}
              onChange={(e) => setCategory(e.target.value as CoachAdjustCategory | '')}
            >
              {CATEGORIES.map((c) => (
                <option key={c.label} value={c.value}>
                  {c.label}
                </option>
              ))}
            </select>
          </label>
          {error && <p className="text-sm text-danger mb-3">{error}</p>}
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              disabled={!reason.trim() || startMutation.isPending}
              onClick={() => startMutation.mutate()}
            >
              {startMutation.isPending
                ? 'Starting...'
                : 'Continue to what your coach will see'}
            </Button>
            <Button href="/coach" variant="secondary">
              Cancel
            </Button>
          </div>
        </Card>
      </div>
    </PageShell>
  );
}

export default function CoachAdjustPage() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="Ask for changes">
          <Card>
            <p className="text-sm text-muted">Loading...</p>
          </Card>
        </PageShell>
      }
    >
      <AdjustPageInner />
    </Suspense>
  );
}
