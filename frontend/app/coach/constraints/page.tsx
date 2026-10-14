'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  ConstraintFormFields,
  emptyConstraintDraft,
  type ConstraintDraft,
} from '@/components/coach/ConstraintFormFields';
import {
  COACH_WEEKDAY_LABELS,
  createCoachConstraint,
  listCoachConstraints,
  retireCoachConstraint,
  updateCoachConstraint,
  type CoachConstraint,
  type CoachConstraintRequest,
} from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';

function toRequest(c: CoachConstraint): CoachConstraintRequest {
  return {
    kind: c.kind as CoachConstraintRequest['kind'],
    startDate: c.startDate,
    endDate: c.endDate,
    distance: c.distance,
    intent: c.intent as CoachConstraintRequest['intent'],
    preset: c.preset as CoachConstraintRequest['preset'],
    weekdays: c.weekdays,
    note: c.note,
  };
}

function describeConstraint(c: CoachConstraint): string {
  if (c.kind === 'secondary_race') {
    return `B-race ${c.distance} on ${c.startDate} (${c.intent})`;
  }
  if (c.kind === 'blackout') {
    const range =
      c.endDate && c.endDate !== c.startDate
        ? `${c.startDate}–${c.endDate}`
        : c.startDate;
    return `Blackout ${range}${c.note ? ` — ${c.note}` : ''}`;
  }
  const days = (c.weekdays ?? [])
    .map((d) => COACH_WEEKDAY_LABELS[d] ?? String(d))
    .join(', ');
  return `${c.preset}: ${days}${c.note ? ` — ${c.note}` : ''}`;
}

function ConstraintsPageContent() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();
  const [showRetired, setShowRetired] = useState(false);
  const [draft, setDraft] = useState<ConstraintDraft>(emptyConstraintDraft());
  const [editingId, setEditingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const listQuery = useQuery({
    queryKey: ['coach-constraints', showRetired ? 'all' : 'active'],
    queryFn: () => listCoachConstraints(showRetired ? 'all' : 'active'),
    enabled: showCoachNav,
  });

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (editingId) {
        return updateCoachConstraint(editingId, draft);
      }
      return createCoachConstraint(draft);
    },
    onSuccess: async () => {
      setError(null);
      setEditingId(null);
      setDraft(emptyConstraintDraft());
      await queryClient.invalidateQueries({ queryKey: ['coach-constraints'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const retireMutation = useMutation({
    mutationFn: (id: string) => retireCoachConstraint(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['coach-constraints'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  if (settingsLoading || !isFetched) {
    return (
      <PageShell density="control" title="Non-negotiables">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  if (!showCoachNav) return null;

  const rows = listQuery.data ?? [];

  return (
    <PageShell
      density="control"
      title="Non-negotiables"
      subtitle="B-races, blackouts, standing schedule"
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Coach
          </Link>
        </div>
      }
    >
      <div className="w-full max-w-2xl space-y-6">
        <Card>
          <div className="flex items-center justify-between gap-2 mb-4">
            <h2 className="text-lg font-semibold text-ink">
              {editingId ? 'Edit constraint' : 'Add constraint'}
            </h2>
            {editingId && (
              <Button
                type="button"
                variant="secondary"
                onClick={() => {
                  setEditingId(null);
                  setDraft(emptyConstraintDraft());
                }}
              >
                Cancel edit
              </Button>
            )}
          </div>
          <ConstraintFormFields value={draft} onChange={setDraft} />
          <div className="mt-4 flex flex-wrap gap-2">
            <Button
              type="button"
              onClick={() => saveMutation.mutate()}
              disabled={saveMutation.isPending}
            >
              {saveMutation.isPending
                ? 'Saving...'
                : editingId
                  ? 'Save changes'
                  : 'Add non-negotiable'}
            </Button>
          </div>
          {error && <p className="text-sm text-danger mt-2">{error}</p>}
        </Card>

        <Card>
          <div className="flex items-center justify-between gap-2 mb-4">
            <h2 className="text-lg font-semibold text-ink">Your constraints</h2>
            <label className="flex items-center gap-2 text-sm text-muted">
              <input
                type="checkbox"
                checked={showRetired}
                onChange={(e) => setShowRetired(e.target.checked)}
              />
              Show retired
            </label>
          </div>
          {listQuery.isLoading ? (
            <p className="text-sm text-muted">Loading...</p>
          ) : listQuery.isError ? (
            <p className="text-sm text-danger">
              {listQuery.error instanceof Error
                ? listQuery.error.message
                : 'Failed to load constraints'}
            </p>
          ) : rows.length === 0 ? (
            <p className="text-sm text-muted">No constraints yet.</p>
          ) : (
            <ul className="space-y-3">
              {rows.map((c) => (
                <li
                  key={c.id}
                  className="flex flex-wrap items-start justify-between gap-2 border-b border-border pb-3 last:border-0"
                >
                  <div>
                    <p className="text-sm text-ink">
                      {describeConstraint(c)}
                      {c.isRetired ? (
                        <span className="text-muted"> (retired)</span>
                      ) : null}
                    </p>
                  </div>
                  {!c.isRetired && (
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="secondary"
                        onClick={() => {
                          setEditingId(c.id);
                          setDraft(toRequest(c));
                          setError(null);
                        }}
                      >
                        Edit
                      </Button>
                      <Button
                        type="button"
                        variant="secondary"
                        onClick={() => retireMutation.mutate(c.id)}
                        disabled={retireMutation.isPending}
                      >
                        Retire
                      </Button>
                    </div>
                  )}
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>
    </PageShell>
  );
}

export default function CoachConstraintsPage() {
  return (
    <AuthGuard>
      <ConstraintsPageContent />
    </AuthGuard>
  );
}
