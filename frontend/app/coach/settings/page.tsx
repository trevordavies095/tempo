'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  ConstraintFormFields,
  coachConstraintToRequest,
  describeCoachConstraint,
  emptyConstraintDraft,
  type ConstraintDraft,
} from '@/components/coach/ConstraintFormFields';
import {
  COACH_ANTHROPIC_MODELS,
  completeCoachOnboarding,
  createCoachConstraint,
  createCoachMemoryNote,
  deleteAllCoachData,
  deleteCoachProvider,
  deleteCoachSetup,
  getCoachOnboarding,
  getCoachOnboardingPrefill,
  getCoachProvider,
  listCoachConstraints,
  listCoachEpisodes,
  listCoachRules,
  putCoachProvider,
  retireCoachConstraint,
  retireCoachEpisode,
  retireCoachRule,
  testCoachProvider,
  unretireCoachEpisode,
  unretireCoachRule,
  updateCoachConstraint,
  type CoachLayer1Paces,
} from '@/lib/api';
import { memorySourceLabel } from '@/lib/coachMemoryDisplay';
import { formatCoachDate } from '@/lib/coachWeek';
import { useSettings, type UnitPreference } from '@/lib/settings';
import { coachSettingsQueryKey, useCoachFeature } from '@/hooks/useCoachFeature';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const KM_PER_MILE = 1.609344;
const PRIVACY_NOTICE =
  'Sent to Anthropic with your weekly packet — avoid names and addresses.';

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

type MemoryRow = {
  key: string;
  kind: 'episode' | 'rule';
  text: string;
  date: string | null;
  source: string;
  retired: boolean;
  sortAt: number;
};

