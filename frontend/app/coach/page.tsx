'use client';

import { useMemo } from 'react';
import Link from 'next/link';
import { useQuery } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Button } from '@/components/ui/Button';
import {
  getCoachCheckIn,
  getCoachCommittedWeekOrNull,
  getCoachDraftWeekOrNull,
  getCoachHome,
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
  formatWeekRangeLabel,
  planWeekStartFromCheckInMonday,
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
  appliedMemoryLines,
}: {
  sectionId?: string;
  heading: string;
  week: CoachWeekDraft;
  todayIso: string;
  unitPreference: 'metric' | 'imperial';
  showAdjust: boolean;
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
            href={`/coach/weeks/${week.id}/receipt`}
            className="text-sm text-muted underline"
          >
            Privacy receipt
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
        {showAdjust && (
          <div className="mt-4">
            <Button
              href={`/coach/adjust?weekStartDate=${encodeURIComponent(week.weekStartDate)}`}
            >
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
  const { unitPreference } = useSettings();

  const weekStartDate = useMemo(() => localMondayIso(), []);
  const todayIso = useMemo(() => localDateIso(), []);
  const ritualRef = useMemo(() => resolveDefaultCheckInReferenceDate(), []);
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
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

  const committed = committedQuery.data;
  const ritualDraft = ritualDraftQuery.data;
  const ritualCommitted = ritualCommittedQuery.data;
  const ritualCheckIn = ritualCheckInQuery.data;

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
                    href={`/coach/adjust?weekStartDate=${encodeURIComponent(committed.weekStartDate)}`}
                    variant="secondary"
                  >
                    Ask for changes
                  </Button>
                )}
                <Button href="/coach/constraints" variant="secondary">
                  Fixed dates & schedule
                </Button>
                <Button href="/coach/memory" variant="secondary">
                  What your coach knows about you
                </Button>
                <Button href="/coach/onboarding" variant="secondary">
                  Profile & goal
                </Button>
                {needsProvider && (
                  <Button href="/settings" variant="secondary">
                    Connect Anthropic key
                  </Button>
                )}
              </div>
            </div>
          ) : (
            <EmptyState
              title="Finish coach setup"
              description={
                needsProvider
                  ? 'Connect your Anthropic API key, then complete coach setup so your paces & zones and primary goal are ready for the weekly loop.'
                  : 'Complete coach setup to save your primary goal and your paces & zones (HR zones, paces, shoes).'
              }
              action={
                <div className="flex flex-wrap gap-2">
                  {needsProvider && (
                    <Button href="/settings" variant="secondary">
                      Coach settings
                    </Button>
                  )}
                  <Button href="/coach/onboarding">Start coaching</Button>
                  <Button href="/coach/constraints" variant="secondary">
                    Fixed dates & schedule
                  </Button>
                  <Button href="/coach/memory" variant="secondary">
                    What your coach knows about you
                  </Button>
                </div>
              }
            />
          )}
        </Card>

        {onboardingDone && goal && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-1">Weekly ritual</h2>
            <p className="text-sm text-muted mb-4">
              Check in on last week, then send next week&apos;s plan to your coach.
            </p>
            {ritualCheckInQuery.isLoading || (ritualCheckIn?.saved && !planWeekStart) ? (
              <p className="text-sm text-muted">Loading ritual…</p>
            ) : ritualCheckInQuery.isError ? (
              <p className="text-sm text-danger">
                {ritualCheckInQuery.error instanceof Error
                  ? ritualCheckInQuery.error.message
                  : 'Failed to load check-in week'}
              </p>
            ) : !ritualCheckIn?.saved ? (
              <div className="space-y-3">
                <Button href="/coach/check-in">
                  Check in on {checkInRangeLabel ?? 'this week'}
                </Button>
                <p className="text-sm text-muted">
                  <Link href="/coach/check-in" className="underline hover:text-ink">
                    Different week…
                  </Link>
                </p>
              </div>
            ) : ritualDraft ? (
              <div className="space-y-3">
                <p className="text-sm text-muted">
                  Draft ready for {planRangeLabel ?? formatCoachDate(ritualDraft.weekStartDate)}.
                </p>
                <Button
                  href={`/coach/review?weekStartDate=${encodeURIComponent(ritualDraft.weekStartDate)}`}
                >
                  {ritualDraft.isAdjustment ? 'Review change draft' : 'Review draft'}
                </Button>
              </div>
            ) : ritualCommitted ? (
              <div className="space-y-3">
                <p className="text-sm text-ink">
                  Next week is set
                  {planRangeLabel ? ` (${planRangeLabel})` : ''}.
                </p>
                <div className="flex flex-wrap gap-3">
                  <Button href="/coach#coach-next-week">View plan</Button>
                  <Button
                    href={`/coach/adjust?weekStartDate=${encodeURIComponent(ritualCommitted.weekStartDate)}`}
                    variant="secondary"
                  >
                    Ask for changes
                  </Button>
                </div>
              </div>
            ) : (
              <div className="space-y-3">
                <Button
                  href={`/coach/preflight?referenceDate=${encodeURIComponent(ritualRef)}`}
                >
                  Send to coach · {planRangeLabel ?? 'next week'}
                </Button>
                <p className="text-sm text-muted">
                  Check-in saved for {checkInRangeLabel}. Plan week is derived automatically.
                </p>
              </div>
            )}
            <p className="text-sm text-muted mt-4">
              <Link href="/coach/preflight?advanced=1" className="underline hover:text-ink">
                Send for another week…
              </Link>
            </p>
          </Card>
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
            appliedMemoryLines={
              planIsFutureWeek ? calendarMemoryLines : upcomingMemoryLines.length
                ? upcomingMemoryLines
                : calendarMemoryLines
            }
          />
        )}
      </div>
    </PageShell>
  );
}
