'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  applyCoachDraftWeek,
  getCoachDraftWeek,
  putCoachDraftWeek,
  type CoachDayType,
  type CoachWeeklyCommit,
  type CoachWeeklyCommitDay,
} from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import { useSettings, type UnitPreference } from '@/lib/settings';
import { formatDistance } from '@/lib/format';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const DAY_TYPES: CoachDayType[] = ['rest', 'easy', 'quality', 'long', 'race', 'other'];

function metersToDisplay(meters: number, unit: UnitPreference): string {
  const value = unit === 'imperial' ? meters / 1609.344 : meters / 1000;
  return value.toFixed(2);
}

function displayToMeters(value: string, unit: UnitPreference): number {
  const n = Number.parseFloat(value);
  if (!Number.isFinite(n) || n < 0) {
    return 0;
  }
  return unit === 'imperial' ? n * 1609.344 : n * 1000;
}

function normalizeCommit(raw: CoachWeeklyCommit): CoachWeeklyCommit {
  return {
    coachesNotes: raw.coachesNotes ?? '',
    days: (raw.days ?? []).map((d) => ({
      date: d.date,
      dayType: d.dayType,
      run: d.run
        ? {
            type: d.run.type,
            distanceM: d.run.distanceM ?? 0,
            structure: d.run.structure ?? null,
            paceOrHr: d.run.paceOrHr ?? null,
            shoeId: d.run.shoeId ?? null,
            notes: d.run.notes ?? null,
          }
        : null,
      strength: d.strength ?? null,
    })),
    safetyFlags: (raw.safetyFlags ?? []).map((f) => ({
      code: f.code ?? '',
      message: f.message ?? '',
      severity: f.severity ?? 'info',
      requiresResponse: Boolean(f.requiresResponse),
    })),
    memoryProposals: (raw.memoryProposals ?? []).map((p) => ({
      layer: p.layer ?? '',
      path: p.path ?? '',
      summary: p.summary ?? '',
    })),
    clarifyingQuestion: raw.clarifyingQuestion ?? null,
    callouts: (raw.callouts ?? []).slice(0, 5),
  };
}

function updateDay(
  commit: CoachWeeklyCommit,
  index: number,
  patch: Partial<CoachWeeklyCommitDay> & { runPatch?: Partial<NonNullable<CoachWeeklyCommitDay['run']>> }
): CoachWeeklyCommit {
  const days = commit.days.map((day, i) => {
    if (i !== index) {
      return day;
    }
    const { runPatch, ...dayPatch } = patch;
    let run = day.run;
    const nextType = (dayPatch.dayType ?? day.dayType) as CoachDayType;
    if (nextType === 'rest') {
      run = null;
    } else {
      const base = run ?? {
        type: nextType === 'other' ? 'easy' : nextType,
        distanceM: 0,
        structure: null,
        paceOrHr: null,
        shoeId: null,
        notes: null,
      };
      run = {
        ...base,
        ...runPatch,
        type: (runPatch?.type ?? base.type ?? nextType) as string,
      };
    }
    return { ...day, ...dayPatch, run };
  });
  return { ...commit, days };
}

function ReviewPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();
  const { unitPreference } = useSettings();
  const weekStartDate = searchParams.get('weekStartDate') ?? '';

  const [commit, setCommit] = useState<CoachWeeklyCommit | null>(null);
  const [acks, setAcks] = useState<Record<number, { acknowledged: boolean; response: string }>>({});
  const [acceptedMemory, setAcceptedMemory] = useState<Set<number>>(new Set());
  const [error, setError] = useState<string | null>(null);

  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeek(weekStartDate),
    enabled: showCoachNav && Boolean(weekStartDate),
  });

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  useEffect(() => {
    if (!draftQuery.data?.commit) {
      return;
    }
    const normalized = normalizeCommit(draftQuery.data.commit);
    setCommit(normalized);
    const initial: Record<number, { acknowledged: boolean; response: string }> = {};
    normalized.safetyFlags.forEach((_, i) => {
      initial[i] = { acknowledged: false, response: '' };
    });
    setAcks(initial);
    setAcceptedMemory(new Set());
  }, [draftQuery.data]);

  const distanceUnitLabel = unitPreference === 'imperial' ? 'mi' : 'km';

  const safetyReady = useMemo(() => {
    if (!commit) {
      return false;
    }
    return commit.safetyFlags.every((flag, i) => {
      const ack = acks[i];
      if (!ack?.acknowledged) {
        return false;
      }
      if (flag.requiresResponse && !ack.response.trim()) {
        return false;
      }
      return true;
    });
  }, [commit, acks]);

  const saveMutation = useMutation({
    mutationFn: async () => {
      if (!commit) {
        throw new Error('No draft loaded');
      }
      return putCoachDraftWeek(weekStartDate, commit);
    },
    onSuccess: () => {
      setError(null);
      queryClient.invalidateQueries({ queryKey: ['coach-draft-week', weekStartDate] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const applyMutation = useMutation({
    mutationFn: async () => {
      if (!commit) {
        throw new Error('No draft loaded');
      }
      return applyCoachDraftWeek({
        weekStartDate,
        commit,
        safetyAcknowledgments: commit.safetyFlags.map((_, i) => ({
          index: i,
          acknowledged: Boolean(acks[i]?.acknowledged),
          response: acks[i]?.response?.trim() || null,
        })),
        acceptedMemoryProposalIndexes: Array.from(acceptedMemory).sort((a, b) => a - b),
      });
    },
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-check-in'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      router.push('/coach');
    },
    onError: (err: Error) => setError(err.message),
  });

  if (settingsLoading || !isFetched) {
    return (
      <PageShell density="control" title="Review weekly plan">
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
      <PageShell density="control" title="Review weekly plan">
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
      title="Review weekly plan"
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
            Tempo AI Coach is not a doctor and does not provide medical advice. It offers training
            suggestions only.
          </div>
        </div>
      }
    >
      <div className="w-full space-y-6">
        {draftQuery.isLoading && (
          <Card>
            <p className="text-sm text-muted">Loading draft...</p>
          </Card>
        )}
        {draftQuery.isError && (
          <Card>
            <p className="text-sm text-danger">
              {draftQuery.error instanceof Error
                ? draftQuery.error.message
                : 'Failed to load draft'}
            </p>
            <Link href="/coach/preflight" className="text-sm underline mt-2 inline-block">
              Generate a plan
            </Link>
          </Card>
        )}

        {commit && (
          <>
            {commit.safetyFlags.length > 0 && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Safety flags</h2>
                <p className="text-sm text-muted mb-4">
                  Acknowledge each flag before applying. Flags that require a response need a short
                  note.
                </p>
                <ul className="space-y-4">
                  {commit.safetyFlags.map((flag, i) => (
                    <li
                      key={`${flag.code}-${i}`}
                      className="rounded-tempo border border-border p-3 space-y-2"
                    >
                      <div className="flex items-start gap-2">
                        <input
                          id={`safety-ack-${i}`}
                          type="checkbox"
                          className="mt-1"
                          checked={Boolean(acks[i]?.acknowledged)}
                          onChange={(e) =>
                            setAcks((prev) => ({
                              ...prev,
                              [i]: {
                                acknowledged: e.target.checked,
                                response: prev[i]?.response ?? '',
                              },
                            }))
                          }
                        />
                        <label htmlFor={`safety-ack-${i}`} className="text-sm text-ink">
                          <span className="font-medium uppercase tracking-wide text-xs text-muted">
                            {flag.severity || 'info'}
                            {flag.requiresResponse ? ' · response required' : ''}
                          </span>
                          <br />
                          {flag.message || flag.code}
                        </label>
                      </div>
                      {flag.requiresResponse && (
                        <textarea
                          className={fieldClass}
                          rows={2}
                          placeholder="Your response"
                          value={acks[i]?.response ?? ''}
                          onChange={(e) =>
                            setAcks((prev) => ({
                              ...prev,
                              [i]: {
                                acknowledged: prev[i]?.acknowledged ?? false,
                                response: e.target.value,
                              },
                            }))
                          }
                        />
                      )}
                    </li>
                  ))}
                </ul>
              </Card>
            )}

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-2">Coach&apos;s Notes</h2>
              <p className="text-sm text-ink whitespace-pre-wrap">{commit.coachesNotes}</p>
              {commit.callouts.length > 0 && (
                <div className="mt-4 flex flex-wrap gap-2">
                  {commit.callouts.map((chip, i) => (
                    <span
                      key={`${chip}-${i}`}
                      className="rounded-tempo border border-border px-2 py-1 text-xs text-muted"
                    >
                      {chip}
                    </span>
                  ))}
                </div>
              )}
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-4">Plan</h2>
              <div className="space-y-4">
                {commit.days.map((day, i) => (
                  <div
                    key={day.date}
                    className="rounded-tempo border border-border p-3 space-y-3"
                  >
                    <div className="flex flex-wrap items-center gap-3">
                      <p className="text-sm font-medium text-ink min-w-[7rem]">{day.date}</p>
                      <select
                        className={fieldClass + ' max-w-[10rem]'}
                        value={day.dayType}
                        onChange={(e) =>
                          setCommit((c) =>
                            c
                              ? updateDay(c, i, { dayType: e.target.value as CoachDayType })
                              : c
                          )
                        }
                      >
                        {DAY_TYPES.map((t) => (
                          <option key={t} value={t}>
                            {t}
                          </option>
                        ))}
                      </select>
                    </div>
                    {day.dayType !== 'rest' && day.run && (
                      <div className="grid gap-3 sm:grid-cols-2">
                        <label className="text-sm text-muted space-y-1">
                          <span>Distance ({distanceUnitLabel})</span>
                          <input
                            className={fieldClass}
                            type="number"
                            min={0}
                            step={0.1}
                            value={metersToDisplay(day.run.distanceM, unitPreference)}
                            onChange={(e) =>
                              setCommit((c) =>
                                c
                                  ? updateDay(c, i, {
                                      runPatch: {
                                        distanceM: displayToMeters(e.target.value, unitPreference),
                                      },
                                    })
                                  : c
                              )
                            }
                          />
                          <span className="text-xs">
                            {formatDistance(day.run.distanceM, unitPreference)}
                          </span>
                        </label>
                        <label className="text-sm text-muted space-y-1">
                          <span>Structure</span>
                          <input
                            className={fieldClass}
                            value={day.run.structure ?? ''}
                            onChange={(e) =>
                              setCommit((c) =>
                                c
                                  ? updateDay(c, i, {
                                      runPatch: { structure: e.target.value || null },
                                    })
                                  : c
                              )
                            }
                          />
                        </label>
                        <label className="text-sm text-muted space-y-1">
                          <span>Pace / HR</span>
                          <input
                            className={fieldClass}
                            value={day.run.paceOrHr ?? ''}
                            onChange={(e) =>
                              setCommit((c) =>
                                c
                                  ? updateDay(c, i, {
                                      runPatch: { paceOrHr: e.target.value || null },
                                    })
                                  : c
                              )
                            }
                          />
                        </label>
                        <label className="text-sm text-muted space-y-1">
                          <span>Notes</span>
                          <input
                            className={fieldClass}
                            value={day.run.notes ?? ''}
                            onChange={(e) =>
                              setCommit((c) =>
                                c
                                  ? updateDay(c, i, {
                                      runPatch: { notes: e.target.value || null },
                                    })
                                  : c
                              )
                            }
                          />
                        </label>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </Card>

            {commit.memoryProposals.length > 0 && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Memory proposals</h2>
                <p className="text-sm text-muted mb-4">
                  Rejected by default. Opt in only to lasting memory changes you want kept.
                </p>
                <ul className="space-y-3">
                  {commit.memoryProposals.map((proposal, i) => (
                    <li key={`${proposal.path}-${i}`} className="flex items-start gap-2">
                      <input
                        id={`memory-${i}`}
                        type="checkbox"
                        className="mt-1"
                        checked={acceptedMemory.has(i)}
                        onChange={(e) => {
                          setAcceptedMemory((prev) => {
                            const next = new Set(prev);
                            if (e.target.checked) {
                              next.add(i);
                            } else {
                              next.delete(i);
                            }
                            return next;
                          });
                        }}
                      />
                      <label htmlFor={`memory-${i}`} className="text-sm text-ink">
                        <span className="text-xs uppercase tracking-wide text-muted">
                          {proposal.layer}
                          {proposal.path ? ` · ${proposal.path}` : ''}
                        </span>
                        <br />
                        {proposal.summary}
                      </label>
                    </li>
                  ))}
                </ul>
              </Card>
            )}

            {commit.clarifyingQuestion && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Clarifying question</h2>
                <p className="text-sm text-muted mb-1">Advisory only — does not block apply.</p>
                <p className="text-sm text-ink">{commit.clarifyingQuestion}</p>
              </Card>
            )}

            {error && (
              <Card>
                <p className="text-sm text-danger">{error}</p>
              </Card>
            )}

            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                variant="secondary"
                disabled={saveMutation.isPending || applyMutation.isPending}
                onClick={() => saveMutation.mutate()}
              >
                {saveMutation.isPending ? 'Saving...' : 'Save edits'}
              </Button>
              <Button
                type="button"
                disabled={!safetyReady || saveMutation.isPending || applyMutation.isPending}
                onClick={() => applyMutation.mutate()}
              >
                {applyMutation.isPending ? 'Applying...' : 'Apply week'}
              </Button>
              <Link href="/coach">
                <Button type="button" variant="secondary">
                  Cancel
                </Button>
              </Link>
            </div>
          </>
        )}
      </div>
    </PageShell>
  );
}

function ReviewPageContent() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="Review weekly plan">
          <Card>
            <p className="text-sm text-muted">Loading...</p>
          </Card>
        </PageShell>
      }
    >
      <ReviewPageInner />
    </Suspense>
  );
}

export default function CoachReviewPage() {
  return (
    <AuthGuard>
      <ReviewPageContent />
    </AuthGuard>
  );
}
