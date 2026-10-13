'use client';

import { useEffect } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { useQuery } from '@tanstack/react-query';
import { AuthGuard } from '@/components/AuthGuard';
import { PageShell } from '@/components/ui/PageShell';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';
import { Button } from '@/components/ui/Button';
import { getCoachHome } from '@/lib/api';
import { useCoachFeature } from '@/hooks/useCoachFeature';

function CoachPageContent() {
  const router = useRouter();
  const { showCoachNav, isLoading: settingsLoading, isFetched } = useCoachFeature();

  const homeQuery = useQuery({
    queryKey: ['coach-home'],
    queryFn: getCoachHome,
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
          ) : (
            <EmptyState
              title="Coach is ready"
              description="Next up: connect your Anthropic API key, then complete coach onboarding. Those steps land in later slices."
              action={
                <div className="flex flex-wrap gap-2">
                  <Link href="/settings">
                    <Button type="button" variant="secondary">
                      Coach settings
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
