'use client';

import { useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/Button';
import { AskForChangesDialog } from '@/components/coach/AskForChangesDialog';
import { CoachExchangeReplyView } from '@/components/coach/CoachExchangeReplyView';
import { useAskForThisChange } from '@/hooks/useAskForThisChange';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import {
  getCoachExchange,
  getCoachJob,
  listCoachExchanges,
  postCoachExchange,
  postCoachExchangePreflight,
  retryCoachExchange,
  type CoachExchange,
  type CoachPrivacyReport,
} from '@/lib/api';
import { formatCoachDate, formatCoachJobError, pollCoachJobUntilSettled } from '@/lib/coachWeek';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function WorkoutExchangeCard({
  exchange,
  timezoneOffsetMinutes,
  weekLocked,
  onRetried,
  onAskForThisChange,
}: {
  exchange: CoachExchange;
  timezoneOffsetMinutes: number;
  weekLocked: boolean;
  onRetried: () => void;
  onAskForThisChange?: (exchange: CoachExchange) => void;
}) {
  const [progress, setProgress] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const retryMutation = useMutation({
    mutationFn: async () => {
      setError(null);
      const started = await retryCoachExchange(exchange.id, { timezoneOffsetMinutes });
      const settled = await pollCoachJobUntilSettled(started.job, (job) => getCoachJob(job.id), {
        onUpdate: (job) => {
          setProgress(
            job.status === 'running'
              ? 'Coach is working…'
              : job.status === 'queued'
                ? 'Queued…'
                : null
          );
        },
      });
      if (settled.status === 'failed') {
        throw new Error(formatCoachJobError(settled.errorCode, settled.errorMessage));
      }
      await getCoachExchange(exchange.id);
      return settled;
    },
    onSuccess: () => {
      setProgress(null);
      onRetried();
    },
    onError: (err: Error) => {
      setProgress(null);
      setError(err.message);
    },
  });

  const createdLocal = useMemo(() => {
    const d = new Date(exchange.createdAt);
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
  }, [exchange.createdAt]);

  return (
    <li className="rounded-tempo border border-border p-3 space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-xs text-muted">{formatCoachDate(createdLocal)}</p>
        <p className="text-xs uppercase tracking-wide text-muted">{exchange.status}</p>
      </div>
      {exchange.athleteText.trim().length > 0 && (
        <p className="text-sm text-ink whitespace-pre-wrap">{exchange.athleteText}</p>
      )}
      {exchange.status === 'ready' && exchange.reply && (
        <CoachExchangeReplyView
          reply={exchange.reply}
          onAskForThisChange={
            exchange.reply.suggestedChange && onAskForThisChange
              ? () => onAskForThisChange(exchange)
              : undefined
          }
        />
      )}
      {(exchange.status === 'queued' || exchange.status === 'running') && (
        <p className="text-sm text-muted" aria-live="polite">
          {exchange.status === 'running' ? 'Coach is working…' : 'Queued…'}
        </p>
      )}
      {exchange.status === 'failed' && (
        <div className="space-y-2">
          <p className="text-sm text-danger">
            {formatCoachJobError(exchange.errorCode, exchange.errorMessage)}
          </p>
          {!weekLocked && (
            <Button
              type="button"
              variant="secondary"
              disabled={retryMutation.isPending}
              onClick={() => retryMutation.mutate()}
            >
              {retryMutation.isPending ? 'Retrying…' : 'Retry'}
            </Button>
          )}
          {progress && <p className="text-sm text-muted">{progress}</p>}
          {error && <p className="text-sm text-danger">{error}</p>}
        </div>
      )}
    </li>
  );
}

export function CoachWorkoutSendPanel({ workoutId }: { workoutId: string }) {
  const { showCoachNav } = useCoachFeature();
  const queryClient = useQueryClient();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const [text, setText] = useState('');
  const [inspectOpen, setInspectOpen] = useState(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const [progress, setProgress] = useState<string | null>(null);
  const askForThisChange = useAskForThisChange(timezoneOffsetMinutes);

  const listQuery = useQuery({
    queryKey: ['coach-exchanges', 'workout', workoutId],
    queryFn: () => listCoachExchanges({ workoutId }),
    enabled: showCoachNav,
    refetchInterval: (q) => {
      const items = q.state.data?.exchanges ?? [];
      return items.some((e) => e.status === 'queued' || e.status === 'running') ? 1000 : false;
    },
  });

  const weekLocked = listQuery.data?.weekLocked === true;

  const previewQuery = useQuery({
    queryKey: ['coach-exchange-preflight', 'workout', workoutId, text.trim()],
    queryFn: () =>
      postCoachExchangePreflight({
        athleteText: text.trim(),
        workoutId,
        timezoneOffsetMinutes,
      }),
    enabled: showCoachNav && !weekLocked && inspectOpen,
    staleTime: 30_000,
  });

  const sendMutation = useMutation({
    mutationFn: async () => {
      setSendError(null);
      const started = await postCoachExchange({
        athleteText: text.trim(),
        workoutId,
        timezoneOffsetMinutes,
      });
      const settled = await pollCoachJobUntilSettled(started.job, (job) => getCoachJob(job.id), {
        onUpdate: (job) => {
          setProgress(
            job.status === 'running'
              ? 'Coach is working…'
              : job.status === 'queued'
                ? 'Queued…'
                : null
          );
        },
      });
      if (settled.status === 'failed') {
        throw new Error(formatCoachJobError(settled.errorCode, settled.errorMessage));
      }
      return settled;
    },
    onSuccess: async () => {
      setText('');
      setProgress(null);
      setSendError(null);
      setInspectOpen(false);
      await queryClient.invalidateQueries({ queryKey: ['coach-exchanges', 'workout', workoutId] });
    },
    onError: (err: Error) => {
      setProgress(null);
      setSendError(err.message);
      void queryClient.invalidateQueries({ queryKey: ['coach-exchanges', 'workout', workoutId] });
    },
  });

  if (!showCoachNav) {
    return null;
  }

  const report: CoachPrivacyReport | null = previewQuery.data?.privacyReport ?? null;
  const prettyPayload = useMemo(() => {
    if (!previewQuery.data?.redactedPayload) {
      return '';
    }
    try {
      return JSON.stringify(previewQuery.data.redactedPayload, null, 2);
    } catch {
      return String(previewQuery.data.redactedPayload);
    }
  }, [previewQuery.data?.redactedPayload]);

  const exchanges = listQuery.data?.exchanges ?? [];

  return (
    <>
    <div className="w-full rounded-tempo border border-border px-4 py-3 mb-3 space-y-3">
      <div>
        <p className="text-sm font-medium text-ink">Send to coach</p>
        <p className="text-xs text-muted mt-0.5">
          {weekLocked
            ? 'This run’s week is locked after you accepted next week’s plan. Prior feedback stays readable.'
            : 'Tempo includes this run’s stats, splits, series, weather, and plan when present. A note is optional.'}
        </p>
      </div>

      {weekLocked ? null : (
        <>
      <label className="block space-y-1">
        <span className="text-sm font-medium text-ink">Note (optional)</span>
        <textarea
          className={fieldClass}
          rows={2}
          value={text}
          onChange={(e) => setText(e.target.value)}
          placeholder="How it felt, anything the numbers miss…"
          maxLength={4000}
        />
        <span className="text-xs text-muted">Avoid names and addresses in free text.</span>
      </label>

      <div className="rounded-tempo border border-border p-3 space-y-2">
        <p className="text-xs text-muted">
          What&apos;s sent: this run (no GPS or name), your note, memory &amp; constraints, plan slice,
          prior exchanges, recent training.
        </p>
        <Button
          type="button"
          variant="secondary"
          disabled={previewQuery.isLoading}
          onClick={() => setInspectOpen((o) => !o)}
        >
          {inspectOpen ? 'Hide Inspect' : 'See what\'s sent'}
        </Button>
        {inspectOpen && report && (
          <div className="space-y-3">
            <div>
              <p className="text-xs font-medium text-ink mb-1">Included</p>
              <ul className="space-y-1">
                {(report.included ?? []).map((item) => (
                  <li key={`in-${item.category}`} className="text-xs text-muted">
                    <span className="text-ink">{item.category}</span> — {item.detail}
                  </li>
                ))}
              </ul>
            </div>
            <div>
              <p className="text-xs font-medium text-ink mb-1">Stripped</p>
              <ul className="space-y-1">
                {(report.stripped ?? []).map((item) => (
                  <li key={`out-${item.category}`} className="text-xs text-muted">
                    <span className="text-ink">{item.category}</span> — {item.detail}
                  </li>
                ))}
              </ul>
            </div>
            <pre className="overflow-auto rounded-tempo border border-border bg-canvas p-3 text-xs text-ink max-h-64">
              {prettyPayload}
            </pre>
          </div>
        )}
        {inspectOpen && previewQuery.isError && (
          <p className="text-xs text-danger">
            {previewQuery.error instanceof Error
              ? previewQuery.error.message
              : 'Could not assemble preview'}
          </p>
        )}
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <Button type="button" disabled={sendMutation.isPending} onClick={() => sendMutation.mutate()}>
          {sendMutation.isPending ? 'Sending…' : 'Send to coach'}
        </Button>
        <p className="text-xs text-muted max-w-md">
          Tempo AI Coach is not a doctor and does not provide medical advice.
        </p>
      </div>
      {progress && (
        <p className="text-sm text-muted" aria-live="polite">
          {progress}
        </p>
      )}
      {sendError && <p className="text-sm text-danger">{sendError}</p>}
        </>
      )}

      {listQuery.isLoading ? (
        <p className="text-sm text-muted">Loading prior feedback…</p>
      ) : listQuery.isError ? (
        <p className="text-sm text-danger">
          {listQuery.error instanceof Error ? listQuery.error.message : 'Failed to load exchanges'}
        </p>
      ) : exchanges.length === 0 ? null : (
        <div className="space-y-2 pt-1">
          <p className="text-xs font-medium text-muted">Prior feedback on this run</p>
          <ul className="space-y-3">
            {exchanges.map((exchange) => (
              <WorkoutExchangeCard
                key={exchange.id}
                exchange={exchange}
                timezoneOffsetMinutes={timezoneOffsetMinutes}
                weekLocked={weekLocked}
                onAskForThisChange={askForThisChange.openFromExchange}
                onRetried={() => {
                  void queryClient.invalidateQueries({
                    queryKey: ['coach-exchanges', 'workout', workoutId],
                  });
                }}
              />
            ))}
          </ul>
        </div>
      )}
    </div>

    <AskForChangesDialog
      open={askForThisChange.open}
      onClose={askForThisChange.close}
      note={askForThisChange.note}
      onNoteChange={askForThisChange.setNote}
      noteLabel="What changed?"
      whatsSentLine="Sends your note (prefilled from the coach suggestion — edit freely) and the usual redacted training packet. Elapsed days stay locked."
      sending={askForThisChange.sending}
      progressLabel={askForThisChange.progressLabel}
      onSend={askForThisChange.send}
    />
    {askForThisChange.error && (
      <p className="text-sm text-danger mt-2 px-1">{askForThisChange.error}</p>
    )}
    </>
  );
}
