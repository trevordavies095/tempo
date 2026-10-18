'use client';

import { Suspense, useMemo, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { useMutation } from '@tanstack/react-query';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  consentCoachPrivacyPreflight,
  getCoachJob,
  postCoachGenerate,
  postCoachPrivacyPreflight,
  type CoachJob,
  type CoachPrivacyPreflight,
} from '@/lib/api';
import {
  addDaysIso,
  formatCoachDate,
  formatCoachJobError,
  formatWeekRangeLabel,
  planWeekStartFromCheckInMonday,
  resolveDefaultCheckInReferenceDate,
} from '@/lib/coachWeek';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function mondayOfContainingWeek(isoDate: string): string {
  const [y, m, d] = isoDate.split('-').map(Number);
  const date = new Date(y, m - 1, d);
  const day = date.getDay();
  const diff = day === 0 ? -6 : 1 - day;
  date.setDate(date.getDate() + diff);
  const yy = date.getFullYear();
  const mm = String(date.getMonth() + 1).padStart(2, '0');
  const dd = String(date.getDate()).padStart(2, '0');
  return `${yy}-${mm}-${dd}`;
}

function PreflightPageInner() {
  const searchParams = useSearchParams();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const isAdjustMode = searchParams.get('mode') === 'adjust';
  const isAdvanced = searchParams.get('advanced') === '1';
  const queryReferenceDate = searchParams.get('referenceDate');
  const initialRef = queryReferenceDate || resolveDefaultCheckInReferenceDate(new Date());
  const [referenceDate, setReferenceDate] = useState(initialRef);
  const isHappyPath = Boolean(queryReferenceDate) && !isAdjustMode && !isAdvanced;
  const [preflight, setPreflight] = useState<CoachPrivacyPreflight | null>(null);
  const [inspectOpen, setInspectOpen] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [job, setJob] = useState<CoachJob | null>(null);
  const [jobPolling, setJobPolling] = useState(false);

  const assembleMutation = useMutation({
    mutationFn: () => postCoachPrivacyPreflight(timezoneOffsetMinutes, referenceDate),
    onSuccess: (doc) => {
      setError(null);
      setPreflight(doc);
      setAcknowledged(false);
      setInspectOpen(false);
      setJob(null);
    },
    onError: (err: Error) => setError(err.message),
  });

  const consentMutation = useMutation({
    mutationFn: (id: string) => consentCoachPrivacyPreflight(id),
    onSuccess: (doc) => {
      setError(null);
      setPreflight(doc);
    },
    onError: (err: Error) => setError(err.message),
  });

  const generateMutation = useMutation({
    mutationFn: (id: string) => postCoachGenerate(id),
    onSuccess: async (started) => {
      setError(null);
      setJob(started);
      setJobPolling(true);
      try {
        let current = started;
        const deadline = Date.now() + 180_000;
        while (current.status !== 'ready' && current.status !== 'failed') {
          if (Date.now() > deadline) {
            throw new Error('Timed out while waiting for your coach.');
          }
          await new Promise((r) => setTimeout(r, 500));
          current = await getCoachJob(started.id);
          setJob(current);
        }
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Send to coach failed');
      } finally {
        setJobPolling(false);
      }
    },
    onError: (err: Error) => setError(err.message),
  });

  const prettyPayload = useMemo(() => {
    if (!preflight?.redactedPayload) return '';
    try {
      return JSON.stringify(preflight.redactedPayload, null, 2);
    } catch {
      return String(preflight.redactedPayload);
    }
  }, [preflight]);

  return (
    <PageShell
      density="control"
      title="What your coach will see"
      subtitle={
        isAdjustMode
          ? 'Re-confirm the redacted packet before asking for mid-week changes'
          : 'Review what leaves your instance before any Anthropic call'
      }
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
            {!isHappyPath && (
              <div className="flex flex-col gap-1">
                <label className="text-xs font-medium text-muted" htmlFor="preflight-ref">
                  Week containing
                </label>
                <input
                  id="preflight-ref"
                  type="date"
                  className={fieldClass}
                  value={referenceDate}
                  onChange={(e) => {
                    setReferenceDate(e.target.value);
                    setPreflight(null);
                    setAcknowledged(false);
                  }}
                />
              </div>
            )}
            {isHappyPath && !preflight && (
              <div className="flex flex-col gap-1 min-w-[12rem]">
                <p className="text-xs font-medium text-muted">Check-in → plan</p>
                <p className="text-sm text-ink">
                  {(() => {
                    const checkInMon = mondayOfContainingWeek(referenceDate);
                    const planMon = planWeekStartFromCheckInMonday(checkInMon);
                    return `${formatWeekRangeLabel(checkInMon, addDaysIso(checkInMon, 6))} → ${formatWeekRangeLabel(planMon, addDaysIso(planMon, 6))}`;
                  })()}
                </p>
              </div>
            )}
            <Button
              type="button"
              onClick={() => assembleMutation.mutate()}
              disabled={assembleMutation.isPending}
            >
              {assembleMutation.isPending ? 'Building…' : 'Show what will be sent'}
            </Button>
            <Button
              href={`/coach/check-in?referenceDate=${encodeURIComponent(referenceDate)}`}
              variant="secondary"
            >
              Edit check-in
            </Button>
          </div>
        </Card>

        {error && <p className="text-sm text-danger">{error}</p>}

        {preflight && (
          <>
            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Weeks</h2>
              <p className="text-sm text-ink">
                Check-in:{' '}
                <span className="text-muted">
                  {formatWeekRangeLabel(
                    preflight.checkInWeekStartDate,
                    preflight.checkInWeekEndDate
                  )}
                </span>
              </p>
              <p className="text-sm text-ink mt-1">
                Plan:{' '}
                <span className="text-muted">
                  {formatWeekRangeLabel(preflight.planWeekStartDate, preflight.planWeekEndDate)}
                </span>
                {preflight.consented ? ' · consented' : ' · not yet consented'}
              </p>
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-3">What will be sent</h2>
              <ul className="space-y-2 mb-6">
                {preflight.report.included.map((item) => (
                  <li key={`in-${item.category}`} className="text-sm text-ink">
                    <span className="font-medium">{item.category.replaceAll('_', ' ')}</span>
                    <span className="text-muted"> — {item.detail}</span>
                  </li>
                ))}
              </ul>
              <h2 className="text-lg font-semibold text-ink mb-3">What is stripped</h2>
              <ul className="space-y-2">
                {preflight.report.stripped.map((item) => (
                  <li key={`out-${item.category}`} className="text-sm text-ink">
                    <span className="font-medium">{item.category.replaceAll('_', ' ')}</span>
                    <span className="text-muted"> — {item.detail}</span>
                  </li>
                ))}
              </ul>
            </Card>

            <Card>
              <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
                <h2 className="text-lg font-semibold text-ink">Inspect JSON</h2>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setInspectOpen((o) => !o)}
                >
                  {inspectOpen ? 'Hide JSON' : 'Inspect JSON'}
                </Button>
              </div>
              {inspectOpen && (
                <pre className="overflow-auto rounded-tempo border border-border bg-canvas p-3 text-xs text-ink max-h-[28rem]">
                  {prettyPayload}
                </pre>
              )}
            </Card>

            {!preflight.consented ? (
              <Card>
                <label className="flex items-start gap-3 text-sm text-ink cursor-pointer">
                  <input
                    type="checkbox"
                    className="mt-1"
                    checked={acknowledged}
                    onChange={(e) => setAcknowledged(e.target.checked)}
                  />
                  <span>
                    I have reviewed the summary (and inspected the payload if I chose). I consent to
                    send this redacted packet — including the multi-week recent training window — to
                    Anthropic using my BYO key when I send to coach.
                  </span>
                </label>
                <div className="mt-4">
                  <Button
                    type="button"
                    disabled={!acknowledged || consentMutation.isPending}
                    onClick={() => consentMutation.mutate(preflight.id)}
                  >
                    {consentMutation.isPending ? 'Saving consent...' : 'Confirm consent'}
                  </Button>
                </div>
              </Card>
            ) : (
              <Card>
                <p className="text-sm text-ink mb-4">
                  Consent recorded. Send to coach sends this redacted packet to Anthropic with your
                  BYO key and stores a draft week for review.
                </p>
                <div className="flex flex-wrap gap-2 items-center">
                  <Button
                    type="button"
                    disabled={generateMutation.isPending || jobPolling}
                    onClick={() => generateMutation.mutate(preflight.id)}
                  >
                    {generateMutation.isPending || jobPolling
                      ? 'Sending...'
                      : isAdjustMode
                        ? 'Send to coach'
                        : 'Send to coach'}
                  </Button>
                  <Button href="/coach" variant="secondary">
                    Back to Week
                  </Button>
                </div>
                {job && (
                  <div className="mt-4 text-sm text-muted space-y-1">
                    <p>
                      Status:{' '}
                      <span className="text-ink">
                        {job.status === 'ready'
                          ? 'Ready'
                          : job.status === 'failed'
                            ? 'Failed'
                            : job.status === 'running'
                              ? 'Working…'
                              : job.status}
                      </span>
                    </p>
                    {job.status === 'ready' && (
                      <p className="text-ink">
                        Draft ready for {formatCoachDate(job.weekStartDate)}.{' '}
                        <Link
                          href={`/coach/review?weekStartDate=${encodeURIComponent(job.weekStartDate)}`}
                          className="underline"
                        >
                          Review and accept
                        </Link>
                        .
                      </p>
                    )}
                    {job.status === 'failed' && (
                      <p className="text-danger">
                        {formatCoachJobError(job.errorCode, job.errorMessage)}
                      </p>
                    )}
                  </div>
                )}
              </Card>
            )}
          </>
        )}
      </div>
    </PageShell>
  );
}

export default function CoachPreflightPage() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="What your coach will see">
          <Card>
            <p className="text-sm text-muted">Loading...</p>
          </Card>
        </PageShell>
      }
    >
      <PreflightPageInner />
    </Suspense>
  );
}
