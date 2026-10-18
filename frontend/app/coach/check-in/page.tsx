'use client';

import { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  getCoachCheckIn,
  getCoachHome,
  getCoachJob,
  postCoachPrivacyPreflight,
  putCoachCheckIn,
  sendCoachCheckIn,
  type CoachCheckInRun,
  type CoachJob,
  type CoachPrivacyPreflight,
  type PutCoachCheckInRequest,
} from '@/lib/api';
import {
  formatCoachJobError,
  formatWeekRangeLabel,
  pollCoachJobUntilSettled,
  resolveDefaultCheckInReferenceDate,
} from '@/lib/coachWeek';
import { useSettings } from '@/lib/settings';
import { formatDistance, formatDuration } from '@/lib/format';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const AUTOSAVE_MS = 600;

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

function ChipRow({
  values,
  selected,
  onSelect,
  ariaLabel,
}: {
  values: number[];
  selected: number | null;
  onSelect: (value: number | null) => void;
  ariaLabel: string;
}) {
  return (
    <div className="flex flex-wrap gap-2" role="group" aria-label={ariaLabel}>
      {values.map((n) => {
        const active = selected === n;
        return (
          <button
            key={n}
            type="button"
            className={`min-w-[2.25rem] rounded-tempo border px-2.5 py-1.5 text-sm ${
              active
                ? 'border-volt bg-volt/15 text-ink'
                : 'border-border bg-canvas text-muted hover:text-ink'
            }`}
            aria-pressed={active}
            onClick={() => onSelect(active ? null : n)}
          >
            {n}
          </button>
        );
      })}
    </div>
  );
}

