'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  completeCoachOnboarding,
  getCoachOnboarding,
  getCoachOnboardingPrefill,
  type CoachLayer1HrZones,
  type CoachLayer1Paces,
} from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import { useAuth } from '@/contexts/AuthContext';
import { useSettings } from '@/lib/settings';
import { formatPace } from '@/lib/format';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

type Step = 'disclaimer' | 'goal' | 'confirm' | 'done';

function emptyPaces(): CoachLayer1Paces {
  return {
    easySecPerKm: 0,
    tempoSecPerKm: 0,
    thresholdSecPerKm: 0,
    intervalSecPerKm: 0,
  };
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

function CoachOnboardingContent() {
  const router = useRouter();
  const { user } = useAuth();
  const { unitPreference } = useSettings();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();

  const [step, setStep] = useState<Step>('disclaimer');
  const [goalType, setGoalType] = useState<'race' | 'pr'>('race');
  const [distance, setDistance] = useState('Half-Marathon');
  const [raceDate, setRaceDate] = useState('');
  const [targetTimeInput, setTargetTimeInput] = useState('');
  const [notes, setNotes] = useState('');
  const [paces, setPaces] = useState<CoachLayer1Paces>(emptyPaces());
  const [hrZones, setHrZones] = useState<CoachLayer1HrZones | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const statusQuery = useQuery({
    queryKey: ['coach-onboarding'],
    queryFn: getCoachOnboarding,
    enabled: showCoachNav,
  });

  const prefillQuery = useQuery({
    queryKey: ['coach-onboarding-prefill'],
    queryFn: getCoachOnboardingPrefill,
    enabled: showCoachNav && !!user?.onboardingCompleted && step !== 'done',
  });

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  useEffect(() => {
    if (user && !user.onboardingCompleted) {
      router.replace('/onboarding');
    }
  }, [user, router]);

  useEffect(() => {
    if (statusQuery.data?.completed) {
      setStep('done');
    }
  }, [statusQuery.data?.completed]);

  useEffect(() => {
    const prefill = prefillQuery.data;
    if (!prefill) return;

    setHrZones(prefill.hrZones);
    if (prefill.goalDistances.includes('Half-Marathon')) {
      setDistance('Half-Marathon');
    } else if (prefill.goalDistances.length > 0) {
      setDistance(prefill.goalDistances[0]);
    }
    if (prefill.suggestedPaces) {
      setPaces(prefill.suggestedPaces);
    }
  }, [prefillQuery.data]);

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

  const pacesValid = useMemo(
    () =>
      paces.easySecPerKm > 0 &&
      paces.tempoSecPerKm > 0 &&
      paces.thresholdSecPerKm > 0 &&
      paces.intervalSecPerKm > 0,
    [paces]
  );

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

    if (!pacesValid) {
      setSubmitError('Confirm all four paces (seconds per km) before continuing.');
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
        paces,
        hrZones: hrZones ?? undefined,
        shoes: prefillQuery.data?.shoes,
        defaultShoeId: prefillQuery.data?.defaultShoeId ?? null,
      });
      await statusQuery.refetch();
      setStep('done');
    } catch (error) {
      setSubmitError(error instanceof Error ? error.message : 'Failed to complete onboarding');
    } finally {
      setIsSubmitting(false);
    }
  }

  if (settingsLoading || !isFetched || statusQuery.isLoading) {
    return (
      <PageShell density="control" title="Coach onboarding">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  if (!showCoachNav) {
    return null;
  }

  return (
    <PageShell
      density="control"
      title="Coach onboarding"
      subtitle="Layer 1 + primary goal"
      leading={
        <div
          className="rounded-tempo border border-border bg-canvas p-4 text-sm text-muted mb-4"
          role="note"
        >
          {disclaimer}
        </div>
      }
    >
      <div className="w-full max-w-2xl space-y-6">
        {step === 'disclaimer' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">Before you continue</h2>
            <p className="text-sm text-muted mb-4">
              AI Coach suggests training plans only. It is not medical advice and is not a
              substitute for a coach, doctor, or physical therapist. Stop and seek care for
              pain, injury, or health concerns.
            </p>
            <Button type="button" onClick={() => setStep('goal')}>
              I understand — set my goal
            </Button>
          </Card>
        )}

        {step === 'goal' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-4">Primary goal</h2>
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
                <Button type="button" variant="secondary" onClick={() => setStep('disclaimer')}>
                  Back
                </Button>
                <Button
                  type="button"
                  onClick={() => {
                    if (goalType === 'race' && !raceDate) {
                      setSubmitError('Race date is required.');
                      return;
                    }
                    setSubmitError(null);
                    setStep('confirm');
                  }}
                >
                  Confirm Tempo data
                </Button>
              </div>
              {submitError && <p className="text-sm text-danger">{submitError}</p>}
            </div>
          </Card>
        )}

        {step === 'confirm' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">Confirm Layer 1</h2>
            <p className="text-sm text-muted mb-4">
              Prefills come from Tempo. Suggested paces are heuristics from your best efforts —
              edit them before saving. Nothing is trusted blindly.
            </p>

            {prefillQuery.isLoading ? (
              <p className="text-sm text-muted">Loading Tempo data...</p>
            ) : prefillQuery.isError ? (
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
                  {(prefillQuery.data?.shoes.length ?? 0) === 0 ? (
                    <p className="text-sm text-muted">No active shoes in Tempo yet.</p>
                  ) : (
                    <ul className="text-sm text-muted space-y-1">
                      {prefillQuery.data!.shoes.map((s) => (
                        <li key={s.id}>
                          {s.brand} {s.model}
                          {prefillQuery.data?.defaultShoeId === s.id ? ' (default)' : ''}
                        </li>
                      ))}
                    </ul>
                  )}
                </section>

                <section>
                  <h3 className="text-sm font-semibold text-ink mb-2">
                    Training paces (sec/km) — confirm or edit
                  </h3>
                  {!prefillQuery.data?.suggestedPaces && (
                    <p className="text-sm text-muted mb-2">
                      No 5K or mile best effort found — enter paces manually.
                    </p>
                  )}
                  <div className="grid gap-3 sm:grid-cols-2">
                    {(
                      [
                        ['easySecPerKm', 'Easy'],
                        ['tempoSecPerKm', 'Tempo'],
                        ['thresholdSecPerKm', 'Threshold'],
                        ['intervalSecPerKm', 'Interval'],
                      ] as const
                    ).map(([key, label]) => (
                      <label key={key} className="block text-sm">
                        <span className="font-medium text-ink">{label}</span>
                        <input
                          type="number"
                          min={1}
                          className={`${fieldClass} mt-1`}
                          value={paces[key] || ''}
                          onChange={(e) =>
                            setPaces((prev) => ({
                              ...prev,
                              [key]: Number(e.target.value) || 0,
                            }))
                          }
                        />
                        <span className="text-xs text-muted">
                          {paces[key] > 0
                            ? formatPace(paces[key], unitPreference)
                            : '—'}
                        </span>
                      </label>
                    ))}
                  </div>
                </section>

                <div className="flex flex-wrap gap-2">
                  <Button type="button" variant="secondary" onClick={() => setStep('goal')}>
                    Back
                  </Button>
                  <Button type="button" onClick={handleSubmit} disabled={isSubmitting || !pacesValid}>
                    {isSubmitting ? 'Saving...' : 'Complete coach onboarding'}
                  </Button>
                </div>
                {submitError && <p className="text-sm text-danger">{submitError}</p>}
              </div>
            )}
          </Card>
        )}

        {step === 'done' && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-2">Coach onboarding complete</h2>
            {statusQuery.data?.goal && (
              <p className="text-sm text-muted mb-4">
                Goal: {statusQuery.data.goal.type} · {statusQuery.data.goal.distance}
                {statusQuery.data.goal.raceDate ? ` · ${statusQuery.data.goal.raceDate}` : ''}
                {statusQuery.data.goal.targetTimeS
                  ? ` · ${formatDuration(statusQuery.data.goal.targetTimeS)}`
                  : ''}
              </p>
            )}
            <Link href="/coach">
              <Button type="button">Back to Coach</Button>
            </Link>
          </Card>
        )}
      </div>
    </PageShell>
  );
}

export default function CoachOnboardingPage() {
  return (
    <AuthGuard>
      <CoachOnboardingContent />
    </AuthGuard>
  );
}
