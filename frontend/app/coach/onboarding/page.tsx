'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  completeCoachOnboarding,
  createCoachConstraint,
  getCoachOnboarding,
  getCoachOnboardingPrefill,
  listCoachConstraints,
  retireCoachConstraint,
  updateCoachConstraint,
  type CoachLayer1HrZones,
  type CoachLayer1Paces,
  type CoachOnboardingStatus,
} from '@/lib/api';
import { formatCoachDate } from '@/lib/coachWeek';
import { useAuth } from '@/contexts/AuthContext';
import { useSettings, type UnitPreference } from '@/lib/settings';
import {
  ConstraintFormFields,
  coachConstraintToRequest,
  describeCoachConstraint,
  emptyConstraintDraft,
  type ConstraintDraft,
} from '@/components/coach/ConstraintFormFields';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const KM_PER_MILE = 1.609344;

type Step = 'disclaimer' | 'goal' | 'constraints' | 'confirm' | 'done';

type PaceFieldKey = 'easy' | 'tempo' | 'threshold' | 'interval';

type PaceInputs = Record<PaceFieldKey, string>;

const PACE_FIELDS: Array<{ key: PaceFieldKey; apiKey: keyof CoachLayer1Paces; label: string }> = [
  { key: 'easy', apiKey: 'easySecPerKm', label: 'Easy' },
  { key: 'tempo', apiKey: 'tempoSecPerKm', label: 'Tempo' },
  { key: 'threshold', apiKey: 'thresholdSecPerKm', label: 'Threshold' },
  { key: 'interval', apiKey: 'intervalSecPerKm', label: 'Interval' },
];

function emptyPaceInputs(): PaceInputs {
  return { easy: '', tempo: '', threshold: '', interval: '' };
}

