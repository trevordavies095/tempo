'use client';

import { useEffect, useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import {
  COACH_ANTHROPIC_MODELS,
  completeCoachOnboarding,
  getCoachOnboardingPrefill,
  getCoachProvider,
  putCoachProvider,
  testCoachProvider,
  type CoachLayer1HrZones,
  type CoachLayer1Paces,
} from '@/lib/api';
import { useSettings, type UnitPreference } from '@/lib/settings';
import { coachSettingsQueryKey } from '@/hooks/useCoachFeature';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const KM_PER_MILE = 1.609344;

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

export function CoachSetupForm({
  disclaimer,
  providerConnected,
  onComplete,
}: {
  disclaimer: string;
  providerConnected: boolean;
  onComplete: () => void;
}) {
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const paceUnitLabel = unitPreference === 'imperial' ? '/mi' : '/km';

  const [goalType, setGoalType] = useState<'race' | 'pr'>('race');
  const [distance, setDistance] = useState('Half-Marathon');
  const [raceDate, setRaceDate] = useState('');
  const [targetTimeInput, setTargetTimeInput] = useState('');
  const [notes, setNotes] = useState('');
  const [paceInputs, setPaceInputs] = useState<PaceInputs>(emptyPaceInputs);
  const [hrZones, setHrZones] = useState<CoachLayer1HrZones | null>(null);
  const [runDaysPerWeek, setRunDaysPerWeek] = useState<string>('');
  const [apiKey, setApiKey] = useState('');
  const [modelId, setModelId] = useState<string>(COACH_ANTHROPIC_MODELS[0].id);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [testing, setTesting] = useState(false);

  const prefillQuery = useQuery({
    queryKey: ['coach-onboarding-prefill'],
    queryFn: getCoachOnboardingPrefill,
  });

  const providerQuery = useQuery({
    queryKey: ['coach-provider'],
    queryFn: getCoachProvider,
    enabled: !providerConnected,
  });

  useEffect(() => {
    const prefill = prefillQuery.data;
    if (!prefill) return;
    if (prefill.suggestedPaces) {
      setPaceInputs(pacesToInputs(prefill.suggestedPaces, unitPreference));
    }
    if (prefill.hrZones) {
      setHrZones(prefill.hrZones);
    }
    if (prefill.suggestedRunDaysPerWeek != null) {
      setRunDaysPerWeek(String(prefill.suggestedRunDaysPerWeek));
    }
    if (prefill.goalDistances?.length && !prefill.goalDistances.includes(distance)) {
      setDistance(prefill.goalDistances[0]);
    }
  }, [prefillQuery.data, unitPreference, distance]);

  useEffect(() => {
    if (providerQuery.data?.modelId) {
      setModelId(providerQuery.data.modelId);
    }
  }, [providerQuery.data?.modelId]);

  const confirmedPaces = useMemo(
    () => pacesFromInputs(paceInputs, unitPreference),
    [paceInputs, unitPreference]
  );

  const goalDistances = prefillQuery.data?.goalDistances ?? [
    '5K',
    '10K',
    'Half-Marathon',
    'Marathon',
  ];

  const needsKey = !providerConnected;

  const handleTest = async () => {
    setTesting(true);
    setError(null);
    try {
      await testCoachProvider(apiKey.trim() || undefined);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Connection test failed');
    } finally {
      setTesting(false);
    }
  };

  const handleSubmit = async () => {
    setError(null);
    if (needsKey && !apiKey.trim()) {
      setError('Add your Anthropic API key to start coaching.');
      return;
    }
    if (goalType === 'race' && !raceDate) {
      setError('Race date is required for a race goal.');
      return;
    }
    let targetTimeS: number | null = null;
    if (targetTimeInput.trim()) {
      targetTimeS = parseDurationToSeconds(targetTimeInput);
      if (targetTimeS == null || targetTimeS <= 0) {
        setError('Target time must look like mm:ss or h:mm:ss.');
        return;
      }
    }
    if (goalType === 'pr' && (targetTimeS == null || targetTimeS <= 0)) {
      setError('Target time is required for a PR goal.');
      return;
    }
    if (!confirmedPaces) {
      setError(`Confirm all four paces as m:ss ${paceUnitLabel}.`);
      return;
    }

    setSubmitting(true);
    try {
      if (needsKey && apiKey.trim()) {
        await putCoachProvider(apiKey.trim(), modelId);
        await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
        await queryClient.invalidateQueries({ queryKey: ['coach-provider'] });
      }
      const runDays =
        runDaysPerWeek.trim() === '' ? null : Number(runDaysPerWeek);
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
        shoes: prefillQuery.data?.shoes,
        defaultShoeId: prefillQuery.data?.defaultShoeId ?? null,
        runDaysPerWeek: runDays,
      });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-onboarding'] });
      onComplete();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start coaching');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-6 max-w-2xl">
      <Card>
        <h2 className="text-lg font-semibold text-ink mb-2">Before you continue</h2>
        <p className="text-sm text-muted whitespace-pre-wrap">{disclaimer}</p>
      </Card>

      {needsKey && (
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">Anthropic connection</h2>
          <p className="text-sm text-muted mb-4">
            Your key stays on this instance and is only used when you send a plan to your coach.
          </p>
          <div className="space-y-3">
            <label className="block text-sm text-muted space-y-1">
              <span>API key</span>
              <input
                className={fieldClass}
                type="password"
                autoComplete="off"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder="sk-ant-…"
              />
            </label>
            <label className="block text-sm text-muted space-y-1">
              <span>Model</span>
              <select
                className={fieldClass}
                value={modelId}
                onChange={(e) => setModelId(e.target.value)}
              >
                {COACH_ANTHROPIC_MODELS.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.label}
                  </option>
                ))}
              </select>
            </label>
            <Button type="button" variant="secondary" disabled={testing} onClick={handleTest}>
              {testing ? 'Testing…' : 'Test connection'}
            </Button>
          </div>
        </Card>
      )}

      <Card>
        <h2 className="text-lg font-semibold text-ink mb-4">Primary goal</h2>
        <div className="space-y-3">
          <div className="flex flex-wrap gap-2">
            <Button
              type="button"
              variant={goalType === 'race' ? 'primary' : 'secondary'}
              onClick={() => setGoalType('race')}
            >
              Race
            </Button>
            <Button
              type="button"
              variant={goalType === 'pr' ? 'primary' : 'secondary'}
              onClick={() => setGoalType('pr')}
            >
              PR
            </Button>
          </div>
          <label className="block text-sm text-muted space-y-1">
            <span>Distance</span>
            <select
              className={fieldClass}
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
            <label className="block text-sm text-muted space-y-1">
              <span>Race date</span>
              <input
                className={fieldClass}
                type="date"
                value={raceDate}
                onChange={(e) => setRaceDate(e.target.value)}
              />
            </label>
          )}
          <label className="block text-sm text-muted space-y-1">
            <span>Target time {goalType === 'pr' ? '(required)' : '(optional)'}</span>
            <input
              className={fieldClass}
              value={targetTimeInput}
              onChange={(e) => setTargetTimeInput(e.target.value)}
              placeholder="1:30:00"
            />
          </label>
          <label className="block text-sm text-muted space-y-1">
            <span>Notes (optional)</span>
            <textarea
              className={fieldClass + ' min-h-[64px]'}
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </label>
        </div>
      </Card>

      <Card>
        <h2 className="text-lg font-semibold text-ink mb-1">Your paces & zones</h2>
        <p className="text-sm text-muted mb-4">
          {prefillQuery.data?.suggestedPaces
            ? 'Suggested from recent races — edit as needed.'
            : `Enter paces as m:ss ${paceUnitLabel}.`}
        </p>
        <div className="grid gap-3 sm:grid-cols-2">
          {PACE_FIELDS.map(({ key, label }) => (
            <label key={key} className="block text-sm text-muted space-y-1">
              <span>
                {label} ({paceUnitLabel})
              </span>
              <input
                className={fieldClass}
                value={paceInputs[key]}
                onChange={(e) =>
                  setPaceInputs((prev) => ({ ...prev, [key]: e.target.value }))
                }
                placeholder="5:30"
              />
            </label>
          ))}
        </div>
      </Card>

      <Card>
        <h2 className="text-lg font-semibold text-ink mb-1">Your week</h2>
        <p className="text-sm text-muted mb-4">
          {prefillQuery.data?.suggestedRunDaysPerWeek != null
            ? 'Suggested from your recent weeks — clear if you have no preference.'
            : 'Optional. How many days a week do you usually want to run?'}
        </p>
        <label className="block text-sm text-muted space-y-1 max-w-xs">
          <span>Run days per week</span>
          <select
            className={fieldClass}
            value={runDaysPerWeek}
            onChange={(e) => setRunDaysPerWeek(e.target.value)}
          >
            <option value="">No preference</option>
            {[1, 2, 3, 4, 5, 6, 7].map((n) => (
              <option key={n} value={String(n)}>
                {n}
              </option>
            ))}
          </select>
        </label>
      </Card>

      {error && <p className="text-sm text-danger">{error}</p>}

      <div className="space-y-2">
        <Button type="button" disabled={submitting} onClick={handleSubmit}>
          {submitting ? 'Starting…' : 'Start coaching'}
        </Button>
        <p className="text-sm text-muted">
          Add races, travel, or schedule rules any time in{' '}
          <a href="/coach/settings" className="underline text-ink">
            Settings
          </a>
          .
        </p>
      </div>
    </div>
  );
}
