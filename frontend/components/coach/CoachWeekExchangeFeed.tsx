'use client';

import { useEffect, useMemo, useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
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
import { CoachExchangeReplyView } from '@/components/coach/CoachExchangeReplyView';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function ExchangeCard({
  exchange,
  timezoneOffsetMinutes,
  referenceDate,
  onRetried,
}: {
  exchange: CoachExchange;
  timezoneOffsetMinutes: number;
  referenceDate: string;
  onRetried: () => void;
}) {
  const [progress, setProgress] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const retryMutation = useMutation({
    mutationFn: async () => {
      setError(null);
      const started = await retryCoachExchange(exchange.id, {
        timezoneOffsetMinutes,
        referenceDate,
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
      <p className="text-sm text-ink whitespace-pre-wrap">{exchange.athleteText}</p>
      {exchange.status === 'ready' && exchange.reply && (
        <CoachExchangeReplyView reply={exchange.reply} />
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
          <Button
            type="button"
            variant="secondary"
            disabled={retryMutation.isPending}
            onClick={() => retryMutation.mutate()}
          >
            {retryMutation.isPending ? 'Retrying…' : 'Retry'}
          </Button>
          {progress && <p className="text-sm text-muted">{progress}</p>}
          {error && <p className="text-sm text-danger">{error}</p>}
        </div>
      )}
    </li>
  );
}

export function CoachWeekExchangeFeed({
  weekStartDate,
  timezoneOffsetMinutes,
  referenceDate,
}: {
  weekStartDate: string;
  timezoneOffsetMinutes: number;
  referenceDate: string;
}) {
  const queryClient = useQueryClient();
  const [text, setText] = useState('');
  const [inspectOpen, setInspectOpen] = useState(false);
  const [sendError, setSendError] = useState<string | null>(null);
  const [progress, setProgress] = useState<string | null>(null);

  const listQuery = useQuery({
    queryKey: ['coach-exchanges', weekStartDate],
    queryFn: () => listCoachExchanges({ weekStartDate }),
    refetchInterval: (q) => {
      const items = q.state.data ?? [];
      return items.some((e) => e.status === 'queued' || e.status === 'running') ? 1000 : false;
    },
  });

  const previewQuery = useQuery({
    queryKey: ['coach-exchange-preflight', weekStartDate, text.trim()],
    queryFn: () =>
      postCoachExchangePreflight({
        athleteText: text.trim(),
        timezoneOffsetMinutes,
        referenceDate,
      }),
    enabled: inspectOpen && text.trim().length > 0,
    staleTime: 30_000,
  });

  useEffect(() => {
    if (text.trim().length === 0) {
      setInspectOpen(false);
    }
  }, [text]);

  const sendMutation = useMutation({
    mutationFn: async () => {
      const athleteText = text.trim();
      if (!athleteText) {
        throw new Error('Add a message before sending');
      }
      setSendError(null);
      const started = await postCoachExchange({
        athleteText,
        timezoneOffsetMinutes,
        referenceDate,
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
      await queryClient.invalidateQueries({ queryKey: ['coach-exchanges', weekStartDate] });
    },
    onError: (err: Error) => {
      setProgress(null);
      setSendError(err.message);
      void queryClient.invalidateQueries({ queryKey: ['coach-exchanges', weekStartDate] });
    },
  });

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

  const exchanges = listQuery.data ?? [];

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-1">This week with your coach</h2>
      <p className="text-sm text-muted mb-4">
        Send a mid-week note. Nothing leaves until you tap Send to coach.
      </p>

      <div className="space-y-3 mb-6">
        <label className="block space-y-1">
          <span className="text-sm font-medium text-ink">Message</span>
          <textarea
            className={fieldClass}
            rows={3}
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="How legs feel, sleep, race week, follow-up…"
            maxLength={4000}
          />
          <span className="text-xs text-muted">Avoid names and addresses in free text.</span>
        </label>

        <div className="rounded-tempo border border-border p-3 space-y-2">
          <p className="text-xs text-muted">
            What&apos;s sent: your message, memory &amp; constraints, this week&apos;s plan slice when
            present, prior exchanges, and recent training (no GPS).
          </p>
          <Button
            type="button"
            variant="secondary"
            disabled={text.trim().length === 0 || previewQuery.isLoading}
            onClick={() => setInspectOpen((o) => !o)}
          >
            {inspectOpen ? 'Hide Inspect' : 'Inspect'}
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
          <Button
            type="button"
            disabled={sendMutation.isPending || text.trim().length === 0}
            onClick={() => sendMutation.mutate()}
          >
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
      </div>

      {listQuery.isLoading ? (
        <p className="text-sm text-muted">Loading conversations…</p>
      ) : listQuery.isError ? (
        <p className="text-sm text-danger">
          {listQuery.error instanceof Error ? listQuery.error.message : 'Failed to load exchanges'}
        </p>
      ) : exchanges.length === 0 ? (
        <p className="text-sm text-muted">No messages this week yet.</p>
      ) : (
        <ul className="space-y-3">
          {exchanges.map((exchange) => (
            <ExchangeCard
              key={exchange.id}
              exchange={exchange}
              timezoneOffsetMinutes={timezoneOffsetMinutes}
              referenceDate={referenceDate}
              onRetried={() => {
                void queryClient.invalidateQueries({ queryKey: ['coach-exchanges', weekStartDate] });
              }}
            />
          ))}
        </ul>
      )}
    </Card>
  );
}
