'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { AskForChangesDialog } from '@/components/coach/AskForChangesDialog';
import { CoachSetupForm } from '@/components/coach/CoachSetupForm';
import { CoachWeekExchangeFeed } from '@/components/coach/CoachWeekExchangeFeed';
import {
  deleteCoachDraftWeek,
  getCoachCheckIn,
  getCoachCommittedWeekOrNull,
  getCoachDraftWeekOrNull,
  getCoachHome,
  getCoachJob,
  getCoachWeekPrivacyReceipt,
  getCurrentCoachJob,
  postCoachCommittedAskForChanges,
  type CoachJob,
  type CoachWeekDraft,
  type CoachWeeklyCommitDay,
} from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { formatCoachRunSummary } from '@/lib/coachDistance';
import {
  applyRecordHasAcceptedMemory,
  buildAppliedMemoryLines,
  dayTypeLabel,
} from '@/lib/coachMemoryDisplay';
import {
  addDaysIso,
  formatCoachDate,
  formatCoachJobError,
  formatWeekRangeLabel,
  planWeekStartFromCheckInMonday,
  pollCoachJobUntilSettled,
  resolveDefaultCheckInReferenceDate,
} from '@/lib/coachWeek';

function formatDuration(totalSeconds: number): string {
  const h = Math.floor(totalSeconds / 3600);
  const m = Math.floor((totalSeconds % 3600) / 60);
  const s = totalSeconds % 60;
  if (h > 0) {
    return `${h}:${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
  }
  return `${m}:${s.toString().padStart(2, '0')}`;
}

function localMondayIso(d = new Date()): string {
  const day = d.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  const monday = new Date(d);
  monday.setDate(d.getDate() + diff);
  const y = monday.getFullYear();
  const m = String(monday.getMonth() + 1).padStart(2, '0');
  const dayNum = String(monday.getDate()).padStart(2, '0');
  return `${y}-${m}-${dayNum}`;
}

function localDateIso(d = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const dayNum = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${dayNum}`;
}

function DayCard({
  day,
  elapsed,
  showAdjustmentBadge,
  unitPreference,
}: {
  day: CoachWeeklyCommitDay;
  elapsed: boolean;
  showAdjustmentBadge: boolean;
  unitPreference: 'metric' | 'imperial';
}) {
  return (
    <div
      className={`rounded-tempo border border-border p-3 space-y-2 ${
        elapsed ? 'opacity-60 bg-canvas' : ''
      }`}
    >
      <div className="flex flex-wrap items-center gap-2">
        <p className="text-sm font-medium text-ink">{formatCoachDate(day.date)}</p>
        <span className="text-xs uppercase tracking-wide text-muted">
          {dayTypeLabel(day.dayType)}
        </span>
        {showAdjustmentBadge && (
          <span className="rounded-tempo border border-border px-2 py-0.5 text-xs text-muted">
            Adjusted
          </span>
        )}
        {elapsed && (
          <span className="rounded-tempo border border-border px-2 py-0.5 text-xs text-muted">
            Elapsed
          </span>
        )}
      </div>
      {day.dayType !== 'rest' && day.run && (
        <div className="text-sm text-muted space-y-1">
          <p>
            {formatCoachRunSummary(
              day.run.distanceM,
              day.run.structure,
              day.run.paceOrHr,
              unitPreference
            )}
          </p>
          {day.run.notes && <p className="text-ink">{day.run.notes}</p>}
        </div>
      )}
      {day.dayType === 'rest' && <p className="text-sm text-muted">Rest</p>}
    </div>
  );
}

function CommittedPlanSection({
  sectionId,
  heading,
  week,
  todayIso,
  unitPreference,
  showAdjust,
  onAskForChanges,
  appliedMemoryLines,
}: {
  sectionId?: string;
  heading: string;
  week: CoachWeekDraft;
  todayIso: string;
  unitPreference: 'metric' | 'imperial';
  showAdjust: boolean;
  onAskForChanges?: (week: CoachWeekDraft) => void;
  appliedMemoryLines: { key: string; text: string }[];
}) {
  const lockedThrough = week.lockedThroughDate ?? null;
  const showAdjustmentBadge = Boolean(
    week.isAdjustment || week.adjustmentStatus === 'applied'
  );
  const commit = week.commit;

  return (
    <div id={sectionId} className="space-y-8 scroll-mt-6">
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3 mb-2">
          <div>
            <h2 className="text-lg font-semibold text-ink">Coach&apos;s Notes</h2>
            <p className="text-sm text-muted mt-1">{heading}</p>
            {showAdjustmentBadge && (
              <p className="text-xs uppercase tracking-wide text-muted mt-1">Adjusted week</p>
            )}
          </div>
          <Link
            href={`/coach/history/${week.id}`}
            className="text-sm text-muted underline"
          >
            What was sent
          </Link>
        </div>
        <p className="text-sm text-ink whitespace-pre-wrap">{commit.coachesNotes}</p>
        {(commit.callouts?.length ?? 0) > 0 && (
          <div className="mt-4 flex flex-wrap gap-2">
            {commit.callouts!.map((chip, i) => (
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
        <h2 className="text-lg font-semibold text-ink mb-4">{heading}</h2>
        <div className="space-y-3">
          {commit.days.map((day) => {
            const elapsed = lockedThrough
              ? day.date <= lockedThrough
              : day.date < todayIso;
            return (
              <DayCard
                key={day.date}
                day={day}
                elapsed={elapsed}
                showAdjustmentBadge={showAdjustmentBadge && elapsed}
                unitPreference={unitPreference}
              />
            );
          })}
        </div>
        {showAdjust && onAskForChanges && (
          <div className="mt-4">
            <Button type="button" onClick={() => onAskForChanges(week)}>
              Ask for changes
            </Button>
          </div>
        )}
      </Card>

      {appliedMemoryLines.length > 0 && (
        <Card>
          <details>
            <summary className="cursor-pointer text-lg font-semibold text-ink">
              What your coach kept
            </summary>
            <ul className="mt-3 space-y-2 text-sm text-ink">
              {appliedMemoryLines.map((line) => (
                <li key={line.key} className="border-l-2 border-border pl-3">
                  {line.text}
                </li>
              ))}
            </ul>
          </details>
        </Card>
      )}
    </div>
  );
}

function memoryLinesForWeek(
  week: CoachWeekDraft | null | undefined,
  unitPreference: 'metric' | 'imperial'
) {
  if (!week?.commit) {
    return [];
  }
  const legacy =
    week.commit.memoryProposals && week.commit.memoryProposals.length > 0
      ? week.commit.memoryProposals
      : null;
  if (!applyRecordHasAcceptedMemory(week.applyRecord) && !legacy) {
    return [];
  }
  return buildAppliedMemoryLines(week.commit, week.applyRecord, unitPreference, legacy);
}

export default function CoachPage() {
  const router = useRouter();
  const { unitPreference } = useSettings();
  const queryClient = useQueryClient();
  const [ritualError, setRitualError] = useState<string | null>(null);
  const [askWeek, setAskWeek] = useState<CoachWeekDraft | null>(null);
  const [askNote, setAskNote] = useState('');
  const [askJob, setAskJob] = useState<CoachJob | null>(null);
  const [askError, setAskError] = useState<string | null>(null);

  const weekStartDate = useMemo(() => localMondayIso(), []);
  const todayIso = useMemo(() => localDateIso(), []);
  const ritualRef = useMemo(() => resolveDefaultCheckInReferenceDate(), []);
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
  });

  const currentJobQuery = useQuery({
    queryKey: ['coach-jobs-current'],
    queryFn: getCurrentCoachJob,
    refetchInterval: (q) => {
      const status = q.state.data?.status;
      return status === 'queued' || status === 'running' ? 1000 : false;
    },
  });

  const ritualCheckInQuery = useQuery({
    queryKey: ['coach-check-in', ritualRef, timezoneOffsetMinutes],
    queryFn: () => getCoachCheckIn(timezoneOffsetMinutes, ritualRef),
  });

  const planWeekStart = ritualCheckInQuery.data?.weekStartDate
    ? planWeekStartFromCheckInMonday(ritualCheckInQuery.data.weekStartDate)
    : null;

  const ritualDraftQuery = useQuery({
    queryKey: ['coach-draft-week', planWeekStart],
    queryFn: () => getCoachDraftWeekOrNull(planWeekStart!),
    enabled: Boolean(planWeekStart),
  });

  const ritualCommittedQuery = useQuery({
    queryKey: ['coach-committed-week', planWeekStart],
    queryFn: () => getCoachCommittedWeekOrNull(planWeekStart!),
    enabled: Boolean(planWeekStart),
  });

  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeekOrNull(weekStartDate),
  });

  const committedQuery = useQuery({
    queryKey: ['coach-committed-week', weekStartDate],
    queryFn: () => getCoachCommittedWeekOrNull(weekStartDate),
  });

  const deleteDraftMutation = useMutation({
    mutationFn: (monday: string) => deleteCoachDraftWeek(monday),
    onSuccess: async () => {
      setRitualError(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-jobs-current'] });
    },
    onError: (err: Error) => setRitualError(err.message),
  });

  const askReceiptQuery = useQuery({
    queryKey: ['coach-week-privacy-receipt', askWeek?.id],
    queryFn: () => getCoachWeekPrivacyReceipt(askWeek!.id),
    enabled: Boolean(askWeek?.id),
  });

  const askMutation = useMutation({
    mutationFn: async () => {
      if (!askWeek) {
        throw new Error('No committed week selected');
      }
      const note = askNote.trim();
      if (!note) {
        throw new Error('Add a short note about what changed');
      }
      const started = await postCoachCommittedAskForChanges({
        weekStartDate: askWeek.weekStartDate,
        note,
        timezoneOffsetMinutes,
        referenceDate: todayIso,
      });
      const settled = await pollCoachJobUntilSettled(started, (job) => getCoachJob(job.id), {
        onUpdate: setAskJob,
      });
      if (settled.status === 'failed') {
        throw new Error(formatCoachJobError(settled.errorCode, settled.errorMessage));
      }
      return settled;
    },
    onSuccess: async (job) => {
      setAskError(null);
      setAskWeek(null);
      setAskNote('');
      setAskJob(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-committed-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-jobs-current'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
      router.push(`/coach/review?weekStartDate=${encodeURIComponent(job.weekStartDate)}`);
    },
    onError: (err: Error) => setAskError(err.message),
  });

  const openAskForChanges = (week: CoachWeekDraft) => {
    setAskWeek(week);
    setAskNote('');
    setAskError(null);
    setAskJob(null);
  };

  const committed = committedQuery.data;
  const ritualDraft = ritualDraftQuery.data;
  const ritualCommitted = ritualCommittedQuery.data;
  const ritualCheckIn = ritualCheckInQuery.data;
  // Ritual card ignores exchange jobs (mid-week Sends must not hijack "coach is working").
  const currentJob =
    currentJobQuery.data && currentJobQuery.data.kind !== 'exchange'
      ? currentJobQuery.data
      : null;

  const calendarMemoryLines = useMemo(
    () => memoryLinesForWeek(committed, unitPreference),
    [committed, unitPreference]
  );
  const upcomingMemoryLines = useMemo(
    () => memoryLinesForWeek(ritualCommitted, unitPreference),
    [ritualCommitted, unitPreference]
  );

  const onboardingDone = homeQuery.data?.onboardingCompleted === true;
  const goal = homeQuery.data?.goal;
  const needsProvider = homeQuery.data?.nextSteps.includes('Connect your Anthropic API key');
  const draft = draftQuery.data;

  const checkInRangeLabel =
    ritualCheckIn?.weekStartDate && ritualCheckIn?.weekEndDate
      ? formatWeekRangeLabel(ritualCheckIn.weekStartDate, ritualCheckIn.weekEndDate)
      : null;
  const planEndIso = planWeekStart ? addDaysIso(planWeekStart, 6) : null;
  const planRangeLabel =
    planWeekStart && planEndIso ? formatWeekRangeLabel(planWeekStart, planEndIso) : null;

  const planIsFutureWeek = Boolean(planWeekStart && planWeekStart !== weekStartDate);
  const upcomingCommitted =
    planIsFutureWeek && ritualCommitted?.commit ? ritualCommitted : null;
  const calendarCommitted = committed?.commit ? committed : null;
  const thisWeekCommitted =
    !planIsFutureWeek && ritualCommitted?.commit
      ? ritualCommitted
      : calendarCommitted;
  const planLoading =
    committedQuery.isLoading ||
    (Boolean(planWeekStart) && ritualCommittedQuery.isLoading);

  return (
    <PageShell
      density="control"
      title={homeQuery.data?.title ?? 'AI Coach'}
      subtitle="Weekly coaching loop"
    >
      <div className="w-full space-y-8">
        <Card>
          {homeQuery.isLoading ? (
            <p className="text-sm text-muted">Loading...</p>
          ) : homeQuery.isError ? (
            <p className="text-sm text-danger">
              {homeQuery.error instanceof Error
                ? homeQuery.error.message
                : 'Failed to load Coach home'}
            </p>
          ) : onboardingDone && goal ? (
            <div className="space-y-4">
              <div>
                <h2 className="text-lg font-semibold text-ink">Primary goal</h2>
                <p className="text-sm text-muted mt-1">
                  {goal.type === 'race' ? 'Race' : 'PR'} · {goal.distance}
                  {goal.raceDate ? ` · ${formatCoachDate(goal.raceDate)}` : ''}
                  {goal.targetTimeS ? ` · ${formatDuration(goal.targetTimeS)}` : ''}
                </p>
                {goal.notes && <p className="text-sm text-muted mt-2">{goal.notes}</p>}
              </div>
              <div className="flex flex-wrap gap-2">
                {draft && (
                  <Button
                    href={`/coach/review?weekStartDate=${encodeURIComponent(draft.weekStartDate)}`}
                    variant="secondary"
                  >
                    {draft.isAdjustment ? 'Review change draft' : 'Review this week draft'}
                  </Button>
                )}
                {committed && !draft && (
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => openAskForChanges(committed)}
                  >
                    Ask for changes
                  </Button>
                )}
                <Button href="/coach/settings" variant="secondary">
                  Coach settings
                </Button>
              </div>
            </div>
          ) : (
            <CoachSetupForm
              disclaimer={
                homeQuery.data?.disclaimer ??
                'Tempo AI Coach is not a doctor and does not provide medical advice. It offers training suggestions only. Seek a qualified professional for pain, injury, or health concerns.'
              }
              providerConnected={!needsProvider}
              onComplete={() => {
                void homeQuery.refetch();
              }}
            />
          )}
        </Card>

        {onboardingDone && goal && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-1">Weekly ritual</h2>
            <p className="text-sm text-muted mb-4">
              Check in on last week, then send next week&apos;s plan to your coach.
            </p>
            {ritualCheckInQuery.isLoading || currentJobQuery.isLoading ? (
              <p className="text-sm text-muted">Loading ritual…</p>
            ) : ritualCheckInQuery.isError ? (
              <p className="text-sm text-danger">
                {ritualCheckInQuery.error instanceof Error
                  ? ritualCheckInQuery.error.message
                  : 'Failed to load check-in week'}
              </p>
            ) : currentJob &&
              (currentJob.status === 'queued' || currentJob.status === 'running') ? (
              <div className="space-y-2">
                <p className="text-sm text-ink">Coach is working…</p>
                <p className="text-xs text-muted">
                  Plan week {formatCoachDate(currentJob.weekStartDate)}
                </p>
              </div>
            ) : currentJob?.status === 'failed' && !ritualDraft ? (
              <div className="space-y-3">
                <p className="text-sm text-danger">
                  Couldn&apos;t generate —{' '}
                  {formatCoachJobError(currentJob.errorCode, currentJob.errorMessage)}
                </p>
                <Button href={`/coach/check-in?referenceDate=${encodeURIComponent(ritualRef)}`}>
                  Try again
                </Button>
              </div>
            ) : ritualDraft ? (
              <div className="space-y-3">
                <p className="text-sm text-muted">
                  Draft ready for {planRangeLabel ?? formatCoachDate(ritualDraft.weekStartDate)}.
                </p>
                <div className="flex flex-wrap gap-2">
                  <Button
                    href={`/coach/review?weekStartDate=${encodeURIComponent(ritualDraft.weekStartDate)}`}
                  >
                    Review your plan →
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    disabled={deleteDraftMutation.isPending}
                    onClick={() => deleteDraftMutation.mutate(ritualDraft.weekStartDate)}
                  >
                    {deleteDraftMutation.isPending ? 'Starting over…' : 'Start over'}
                  </Button>
                </div>
              </div>
            ) : ritualCommitted ? (
              <div className="space-y-3">
                <p className="text-sm text-ink">
                  Committed plan
                  {planRangeLabel ? ` · ${planRangeLabel}` : ''}.
                </p>
                <div className="flex flex-wrap gap-3">
                  <Button href="/coach#coach-next-week">View plan</Button>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={() => openAskForChanges(ritualCommitted)}
                  >
                    Ask for changes
                  </Button>
                </div>
              </div>
            ) : (
              <div className="space-y-3">
                <Button href={`/coach/check-in?referenceDate=${encodeURIComponent(ritualRef)}`}>
                  Check in on {checkInRangeLabel ?? 'this week'}
                </Button>
                <p className="text-sm text-muted">
                  <Link href="/coach/check-in" className="underline hover:text-ink">
                    Different week…
                  </Link>
                </p>
              </div>
            )}
            {ritualError && <p className="text-sm text-danger mt-3">{ritualError}</p>}
          </Card>
        )}

        {onboardingDone && goal && (
          <CoachWeekExchangeFeed
            weekStartDate={weekStartDate}
            timezoneOffsetMinutes={timezoneOffsetMinutes}
            referenceDate={todayIso}
          />
        )}

        {planLoading && (
          <Card>
            <p className="text-sm text-muted">Loading committed week...</p>
          </Card>
        )}

        {upcomingCommitted && (
          <CommittedPlanSection
            sectionId="coach-next-week"
            heading={
              planRangeLabel ? `Next week (${planRangeLabel})` : 'Next week'
            }
            week={upcomingCommitted}
            todayIso={todayIso}
            unitPreference={unitPreference}
            showAdjust={!ritualDraft}
            onAskForChanges={openAskForChanges}
            appliedMemoryLines={upcomingMemoryLines}
          />
        )}

        {thisWeekCommitted && (
          <CommittedPlanSection
            sectionId={upcomingCommitted ? 'coach-this-week' : 'coach-next-week'}
            heading="This week"
            week={thisWeekCommitted}
            todayIso={todayIso}
            unitPreference={unitPreference}
            showAdjust={!draft && !upcomingCommitted}
            onAskForChanges={openAskForChanges}
            appliedMemoryLines={
              planIsFutureWeek ? calendarMemoryLines : upcomingMemoryLines.length
                ? upcomingMemoryLines
                : calendarMemoryLines
            }
          />
        )}

        {askError && (
          <Card>
            <p className="text-sm text-danger">{askError}</p>
          </Card>
        )}
      </div>

      <AskForChangesDialog
        open={Boolean(askWeek)}
        onClose={() => {
          if (!askMutation.isPending) {
            setAskWeek(null);
          }
        }}
        note={askNote}
        onNoteChange={setAskNote}
        noteLabel="What changed?"
        whatsSentLine="Sends your note and the usual redacted training packet for this week (elapsed days stay locked)."
        inspectContent={
          askReceiptQuery.isLoading ? (
            <p>Loading last packet…</p>
          ) : askReceiptQuery.isError ? (
            <p>
              Could not load the previous receipt. Your note will still be included on Send.
            </p>
          ) : askReceiptQuery.data ? (
            <>
              <p className="text-ink">
                Prior send for this week (hash {askReceiptQuery.data.payloadHash.slice(0, 12)}…)
                plus your note and an adjustment block for remaining days.
              </p>
              {(askReceiptQuery.data.recentTraining?.length ?? 0) > 0 && (
                <ul className="list-disc pl-4 space-y-1">
                  {askReceiptQuery.data.recentTraining!.map((w) => (
                    <li key={w.weekStartDate}>
                      {w.weekStartDate}–{w.weekEndDate}: {w.runCount} runs
                    </li>
                  ))}
                </ul>
              )}
            </>
          ) : (
            <p>
              Your note, locked/remaining day context, and the redacted training packet leave on
              Send.
            </p>
          )
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