function CheckInPageContent() {
  const searchParams = useSearchParams();
  const router = useRouter();
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const initialRef =
    searchParams.get('referenceDate') || resolveDefaultCheckInReferenceDate(new Date());
  const hasQueryRef = Boolean(searchParams.get('referenceDate'));

  const [referenceDate, setReferenceDate] = useState(initialRef);
  const [showWeekPicker, setShowWeekPicker] = useState(hasQueryRef);
  const [overallFeel, setOverallFeel] = useState('');
  const [feelScore, setFeelScore] = useState<number | null>(null);
  const [niggles, setNiggles] = useState('');
  const [lifeConstraints, setLifeConstraints] = useState('');
  const [questionForCoach, setQuestionForCoach] = useState('');
  const [runNotes, setRunNotes] = useState<Record<string, string>>({});
  const [runRpes, setRunRpes] = useState<Record<string, number | null>>({});
  const [error, setError] = useState<string | null>(null);
  const [hydratedKey, setHydratedKey] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [saveStatus, setSaveStatus] = useState<'idle' | 'saving' | 'saved'>('idle');
  const [preview, setPreview] = useState<CoachPrivacyPreflight | null>(null);
  const [inspectOpen, setInspectOpen] = useState(false);
  const [job, setJob] = useState<CoachJob | null>(null);
  const [jobPolling, setJobPolling] = useState(false);
  const skipAutosaveUntil = useRef(0);

  const checkInQuery = useQuery({
    queryKey: ['coach-check-in', referenceDate, timezoneOffsetMinutes],
    queryFn: () => getCoachCheckIn(timezoneOffsetMinutes, referenceDate),
  });

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
  });

  const data = checkInQuery.data;
  const disclaimer =
    homeQuery.data?.disclaimer ??
    'Your redacted packet is sent to Anthropic with your BYO key. Review what will be sent before you send.';

  useEffect(() => {
    if (!data) return;
    const key = `${data.weekStartDate}:${data.updatedAt ?? 'unsaved'}`;
    if (hydratedKey === key) return;
    setOverallFeel(data.overallFeel ?? '');
    setFeelScore(data.feelScore ?? null);
    setNiggles(data.niggles ?? '');
    setLifeConstraints(data.lifeConstraints ?? '');
    setQuestionForCoach(data.questionForCoach ?? '');
    const notes: Record<string, string> = {};
    const rpes: Record<string, number | null> = {};
    for (const run of data.runs) {
      notes[run.workoutId] = run.notes ?? '';
      rpes[run.workoutId] = run.rpe ?? null;
    }
    setRunNotes(notes);
    setRunRpes(rpes);
    setHydratedKey(key);
    setDirty(false);
    skipAutosaveUntil.current = Date.now() + AUTOSAVE_MS + 50;
  }, [data, hydratedKey]);

  useEffect(() => {
    setHydratedKey(null);
    setPreview(null);
    setDirty(false);
    setSaveStatus('idle');
  }, [referenceDate]);

  const buildBody = (): PutCoachCheckInRequest => {
    const workoutIds = new Set([
      ...Object.keys(runNotes),
      ...Object.keys(runRpes),
      ...(data?.runs.map((r) => r.workoutId) ?? []),
    ]);
    return {
      referenceDate,
      timezoneOffsetMinutes,
      overallFeel: overallFeel.trim() || null,
      feelScore,
      niggles: niggles.trim() || null,
      lifeConstraints: lifeConstraints.trim() || null,
      questionForCoach: questionForCoach.trim() || null,
      runNotes: [...workoutIds].map((workoutId) => ({
        workoutId,
        notes: (runNotes[workoutId] ?? '').trim() || null,
        rpe: runRpes[workoutId] ?? null,
      })),
    };
  };

  const saveMutation = useMutation({
    mutationFn: () => putCoachCheckIn(buildBody()),
    onMutate: () => setSaveStatus('saving'),
    onSuccess: async (saved) => {
      setError(null);
      setDirty(false);
      setSaveStatus('saved');
      await queryClient.setQueryData(
        ['coach-check-in', referenceDate, timezoneOffsetMinutes],
        saved
      );
      setHydratedKey(`${saved.weekStartDate}:${saved.updatedAt ?? 'unsaved'}`);
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-committed-week'] });
      try {
        const assembled = await postCoachPrivacyPreflight(timezoneOffsetMinutes, referenceDate);
        setPreview(assembled);
      } catch {
        // Preview is best-effort; send still validates on the server.
      }
    },
    onError: (err: Error) => {
      setSaveStatus('idle');
      setError(err.message);
    },
  });

  useEffect(() => {
    if (!dirty || !hydratedKey) return;
    if (Date.now() < skipAutosaveUntil.current) return;
    const handle = window.setTimeout(() => {
      if (!saveMutation.isPending) {
        saveMutation.mutate();
      }
    }, AUTOSAVE_MS);
    return () => window.clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- debounce on field dirtiness only
  }, [
    dirty,
    hydratedKey,
    overallFeel,
    feelScore,
    niggles,
    lifeConstraints,
    questionForCoach,
    runNotes,
    runRpes,
  ]);

  const markDirty = () => {
    setDirty(true);
    setSaveStatus('idle');
  };

  const sendMutation = useMutation({
    mutationFn: async () => {
      const started = await sendCoachCheckIn(buildBody());
      setJob(started);
      setJobPolling(true);
      return pollCoachJobUntilSettled(started, (j) => getCoachJob(j.id), {
        onUpdate: setJob,
      });
    },
    onSuccess: async (finished) => {
      setJobPolling(false);
      setDirty(false);
      setSaveStatus('saved');
      await queryClient.invalidateQueries({ queryKey: ['coach-check-in'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-jobs-current'] });
      if (finished.status === 'ready') {
        router.push(
          `/coach/review?weekStartDate=${encodeURIComponent(finished.weekStartDate)}`
        );
      } else {
        setError(formatCoachJobError(finished.errorCode, finished.errorMessage));
      }
    },
    onError: (err: Error) => {
      setJobPolling(false);
      setError(err.message);
    },
  });

  const weekLabel = useMemo(() => {
    if (!data) return null;
    return formatWeekRangeLabel(data.weekStartDate, data.weekEndDate);
  }, [data]);

  const prettyPayload = useMemo(() => {
    if (!preview?.redactedPayload) return '';
    try {
      return JSON.stringify(preview.redactedPayload, null, 2);
    } catch {
      return String(preview.redactedPayload);
    }
  }, [preview]);

  const sending = sendMutation.isPending || jobPolling;

  return (
    <PageShell
      density="control"
      title="Weekly check-in"
      subtitle="Auto-filled from Tempo · subjective fields optional"
      leading={
        <div className="mb-4 flex flex-wrap items-center justify-between gap-2">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Week
          </Link>
          <p className="text-xs text-muted" aria-live="polite">
            {saveStatus === 'saving'
              ? 'Saving…'
              : saveStatus === 'saved'
                ? 'Saved'
                : dirty
                  ? 'Unsaved changes'
                  : null}
          </p>
        </div>
      }
    >
      <div className="w-full space-y-6">
        <Card>
          <div className="space-y-3">
            {weekLabel && (
              <p className="text-sm text-ink">
                Check-in week: <span className="text-muted">{weekLabel}</span>
              </p>
            )}
            {!showWeekPicker ? (
              <button
                type="button"
                className="text-sm text-muted underline hover:text-ink"
                onClick={() => setShowWeekPicker(true)}
              >
                Different week…
              </button>
            ) : (
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
              </div>
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
              <h2 className="text-lg font-semibold text-ink mb-1">How did the week feel?</h2>
              <p className="text-xs text-muted mb-4">Optional — skip anything you do not want to share.</p>
              <div className="space-y-4">
                <div>
                  <p className="text-xs font-medium text-muted mb-2">Feel (1–5)</p>
                  <ChipRow
                    values={[1, 2, 3, 4, 5]}
                    selected={feelScore}
                    onSelect={(v) => {
                      setFeelScore(v);
                      markDirty();
                    }}
                    ariaLabel="Feel score"
                  />
                </div>
                <div>
                  <label className="text-xs font-medium text-muted" htmlFor="overall-feel">
                    Feel notes
                  </label>
                  <input
                    id="overall-feel"
                    className={`${fieldClass} mt-1`}
                    value={overallFeel}
                    onChange={(e) => {
                      setOverallFeel(e.target.value);
                      markDirty();
                    }}
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
                    onChange={(e) => {
                      setNiggles(e.target.value);
                      markDirty();
                    }}
                    placeholder="Aches, fatigue, anything to watch"
                  />
                </div>
                <div>
                  <label className="text-xs font-medium text-muted" htmlFor="life-constraints">
                    Anything the coach should know for next week?
                  </label>
                  <textarea
                    id="life-constraints"
                    className={`${fieldClass} mt-1 min-h-[72px]`}
                    value={lifeConstraints}
                    onChange={(e) => {
                      setLifeConstraints(e.target.value);
                      markDirty();
                    }}
                    placeholder="Travel, late meetings, family — next week only"
                  />
                </div>
                <div>
                  {data?.clarifyingQuestion && (
                    <p className="text-sm text-ink mb-2">
                      Your coach asked:{' '}
                      <span className="text-muted">{data.clarifyingQuestion}</span>
                    </p>
                  )}
                  <label className="text-xs font-medium text-muted" htmlFor="question-for-coach">
                    Ask your coach
                  </label>
                  <textarea
                    id="question-for-coach"
                    className={`${fieldClass} mt-1 min-h-[72px]`}
                    value={questionForCoach}
                    onChange={(e) => {
                      setQuestionForCoach(e.target.value);
                      markDirty();
                    }}
                    placeholder="A question for next week’s plan"
                  />
                </div>
              </div>
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Runs this week</h2>
              <p className="text-xs text-muted mb-4">
                Metrics come from Tempo. RPE and notes here do not change the activity page.
              </p>
              {!data || data.runs.length === 0 ? (
                <p className="text-sm text-muted">
                  No runs in this week — you can still send a check-in.
                </p>
              ) : (
                <ul className="space-y-4">
                  {data.runs.map((run) => (
                    <RunRow
                      key={run.workoutId}
                      run={run}
                      unitPreference={unitPreference}
                      notes={runNotes[run.workoutId] ?? ''}
                      rpe={runRpes[run.workoutId] ?? null}
                      onNotesChange={(value) => {
                        setRunNotes((prev) => ({ ...prev, [run.workoutId]: value }));
                        markDirty();
                      }}
                      onRpeChange={(value) => {
                        setRunRpes((prev) => ({ ...prev, [run.workoutId]: value }));
                        markDirty();
                      }}
                    />
                  ))}
                </ul>
              )}
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">What your coach will see</h2>
              <p className="text-xs text-muted mb-4">
                Assembled after each autosave. Nothing is sent until you tap Send to coach.
              </p>
              {!preview ? (
                <p className="text-sm text-muted">
                  {saveMutation.isPending || dirty
                    ? 'Updating preview…'
                    : 'Save once to refresh the privacy summary.'}
                </p>
              ) : (
                <>
                  <h3 className="text-sm font-medium text-ink mb-2">Included</h3>
                  <ul className="space-y-2 mb-4">
                    {preview.report.included.map((item) => (
                      <li key={`in-${item.category}`} className="text-sm text-ink">
                        <span className="font-medium">{item.category.replaceAll('_', ' ')}</span>
                        <span className="text-muted"> — {item.detail}</span>
                      </li>
                    ))}
                  </ul>
                  <h3 className="text-sm font-medium text-ink mb-2">Stripped</h3>
                  <ul className="space-y-2 mb-4">
                    {preview.report.stripped.map((item) => (
                      <li key={`out-${item.category}`} className="text-sm text-ink">
                        <span className="font-medium">{item.category.replaceAll('_', ' ')}</span>
                        <span className="text-muted"> — {item.detail}</span>
                      </li>
                    ))}
                  </ul>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => setInspectOpen((o) => !o)}
                  >
                    {inspectOpen ? 'Hide JSON' : 'Inspect JSON'}
                  </Button>
                  {inspectOpen && (
                    <pre className="mt-3 overflow-auto rounded-tempo border border-border bg-canvas p-3 text-xs text-ink max-h-[28rem]">
                      {prettyPayload}
                    </pre>
                  )}
                </>
              )}
            </Card>

            {error && <p className="text-sm text-danger">{error}</p>}
            {job && job.status !== 'ready' && (
              <p className="text-sm text-muted" aria-live="polite">
                {job.status === 'failed'
                  ? formatCoachJobError(job.errorCode, job.errorMessage)
                  : job.status === 'running'
                    ? 'Coach is working…'
                    : 'Queued…'}
              </p>
            )}

            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                onClick={() => sendMutation.mutate()}
                disabled={sending}
              >
                {sending ? 'Sending…' : 'Send to coach'}
              </Button>
              <p className="text-xs text-muted max-w-md">{disclaimer}</p>
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
  rpe,
  onNotesChange,
  onRpeChange,
}: {
  run: CoachCheckInRun;
  unitPreference: 'metric' | 'imperial';
  notes: string;
  rpe: number | null;
  onNotesChange: (value: string) => void;
  onRpeChange: (value: number | null) => void;
}) {
  const started = new Date(run.startedAt);
  const title = run.name || run.runType || 'Run';
  return (
    <li className="rounded-tempo border border-border p-3 space-y-3">
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
        <p className="text-xs font-medium text-muted mb-2">
          RPE (1–10)
          {run.workoutRpe != null && !run.rpeEdited ? ` · from activity (${run.workoutRpe})` : ''}
          {run.rpeEdited ? ' · edited' : ''}
        </p>
        <ChipRow
          values={[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]}
          selected={rpe}
          onSelect={onRpeChange}
          ariaLabel={`RPE for ${title}`}
        />
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
