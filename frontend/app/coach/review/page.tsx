'use client';

import { Suspense, useEffect, useMemo, useState, type ReactNode } from 'react';
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
import {
  formatCoachDistance,
  normalizePlannedDistanceM,
  stripRedundantLeadingDistance,
} from '@/lib/coachDistance';
import {
  categoryLabel,
  formatLayer1PatchPrimary,
  operationLabel,
  shouldShowMemoryCard,
  targetLayerLabel,
} from '@/lib/coachMemoryDisplay';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const DAY_TYPES: CoachDayType[] = ['rest', 'easy', 'quality', 'long', 'race', 'other'];

function metersToDisplay(meters: number, unit: UnitPreference): string {
  const normalized = normalizePlannedDistanceM(meters);
  const value = unit === 'imperial' ? normalized / 1609.344 : normalized / 1000;
  return Number.isInteger(value) || Math.abs(value - Math.round(value)) < 1e-9
    ? String(Math.round(value))
    : value.toFixed(2);
}

function displayToMeters(value: string, unit: UnitPreference): number {
  const n = Number.parseFloat(value);
  if (!Number.isFinite(n) || n < 0) {
    return 0;
  }
  return normalizePlannedDistanceM(unit === 'imperial' ? n * 1609.344 : n * 1000);
}

function normalizeCommit(raw: CoachWeeklyCommit): CoachWeeklyCommit {
  return {
    coachesNotes: raw.coachesNotes ?? '',
    days: (raw.days ?? []).map((d) => ({
      date: d.date,
      dayType: d.dayType,
      run: d.run
        ? (() => {
            const distanceM = normalizePlannedDistanceM(d.run.distanceM ?? 0);
            return {
              type: d.run.type,
              distanceM,
              structure: stripRedundantLeadingDistance(d.run.structure, distanceM),
              paceOrHr: d.run.paceOrHr ?? null,
              shoeId: d.run.shoeId ?? null,
              notes: d.run.notes ?? null,
            };
          })()
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
      currentRuleText: p.currentRuleText ?? null,
      validity: p.validity
        ? { ok: Boolean(p.validity.ok), reason: p.validity.reason ?? null }
        : null,
    })),
    clarifyingQuestion: raw.clarifyingQuestion ?? null,
    callouts: (raw.callouts ?? []).slice(0, 5),
    memoryProposals: raw.memoryProposals ?? null,
  };
}

function MemoryProposalRow({
  id,
  validityOk,
  validityReason,
  accepted,
  onToggle,
  children,
}: {
  id: string;
  validityOk: boolean;
  validityReason: string | null;
  accepted: boolean;
  onToggle: (checked: boolean) => void;
  children: ReactNode;
}) {
  return (
    <li className={`flex items-start gap-2 ${validityOk ? '' : 'opacity-60'}`}>
      <input
        id={id}
        type="checkbox"
        className="mt-1"
        disabled={!validityOk}
        checked={accepted}
        onChange={(e) => onToggle(e.target.checked)}
      />
      <label htmlFor={id} className={`min-w-0 flex-1 text-sm ${validityOk ? 'text-ink' : 'text-muted'}`}>
        {children}
        {!validityOk && validityReason && (
          <span className="mt-1 block text-xs text-danger">{validityReason}</span>
        )}
      </label>
    </li>
  );
}

