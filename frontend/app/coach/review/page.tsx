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
  type CoachMemoryChecklistItem,
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
    layer1Patches: (raw.layer1Patches ?? []).map((p) => ({
      fieldPath: p.fieldPath ?? '',
      oldValue: p.oldValue ?? null,
      newValue: p.newValue ?? null,
      rationale: p.rationale ?? '',
      validity: p.validity
        ? { ok: Boolean(p.validity.ok), reason: p.validity.reason ?? null }
        : null,
    })),
    layer2EpisodeDrafts: (raw.layer2EpisodeDrafts ?? []).map((d) => ({
      date: d.date ?? '',
      category: d.category ?? '',
      summary: d.summary ?? '',
      sourceWeekEnding: d.sourceWeekEnding ?? null,
      validity: d.validity
        ? { ok: Boolean(d.validity.ok), reason: d.validity.reason ?? null }
        : null,
    })),
    layer3RuleProposals: (raw.layer3RuleProposals ?? []).map((p) => ({
      operation: p.operation ?? '',
      ruleText: p.ruleText ?? null,
      evidence: p.evidence ?? [],
      targetRuleId: p.targetRuleId ?? null,
      targetLayer: p.targetLayer ?? '',
      validity: p.validity
        ? { ok: Boolean(p.validity.ok), reason: p.validity.reason ?? null }
        : null,
    })),
    clarifyingQuestion: raw.clarifyingQuestion ?? null,
    callouts: (raw.callouts ?? []).slice(0, 5),
  };
}

function buildMemoryChecklist(commit: CoachWeeklyCommit): CoachMemoryChecklistItem[] {
  const items: CoachMemoryChecklistItem[] = [];
  commit.layer1Patches.forEach((p, i) => {
    items.push({
      channel: 'l1',
      channelIndex: i,
      label: `L1 · ${p.fieldPath}`,
      detail: p.rationale || `${String(p.oldValue)} → ${String(p.newValue)}`,
      validityOk: p.validity?.ok !== false,
      validityReason: p.validity?.reason ?? null,
    });
  });
  commit.layer2EpisodeDrafts.forEach((d, i) => {
    items.push({
      channel: 'l2',
      channelIndex: i,
      label: `L2 · ${d.category} · ${d.date}`,
      detail: d.summary,
      validityOk: d.validity?.ok !== false,
      validityReason: d.validity?.reason ?? null,
    });
  });
  commit.layer3RuleProposals.forEach((p, i) => {
    items.push({
      channel: 'l3',
      channelIndex: i,
      label: `L3 · ${p.operation} · ${p.targetLayer}`,
      detail: p.ruleText || p.targetRuleId || '',
      validityOk: p.validity?.ok !== false,
      validityReason: p.validity?.reason ?? null,
    });
  });
  return items;
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
  const [acceptedL1, setAcceptedL1] = useState<Set<number>>(new Set());
  const [acceptedEpisodes, setAcceptedEpisodes] = useState<Set<number>>(new Set());
  const [acceptedRules, setAcceptedRules] = useState<Set<number>>(new Set());
  const [archiveNotesAsRecap, setArchiveNotesAsRecap] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeek(weekStartDate),
    enabled: showCoachNav && Boolean(weekStartDate),
  });

  const lockedThroughDate = draftQuery.data?.lockedThroughDate ?? null;
  const isAdjustment = Boolean(draftQuery.data?.isAdjustment);

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
    setAcceptedL1(new Set());
    setAcceptedEpisodes(new Set());
    setAcceptedRules(new Set());
    setArchiveNotesAsRecap(false);
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
        acceptedLayer1PatchIndexes: Array.from(acceptedL1).sort((a, b) => a - b),
        acceptedEpisodeIndexes: Array.from(acceptedEpisodes).sort((a, b) => a - b),
        acceptedRuleProposalIndexes: Array.from(acceptedRules).sort((a, b) => a - b),
        archiveNotesAsRecap,
      });
    },
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-committed-week'] });
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
      title={isAdjustment ? 'Review adjust draft' : 'Review weekly plan'}
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
            {isAdjustment
              ? ' Elapsed days are locked historical; only remaining days are editable.'
              : ''}
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
                {commit.days.map((day, i) => {
                  const locked =
                    Boolean(lockedThroughDate) && day.date <= (lockedThroughDate as string);
                  return (
                  <div
                    key={day.date}
                    className={`rounded-tempo border border-border p-3 space-y-3 ${
                      locked ? 'opacity-60 bg-canvas' : ''
                    }`}
                  >
                    <div className="flex flex-wrap items-center gap-3">
                      <p className="text-sm font-medium text-ink min-w-[7rem]">{day.date}</p>
                      {locked && (
                        <span className="rounded-tempo border border-border px-2 py-0.5 text-xs text-muted">
                          Locked · Adjustment
                        </span>
                      )}
                      <select
                        className={fieldClass + ' max-w-[10rem]'}
                        value={day.dayType}
                        disabled={locked}
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
                            disabled={locked}
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
                            disabled={locked}
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
                            disabled={locked}
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
                            disabled={locked}
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
                  );
                })}
              </div>
            </Card>

            <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Memory proposals</h2>
                <p className="text-sm text-muted mb-4">
                  Rejected by default. Opt in only to lasting memory changes you want kept.
                  Invalid proposals cannot be accepted.
                </p>
                <ul className="space-y-3">
                  {buildMemoryChecklist(commit).map((item) => {
                    const key = `${item.channel}-${item.channelIndex}`;
                    const accepted =
                      item.channel === 'l1'
                        ? acceptedL1.has(item.channelIndex)
                        : item.channel === 'l2'
                          ? acceptedEpisodes.has(item.channelIndex)
                          : acceptedRules.has(item.channelIndex);
                    const setAccepted =
                      item.channel === 'l1'
                        ? setAcceptedL1
                        : item.channel === 'l2'
                          ? setAcceptedEpisodes
                          : setAcceptedRules;
                    return (
                      <li key={key} className="flex items-start gap-2">
                        <input
                          id={`memory-${key}`}
                          type="checkbox"
                          className="mt-1"
                          disabled={!item.validityOk}
                          checked={accepted}
                          onChange={(e) => {
                            setAccepted((prev) => {
                              const next = new Set(prev);
                              if (e.target.checked) {
                                next.add(item.channelIndex);
                              } else {
                                next.delete(item.channelIndex);
                              }
                              return next;
                            });
                          }}
                        />
                        <label
                          htmlFor={`memory-${key}`}
                          className={`text-sm ${item.validityOk ? 'text-ink' : 'text-muted'}`}
                        >
                          <span className="text-xs uppercase tracking-wide text-muted">
                            {item.label}
                          </span>
                          <br />
                          {item.detail}
                          {!item.validityOk && item.validityReason && (
                            <>
                              <br />
                              <span className="text-xs text-danger">{item.validityReason}</span>
                            </>
                          )}
                        </label>
                      </li>
                    );
                  })}
                </ul>
                <label className="mt-4 flex items-start gap-2 text-sm text-ink">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={archiveNotesAsRecap}
                    onChange={(e) => setArchiveNotesAsRecap(e.target.checked)}
                  />
                  <span>
                    Archive this week&apos;s Coach&apos;s Notes to memory
                    <span className="block text-xs text-muted">
                      Creates a recap episode from coachesNotes (default off).
                    </span>
                  </span>
                </label>
              </Card>

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
