'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  getCoachCheckIn,
  putCoachCheckIn,
  type CoachCheckInRun,
} from '@/lib/api';
import {
  formatWeekRangeLabel,
  resolveDefaultCheckInReferenceDate,
} from '@/lib/coachWeek';
import { useSettings } from '@/lib/settings';
import { formatDistance, formatDuration } from '@/lib/format';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function localDateInputValue(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function shiftReferenceDate(isoDate: string, deltaDays: number): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  date.setDate(date.getDate() + deltaDays);
  return localDateInputValue(date);
}

function CheckInPageContent() {
  const searchParams = useSearchParams();
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const initialRef =
    searchParams.get('referenceDate') || resolveDefaultCheckInReferenceDate(new Date());
  const [referenceDate, setReferenceDate] = useState(initialRef);
  const [overallFeel, setOverallFeel] = useState('');
  const [niggles, setNiggles] = useState('');
  const [lifeConstraints, setLifeConstraints] = useState('');
  const [runNotes, setRunNotes] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);

  const checkInQuery = useQuery({
    queryKey: ['coach-check-in', referenceDate, timezoneOffsetMinutes],
    queryFn: () => getCoachCheckIn(timezoneOffsetMinutes, referenceDate),
  });

  const data = checkInQuery.data;

  useEffect(() => {
    if (!data) return;
    const key = `${data.weekStartDate}:${data.updatedAt ?? 'unsaved'}`;
    if (hydratedKey === key) return;
    // Sync form fields from the loaded check-in document.
    /* eslint-disable react-hooks/set-state-in-effect -- hydrate editable fields from query */
    setOverallFeel(data.overallFeel ?? '');
    setNiggles(data.niggles ?? '');
    setLifeConstraints(data.lifeConstraints ?? '');
    const notes: Record<string, string> = {};
    for (const run of data.runs) {
      notes[run.workoutId] = run.notes ?? '';
    }
    setRunNotes(notes);
    setHydratedKey(key);
    /* eslint-enable react-hooks/set-state-in-effect */
  }, [data, hydratedKey]);

  useEffect(() => {
    // Reset hydration when the week picker changes.
    // eslint-disable-next-line react-hooks/set-state-in-effect -- clear hydrate key on week change
    setHydratedKey(null);
  }, [referenceDate]);

  const saveMutation = useMutation({
    mutationFn: () =>
      putCoachCheckIn({
        referenceDate,
        timezoneOffsetMinutes,
        overallFeel: overallFeel.trim() || null,
        niggles: niggles.trim() || null,
        lifeConstraints: lifeConstraints.trim() || null,
        runNotes: Object.entries(runNotes).map(([workoutId, notes]) => ({
          workoutId,
          notes: notes.trim() || null,
        })),
      }),
    onSuccess: async (saved) => {
      setError(null);
      await queryClient.setQueryData(
        ['coach-check-in', referenceDate, timezoneOffsetMinutes],
        saved
      );
      setHydratedKey(`${saved.weekStartDate}:${saved.updatedAt ?? 'unsaved'}`);
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-check-in'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-committed-week'] });
    },
    onError: (err: Error) => setError(err.message),
  });

  const weekLabel = useMemo(() => {
    if (!data) return null;
    return formatWeekRangeLabel(data.weekStartDate, data.weekEndDate);
  }, [data]);

  return (
    <PageShell
      density="control"
      title="Weekly check-in"
      subtitle="Auto-filled from Tempo · subjective fields optional"
      leading={
        <div className="mb-4">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Week
          </Link>
        </div>
      }
    >
      <div className="w-full space-y-6">
        <Card>
          <div className="flex flex-wrap items-end gap-3">
            <div className="flex flex-col gap-1">
              <label className="text-xs font-medium text-muted" htmlFor="check-in-ref">
                Week containing
              </label>
              <input
                id="check-in-ref"
                type="date"
                className={fieldClass}
                value={referenceDate}
                onChange={(e) => setReferenceDate(e.target.value)}
              />
            </div>
            <Button
              type="button"
              variant="secondary"
              onClick={() => setReferenceDate((d) => shiftReferenceDate(d, -7))}
            >
              Previous week
            </Button>
            <Button
              type="button"
              variant="secondary"
              onClick={() => setReferenceDate((d) => shiftReferenceDate(d, 7))}
            >
              Next week
            </Button>
            {weekLabel && (
              <p className="text-sm text-muted self-center">Mon–Sun: {weekLabel}</p>
            )}
          </div>
        </Card>

        {checkInQuery.isLoading ? (
          <Card>
            <p className="text-sm text-muted">Loading check-in...</p>
          </Card>
        ) : checkInQuery.isError ? (
          <Card>
            <p className="text-sm text-danger">
              {checkInQuery.error instanceof Error
                ? checkInQuery.error.message
                : 'Failed to load check-in'}
            </p>
          </Card>
        ) : (
          <>
            {data && data.adherenceHints.length > 0 && (
              <Card>
                <h2 className="text-lg font-semibold text-ink mb-2">Adherence hints</h2>
                <p className="text-xs text-muted mb-3">
                  Light signals vs a prior committed plan. Your notes explain intentional deviations.
                </p>
                <ul className="flex flex-wrap gap-2">
                  {data.adherenceHints.map((hint) => (
                    <li
                      key={`${hint.code}-${hint.date ?? 'x'}-${hint.message}`}
                      className="rounded-tempo border border-border bg-canvas px-3 py-1 text-xs text-ink"
                    >
                      {hint.message}
                    </li>
                  ))}
                </ul>
              </Card>
            )}

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Subjective (optional)</h2>
              <p className="text-xs text-muted mb-4">
                Skip any or all of these — you can still save a check-in from workouts alone.
              </p>
              <div className="space-y-4">
                <div>
                  <label className="text-xs font-medium text-muted" htmlFor="overall-feel">
                    Overall feel
                  </label>
                  <input
                    id="overall-feel"
                    className={`${fieldClass} mt-1`}
                    value={overallFeel}
                    onChange={(e) => setOverallFeel(e.target.value)}
                    placeholder="How did the week feel?"
                  />
                </div>
                <div>
                  <label className="text-xs font-medium text-muted" htmlFor="niggles">
                    Niggles
                  </label>
                  <textarea
                    id="niggles"
                    className={`${fieldClass} mt-1 min-h-[72px]`}
                    value={niggles}
                    onChange={(e) => setNiggles(e.target.value)}
                    placeholder="Aches, fatigue, anything to watch"
                  />
                </div>
                <div>
                  <label className="text-xs font-medium text-muted" htmlFor="life-constraints">
                    One-week life constraints
                  </label>
                  <textarea
                    id="life-constraints"
                    className={`${fieldClass} mt-1 min-h-[72px]`}
                    value={lifeConstraints}
                    onChange={(e) => setLifeConstraints(e.target.value)}
                    placeholder="Travel, late meetings, family — this week only"
                  />
                </div>
              </div>
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Runs this week</h2>
              <p className="text-xs text-muted mb-4">
                Metrics come from Tempo. Editing notes here does not change the activity page.
              </p>
              {!data || data.runs.length === 0 ? (
                <p className="text-sm text-muted">
                  No runs in this week — you can still save an empty check-in.
                </p>
              ) : (
                <ul className="space-y-4">
                  {data.runs.map((run) => (
                    <RunRow
                      key={run.workoutId}
                      run={run}
                      unitPreference={unitPreference}
                      notes={runNotes[run.workoutId] ?? ''}
                      onNotesChange={(value) =>
                        setRunNotes((prev) => ({ ...prev, [run.workoutId]: value }))
                      }
                    />
                  ))}
                </ul>
              )}
            </Card>

            {error && <p className="text-sm text-danger">{error}</p>}

            <div className="flex flex-wrap gap-2 items-center">
              <Button
                type="button"
                onClick={() => saveMutation.mutate()}
                disabled={saveMutation.isPending}
              >
                {saveMutation.isPending ? 'Saving...' : 'Save check-in'}
              </Button>
              <Button
                href={`/coach/preflight?referenceDate=${encodeURIComponent(referenceDate)}`}
                variant="secondary"
              >
                What your coach will see
              </Button>
              {data?.saved && (
                <p className="text-sm text-muted self-center">Saved for this week</p>
              )}
            </div>
          </>
        )}
      </div>
    </PageShell>
  );
}

