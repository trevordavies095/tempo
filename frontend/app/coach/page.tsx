'use client';

import { useEffect, useMemo } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Button } from '@/components/ui/Button';
import {
  dismissCoachMemoryUpgradeNotice,
  dismissCoachSeedMemoryNudge,
  getCoachCheckIn,
  getCoachCommittedWeekOrNull,
  getCoachDraftWeekOrNull,
  getCoachHome,
  type CoachWeeklyCommitDay,
} from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import { useSettings } from '@/lib/settings';
import { formatDistance } from '@/lib/format';
import {
  applyRecordHasAcceptedMemory,
  buildAppliedMemoryLines,
} from '@/lib/coachMemoryDisplay';
import {
  addDaysIso,
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
        <p className="text-sm font-medium text-ink">{day.date}</p>
        <span className="text-xs uppercase tracking-wide text-muted">{day.dayType}</span>
        {showAdjustmentBadge && (
          <span className="rounded-tempo border border-border px-2 py-0.5 text-xs text-muted">
            Adjustment
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
            {formatDistance(day.run.distanceM, unitPreference)}
            {day.run.structure ? ` · ${day.run.structure}` : ''}
            {day.run.paceOrHr ? ` · ${day.run.paceOrHr}` : ''}
          </p>
          {day.run.notes && <p className="text-ink">{day.run.notes}</p>}
        </div>
      )}
      {day.dayType === 'rest' && <p className="text-sm text-muted">Rest</p>}
    </div>
  );
}