function formatDuration(totalSeconds: number): string {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  if (h > 0) {
    return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  }
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function parseDurationToSeconds(value: string): number | null {
  const trimmed = value.trim();
  if (!trimmed) return null;
  const parts = trimmed.split(':').map((p) => Number(p));
  if (parts.some((n) => Number.isNaN(n) || n < 0)) return null;
  if (parts.length === 1) return Math.round(parts[0]);
  if (parts.length === 2) return Math.round(parts[0] * 60 + parts[1]);
  if (parts.length === 3) return Math.round(parts[0] * 3600 + parts[1] * 60 + parts[2]);
  return null;
}

/** API stores sec/km; UI edits m:ss per km or per mile. */
function secPerKmToPaceInput(secPerKm: number, unit: UnitPreference): string {
  if (secPerKm <= 0) return '';
  const displaySec =
    unit === 'imperial' ? Math.round(secPerKm * KM_PER_MILE) : Math.round(secPerKm);
  const m = Math.floor(displaySec / 60);
  const s = displaySec % 60;
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function paceInputToSecPerKm(value: string, unit: UnitPreference): number | null {
  const displaySec = parseDurationToSeconds(value);
  if (displaySec == null || displaySec <= 0) return null;
  if (unit === 'imperial') {
    return Math.max(1, Math.round(displaySec / KM_PER_MILE));
  }
  return displaySec;
}

function pacesFromInputs(inputs: PaceInputs, unit: UnitPreference): CoachLayer1Paces | null {
  const parsed = PACE_FIELDS.map(({ key }) => paceInputToSecPerKm(inputs[key], unit));
  if (parsed.some((n) => n == null)) return null;
  return {
    easySecPerKm: parsed[0]!,
    tempoSecPerKm: parsed[1]!,
    thresholdSecPerKm: parsed[2]!,
    intervalSecPerKm: parsed[3]!,
  };
}

function pacesToInputs(paces: CoachLayer1Paces, unit: UnitPreference): PaceInputs {
  return {
    easy: secPerKmToPaceInput(paces.easySecPerKm, unit),
    tempo: secPerKmToPaceInput(paces.tempoSecPerKm, unit),
    threshold: secPerKmToPaceInput(paces.thresholdSecPerKm, unit),
    interval: secPerKmToPaceInput(paces.intervalSecPerKm, unit),
  };
}

function CoachOnboardingContent() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { user } = useAuth();
  const { unitPreference } = useSettings();

  const [step, setStep] = useState<Step>('disclaimer');
  const [isEditing, setIsEditing] = useState(false);
  const [justSavedEdit, setJustSavedEdit] = useState(false);
  const [goalType, setGoalType] = useState<'race' | 'pr'>('race');
  const [distance, setDistance] = useState('Half-Marathon');
  const [raceDate, setRaceDate] = useState('');
  const [targetTimeInput, setTargetTimeInput] = useState('');
  const [notes, setNotes] = useState('');
  const [paceInputs, setPaceInputs] = useState<PaceInputs>(emptyPaceInputs);
  const [hrZones, setHrZones] = useState<CoachLayer1HrZones | null>(null);
  const [constraintDraft, setConstraintDraft] = useState<ConstraintDraft>(emptyConstraintDraft());
  const [editingConstraintId, setEditingConstraintId] = useState<string | null>(null);
  const [constraintError, setConstraintError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const paceUnitLabel = unitPreference === 'imperial' ? '/mi' : '/km';

  const statusQuery = useQuery({
    queryKey: ['coach-onboarding'],
    queryFn: getCoachOnboarding,

  });

  const prefillQuery = useQuery({
    queryKey: ['coach-onboarding-prefill'],
    queryFn: getCoachOnboardingPrefill,
    enabled: !!user?.onboardingCompleted && step !== 'done',
  });

  const constraintsQuery = useQuery({
    queryKey: ['coach-constraints', 'active'],
    queryFn: () => listCoachConstraints('active'),
    enabled: step === 'constraints' || isEditing,
  });

  const saveConstraintMutation = useMutation({
    mutationFn: async () => {
      if (editingConstraintId) {
        return updateCoachConstraint(editingConstraintId, constraintDraft);
      }
      return createCoachConstraint(constraintDraft);
    },
    onSuccess: async () => {
      setConstraintError(null);
      setEditingConstraintId(null);
      setConstraintDraft(emptyConstraintDraft());
      await queryClient.invalidateQueries({ queryKey: ['coach-constraints'] });
    },
    onError: (err: Error) => setConstraintError(err.message),
  });

  const retireConstraintMutation = useMutation({
    mutationFn: (id: string) => retireCoachConstraint(id),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['coach-constraints'] });
    },
    onError: (err: Error) => setConstraintError(err.message),
  });

  function hydrateFromStatus(status: CoachOnboardingStatus) {
    const goal = status.goal;
    const layer1 = status.layer1;
    if (goal) {
      setGoalType(goal.type === 'pr' ? 'pr' : 'race');
      setDistance(goal.distance);
      setRaceDate(goal.raceDate ?? '');
      setTargetTimeInput(goal.targetTimeS != null ? formatDuration(goal.targetTimeS) : '');
      setNotes(goal.notes ?? '');
    }
    if (layer1) {
      setHrZones(layer1.hrZones);
      setPaceInputs(pacesToInputs(layer1.paces, unitPreference));
    }
  }

  function startEditing() {
    if (!statusQuery.data) return;
    setJustSavedEdit(false);
    setIsEditing(true);
    hydrateFromStatus(statusQuery.data);
    setSubmitError(null);
    setStep('goal');
  }

  function cancelEditing() {
    setIsEditing(false);
    setJustSavedEdit(false);
    setSubmitError(null);
    setConstraintError(null);
    setEditingConstraintId(null);
    setConstraintDraft(emptyConstraintDraft());
    setStep('done');
  }

  useEffect(() => {
    if (user && !user.onboardingCompleted) {
      router.replace('/onboarding');
    }
  }, [user, router]);

  useEffect(() => {
    if (statusQuery.data?.completed && !isEditing) {
      setStep('done');
    }
  }, [statusQuery.data?.completed, isEditing]);

  useEffect(() => {
    const prefill = prefillQuery.data;
    if (!prefill || isEditing || statusQuery.data?.completed) return;

    setHrZones(prefill.hrZones);
    if (prefill.goalDistances.includes('Half-Marathon')) {
      setDistance('Half-Marathon');
    } else if (prefill.goalDistances.length > 0) {
      setDistance(prefill.goalDistances[0]);
    }
    if (prefill.suggestedPaces) {
      const unit =
        prefill.unitPreference === 'imperial' || prefill.unitPreference === 'metric'
          ? prefill.unitPreference
          : unitPreference;
      setPaceInputs(pacesToInputs(prefill.suggestedPaces, unit));
    }
  }, [prefillQuery.data, unitPreference, isEditing, statusQuery.data?.completed]);

  const disclaimer =
    prefillQuery.data?.disclaimer ??
    statusQuery.data?.disclaimer ??
    'Tempo AI Coach is not a doctor and does not provide medical advice.';

  const goalDistances = prefillQuery.data?.goalDistances ?? [
    '5K',
    '10K',
    '15K',
    '10 mile',
    '20K',
    'Half-Marathon',
    '30K',
    'Marathon',
  ];

  const confirmedPaces = useMemo(
    () => pacesFromInputs(paceInputs, unitPreference),
    [paceInputs, unitPreference]
  );
  const pacesValid = confirmedPaces != null;

  const shoeSource = isEditing
    ? statusQuery.data?.layer1
    : prefillQuery.data
      ? {
          shoes: prefillQuery.data.shoes,
          defaultShoeId: prefillQuery.data.defaultShoeId,
        }
      : statusQuery.data?.layer1;

  async function handleSubmit() {
    setSubmitError(null);

    if (goalType === 'race' && !raceDate) {
      setSubmitError('Race date is required for a race goal.');
      return;
    }

    let targetTimeS: number | null = null;
    if (targetTimeInput.trim()) {
      targetTimeS = parseDurationToSeconds(targetTimeInput);
      if (targetTimeS == null || targetTimeS <= 0) {
        setSubmitError('Target time must look like mm:ss or h:mm:ss.');
        return;
      }
    }
    if (goalType === 'pr' && (targetTimeS == null || targetTimeS <= 0)) {
      setSubmitError('Target time is required for a PR goal.');
      return;
    }

    if (!confirmedPaces) {
      setSubmitError(`Confirm all four paces as m:ss ${paceUnitLabel} before continuing.`);
      return;
    }

    setIsSubmitting(true);
    try {
      await completeCoachOnboarding({
        goal: {
          type: goalType,
          distance,
          raceDate: goalType === 'race' ? raceDate : raceDate || null,
          targetTimeS,
          notes: notes.trim() || null,
        },
        paces: confirmedPaces,
        hrZones: hrZones ?? undefined,
        shoes: shoeSource?.shoes,
        defaultShoeId: shoeSource?.defaultShoeId ?? null,
      });
      const wasEditing = isEditing;
      setIsEditing(false);
      setJustSavedEdit(wasEditing);
      await statusQuery.refetch();
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      setStep('done');
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : 'Failed to complete onboarding');
    } finally {
      setIsSubmitting(false);
    }
  }

  if (statusQuery.isLoading) {
    return (
      <PageShell density="control" title="Profile & goal">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  const activeConstraints = constraintsQuery.data ?? [];

  return (
    <PageShell
      density="control"
      title="Profile & goal"
      subtitle="Your paces & zones and primary goal"
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Week
          </Link>
        </div>
      }
    >
      <div className="w-full max-w-2xl space-y-6">
        {step === 'disclaimer' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">Before you continue</h2>
            <p className="text-sm text-muted mb-4">
              {disclaimer}
            </p>
            <Button type="button" onClick={() => setStep('goal')}>
              I understand — set my goal
            </Button>
          </Card>
        )}

        {step === 'goal' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-4">
              {isEditing ? 'Edit primary goal' : 'Primary goal'}
            </h2>
            <div className="space-y-4">
              <fieldset className="space-y-2">
                <legend className="text-sm font-medium text-ink">Goal type</legend>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="radio"
                    name="goalType"
                    checked={goalType === 'race'}
                    onChange={() => setGoalType('race')}
                  />
                  Race (distance + date, optional time goal)
                </label>
                <label className="flex items-center gap-2 text-sm">
                  <input
                    type="radio"
                    name="goalType"
                    checked={goalType === 'pr'}
                    onChange={() => setGoalType('pr')}
                  />
                  PR (distance + target time)
                </label>
              </fieldset>

              <label className="block text-sm">
                <span className="font-medium text-ink">Distance</span>
                <select
                  className={`${fieldClass} mt-1`}
                  value={distance}
                  onChange={(e) => setDistance(e.target.value)}
                >
                  {goalDistances.map((d) => (
                    <option key={d} value={d}>
                      {d}
                    </option>
                  ))}
                </select>
              </label>

              {goalType === 'race' && (
                <label className="block text-sm">
                  <span className="font-medium text-ink">Race date</span>
                  <input
                    type="date"
                    className={`${fieldClass} mt-1`}
                    value={raceDate}
                    onChange={(e) => setRaceDate(e.target.value)}
                  />
                </label>
              )}

              <label className="block text-sm">
                <span className="font-medium text-ink">
                  {goalType === 'pr' ? 'Target time' : 'Optional time goal'} (h:mm:ss or mm:ss)
                </span>
                <input
                  type="text"
                  className={`${fieldClass} mt-1`}
                  placeholder={goalType === 'pr' ? '1:45:00' : 'optional'}
                  value={targetTimeInput}
                  onChange={(e) => setTargetTimeInput(e.target.value)}
                />
              </label>

              <label className="block text-sm">
                <span className="font-medium text-ink">Notes (optional)</span>
                <textarea
                  className={`${fieldClass} mt-1`}
                  rows={3}
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
              </label>

              <div className="flex flex-wrap gap-2">
                {isEditing ? (
                  <Button type="button" variant="secondary" onClick={cancelEditing}>
                    Cancel
                  </Button>
                ) : (
                  <Button type="button" variant="secondary" onClick={() => setStep('disclaimer')}>
                    Back
                  </Button>
                )}
                <Button
                  type="button"
                  onClick={() => {
                    if (goalType === 'race' && !raceDate) {
                      setSubmitError('Race date is required.');
                      return;
                    }
                    setSubmitError(null);
                    setStep('constraints');
                  }}
                >
                  Fixed dates & schedule
                </Button>
              </div>
              {submitError && <p className="text-sm text-danger">{submitError}</p>}
            </div>
          </Card>
        )}

        {step === 'constraints' && (
          <>
            <Card>
              <div className="flex items-center justify-between gap-2 mb-2">
                <h2 className="text-lg font-semibold text-ink">
                  {editingConstraintId ? 'Edit fixed date' : 'Fixed dates & schedule (optional)'}
                </h2>
                {editingConstraintId && (
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => {
                      setEditingConstraintId(null);
                      setConstraintDraft(emptyConstraintDraft());
                      setConstraintError(null);
                    }}
                  >
                    Cancel edit
                  </Button>
                )}
              </div>
              <p className="text-sm text-muted mb-4">
                Changes save immediately. You can also manage them later under Fixed dates & schedule.
              </p>
              <ConstraintFormFields value={constraintDraft} onChange={setConstraintDraft} />
              <div className="mt-3 flex flex-wrap gap-2">
                <Button
                  type="button"
                  onClick={() => saveConstraintMutation.mutate()}
                  disabled={saveConstraintMutation.isPending}
                >
                  {saveConstraintMutation.isPending
                    ? 'Saving...'
                    : editingConstraintId
                      ? 'Save changes'
                      : 'Add fixed date'}
                </Button>
              </div>
              {constraintError && <p className="text-sm text-danger mt-2">{constraintError}</p>}
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-4">Your fixed dates</h2>
              {constraintsQuery.isLoading ? (
                <p className="text-sm text-muted">Loading...</p>
              ) : constraintsQuery.isError ? (
                <p className="text-sm text-danger">
                  {constraintsQuery.error instanceof Error
                    ? constraintsQuery.error.message
                    : 'Failed to load constraints'}
                </p>
              ) : activeConstraints.length === 0 ? (
                <p className="text-sm text-muted">No fixed dates yet.</p>
              ) : (
                <ul className="space-y-3 mb-4">
                  {activeConstraints.map((c) => (
                    <li
                      key={c.id}
                      className="flex flex-wrap items-start justify-between gap-2 border-b border-border pb-3 last:border-0"
                    >
                      <p className="text-sm text-ink">{describeCoachConstraint(c)}</p>
                      <div className="flex gap-2">
                        <Button
                          type="button"
                          variant="secondary"
                          onClick={() => {
                            setEditingConstraintId(c.id);
                            setConstraintDraft(coachConstraintToRequest(c));
                            setConstraintError(null);
                          }}
                        >
                          Edit
                        </Button>
                        <Button
                          type="button"
                          variant="secondary"
                          onClick={() => retireConstraintMutation.mutate(c.id)}
                          disabled={retireConstraintMutation.isPending}
                        >
                          Remove
                        </Button>
                      </div>
                    </li>
                  ))}
                </ul>
              )}
              <div className="flex flex-wrap gap-2">
                <Button type="button" variant="secondary" onClick={() => setStep('goal')}>
                  Back
                </Button>
                <Button type="button" variant="secondary" onClick={() => setStep('confirm')}>
                  Skip
                </Button>
                <Button type="button" onClick={() => setStep('confirm')}>
                  Continue to your paces & zones
                </Button>
              </div>
            </Card>
          </>
        )}

        {step === 'confirm' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">Confirm your paces & zones</h2>
            <p className="text-sm text-muted mb-4">
              {isEditing
                ? 'Values below are your saved paces & zones — edit paces before saving. Existing weekly plans are not changed automatically.'
                : 'Prefills come from Tempo. Suggested paces average your Race workouts from the last ~4 months (normalized across distances) — edit them before saving. Nothing is trusted blindly.'}
            </p>

            {prefillQuery.isLoading && !isEditing ? (
              <p className="text-sm text-muted">Loading Tempo data...</p>
            ) : prefillQuery.isError && !isEditing && !hrZones ? (
              <p className="text-sm text-danger">
                {prefillQuery.error instanceof Error
                  ? prefillQuery.error.message
                  : 'Failed to load prefill'}
              </p>
            ) : (
              <div className="space-y-6">
                <section>
                  <h3 className="text-sm font-semibold text-ink mb-2">Heart rate zones</h3>
                  <ul className="text-sm text-muted space-y-1">
                    {(hrZones?.zones ?? []).map((z) => (
                      <li key={z.zoneNumber}>
                        Zone {z.zoneNumber}: {z.minBpm}–{z.maxBpm} bpm
                      </li>
                    ))}
                  </ul>
                </section>

                <section>
                  <h3 className="text-sm font-semibold text-ink mb-2">Active shoes</h3>
                  {(shoeSource?.shoes.length ?? 0) === 0 ? (
                    <p className="text-sm text-muted">No active shoes in Tempo yet.</p>
                  ) : (
                    <ul className="text-sm text-muted space-y-1">
                      {shoeSource!.shoes.map((s) => (
                        <li key={s.id}>
                          {s.brand} {s.model}
                          {shoeSource?.defaultShoeId === s.id ? ' (default)' : ''}
                        </li>
                      ))}
                    </ul>
                  )}
                </section>

                <section>
                  <h3 className="text-sm font-semibold text-ink mb-2">
                    Training paces (m:ss {paceUnitLabel}) — confirm or edit
                  </h3>
                  {!isEditing && !prefillQuery.data?.suggestedPaces && (
                    <p className="text-sm text-muted mb-2">
                      No recent races (or best-effort fallback) found — enter paces manually.
                    </p>
                  )}
                  <div className="grid gap-3 sm:grid-cols-2">
                    {PACE_FIELDS.map(({ key, label }) => (
                      <label key={key} className="block text-sm">
                        <span className="font-medium text-ink">{label}</span>
                        <div className="mt-1 flex items-center gap-2">
                          <input
                            type="text"
                            inputMode="numeric"
                            placeholder="m:ss"
                            className={fieldClass}
                            value={paceInputs[key]}
                            onChange={(e) =>
                              setPaceInputs((prev) => ({
                                ...prev,
                                [key]: e.target.value,
                              }))
                            }
                          />
                          <span className="text-xs text-muted whitespace-nowrap">
                            {paceUnitLabel}
                          </span>
                        </div>
                      </label>
                    ))}
                  </div>
                </section>

                <div className="flex flex-wrap gap-2">
                  <Button type="button" variant="secondary" onClick={() => setStep('constraints')}>
                    Back
                  </Button>
                  <Button type="button" onClick={handleSubmit} disabled={isSubmitting || !pacesValid}>
                    {isSubmitting
                      ? 'Saving...'
                      : isEditing
                        ? 'Save'
                        : 'Start coaching'}
                  </Button>
                </div>
                {submitError && <p className="text-sm text-danger">{submitError}</p>}
              </div>
            )}
          </Card>
        )}

        {step === 'done' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">You&apos;re set up</h2>
            {statusQuery.data?.goal && (
              <p className="text-sm text-muted mb-4">
                Goal: {statusQuery.data.goal.type} · {statusQuery.data.goal.distance}
                {statusQuery.data.goal.raceDate ? ` · ${formatCoachDate(statusQuery.data.goal.raceDate)}` : ''}
                {statusQuery.data.goal.targetTimeS
                  ? ` · ${formatDuration(statusQuery.data.goal.targetTimeS)}`
                  : ''}
              </p>
            )}
            {justSavedEdit && (
              <p className="text-sm text-muted mb-4">
                Saved. Existing weekly plans are unchanged — send to coach again if you want them to use
                these inputs.
              </p>
            )}
            <div className="flex flex-wrap gap-2">
              <Button type="button" variant="secondary" onClick={startEditing}>
                Edit profile & goal
              </Button>
              <Button href="/coach">Back to Week</Button>
            </div>
          </Card>
        )}
      </div>
    </PageShell>
  );
}

export default function CoachOnboardingPage() {
  return <CoachOnboardingContent />;
}