function RunRow({
  run,
  unitPreference,
  notes,
  onNotesChange,
}: {
  run: CoachCheckInRun;
  unitPreference: 'metric' | 'imperial';
  notes: string;
  onNotesChange: (value: string) => void;
}) {
  const started = new Date(run.startedAt);
  const title = run.name || run.runType || 'Run';
  return (
    <li className="rounded-tempo border border-border p-3 space-y-2">
      <div className="flex flex-wrap justify-between gap-2">
        <div>
          <p className="text-sm font-medium text-ink">{title}</p>
          <p className="text-xs text-muted">
            {started.toLocaleString('en-GB', {
              weekday: 'short',
              month: 'short',
              day: 'numeric',
              hour: 'numeric',
              minute: '2-digit',
            })}
          </p>
        </div>
        <p className="text-sm text-muted">
          {formatDistance(run.distanceM, unitPreference)} · {formatDuration(run.durationS)}
          {run.avgHeartRateBpm != null ? ` · ${run.avgHeartRateBpm} bpm` : ''}
        </p>
      </div>
      <div>
        <label className="text-xs font-medium text-muted" htmlFor={`notes-${run.workoutId}`}>
          Check-in notes
          {run.notesEdited ? ' (edited)' : ''}
        </label>
        <textarea
          id={`notes-${run.workoutId}`}
          className={`${fieldClass} mt-1 min-h-[64px]`}
          value={notes}
          onChange={(e) => onNotesChange(e.target.value)}
          placeholder={run.workoutNotes || 'Optional note for this run'}
        />
        {run.workoutNotes && run.notesEdited && (
          <p className="text-xs text-muted mt-1">Activity notes unchanged: {run.workoutNotes}</p>
        )}
      </div>
    </li>
  );
}

function CheckInPageFallback() {
  return (
    <PageShell density="control" title="Weekly check-in">
      <p className="text-sm text-muted">Loading check-in…</p>
    </PageShell>
  );
}

export default function CoachCheckInPage() {
  return (
    <Suspense fallback={<CheckInPageFallback />}>
      <CheckInPageContent />
    </Suspense>
  );
}
