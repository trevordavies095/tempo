'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { useMutation } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import {
  consentCoachPrivacyPreflight,
  postCoachPrivacyPreflight,
  type CoachPrivacyPreflight,
} from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function localDateInputValue(d: Date): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

function PreflightPageInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const initialRef = searchParams.get('referenceDate') || localDateInputValue(new Date());
  const [referenceDate, setReferenceDate] = useState(initialRef);
  const [preflight, setPreflight] = useState<CoachPrivacyPreflight | null>(null);
  const [inspectOpen, setInspectOpen] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const assembleMutation = useMutation({
    mutationFn: () => postCoachPrivacyPreflight(timezoneOffsetMinutes, referenceDate),
    onSuccess: (doc) => {
      setError(null);
      setPreflight(doc);
      setAcknowledged(false);
      setInspectOpen(false);
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

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

  const prettyPayload = useMemo(() => {
    if (!preflight?.redactedPayload) return '';
    try {
      return JSON.stringify(preflight.redactedPayload, null, 2);
    } catch {
      return String(preflight.redactedPayload);
    }
  }, [preflight]);

  if (settingsLoading || !isFetched) {
    return (
      <PageShell density="control" title="Privacy pre-flight">
        <Card>
          <p className="text-sm text-muted">Loading...</p>
        </Card>
      </PageShell>
    );
  }

  if (!showCoachNav) {
    return null;
  }

  return (
    <PageShell
      density="control"
      title="Privacy pre-flight"
      subtitle="Review what leaves your instance before any Anthropic call"
      leading={
        <div className="mb-4 space-y-3">
          <Link href="/coach" className="text-sm text-muted hover:text-ink">
            ← Coach
          </Link>
          <div
            className="rounded-tempo border border-border bg-canvas p-4 text-sm text-muted"
            role="note"
          >
            Tempo AI Coach is not a doctor and does not provide medical advice. Nothing is sent to
            Anthropic until you consent to this redacted packet.
          </div>
        </div>
      }
    >
      <div className="w-full space-y-6">
        <Card>
          <div className="flex flex-wrap items-end gap-3">
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
            <Button
              type="button"
              onClick={() => assembleMutation.mutate()}
              disabled={assembleMutation.isPending}
            >
              {assembleMutation.isPending ? 'Assembling...' : 'Assemble redacted packet'}
            </Button>
            <Link href={`/coach/check-in`}>
              <Button type="button" variant="secondary">
                Edit check-in
              </Button>
            </Link>
          </div>
        </Card>

        {error && <p className="text-sm text-danger">{error}</p>}

        {preflight && (
          <>
            <Card>
              <h2 className="text-lg font-semibold text-ink mb-1">Week</h2>
              <p className="text-sm text-muted">
                {preflight.weekStartDate} → {preflight.weekEndDate}
                {preflight.consented ? ' · consented' : ' · not yet consented'}
              </p>
              <p className="text-xs text-muted mt-2 font-mono">hash {preflight.payloadHash}</p>
            </Card>

            <Card>
              <h2 className="text-lg font-semibold text-ink mb-3">What will be sent</h2>
              <ul className="space-y-2 mb-6">
                {preflight.report.included.map((item) => (
                  <li key={`in-${item.category}`} className="text-sm text-ink">
                    <span className="font-medium">{item.category}</span>
                    <span className="text-muted"> — {item.detail}</span>
                  </li>
                ))}
              </ul>
              <h2 className="text-lg font-semibold text-ink mb-3">What is stripped</h2>
              <ul className="space-y-2">
                {preflight.report.stripped.map((item) => (
                  <li key={`out-${item.category}`} className="text-sm text-ink">
                    <span className="font-medium">{item.category}</span>
                    <span className="text-muted"> — {item.detail}</span>
                  </li>
                ))}
              </ul>
            </Card>

            <Card>
              <div className="flex flex-wrap items-center justify-between gap-2 mb-3">
                <h2 className="text-lg font-semibold text-ink">Inspect payload</h2>
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => setInspectOpen((o) => !o)}
                >
                  {inspectOpen ? 'Hide JSON' : 'Show redacted JSON'}
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
                    send this redacted packet to Anthropic using my BYO key when I generate a plan.
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
                <p className="text-sm text-ink">
                  Consent recorded. Plan generate (next slice) can use this pre-flight — no Anthropic
                  call from this screen.
                </p>
                <div className="mt-4">
                  <Link href="/coach">
                    <Button type="button" variant="secondary">
                      Back to Coach
                    </Button>
                  </Link>
                </div>
              </Card>
            )}
          </>
        )}
      </div>
    </PageShell>
  );
}

function PreflightPageContent() {
  return (
    <Suspense
      fallback={
        <PageShell density="control" title="Privacy pre-flight">
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

export default function CoachPreflightPage() {
  return (
    <AuthGuard>
      <PreflightPageContent />
    </AuthGuard>
  );
}
