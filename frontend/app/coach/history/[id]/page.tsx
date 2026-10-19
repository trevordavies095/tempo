'use client';

import { useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter } from 'next/navigation';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getCoachJob,
  getCoachWeekById,
  postCoachCommittedAskForChanges,
  type CoachJob,
  type CoachPlanDay,
  type CoachWeekHistoryDetail,
} from '@/lib/api';
import { formatCoachDistance, formatCoachRunSummary } from '@/lib/coachDistance';
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
  pollCoachJobUntilSettled,
} from '@/lib/coachWeek';
import { formatDuration } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { AskForChangesDialog } from '@/components/coach/AskForChangesDialog';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';

function localDateIso(d = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function localMondayIso(d = new Date()): string {
  const date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const day = (date.getDay() + 6) % 7;
  date.setDate(date.getDate() - day);
  return localDateIso(date);
}

function HistoryDayCard({
  day,
  unitPreference,
}: {
  day: CoachPlanDay;
  unitPreference: 'metric' | 'imperial';
}) {
  const isRest = day.dayType === 'rest' || !day.run;
  const planned = isRest
    ? 'Rest'
    : day.run
      ? formatCoachRunSummary(
          day.run.distanceM,
          day.run.structure,
          day.run.paceOrHr,
          unitPreference
        )
      : dayTypeLabel(day.dayType);

  const actualTotalM = day.workouts.reduce((sum, w) => sum + w.distanceM, 0);
  const actualDurationS = day.workouts.reduce((sum, w) => sum + w.durationS, 0);
  const actual =
    day.workouts.length === 0
      ? '—'
      : `${formatCoachDistance(actualTotalM, unitPreference)}${
          actualDurationS > 0 ? ` · ${formatDuration(actualDurationS)}` : ''
        }${day.workouts.length > 1 ? ` (${day.workouts.length} runs)` : ''}`;

  return (
    <div className="rounded-tempo border border-border p-3 space-y-2">
      <div className="flex flex-wrap items-center gap-2">
        <p className="text-sm font-medium text-ink">{formatCoachDate(day.date)}</p>
        <span className="text-xs uppercase tracking-wide text-muted">
          {dayTypeLabel(day.dayType)}
        </span>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-sm">
        <div>
          <p className="text-xs text-muted mb-0.5">Planned</p>
          <p className="text-ink">{planned}</p>
          {day.run?.notes && <p className="text-muted mt-1">{day.run.notes}</p>}
        </div>
        <div>
          <p className="text-xs text-muted mb-0.5">Actual</p>
          <p className="text-ink">{actual}</p>
        </div>
      </div>
      {day.adherenceHint?.message && (
        <p className="text-xs text-muted">{day.adherenceHint.message}</p>
      )}
    </div>
  );
}

function isCurrentCommittedWeek(detail: CoachWeekHistoryDetail, thisMonday: string): boolean {
  if (detail.status !== 'committed') {
    return false;
  }
  const nextMonday = addDaysIso(thisMonday, 7);
  return detail.weekStartDate === thisMonday || detail.weekStartDate === nextMonday;
}

export default function CoachHistoryDetailPage() {
  const params = useParams();
  const weekId = typeof params.id === 'string' ? params.id : '';
  const router = useRouter();
  const queryClient = useQueryClient();
  const { unitPreference } = useSettings();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const todayIso = useMemo(() => localDateIso(), []);
  const thisMonday = useMemo(() => localMondayIso(), []);

  const [inspectOpen, setInspectOpen] = useState(false);
  const [askOpen, setAskOpen] = useState(false);
  const [askNote, setAskNote] = useState('');
  const [askJob, setAskJob] = useState<CoachJob | null>(null);
  const [askError, setAskError] = useState<string | null>(null);

  const detailQuery = useQuery({
    queryKey: ['coach-week-history', weekId, timezoneOffsetMinutes],
    queryFn: () => getCoachWeekById(weekId, timezoneOffsetMinutes),
    enabled: Boolean(weekId),
  });

  const detail = detailQuery.data;
  const weekEnd = detail ? addDaysIso(detail.weekStartDate, 6) : null;
  const rangeLabel =
    detail && weekEnd ? formatWeekRangeLabel(detail.weekStartDate, weekEnd) : null;

  const appliedMemoryLines = useMemo(() => {
    if (!detail?.commit) {
      return [];
    }
    const legacy =
      Array.isArray(detail.commit.memoryProposals) && detail.commit.memoryProposals.length > 0
        ? detail.commit.memoryProposals
        : null;
    if (!applyRecordHasAcceptedMemory(detail.applyRecord) && !legacy) {
      return [];
    }
    return buildAppliedMemoryLines(detail.commit, detail.applyRecord, unitPreference, legacy);
  }, [detail, unitPreference]);

  const prettyPayload = useMemo(() => {
    if (!detail?.privacyReceipt?.redactedPayload) {
      return '';
    }
    try {
      return JSON.stringify(detail.privacyReceipt.redactedPayload, null, 2);
    } catch {
      return String(detail.privacyReceipt.redactedPayload);
    }
  }, [detail]);

  const canAskForChanges = Boolean(detail && isCurrentCommittedWeek(detail, thisMonday));

  const askMutation = useMutation({
    mutationFn: async () => {
      if (!detail) {
        throw new Error('No week loaded');
      }
      const note = askNote.trim();
      if (!note) {
        throw new Error('Add a short note about what changed');
      }
      const started = await postCoachCommittedAskForChanges({
        weekStartDate: detail.weekStartDate,
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
      setAskOpen(false);
      setAskNote('');
      setAskJob(null);
      await queryClient.invalidateQueries({ queryKey: ['coach-draft-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-committed-week'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-jobs-current'] });
      await queryClient.invalidateQueries({ queryKey: ['coach-weeks'] });
      router.push(`/coach/review?weekStartDate=${encodeURIComponent(job.weekStartDate)}`);
    },
    onError: (err: Error) => setAskError(err.message),
  });

  const sending = askMutation.isPending;
  const backLink = (
    <Link href="/coach/history" className="text-sm text-muted underline">
      ← History
    </Link>
  );

  if (detailQuery.isLoading) {
    return (
      <PageShell density="control" title="Week" leading={backLink}>
        <Card>
          <p className="text-sm text-muted">Loading week…</p>
        </Card>
      </PageShell>
    );
  }

  if (detailQuery.isError || !detail) {
    return (
      <PageShell density="control" title="Week" leading={backLink}>
        <EmptyState
          title="Week not found"
          description={
            detailQuery.error instanceof Error
              ? detailQuery.error.message
              : 'This week may have been removed.'
          }
        />
      </PageShell>
    );
  }

  const receipt = detail.privacyReceipt;

  return (
    <PageShell
      density="control"
      title={rangeLabel ?? 'Week'}
      subtitle={
        detail.isAdjustment
          ? 'Adjusted plan'
          : detail.status === 'archived'
            ? 'Archived plan'
            : 'Committed plan'
      }
      leading={backLink}
    >
      <div className="w-full space-y-6">
        {detail.isAdjustment && detail.sourceExchangeId && (
          <p className="text-sm text-muted">From run feedback</p>
        )}
        <Card>
          <h2 className="text-lg font-semibold text-ink mb-2">Coach&apos;s Notes</h2>
          <p className="text-sm text-ink whitespace-pre-wrap">
            {detail.commit.coachesNotes || '—'}
          </p>
          {(detail.commit.callouts?.length ?? 0) > 0 && (
            <div className="mt-4 flex flex-wrap gap-2">
              {detail.commit.callouts!.map((chip, i) => (
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
          <div className="flex flex-wrap items-center justify-between gap-3 mb-4">
            <h2 className="text-lg font-semibold text-ink">Plan</h2>
            {canAskForChanges && (
              <Button
                type="button"
                size="sm"
                onClick={() => {
                  setAskOpen(true);
                  setAskError(null);
                  setAskJob(null);
                  setAskNote('');
                }}
              >
                Ask for changes
              </Button>
            )}
          </div>
          <div className="space-y-3">
            {detail.days.map((day) => (
              <HistoryDayCard key={day.date} day={day} unitPreference={unitPreference} />
            ))}
          </div>
        </Card>

        {appliedMemoryLines.length > 0 && (
          <Card>
            <h2 className="text-lg font-semibold text-ink mb-3">Your coach kept</h2>
            <ul className="space-y-2 text-sm text-ink">
              {appliedMemoryLines.map((line) => (
                <li key={line.key} className="border-l-2 border-border pl-3">
                  {line.text}
                </li>
              ))}
            </ul>
          </Card>
        )}

        <Card>
          <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
            <div>
              <h2 className="text-lg font-semibold text-ink">What was sent</h2>
              <p className="text-xs text-muted mt-1 font-mono">hash {receipt.payloadHash}</p>
            </div>
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => setInspectOpen((o) => !o)}
            >
              {inspectOpen ? 'Hide JSON' : 'Inspect JSON'}
            </Button>
          </div>
          {receipt.recentTraining && receipt.recentTraining.length > 0 && (
            <ul className="mb-3 space-y-1 text-sm text-muted">
              {receipt.recentTraining.map((w) => (
                <li key={w.weekStartDate}>
                  {formatWeekRangeLabel(w.weekStartDate, w.weekEndDate)}: {w.runCount} run
                  {w.runCount === 1 ? '' : 's'}
                </li>
              ))}
            </ul>
          )}
          {inspectOpen && (
            <pre className="overflow-auto rounded-tempo border border-border bg-canvas p-3 text-xs text-ink max-h-[28rem]">
              {prettyPayload}
            </pre>
          )}
        </Card>
      </div>

      <AskForChangesDialog
        open={askOpen}
        onClose={() => {
          if (sending) {
            return;
          }
          setAskOpen(false);
          setAskError(null);
          setAskJob(null);
        }}
        note={askNote}
        onNoteChange={setAskNote}
        whatsSentLine="Sends your note plus the locked plan context for remaining days."
        sending={sending}
        progressLabel={
          askError ??
          (askJob
            ? askJob.status === 'ready'
              ? 'Plan ready'
              : askJob.status === 'failed'
                ? formatCoachJobError(askJob.errorCode, askJob.errorMessage)
                : 'Coach is working…'
            : null)
        }
        onSend={() => askMutation.mutate()}
      />
    </PageShell>
  );
}
