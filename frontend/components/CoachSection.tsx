'use client';

import { useState } from 'react';
import Link from 'next/link';
import { useQueryClient } from '@tanstack/react-query';
import { optInCoach, optOutCoach } from '@/lib/api';
import { coachSettingsQueryKey, useCoachFeature } from '@/hooks/useCoachFeature';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

export function CoachSection() {
  const queryClient = useQueryClient();
  const { available, optedIn, isLoading, isError, error: loadError } = useCoachFeature();
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (!isLoading && !available) {
    return null;
  }

  const invalidateCoach = async () => {
    await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
    await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
  };

  const handleOptIn = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await optInCoach();
      await invalidateCoach();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to enable Coach');
    } finally {
      setIsSaving(false);
    }
  };

  const handleOptOut = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await optOutCoach();
      await invalidateCoach();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disable Coach');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Card>
      <h3 className="text-lg font-semibold text-ink mb-2">AI Coach</h3>
      <p className="text-sm text-muted mb-4">
        Optional weekly coaching loop. Off until you enable it. Profile, fixed dates, memory, and
        your Anthropic key live under Coach → Settings.
      </p>

      {isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : isError ? (
        <p className="text-sm text-danger">
          {loadError instanceof Error ? loadError.message : 'Failed to load Coach settings'}
        </p>
      ) : (
        <div className="space-y-4">
          <dl className="text-sm space-y-1">
            <div className="flex gap-2">
              <dt className="text-muted">Coach</dt>
              <dd className="text-ink">{optedIn ? 'Enabled' : 'Available — not opted in'}</dd>
            </div>
          </dl>
          <div className="flex flex-wrap items-center gap-3">
            {optedIn ? (
              <Button type="button" variant="danger" onClick={handleOptOut} disabled={isSaving}>
                {isSaving ? 'Disabling...' : 'Disable Coach'}
              </Button>
            ) : (
              <Button type="button" onClick={handleOptIn} disabled={isSaving}>
                {isSaving ? 'Enabling...' : 'Enable Coach'}
              </Button>
            )}
            {optedIn && (
              <Link href="/coach/settings" className="text-sm text-ink underline">
                Coach settings →
              </Link>
            )}
          </div>
        </div>
      )}

      {error && <p className="mt-3 text-sm text-danger">{error}</p>}
    </Card>
  );
}