function CoachPageContent() {
  const router = useRouter();
  const queryClient = useQueryClient();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();
  const { unitPreference } = useSettings();

  const weekStartDate = useMemo(() => localMondayIso(), []);
  const todayIso = useMemo(() => localDateIso(), []);
  const ritualRef = useMemo(() => resolveDefaultCheckInReferenceDate(), []);
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
    enabled: showCoachNav,
  });

  const dismissUpgradeNotice = useMutation({
    mutationFn: dismissCoachMemoryUpgradeNotice,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['coach-home'] }),
  });

  const dismissSeedNudge = useMutation({
    mutationFn: dismissCoachSeedMemoryNudge,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['coach-home'] }),
  });

  const ritualCheckInQuery = useQuery({
    queryKey: ['coach-check-in', ritualRef, timezoneOffsetMinutes],
    queryFn: () => getCoachCheckIn(timezoneOffsetMinutes, ritualRef),
    enabled: showCoachNav,
  });

  const planWeekStart = ritualCheckInQuery.data?.weekStartDate
    ? planWeekStartFromCheckInMonday(ritualCheckInQuery.data.weekStartDate)
    : null;

  const ritualDraftQuery = useQuery({
    queryKey: ['coach-draft-week', planWeekStart],
    queryFn: () => getCoachDraftWeekOrNull(planWeekStart!),
    enabled: showCoachNav && Boolean(planWeekStart),
  });

  const ritualCommittedQuery = useQuery({
    queryKey: ['coach-committed-week', planWeekStart],
    queryFn: () => getCoachCommittedWeekOrNull(planWeekStart!),
    enabled: showCoachNav && Boolean(planWeekStart),
  });

  // Calendar "this week" (today's Monday) — follow the live plan.
  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeekOrNull(weekStartDate),
    enabled: showCoachNav,
  });

  const committedQuery = useQuery({
    queryKey: ['coach-committed-week', weekStartDate],
    queryFn: () => getCoachCommittedWeekOrNull(weekStartDate),
    enabled: showCoachNav,
  });

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  const committed = committedQuery.data;
  const ritualDraft = ritualDraftQuery.data;
  const ritualCommitted = ritualCommittedQuery.data;
  const ritualCheckIn = ritualCheckInQuery.data;

  // Must stay above the early returns below (rules-of-hooks).
  const appliedMemoryLines = useMemo(() => {
    if (!committed?.commit) {
      return [];
    }
    const legacy =
      committed.commit.memoryProposals && committed.commit.memoryProposals.length > 0
        ? committed.commit.memoryProposals
        : null;
    if (!applyRecordHasAcceptedMemory(committed.applyRecord) && !legacy) {
      return [];
    }
    return buildAppliedMemoryLines(
      committed.commit,
      committed.applyRecord,
      unitPreference,
      legacy
    );
  }, [committed, unitPreference]);

  if (settingsLoading || !isFetched) {
    return (
      <PageShell density="control" title="AI Coach">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  if (!showCoachNav) {
    return null;
  }

  const disclaimer =
    homeQuery.data?.disclaimer ??
    'Tempo AI Coach is not a doctor and does not provide medical advice. It offers training suggestions only.';

  const onboardingDone = homeQuery.data?.onboardingCompleted === true;
  const goal = homeQuery.data?.goal;
  const needsProvider = homeQuery.data?.nextSteps.includes('Connect your Anthropic API key');
  const draft = draftQuery.data;
  const lockedThrough = committed?.lockedThroughDate ?? draft?.lockedThroughDate ?? null;
  const showAdjustmentBadge = Boolean(
    committed?.isAdjustment || draft?.isAdjustment || committed?.adjustmentStatus === 'applied'
  );

  const checkInRangeLabel =
    ritualCheckIn?.weekStartDate && ritualCheckIn?.weekEndDate
      ? formatWeekRangeLabel(ritualCheckIn.weekStartDate, ritualCheckIn.weekEndDate)
      : null;
  const planEndIso = planWeekStart ? addDaysIso(planWeekStart, 6) : null;
  const planRangeLabel =
    planWeekStart && planEndIso ? formatWeekRangeLabel(planWeekStart, planEndIso) : null;

  return (
    <PageShell
      density="control"
      title={homeQuery.data?.title ?? 'AI Coach'}
      subtitle="Weekly coaching loop"
      leading={
        <div
          className="rounded-tempo border border-border bg-canvas p-4 text-sm text-muted mb-4"
          role="note"
        >
          {disclaimer}
        </div>
      }
    >
      <div className="w-full space-y-8">
        {homeQuery.data?.pendingMemoryUpgradeNotice && (
          <Card>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <h2 className="text-lg font-semibold text-ink">Regenerate your weekly draft</h2>
                <p className="text-sm text-muted mt-1">
                  Coach memory upgraded to typed Layer 1/2/3 channels. Any open draft was discarded —
                  generate a new weekly plan when you are ready.
                </p>
              </div>
              <Button
                type="button"
                variant="secondary"
                disabled={dismissUpgradeNotice.isPending}
                onClick={() => dismissUpgradeNotice.mutate()}
              >
                Dismiss
              </Button>
            </div>
          </Card>
        )}

        {homeQuery.data?.showSeedMemoryNudge && (
          <Card>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div>
                <h2 className="text-lg font-semibold text-ink">Seed your coach&apos;s memory</h2>
                <p className="text-sm text-muted mt-1">
                  Add a past injury, race result, or standing rule you already know about yourself
                  before the first weekly plan.
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Link href="/coach/memory">
                  <Button type="button">Open Memory</Button>
                </Link>
                <Button
                  type="button"
                  variant="secondary"
                  disabled={dismissSeedNudge.isPending}
                  onClick={() => dismissSeedNudge.mutate()}
                >
                  Dismiss
                </Button>
              </div>
            </div>
          </Card>
        )}
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
                  {goal.raceDate ? ` · ${goal.raceDate}` : ''}
                  {goal.targetTimeS ? ` · ${formatDuration(goal.targetTimeS)}` : ''}
                </p>
                {goal.notes && <p className="text-sm text-muted mt-2">{goal.notes}</p>}
              </div>
              <div className="flex flex-wrap gap-2">
                {draft && (
                  <Link
                    href={`/coach/review?weekStartDate=${encodeURIComponent(draft.weekStartDate)}`}
                  >
                    <Button type="button" variant="secondary">
                      {draft.isAdjustment ? 'Review adjust draft' : 'Review this week draft'}
                    </Button>
                  </Link>
                )}
                {committed && !draft && (
                  <Link
                    href={`/coach/adjust?weekStartDate=${encodeURIComponent(committed.weekStartDate)}`}
                  >
                    <Button type="button" variant="secondary">
                      Adjust this week
                    </Button>
                  </Link>
                )}
                <Link href="/coach/constraints">
                  <Button type="button" variant="secondary">
                    Non-negotiables
                  </Button>
                </Link>
                <Link href="/coach/memory">
                  <Button type="button" variant="secondary">
                    Memory
                  </Button>
                </Link>
                <Link href="/coach/onboarding">
                  <Button type="button" variant="secondary">
                    Review onboarding
                  </Button>
                </Link>
                {needsProvider && (
                  <Link href="/settings">
                    <Button type="button" variant="secondary">
                      Connect Anthropic key
                    </Button>
                  </Link>
                )}
              </div>
            </div>
          ) : (
            <EmptyState
              title="Finish coach setup"
              description={
                needsProvider
                  ? 'Connect your Anthropic API key, then complete coach onboarding so Layer 1 and your primary goal are ready for the weekly loop.'
                  : 'Complete coach onboarding to save your primary goal and Layer 1 (HR zones, paces, shoes).'
              }
              action={
                <div className="flex flex-wrap gap-2">
                  {needsProvider && (
                    <Link href="/settings">
                      <Button type="button" variant="secondary">
                        Coach settings
                      </Button>
                    </Link>
                  )}
                  <Link href="/coach/onboarding">
                    <Button type="button">Start coach onboarding</Button>
                  </Link>
                  <Link href="/coach/constraints">
                    <Button type="button" variant="secondary">
                      Non-negotiables
                    </Button>
                  </Link>
                  <Link href="/coach/memory">
                    <Button type="button" variant="secondary">
                      Memory
                    </Button>
                  </Link>
                </div>
              }
            />
          )}
        </Card>

        {onboardingDone && goal && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-1">Weekly ritual</h2>
            <p className="text-sm text-muted mb-4">
              Check in on last week, then generate next week&apos;s plan.
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
                <Link href="/coach/check-in">
                  <Button type="button">
                    Check in on {checkInRangeLabel ?? 'this week'}
                  </Button>
                </Link>
                <p className="text-sm text-muted">
                  <Link href="/coach/check-in" className="underline hover:text-ink">
                    Different week…
                  </Link>
                </p>
              </div>
            ) : ritualDraft ? (
              <div className="space-y-3">
                <p className="text-sm text-muted">
                  Draft ready for {planRangeLabel ?? ritualDraft.weekStartDate}.
                </p>
                <Link
                  href={`/coach/review?weekStartDate=${encodeURIComponent(ritualDraft.weekStartDate)}`}
                >
                  <Button type="button">
                    {ritualDraft.isAdjustment ? 'Review adjust draft' : 'Review draft'}
                  </Button>
                </Link>
              </div>
            ) : ritualCommitted ? (
              <div className="space-y-3">
                <p className="text-sm text-ink">
                  Next week is set
                  {planRangeLabel ? ` (${planRangeLabel})` : ''}.
                </p>
                <Link
                  href={`/coach/adjust?weekStartDate=${encodeURIComponent(ritualCommitted.weekStartDate)}`}
                >
                  <Button type="button">Adjust</Button>
                </Link>
              </div>
            ) : (
              <div className="space-y-3">
                <Link
                  href={`/coach/preflight?referenceDate=${encodeURIComponent(ritualRef)}`}
                >
                  <Button type="button">
                    Generate {planRangeLabel ?? 'next week'}
                  </Button>
                </Link>
                <p className="text-sm text-muted">
                  Check-in saved for {checkInRangeLabel}. Plan week is derived automatically.
                </p>
              </div>
            )}
            <p className="text-sm text-muted mt-4">
              <Link href="/coach/preflight?advanced=1" className="underline hover:text-ink">
                Generate for another week…
              </Link>
            </p>
          </Card>
        )}

        {committedQuery.isLoading && (
          <Card>
            <p className="text-sm text-muted">Loading committed week...</p>
          </Card>
        )}

        {committed && committed.commit && (
          <>
            <Card>
              <div className="flex flex-wrap items-start justify-between gap-3 mb-2">
                <div>
                  <h2 className="text-lg font-semibold text-ink">Coach&apos;s Notes</h2>
                  {showAdjustmentBadge && (
                    <p className="text-xs uppercase tracking-wide text-muted mt-1">Adjusted week</p>
                  )}
                </div>
                <Link
                  href={`/coach/weeks/${committed.id}/receipt`}
                  className="text-sm text-muted underline"
                >
                  Privacy receipt
                </Link>
              </div>
              <p className="text-sm text-ink whitespace-pre-wrap">{committed.commit.coachesNotes}</p>
              {(committed.commit.callouts?.length ?? 0) > 0 && (
                <div className="mt-4 flex flex-wrap gap-2">
                  {committed.commit.callouts.map((chip, i) => (
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
              <h2 className="text-lg font-semibold text-ink mb-4">This week</h2>
              <div className="space-y-3">
                {committed.commit.days.map((day) => {
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
              {!draft && (
                <div className="mt-4">
                  <Link
                    href={`/coach/adjust?weekStartDate=${encodeURIComponent(committed.weekStartDate)}`}
                  >
                    <Button type="button">Adjust remaining days</Button>
                  </Link>
                </div>
              )}
            </Card>

            {appliedMemoryLines.length > 0 && (
              <Card>
                <details>
                  <summary className="cursor-pointer text-lg font-semibold text-ink">
                    Applied to memory
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
          </>
        )}
      </div>
    </PageShell>
  );
}

export default function CoachPage() {
  return (
    <AuthGuard>
      <CoachPageContent />
    </AuthGuard>
  );
}
