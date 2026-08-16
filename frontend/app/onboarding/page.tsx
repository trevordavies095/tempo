'use client';

import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AuthGuard } from '@/components/AuthGuard';
import { TempoExportImport } from '@/components/TempoExportImport';
import { EssentialsStep } from '@/components/onboarding/EssentialsStep';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { useAuth } from '@/contexts/AuthContext';
import { importJobHasSettings, type ImportJob } from '@/lib/api';

type OnboardingStep =
  | 'ask_restore'
  | 'tempo_restore'
  | 'essentials'
  | 'ask_strava'
  | 'strava_stub';

function stepTitle(step: OnboardingStep): string {
  switch (step) {
    case 'ask_restore':
      return 'Welcome to Tempo';
    case 'tempo_restore':
      return 'Restore from Tempo export';
    case 'essentials':
      return 'Essential settings';
    case 'ask_strava':
      return 'Import from Strava?';
    case 'strava_stub':
      return 'Strava import';
  }
}

function stepSubtitle(step: OnboardingStep): string {
  switch (step) {
    case 'ask_restore':
      return 'Start by restoring a previous Tempo export, or set up this install fresh.';
    case 'tempo_restore':
      return 'Upload a Tempo export ZIP. Settings, shoes, workouts, and media come back together.';
    case 'essentials':
      return 'Set units and heart rate zones before you import runs. A default shoe is optional.';
    case 'ask_strava':
      return 'If you have a Strava archive, you can import it after essentials.';
    case 'strava_stub':
      return 'Strava archive upload lands in a following update. You can finish setup now.';
  }
}

function OnboardingWizard() {
  const { user, completeOnboarding } = useAuth();
  const router = useRouter();
  const [step, setStep] = useState<OnboardingStep>('ask_restore');
  const [restoreFailed, setRestoreFailed] = useState(false);
  const [isCompleting, setIsCompleting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [restoreKey, setRestoreKey] = useState(0);

  useEffect(() => {
    if (user?.onboardingCompleted) {
      router.replace('/dashboard');
    }
  }, [user, router]);

  const finish = useCallback(async () => {
    setError(null);
    setIsCompleting(true);
    try {
      await completeOnboarding();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to finish setup');
      setIsCompleting(false);
    }
  }, [completeOnboarding]);

  const goEssentials = useCallback(() => {
    setError(null);
    setRestoreFailed(false);
    setStep('essentials');
  }, []);

  const handleRestoreCompleted = useCallback(
    async (job: ImportJob) => {
      setRestoreFailed(false);
      if (importJobHasSettings(job)) {
        await finish();
        return;
      }
      goEssentials();
    },
    [finish, goEssentials]
  );

  const handleRestoreFailed = useCallback(() => {
    setRestoreFailed(true);
  }, []);

  if (user?.onboardingCompleted) {
    return null;
  }

  return (
    <PageShell density="control" title={stepTitle(step)} subtitle={stepSubtitle(step)}>
      {step === 'ask_restore' ? (
        <Card className="max-w-lg space-y-4">
          <p className="text-sm text-muted">
            Restoring brings back settings, shoes, workouts, and media from a Tempo export ZIP.
            If this is a new install, choose no and configure essentials next.
          </p>
          <div className="flex flex-wrap gap-3">
            <Button onClick={() => setStep('tempo_restore')}>Yes, restore export</Button>
            <Button variant="secondary" onClick={goEssentials}>
              No, set up fresh
            </Button>
          </div>
        </Card>
      ) : null}

      {step === 'tempo_restore' ? (
        <div className="space-y-4 max-w-2xl">
          <TempoExportImport
            key={restoreKey}
            onJobCompleted={handleRestoreCompleted}
            onJobFailedOrCancelled={handleRestoreFailed}
          />
          {restoreFailed ? (
            <Card className="space-y-3">
              <p className="text-sm text-muted">
                Restore did not finish. Retry with another ZIP, or set up this install fresh
                without marking onboarding complete yet.
              </p>
              <div className="flex flex-wrap gap-3">
                <Button
                  variant="secondary"
                  onClick={() => {
                    setRestoreFailed(false);
                    setRestoreKey((k) => k + 1);
                  }}
                >
                  Retry
                </Button>
                <Button variant="secondary" onClick={goEssentials}>
                  Set up fresh instead
                </Button>
              </div>
            </Card>
          ) : null}
          {error ? (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          ) : null}
          {isCompleting ? <p className="text-sm text-muted">Finishing setup…</p> : null}
        </div>
      ) : null}

      {step === 'essentials' ? (
        <EssentialsStep onContinue={() => setStep('ask_strava')} />
      ) : null}

      {step === 'ask_strava' ? (
        <Card className="max-w-lg space-y-4">
          <p className="text-sm text-muted">
            You can import a Strava bulk export ZIP during setup, or skip and add individual
            GPX/FIT files later from Import.
          </p>
          {error ? (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex flex-wrap gap-3">
            <Button onClick={() => setStep('strava_stub')} disabled={isCompleting}>
              Yes, I have a Strava archive
            </Button>
            <Button
              variant="secondary"
              onClick={() => void finish()}
              disabled={isCompleting}
            >
              {isCompleting ? 'Finishing…' : 'No, finish setup'}
            </Button>
          </div>
        </Card>
      ) : null}

      {step === 'strava_stub' ? (
        <Card className="max-w-lg space-y-4">
          <p className="text-sm text-muted">
            Strava archive upload will be available here in a following update. You can skip for
            now and finish onboarding — bulk Strava import will also live under Settings later.
          </p>
          {error ? (
            <p className="text-sm text-danger" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex flex-wrap gap-3">
            <Button onClick={() => void finish()} disabled={isCompleting}>
              {isCompleting ? 'Finishing…' : 'Skip for now'}
            </Button>
            <Button
              variant="secondary"
              onClick={() => setStep('ask_strava')}
              disabled={isCompleting}
            >
              Back
            </Button>
          </div>
        </Card>
      ) : null}
    </PageShell>
  );
}

export default function OnboardingPage() {
  return (
    <AuthGuard>
      <OnboardingWizard />
    </AuthGuard>
  );
}
