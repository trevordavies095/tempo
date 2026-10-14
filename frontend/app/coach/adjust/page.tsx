'use client';

import { Suspense, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { startCoachAdjust, type CoachAdjustCategory } from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';

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
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();
  const weekStartDate = searchParams.get('weekStartDate') ?? '';
  const [reason, setReason] = useState('');
  const [category, setCategory] = useState<CoachAdjustCategory | ''>('');
  const [error, setError] = useState<string | null>(null);

  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const referenceDate = localDateIso();

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

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

  if (settingsLoading || !isFetched) {
    return (
      <PageShell density="control" title="Adjust week">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  if (!showCoachNav) {
    return null;
  }

  if (!weekStartDate) {
    return (
      <PageShell density="control" title="Adjust week">
        <Card>
          <p className="text-sm text-danger">Missing weekStartDate.</p>
          <Link href="/coach" className="text-sm underline mt-2 inline-block">
            Back to Coach
          </Link>
        </Card>
      </PageShell>
    );
  }

  return (
    <PageShell
      density="control"
      title="Adjust week"
      subtitle={`Week of ${weekStartDate}`}
      leading={
        <div className="space-y-3 mb-4">
          <Link href="/coach" className="text-sm text-muted underline">
            ← Coach
          </Link>
          <div
            className="rounded-tempo border border-border bg-canvas p-4 text-sm text-muted"
            role="note"
          >
            Tempo AI Coach is not a doctor and does not provide medical advice. For severe niggles it
            may suggest backing off or referring out — it will not diagnose. Starting Adjust does not
            call Anthropic until you complete privacy pre-flight and generate.
          </div>
        </div>
      }
    >
      <div className="w-full space-y-6">
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">Why are you adjusting?</h2>
          <p className="text-sm text-muted mb-4">
            Required free-text reason. Only remaining days will be replanned; elapsed days stay
            historical.
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
              {startMutation.isPending ? 'Starting...' : 'Continue to privacy pre-flight'}
            </Button>
            <Link href="/coach">
              <Button type="button" variant="secondary">
                Cancel
              </Button>
            </Link>
          </div>
        </Card>
      </div>
    </PageShell>
  );
}

function AdjustPageContent() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="Adjust week">
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

export default function CoachAdjustPage() {
  return (
    <AuthGuard>
      <AdjustPageContent />
    </AuthGuard>
  );
}
