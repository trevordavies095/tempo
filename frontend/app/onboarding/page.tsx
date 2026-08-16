'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { useAuth } from '@/contexts/AuthContext';

function OnboardingStub() {
  const { user, completeOnboarding } = useAuth();
  const router = useRouter();
  const [isFinishing, setIsFinishing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (user?.onboardingCompleted) {
      router.replace('/dashboard');
    }
  }, [user, router]);

  if (user?.onboardingCompleted) {
    return null;
  }

  const handleFinish = async () => {
    setError(null);
    setIsFinishing(true);
    try {
      await completeOnboarding();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to finish setup');
      setIsFinishing(false);
    }
  };

  return (
    <PageShell
      density="control"
      title="Welcome to Tempo"
      subtitle="Finish setup to start tracking your runs."
    >
      <Card className="max-w-lg space-y-4">
        <p className="text-sm text-muted">
          This temporary step marks your account as set up. Guided restore,
          settings, and Strava import will land here in a later update.
        </p>
        {error ? (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        ) : null}
        <Button onClick={handleFinish} disabled={isFinishing}>
          {isFinishing ? 'Finishing…' : 'Finish setup'}
        </Button>
      </Card>
    </PageShell>
  );
}

export default function OnboardingPage() {
  return (
    <AuthGuard>
      <OnboardingStub />
    </AuthGuard>
  );
}
