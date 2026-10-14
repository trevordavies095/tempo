'use client';

import { useEffect, useMemo } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Button } from '@/components/ui/Button';
import { getCoachDraftWeekOrNull, getCoachHome } from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';

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

function CoachPageContent() {
  const router = useRouter();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();

  const weekStartDate = useMemo(() => localMondayIso(), []);

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
    enabled: showCoachNav,
  });

  const draftQuery = useQuery({
    queryKey: ['coach-draft-week', weekStartDate],
    queryFn: () => getCoachDraftWeekOrNull(weekStartDate),
    enabled: showCoachNav,
  });

  useEffect(() => {
    if (isFetched && !showCoachNav) {
      router.replace('/dashboard');
    }
  }, [isFetched, showCoachNav, router]);

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
                {draftQuery.data && (
                  <Link
                    href={`/coach/review?weekStartDate=${encodeURIComponent(draftQuery.data.weekStartDate)}`}
                  >
                    <Button type="button">Review draft</Button>
                  </Link>
                )}
                <Link href="/coach/check-in">
                  <Button type="button" variant={draftQuery.data ? 'secondary' : undefined}>
                    Weekly check-in
                  </Button>
                </Link>
                <Link href="/coach/preflight">
                  <Button type="button" variant="secondary">
                    Privacy pre-flight
                  </Button>
                </Link>
                <Link href="/coach/constraints">
                  <Button type="button" variant="secondary">
                    Non-negotiables
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
                </div>
              }
            />
          )}
        </Card>
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
