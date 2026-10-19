'use client';

import { Suspense, useEffect, useMemo, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { ShoeSelector } from '@/components/ShoeSelector';
import { AskForChangesDialog } from '@/components/coach/AskForChangesDialog';
import {
  applyCoachDraftWeek,
  deleteCoachDraftWeek,
  getCoachDraftWeek,
  getCoachHome,
  getCoachJob,
  getCoachPrivacyPreflight,
  postCoachDraftAskForChanges,
  putCoachDraftWeek,
  type CoachDayType,
  type CoachJob,
  type CoachWeeklyCommit,
  type CoachWeeklyCommitDay,
} from '@/lib/api';
import { useSettings, type UnitPreference } from '@/lib/settings';
import {
  formatCoachDistance,
  normalizePlannedDistanceM,
  stripRedundantLeadingDistance,
} from '@/lib/coachDistance';
import {
  categoryLabel,
  dayTypeLabel,
  formatLayer1PatchPrimary,
  operationLabel,
  shouldShowMemoryCard,
  targetLayerLabel,
} from '@/lib/coachMemoryDisplay';
import {
  formatCoachDate,
  formatCoachJobError,
  pollCoachJobUntilSettled,
} from '@/lib/coachWeek';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const DAY_TYPES: CoachDayType[] = ['rest', 'easy', 'quality', 'long', 'race', 'other'];

function localTodayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function timezoneOffsetMinutes(): number {
  return -new Date().getTimezoneOffset();
}

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

function stepMeters(unit: UnitPreference): number {
  return unit === 'imperial' ? 0.5 * 1609.344 : 500;
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
        <span className="font-medium">Keep</span>
        <span className="mx-1.5 text-muted">·</span>
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

function swapDayContent(commit: CoachWeeklyCommit, a: number, b: number): CoachWeeklyCommit {
  if (a === b || a < 0 || b < 0 || a >= commit.days.length || b >= commit.days.length) {
    return commit;
  }
  const days = commit.days.map((d) => ({ ...d, run: d.run ? { ...d.run } : null }));
  const dayA = days[a];
  const dayB = days[b];
  const contentA = { dayType: dayA.dayType, run: dayA.run, strength: dayA.strength };
  const contentB = { dayType: dayB.dayType, run: dayB.run, strength: dayB.strength };
  days[a] = { ...dayA, ...contentB };
  days[b] = { ...dayB, ...contentA };
  return { ...commit, days };
}

function nudgeDistance(
  commit: CoachWeeklyCommit,
  index: number,
  deltaM: number,
  unit: UnitPreference
): CoachWeeklyCommit {
  const day = commit.days[index];
  if (!day || day.dayType === 'rest' || !day.run) {
    return commit;
  }
  const next = Math.max(0, normalizePlannedDistanceM(day.run.distanceM + deltaM));
  // Re-snap after display round-trip so stepper lands on clean half-units.
  const display = metersToDisplay(next, unit);
  return updateDay(commit, index, {
    runPatch: { distanceM: displayToMeters(display, unit) },
  });
}

function ReviewPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const weekStartDate = searchParams.get('weekStartDate') ?? '';
  const todayIso = localTodayIso();

  const [commit, setCommit] = useState<CoachWeeklyCommit | null>(null);
  const [acks, setAcks] = useState<Record<number, { acknowledged: boolean; response: string }>>({});
  const [acceptedL1, setAcceptedL1] = useState<Set<number>>(new Set());
  const [acceptedEpisodes, setAcceptedEpisodes] = useState<Set<number>>(new Set());
  const [acceptedRules, setAcceptedRules] = useState<Set<number>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [swapSource, setSwapSource] = useState<number | null>(null);
  const [askOpen, setAskOpen] = useState(false);
  const [askNote, setAskNote] = useState('');
  const [askJob, setAskJob] = useState<CoachJob | null>(null);

  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeek(weekStartDate),
    enabled: Boolean(weekStartDate),
  });

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
  });

  const preflightId = draftQuery.data?.preflightId ?? null;
  const preflightQuery = useQuery({
    queryKey: ['coach-privacy-preflight', preflightId],
    queryFn: () => getCoachPrivacyPreflight(preflightId as string),
    enabled: Boolean(preflightId) && askOpen,
  });

  const lockedThroughDate = draftQuery.data?.lockedThroughDate ?? null;
  const isAdjustment = Boolean(draftQuery.data?.isAdjustment);
  const disclaimer =
    homeQuery.data?.disclaimer ??
    'Tempo AI Coach is not medical advice. Back off or seek care for pain or illness.';

  useEffect(() => {
    if (!draftQuery.data?.commit) {
      return;
    }
    const normalized = normalizeCommit(draftQuery.data.commit);
    /* eslint-disable react-hooks/set-state-in-effect -- hydrate editor from draft query */
    setCommit(normalized);
    const initial: Record<number, { acknowledged: boolean; response: string }> = {};
    normalized.safetyFlags.forEach((_, i) => {
      initial[i] = { acknowledged: false, response: '' };
    });
    setAcks(initial);
    setAcceptedL1(new Set());
    setAcceptedEpisodes(new Set());
    setAcceptedRules(new Set());
    setSwapSource(null);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [draftQuery.data]);

  const distanceUnitLabel = unitPreference === 'imperial' ? 'mi' : 'km';
  const step = stepMeters(unitPreference);

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

  const isDayLocked = (date: string) =>
    Boolean(lockedThroughDate) && date <= (lockedThroughDate as string);

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
        archiveNotesAsRecap: false,
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

  const deleteMutation = useMutation({
    mutationFn: () => deleteCoachDraftWeek(weekStartDate),
    onSuccess: async () => {
      setError(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-job-current'] });
      router.push('/coach');
    },
    onError: (err: Error) => setError(err.message),
  });

  const askMutation = useMutation({
    mutationFn: async () => {
      if (!commit) {
        throw new Error('No draft loaded');
      }
      const note = askNote.trim();
      if (!note) {
        throw new Error('Add a short note about what to change');
      }
      await putCoachDraftWeek(weekStartDate, commit);
      const started = await postCoachDraftAskForChanges({
        weekStartDate,
        note,
        timezoneOffsetMinutes: timezoneOffsetMinutes(),
      });
      const settled = await pollCoachJobUntilSettled(started, (job) => getCoachJob(job.id), {
        onUpdate: setAskJob,
      });
      if (settled.status === 'failed') {
        throw new Error(formatCoachJobError(settled.errorCode, settled.errorMessage));
      }
      return settled;
    },
    onSuccess: async () => {
      setError(null);
      setAskOpen(false);
      setAskNote('');
      setAskJob(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week', weekStartDate] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-job-current'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const busy =
    applyMutation.isPending || deleteMutation.isPending || askMutation.isPending;

  if (!weekStartDate) {
    return (
      <PageShell density="control" title="Review weekly plan">
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
      title={isAdjustment ? 'Review change draft' : 'Review weekly plan'}
      subtitle={`Week of ${formatCoachDate(weekStartDate)}`}
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted underline">
            ← Week
          </Link>
          {isAdjustment && (
            <p className="text-sm text-muted mt-2">
              Elapsed days are locked; only remaining days are editable.
            </p>
          )}
        </div>
      }
    >
      <div className="w-full space-y-6 pb-28">
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
            <Link href="/coach/check-in" className="text-sm underline mt-2 inline-block">
              Back to check-in
            </Link>
          </Card>
        )}

        {commit && (
          <>
            {commit.safetyFlags.length > 0 && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Safety flags</h2>
                <p className="text-sm text-muted mb-4">
                  Acknowledge each flag before accepting. Flags that require a response need a short
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
                  const locked = isDayLocked(day.date);
                  const isToday = day.date === todayIso;
                  const isRest = day.dayType === 'rest';
                  return (
                    <div
                      key={day.date}
                      className={`rounded-tempo border border-border p-3 space-y-3 ${
                        locked ? 'opacity-60 bg-canvas' : ''
                      }`}
                    >
                      <div className="flex flex-wrap items-center gap-2">
                        <p className="text-sm font-medium text-ink min-w-[7rem]">
                          {formatCoachDate(day.date)}
                        </p>
                        {isToday && (
                          <span className="rounded-tempo border border-volt px-2 py-0.5 text-xs text-ink">
                            Today
                          </span>
                        )}
                        {locked && (
                          <span className="rounded-tempo border border-border px-2 py-0.5 text-xs text-muted">
                            Locked
                          </span>
                        )}
                        <span className="text-sm text-muted">{dayTypeLabel(day.dayType)}</span>
                        {!isRest && day.run && (
                          <span className="text-sm text-muted">
                            · {formatCoachDistance(day.run.distanceM, unitPreference)}
                          </span>
                        )}
                      </div>

                      {!locked && (
                        <div className="flex flex-wrap items-center gap-2">
                          {swapSource === i ? (
                            <label className="flex items-center gap-2 text-sm text-muted">
                              <span>Swap with</span>
                              <select
                                className={fieldClass + ' w-auto max-w-[12rem]'}
                                defaultValue=""
                                onChange={(e) => {
                                  const target = Number.parseInt(e.target.value, 10);
                                  if (Number.isFinite(target)) {
                                    setCommit((c) => (c ? swapDayContent(c, i, target) : c));
                                  }
                                  setSwapSource(null);
                                }}
                              >
                                <option value="" disabled>
                                  Choose day…
                                </option>
                                {commit.days.map((other, j) =>
                                  j === i || isDayLocked(other.date) ? null : (
                                    <option key={other.date} value={j}>
                                      {formatCoachDate(other.date)}
                                    </option>
                                  )
                                )}
                              </select>
                              <Button
                                type="button"
                                variant="secondary"
                                onClick={() => setSwapSource(null)}
                              >
                                Cancel
                              </Button>
                            </label>
                          ) : (
                            <Button
                              type="button"
                              variant="secondary"
                              onClick={() => setSwapSource(i)}
                            >
                              Swap
                            </Button>
                          )}
                          {!isRest && (
                            <Button
                              type="button"
                              variant="secondary"
                              onClick={() =>
                                setCommit((c) =>
                                  c ? updateDay(c, i, { dayType: 'rest' }) : c
                                )
                              }
                            >
                              Make rest
                            </Button>
                          )}
                          {!isRest && day.run && (
                            <div className="flex items-center gap-1">
                              <Button
                                type="button"
                                variant="secondary"
                                aria-label={`Decrease distance by 0.5 ${distanceUnitLabel}`}
                                onClick={() =>
                                  setCommit((c) =>
                                    c ? nudgeDistance(c, i, -step, unitPreference) : c
                                  )
                                }
                              >
                                −
                              </Button>
                              <span className="min-w-[4.5rem] text-center text-sm text-ink">
                                {metersToDisplay(day.run.distanceM, unitPreference)}{' '}
                                {distanceUnitLabel}
                              </span>
                              <Button
                                type="button"
                                variant="secondary"
                                aria-label={`Increase distance by 0.5 ${distanceUnitLabel}`}
                                onClick={() =>
                                  setCommit((c) =>
                                    c ? nudgeDistance(c, i, step, unitPreference) : c
                                  )
                                }
                              >
                                +
                              </Button>
                            </div>
                          )}
                        </div>
                      )}

                      {!locked && !isRest && day.run && (
                        <details className="text-sm">
                          <summary className="cursor-pointer text-muted select-none">
                            Edit details
                          </summary>
                          <div className="mt-3 grid gap-3 sm:grid-cols-2">
                            <label className="text-sm text-muted space-y-1">
                              <span>Day type</span>
                              <select
                                className={fieldClass}
                                value={day.dayType}
                                disabled={locked}
                                onChange={(e) =>
                                  setCommit((c) =>
                                    c
                                      ? updateDay(c, i, {
                                          dayType: e.target.value as CoachDayType,
                                        })
                                      : c
                                  )
                                }
                              >
                                {DAY_TYPES.map((t) => (
                                  <option key={t} value={t}>
                                    {dayTypeLabel(t)}
                                  </option>
                                ))}
                              </select>
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
                            <label className="text-sm text-muted space-y-1 sm:col-span-2">
                              <span>Shoe</span>
                              <ShoeSelector
                                className={fieldClass}
                                value={day.run.shoeId}
                                onChange={(shoeId) =>
                                  setCommit((c) =>
                                    c
                                      ? updateDay(c, i, { runPatch: { shoeId } })
                                      : c
                                  )
                                }
                              />
                            </label>
                          </div>
                        </details>
                      )}
                    </div>
                  );
                })}
              </div>
            </Card>

            {shouldShowMemoryCard(commit) && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">
                  What your coach wants to remember
                </h2>
                <p className="text-sm text-muted mb-4">
                  Off by default. Turn on Keep only for lasting notes you want your coach to keep.
                  Invalid proposals cannot be kept.
                </p>

                {commit.layer1Patches.length > 0 && (
                  <section className="mb-5">
                    <h3 className="text-sm font-semibold text-ink mb-2">Your paces & zones</h3>
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
                          </MemoryProposalRow>
                        );
                      })}
                    </ul>
                  </section>
                )}

                {commit.layer2EpisodeDrafts.length > 0 && (
                  <section className="mb-5">
                    <h3 className="text-sm font-semibold text-ink mb-2">Notes about you</h3>
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
                              <span className="text-xs text-muted">
                                {draft.date ? formatCoachDate(draft.date) : ''}
                              </span>
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
                    <h3 className="text-sm font-semibold text-ink mb-2">Ongoing preferences</h3>
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
                              <p className="text-muted italic">Remove this preference</p>
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
              </Card>
            )}

            {error && (
              <Card>
                <p className="text-sm text-danger">{error}</p>
              </Card>
            )}
          </>
        )}
      </div>

      {commit && (
        <div className="fixed bottom-0 inset-x-0 z-40 border-t border-border bg-raised/95 backdrop-blur supports-[backdrop-filter]:bg-raised/80">
          <div className="mx-auto max-w-3xl px-4 py-3 space-y-2">
            {commit.clarifyingQuestion && (
              <p className="text-sm text-muted">
                <span className="font-medium text-ink">Your coach asked:</span>{' '}
                {commit.clarifyingQuestion}
              </p>
            )}
            <div className="flex flex-wrap items-center gap-2">
              <Button
                type="button"
                variant="secondary"
                disabled={busy}
                onClick={() => deleteMutation.mutate()}
              >
                {deleteMutation.isPending ? 'Starting over…' : 'Start over'}
              </Button>
              <Button
                type="button"
                variant="secondary"
                disabled={busy}
                onClick={() => {
                  setAskOpen(true);
                  setError(null);
                }}
              >
                Ask for changes
              </Button>
              <Button
                type="button"
                disabled={!safetyReady || busy}
                onClick={() => applyMutation.mutate()}
              >
                {applyMutation.isPending
                  ? 'Accepting…'
                  : isAdjustment
                    ? 'Accept changes'
                    : 'Accept plan'}
              </Button>
              {!isAdjustment && (
                <p className="text-xs text-muted max-w-sm">
                  Accept after your last run of the week.
                </p>
              )}
              <p className="text-xs text-muted max-w-sm">{disclaimer}</p>
            </div>
          </div>
        </div>
      )}

      <AskForChangesDialog
        open={askOpen}
        onClose={() => {
          if (!askMutation.isPending) {
            setAskOpen(false);
          }
        }}
        note={askNote}
        onNoteChange={setAskNote}
        noteLabel="What should change?"
        notePlaceholder="e.g. Make Tuesday a rest day and move the long to Sunday"
        whatsSentLine="Sends your note, a summary of this draft, and the usual redacted training packet."
        inspectContent={
          preflightQuery.isLoading ? (
            <p>Loading last packet…</p>
          ) : preflightQuery.isError ? (
            <p>Could not load the previous packet. Your note will still be included on Send.</p>
          ) : preflightQuery.data ? (
            <>
              <p className="text-ink">
                Last send plus your note and this draft&apos;s day summary. Categories:
              </p>
              <ul className="list-disc pl-4 space-y-1">
                {preflightQuery.data.report.included.map((item) => (
                  <li key={item.category}>
                    <span className="text-ink">{item.category}</span> — {item.detail}
                  </li>
                ))}
                <li>
                  <span className="text-ink">athleteFeedback</span> — your note and previous draft
                  day summary
                </li>
              </ul>
            </>
          ) : null
        }
        sending={askMutation.isPending}
        progressLabel={
          askJob && askJob.status !== 'ready' && askJob.status !== 'failed'
            ? askJob.status === 'running'
              ? 'Coach is working…'
              : 'Queued…'
            : null
        }
        onSend={() => askMutation.mutate()}
      />
    </PageShell>
  );
}

export default function CoachReviewPage() {
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
