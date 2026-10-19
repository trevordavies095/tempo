'use client';

import { useState } from 'react';
import { useMutation } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import {
  getCoachJob,
  postCoachCommittedAskForChanges,
  type CoachExchange,
} from '@/lib/api';
import { formatCoachJobError, pollCoachJobUntilSettled } from '@/lib/coachWeek';

/**
 * Prefills Ask for changes from an exchange suggestedChange.note,
 * stamps sourceExchangeId on Send (consent = click).
 */
export function useAskForThisChange(timezoneOffsetMinutes: number) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState('');
  const [exchangeId, setExchangeId] = useState<string | null>(null);
  const [weekStartDate, setWeekStartDate] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [progressLabel, setProgressLabel] = useState<string | null>(null);

  const mutation = useMutation({
    mutationFn: async () => {
      if (!weekStartDate || !exchangeId) {
        throw new Error('No exchange selected');
      }
      const trimmed = note.trim();
      if (!trimmed) {
        throw new Error('Add a short note about what changed');
      }
      setError(null);
      const today = new Date();
      const y = today.getFullYear();
      const m = String(today.getMonth() + 1).padStart(2, '0');
      const d = String(today.getDate()).padStart(2, '0');
      const started = await postCoachCommittedAskForChanges({
        weekStartDate,
        note: trimmed,
        timezoneOffsetMinutes,
        referenceDate: `${y}-${m}-${d}`,
        sourceExchangeId: exchangeId,
      });
      const settled = await pollCoachJobUntilSettled(started, (job) => getCoachJob(job.id), {
        onUpdate: (job) => {
          setProgressLabel(
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
    onSuccess: (job) => {
      setOpen(false);
      setExchangeId(null);
      setWeekStartDate(null);
      setNote('');
      setProgressLabel(null);
      setError(null);
      router.push(`/coach/review?weekStartDate=${encodeURIComponent(job.weekStartDate)}`);
    },
    onError: (err: Error) => {
      setProgressLabel(null);
      setError(err.message);
    },
  });

  const openFromExchange = (exchange: CoachExchange) => {
    const prefill =
      exchange.suggestedChange?.note?.trim() ||
      exchange.reply?.suggestedChange?.note?.trim() ||
      '';
    if (!prefill) {
      return;
    }
    setNote(prefill);
    setExchangeId(exchange.id);
    setWeekStartDate(exchange.weekStartDate);
    setError(null);
    setProgressLabel(null);
    setOpen(true);
  };

  const close = () => {
    if (mutation.isPending) {
      return;
    }
    setOpen(false);
    setExchangeId(null);
    setWeekStartDate(null);
    setNote('');
    setError(null);
    setProgressLabel(null);
  };

  return {
    open,
    note,
    setNote,
    error,
    progressLabel,
    sending: mutation.isPending,
    openFromExchange,
    close,
    send: () => mutation.mutate(),
  };
}