export default function CoachSettingsPage() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const { providerConnected, modelId: settingsModelId } = useCoachFeature();
  const paceUnitLabel = unitPreference === 'imperial' ? '/mi' : '/km';

  const statusQuery = useQuery({
    queryKey: ['coach-onboarding'],
    queryFn: getCoachOnboarding,
  });
  const prefillQuery = useQuery({
    queryKey: ['coach-onboarding-prefill'],
    queryFn: getCoachOnboardingPrefill,
  });
  const providerQuery = useQuery({
    queryKey: ['coach-provider'],
    queryFn: getCoachProvider,
  });

  const [goalType, setGoalType] = useState<'race' | 'pr'>('race');
  const [distance, setDistance] = useState('Half-Marathon');
  const [raceDate, setRaceDate] = useState('');
  const [targetTimeInput, setTargetTimeInput] = useState('');
  const [notes, setNotes] = useState('');
  const [paceInputs, setPaceInputs] = useState<PaceInputs>(emptyPaceInputs);
  const [runDaysPerWeek, setRunDaysPerWeek] = useState<string>('');
  const [profileError, setProfileError] = useState<string | null>(null);
  const [profileSaving, setProfileSaving] = useState(false);

  const [showRetiredConstraints, setShowRetiredConstraints] = useState(false);
  const [constraintDraft, setConstraintDraft] = useState<ConstraintDraft>(emptyConstraintDraft());
  const [editingConstraintId, setEditingConstraintId] = useState<string | null>(null);
  const [constraintError, setConstraintError] = useState<string | null>(null);

  const [showRemovedMemory, setShowRemovedMemory] = useState(false);
  const [noteText, setNoteText] = useState('');
  const [noteDate, setNoteDate] = useState('');
  const [memoryError, setMemoryError] = useState<string | null>(null);

  const [apiKey, setApiKey] = useState('');
  const [modelId, setModelId] = useState<string>(COACH_ANTHROPIC_MODELS[0].id);
  const [providerBusy, setProviderBusy] = useState(false);
  const [providerError, setProviderError] = useState<string | null>(null);
  const [providerSuccess, setProviderSuccess] = useState<string | null>(null);

  const [confirmReset, setConfirmReset] = useState(false);
  const [confirmDeleteAll, setConfirmDeleteAll] = useState(false);
  const [dangerBusy, setDangerBusy] = useState(false);
  const [dangerError, setDangerError] = useState<string | null>(null);

  useEffect(() => {
    const status = statusQuery.data;
    if (!status?.goal || !status.layer1) return;
    setGoalType(status.goal.type === 'pr' ? 'pr' : 'race');
    setDistance(status.goal.distance);
    setRaceDate(status.goal.raceDate ?? '');
    setTargetTimeInput(
      status.goal.targetTimeS != null
        ? (() => {
            const t = status.goal!.targetTimeS!;
            const h = Math.floor(t / 3600);
            const m = Math.floor((t % 3600) / 60);
            const s = t % 60;
            return h > 0
              ? `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
              : `${m}:${s.toString().padStart(2, '0')}`;
          })()
        : ''
    );
    setNotes(status.goal.notes ?? '');
    if (status.layer1.paces) {
      setPaceInputs(pacesToInputs(status.layer1.paces, unitPreference));
    }
    setRunDaysPerWeek(
      status.layer1.runDaysPerWeek != null ? String(status.layer1.runDaysPerWeek) : ''
    );
  }, [statusQuery.data, unitPreference]);

  useEffect(() => {
    const mid = providerQuery.data?.modelId ?? settingsModelId;
    if (mid) setModelId(mid);
  }, [providerQuery.data?.modelId, settingsModelId]);

  const constraintsQuery = useQuery({
    queryKey: ['coach-constraints', showRetiredConstraints ? 'all' : 'active'],
    queryFn: () => listCoachConstraints(showRetiredConstraints ? 'all' : 'active'),
  });

  const episodesQuery = useQuery({
    queryKey: ['coach-episodes', showRemovedMemory],
    queryFn: () => listCoachEpisodes({ includeRetired: showRemovedMemory }),
  });
  const rulesQuery = useQuery({
    queryKey: ['coach-rules', showRemovedMemory],
    queryFn: () => listCoachRules({ includeRetired: showRemovedMemory }),
  });

  const memoryRows = useMemo(() => {
    const rows: MemoryRow[] = [];
    for (const ep of episodesQuery.data ?? []) {
      if (!showRemovedMemory && ep.retired) continue;
      rows.push({
        key: `ep-${ep.id}`,
        kind: 'episode',
        text: ep.summary,
        date: ep.date,
        source: ep.source,
        retired: ep.retired,
        sortAt: new Date(ep.date + 'T00:00:00').getTime(),
      });
    }
    for (const rule of rulesQuery.data ?? []) {
      const retired = rule.status === 'retired';
      if (!showRemovedMemory && retired) continue;
      rows.push({
        key: `rule-${rule.id}`,
        kind: 'rule',
        text: rule.ruleText,
        date: null,
        source: rule.source,
        retired,
        sortAt: new Date(rule.updatedAt || rule.createdAt).getTime(),
      });
    }
    rows.sort((a, b) => b.sortAt - a.sortAt);
    return rows;
  }, [episodesQuery.data, rulesQuery.data, showRemovedMemory]);

  const goalDistances = prefillQuery.data?.goalDistances ?? [
    '5K',
    '10K',
    'Half-Marathon',
    'Marathon',
  ];

  const saveProfile = async () => {
    setProfileError(null);
    const paces = pacesFromInputs(paceInputs, unitPreference);
    if (!paces) {
      setProfileError(`Confirm all four paces as m:ss ${paceUnitLabel}.`);
      return;
    }
    if (goalType === 'race' && !raceDate) {
      setProfileError('Race date is required.');
      return;
    }
    let targetTimeS: number | null = null;
    if (targetTimeInput.trim()) {
      targetTimeS = parseDurationToSeconds(targetTimeInput);
      if (targetTimeS == null || targetTimeS <= 0) {
        setProfileError('Target time must look like mm:ss or h:mm:ss.');
        return;
      }
    }
    if (goalType === 'pr' && (targetTimeS == null || targetTimeS <= 0)) {
      setProfileError('Target time is required for a PR goal.');
      return;
    }
    setProfileSaving(true);
    try {
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
        paces,
        hrZones: statusQuery.data?.layer1?.hrZones ?? prefillQuery.data?.hrZones ?? undefined,
        shoes: statusQuery.data?.layer1?.shoes ?? prefillQuery.data?.shoes,
        defaultShoeId:
          statusQuery.data?.layer1?.defaultShoeId ?? prefillQuery.data?.defaultShoeId ?? null,
        runDaysPerWeek: runDays,
      });
      await queryClient.invalidateQueries({ queryKey: ['coach-onboarding'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
    } catch (err) {
      setProfileError(err instanceof Error ? err.message : 'Failed to save profile');
    } finally {
      setProfileSaving(false);
    }
  };

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

  const noteMutation = useMutation({
    mutationFn: () =>
      createCoachMemoryNote({
        text: noteText.trim(),
        date: noteDate.trim() || null,
      }),
    onSuccess: async () => {
      setMemoryError(null);
      setNoteText('');
      setNoteDate('');
      await queryClient.invalidateQueries({ queryKey: ['coach-episodes'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-rules'] });
    },
    onError: (err: Error) => setMemoryError(err.message),
  });

  const invalidateProvider = async () => {
    await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
    await queryClient.invalidateQueries({ queryKey: ['coach-provider'] });
    await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
  };

  return (
    <PageShell
      density="control"
      title="Settings"
      subtitle="Profile, fixed dates, and what your coach knows about you"
    >
      <div className="w-full max-w-2xl space-y-8">
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-4">Profile & goal</h2>
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
              <span>Target time</span>
              <input
                className={fieldClass}
                value={targetTimeInput}
                onChange={(e) => setTargetTimeInput(e.target.value)}
                placeholder="1:30:00"
              />
            </label>
            <label className="block text-sm text-muted space-y-1">
              <span>Notes</span>
              <textarea
                className={fieldClass + ' min-h-[64px]'}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            </label>
            <h3 className="text-sm font-semibold text-ink pt-2">Paces ({paceUnitLabel})</h3>
            <div className="grid gap-3 sm:grid-cols-2">
              {PACE_FIELDS.map(({ key, label }) => (
                <label key={key} className="block text-sm text-muted space-y-1">
                  <span>{label}</span>
                  <input
                    className={fieldClass}
                    value={paceInputs[key]}
                    onChange={(e) =>
                      setPaceInputs((prev) => ({ ...prev, [key]: e.target.value }))
                    }
                  />
                </label>
              ))}
            </div>
            <h3 className="text-sm font-semibold text-ink pt-2">Your week</h3>
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
            {profileError && <p className="text-sm text-danger">{profileError}</p>}
            <Button type="button" disabled={profileSaving} onClick={saveProfile}>
              {profileSaving ? 'Saving…' : 'Save profile'}
            </Button>
          </div>
        </Card>

        <Card>
          <div className="flex flex-wrap items-center justify-between gap-2 mb-4">
            <h2 className="text-lg font-semibold text-ink">Fixed dates & schedule</h2>
            <Button
              type="button"
              variant="secondary"
              onClick={() => setShowRetiredConstraints((v) => !v)}
            >
              {showRetiredConstraints ? 'Hide retired' : 'Show retired'}
            </Button>
          </div>
          <div className="space-y-4 mb-6">
            {(constraintsQuery.data ?? []).map((c) => (
              <div
                key={c.id}
                className="flex flex-wrap items-start justify-between gap-2 rounded-tempo border border-border p-3"
              >
                <p className="text-sm text-ink">{describeCoachConstraint(c)}</p>
                {!c.isRetired && (
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
                      variant="danger"
                      onClick={() => retireCoachConstraint(c.id).then(() =>
                        queryClient.invalidateQueries({ queryKey: ['coach-constraints'] })
                      )}
                    >
                      Remove
                    </Button>
                  </div>
                )}
              </div>
            ))}
          </div>
          <h3 className="text-sm font-semibold text-ink mb-2">
            {editingConstraintId ? 'Edit fixed date' : 'Add fixed date'}
          </h3>
          <ConstraintFormFields value={constraintDraft} onChange={setConstraintDraft} />
          {constraintError && <p className="text-sm text-danger mt-2">{constraintError}</p>}
          <div className="flex flex-wrap gap-2 mt-3">
            <Button
              type="button"
              disabled={saveConstraintMutation.isPending}
              onClick={() => saveConstraintMutation.mutate()}
            >
              {saveConstraintMutation.isPending ? 'Saving…' : 'Save'}
            </Button>
            {editingConstraintId && (
              <Button
                type="button"
                variant="secondary"
                onClick={() => {
                  setEditingConstraintId(null);
                  setConstraintDraft(emptyConstraintDraft());
                }}
              >
                Cancel
              </Button>
            )}
          </div>
        </Card>

        <Card>
          <h2 className="text-lg font-semibold text-ink mb-1">
            What your coach knows about you
          </h2>
          <p className="text-xs text-muted mb-4">{PRIVACY_NOTICE}</p>
          <ul className="space-y-3 mb-6">
            {memoryRows.length === 0 && (
              <li className="text-sm text-muted">Nothing saved yet.</li>
            )}
            {memoryRows.map((row) => (
              <li
                key={row.key}
                className={`rounded-tempo border border-border p-3 ${
                  row.retired ? 'opacity-60' : ''
                }`}
              >
                <div className="flex flex-wrap items-start justify-between gap-2">
                  <div>
                    <p className="text-sm text-ink">{row.text}</p>
                    <p className="text-xs text-muted mt-1">
                      {row.date ? formatCoachDate(row.date) + ' · ' : ''}
                      {memorySourceLabel(row.source)}
                      {row.retired ? ' · removed' : ''}
                    </p>
                  </div>
                  {row.retired ? (
                    <Button
                      type="button"
                      variant="secondary"
                      onClick={() => {
                        const id = row.key.replace(/^(ep|rule)-/, '');
                        const action =
                          row.kind === 'episode'
                            ? unretireCoachEpisode(id)
                            : unretireCoachRule(id);
                        void action.then(() => {
                          void queryClient.invalidateQueries({ queryKey: ['coach-episodes'] });
                          void queryClient.invalidateQueries({ queryKey: ['coach-rules'] });
                        });
                      }}
                    >
                      Restore
                    </Button>
                  ) : (
                    <Button
                      type="button"
                      variant="danger"
                      onClick={() => {
                        const id = row.key.replace(/^(ep|rule)-/, '');
                        const action =
                          row.kind === 'episode'
                            ? retireCoachEpisode(id)
                            : retireCoachRule(id);
                        void action.then(() => {
                          void queryClient.invalidateQueries({ queryKey: ['coach-episodes'] });
                          void queryClient.invalidateQueries({ queryKey: ['coach-rules'] });
                        });
                      }}
                    >
                      Remove
                    </Button>
                  )}
                </div>
              </li>
            ))}
          </ul>
          <Button
            type="button"
            variant="secondary"
            className="mb-4"
            onClick={() => setShowRemovedMemory((v) => !v)}
          >
            {showRemovedMemory ? 'Hide removed' : 'Show removed'}
          </Button>
          <h3 className="text-sm font-semibold text-ink mb-2">Tell your coach something</h3>
          <div className="space-y-3">
            <textarea
              className={fieldClass + ' min-h-[80px]'}
              value={noteText}
              onChange={(e) => setNoteText(e.target.value)}
              placeholder="A lasting preference or a dated milestone"
            />
            <label className="block text-sm text-muted space-y-1">
              <span>Date (optional — dated notes become events)</span>
              <input
                className={fieldClass}
                type="date"
                value={noteDate}
                onChange={(e) => setNoteDate(e.target.value)}
              />
            </label>
            {memoryError && <p className="text-sm text-danger">{memoryError}</p>}
            <Button
              type="button"
              disabled={noteMutation.isPending || !noteText.trim()}
              onClick={() => noteMutation.mutate()}
            >
              {noteMutation.isPending ? 'Saving…' : 'Save note'}
            </Button>
          </div>
        </Card>

        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">Anthropic connection</h2>
          <p className="text-sm text-muted mb-4">
            {providerConnected || providerQuery.data?.connected
              ? 'Connected. Replace the key below if needed.'
              : 'Not connected.'}
          </p>
          <div className="space-y-3">
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
            <label className="block text-sm text-muted space-y-1">
              <span>API key</span>
              <input
                className={fieldClass}
                type="password"
                autoComplete="off"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder={providerConnected ? 'Replace API key' : 'sk-ant-…'}
              />
            </label>
            <div className="flex flex-wrap gap-2">
              <Button
                type="button"
                disabled={providerBusy || !apiKey.trim()}
                onClick={async () => {
                  setProviderBusy(true);
                  setProviderError(null);
                  setProviderSuccess(null);
                  try {
                    await putCoachProvider(apiKey.trim(), modelId);
                    setApiKey('');
                    setProviderSuccess('API key saved.');
                    await invalidateProvider();
                  } catch (err) {
                    setProviderError(err instanceof Error ? err.message : 'Save failed');
                  } finally {
                    setProviderBusy(false);
                  }
                }}
              >
                Save key
              </Button>
              <Button
                type="button"
                variant="secondary"
                disabled={providerBusy || (!apiKey.trim() && !providerConnected)}
                onClick={async () => {
                  setProviderBusy(true);
                  setProviderError(null);
                  setProviderSuccess(null);
                  try {
                    await testCoachProvider(apiKey.trim() || undefined);
                    setProviderSuccess('Connection OK.');
                    await invalidateProvider();
                  } catch (err) {
                    setProviderError(err instanceof Error ? err.message : 'Test failed');
                  } finally {
                    setProviderBusy(false);
                  }
                }}
              >
                Test connection
              </Button>
              {(providerConnected || providerQuery.data?.connected) && (
                <Button
                  type="button"
                  variant="danger"
                  disabled={providerBusy}
                  onClick={async () => {
                    setProviderBusy(true);
                    setProviderError(null);
                    try {
                      await deleteCoachProvider();
                      await invalidateProvider();
                    } catch (err) {
                      setProviderError(err instanceof Error ? err.message : 'Remove failed');
                    } finally {
                      setProviderBusy(false);
                    }
                  }}
                >
                  Remove key
                </Button>
              )}
            </div>
            {providerError && <p className="text-sm text-danger">{providerError}</p>}
            {providerSuccess && !providerError && (
              <p className="text-sm text-ink">{providerSuccess}</p>
            )}
          </div>
        </Card>

        <Card>
          <h2 className="text-lg font-semibold text-ink mb-4">Danger zone</h2>
          <div className="space-y-6">
            <div className="space-y-3">
              <h3 className="text-sm font-semibold text-ink">Reset coach setup</h3>
              <p className="text-sm text-muted">
                Clears goal, paces & zones, fixed dates, check-ins, plans, and jobs. Keeps Coach
                enabled and your Anthropic key.
              </p>
              {!confirmReset ? (
                <Button type="button" variant="danger" onClick={() => setConfirmReset(true)}>
                  Reset coach setup
                </Button>
              ) : (
                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => setConfirmReset(false)}
                    disabled={dangerBusy}
                  >
                    Cancel
                  </Button>
                  <Button
                    type="button"
                    variant="danger"
                    disabled={dangerBusy}
                    onClick={async () => {
                      setDangerBusy(true);
                      setDangerError(null);
                      try {
                        await deleteCoachSetup();
                        setConfirmReset(false);
                        await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
                        await queryClient.invalidateQueries({ queryKey: ['coach-onboarding'] });
                        router.push('/coach');
                      } catch (err) {
                        setDangerError(err instanceof Error ? err.message : 'Reset failed');
                      } finally {
                        setDangerBusy(false);
                      }
                    }}
                  >
                    {dangerBusy ? 'Resetting…' : 'Confirm reset'}
                  </Button>
                </div>
              )}
            </div>

            <div className="space-y-3 border-t border-border pt-4">
              <h3 className="text-sm font-semibold text-ink">Delete all coach data</h3>
              <p className="text-sm text-muted">
                Removes everything including your Anthropic key and opt-in. Workouts and shoes stay.
                You return to global Settings as a fresh opt-in.
              </p>
              {!confirmDeleteAll ? (
                <Button type="button" variant="danger" onClick={() => setConfirmDeleteAll(true)}>
                  Delete all coach data
                </Button>
              ) : (
                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => setConfirmDeleteAll(false)}
                    disabled={dangerBusy}
                  >
                    Cancel
                  </Button>
                  <Button
                    type="button"
                    variant="danger"
                    disabled={dangerBusy}
                    onClick={async () => {
                      setDangerBusy(true);
                      setDangerError(null);
                      try {
                        await deleteAllCoachData();
                        await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
                        await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
                        router.push('/settings');
                      } catch (err) {
                        setDangerError(err instanceof Error ? err.message : 'Delete failed');
                      } finally {
                        setDangerBusy(false);
                      }
                    }}
                  >
                    {dangerBusy ? 'Deleting…' : 'Confirm delete all'}
                  </Button>
                </div>
              )}
            </div>
            {dangerError && <p className="text-sm text-danger">{dangerError}</p>}
          </div>
        </Card>
      </div>
    </PageShell>
  );
}