function Chip({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex items-center rounded-tempo border border-border bg-canvas px-2 py-0.5 text-xs uppercase tracking-wide text-muted">
      {children}
    </span>
  );
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
                            {formatCoachDistance(day.run.distanceM, unitPreference)}
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

            {shouldShowMemoryCard(commit) && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Memory</h2>
                <p className="text-sm text-muted mb-4">
                  Rejected by default. Opt in only to lasting memory changes you want kept.
                  Invalid proposals cannot be accepted.
                </p>

                {commit.layer1Patches.length > 0 && (
                  <section className="mb-5">
                    <h3 className="text-sm font-semibold text-ink mb-2">Athlete state</h3>
                    <ul className="space-y-3">
                      {commit.layer1Patches.map((patch, i) => {
                        const validityOk = patch.validity?.ok !== false;
                        return (
                          <MemoryProposalRow
                            key={`l1-${i}`}
                            id={`memory-l1-${i}`}
                            validityOk={validityOk}
                            validityReason={patch.validity?.reason ?? null}
                            accepted={acceptedL1.has(i)}
                            onToggle={(checked) => {
                              setAcceptedL1((prev) => {
                                const next = new Set(prev);
                                if (checked) {
                                  next.add(i);
                                } else {
                                  next.delete(i);
                                }
                                return next;
                              });
                            }}
                          >
                            <span className="font-medium">
                              {formatLayer1PatchPrimary(patch, unitPreference)}
                            </span>
                            {patch.rationale ? (
                              <span className="mt-0.5 block text-muted">{patch.rationale}</span>
                            ) : null}
                            <details className="mt-1 text-xs text-muted">
                              <summary className="cursor-pointer">Show path</summary>
                              <code className="mt-1 block break-all">{patch.fieldPath}</code>
                            </details>
                          </MemoryProposalRow>
                        );
                      })}
                    </ul>
                  </section>
                )}

                {commit.layer2EpisodeDrafts.length > 0 && (
                  <section className="mb-5">
                    <h3 className="text-sm font-semibold text-ink mb-2">Episodes</h3>
                    <ul className="space-y-3">
                      {commit.layer2EpisodeDrafts.map((draft, i) => {
                        const validityOk = draft.validity?.ok !== false;
                        return (
                          <MemoryProposalRow
                            key={`l2-${i}`}
                            id={`memory-l2-${i}`}
                            validityOk={validityOk}
                            validityReason={draft.validity?.reason ?? null}
                            accepted={acceptedEpisodes.has(i)}
                            onToggle={(checked) => {
                              setAcceptedEpisodes((prev) => {
                                const next = new Set(prev);
                                if (checked) {
                                  next.add(i);
                                } else {
                                  next.delete(i);
                                }
                                return next;
                              });
                            }}
                          >
                            <div className="flex flex-wrap items-center gap-2 mb-1">
                              <Chip>{categoryLabel(draft.category)}</Chip>
                              <span className="text-xs text-muted">{draft.date}</span>
                            </div>
                            <span>{draft.summary}</span>
                          </MemoryProposalRow>
                        );
                      })}
                    </ul>
                  </section>
                )}

                {commit.layer3RuleProposals.length > 0 && (
                  <section className="mb-5">
                    <h3 className="text-sm font-semibold text-ink mb-2">Standing rules</h3>
                    <ul className="space-y-3">
                      {commit.layer3RuleProposals.map((proposal, i) => {
                        const validityOk = proposal.validity?.ok !== false;
                        const op = proposal.operation.toLowerCase();
                        const showDiff =
                          (op === 'revise' || op === 'retire') &&
                          Boolean(proposal.currentRuleText?.trim());
                        return (
                          <MemoryProposalRow
                            key={`l3-${i}`}
                            id={`memory-l3-${i}`}
                            validityOk={validityOk}
                            validityReason={proposal.validity?.reason ?? null}
                            accepted={acceptedRules.has(i)}
                            onToggle={(checked) => {
                              setAcceptedRules((prev) => {
                                const next = new Set(prev);
                                if (checked) {
                                  next.add(i);
                                } else {
                                  next.delete(i);
                                }
                                return next;
                              });
                            }}
                          >
                            <div className="flex flex-wrap items-center gap-2 mb-1">
                              <Chip>{operationLabel(proposal.operation)}</Chip>
                              <Chip>{targetLayerLabel(proposal.targetLayer)}</Chip>
                            </div>
                            {showDiff && (
                              <p className="text-muted">
                                <span className="line-through">
                                  &ldquo;{proposal.currentRuleText}&rdquo;
                                </span>
                              </p>
                            )}
                            {proposal.ruleText?.trim() ? (
                              <p className={showDiff ? 'mt-1' : undefined}>{proposal.ruleText}</p>
                            ) : op === 'retire' ? (
                              <p className="text-muted italic">Retire this rule</p>
                            ) : null}
                            {(proposal.evidence?.length ?? 0) > 0 && (
                              <details className="mt-1 text-xs text-muted">
                                <summary className="cursor-pointer">Evidence</summary>
                                <ul className="mt-1 list-disc pl-4 space-y-0.5">
                                  {proposal.evidence.map((item, ei) => (
                                    <li key={`${ei}-${item}`}>{item}</li>
                                  ))}
                                </ul>
                              </details>
                            )}
                          </MemoryProposalRow>
                        );
                      })}
                    </ul>
                  </section>
                )}

                <label className="mt-2 flex items-start gap-2 text-sm text-ink">
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
