'use client';

import { useState } from 'react';
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

  const handleOptIn = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await optInCoach();
      await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
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
      await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
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
        Optional weekly coaching loop using your own Anthropic API key. Off until you opt in.
        Disabling hides Coach without deleting coach data. Tempo AI Coach is not a doctor and does
        not provide medical advice.
      </p>

      {isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : isError ? (
        <p className="text-sm text-danger">
          {loadError instanceof Error ? loadError.message : 'Failed to load Coach settings'}
        </p>
      ) : optedIn ? (
        <div className="space-y-3">
          <dl className="text-sm space-y-1">
            <div className="flex gap-2">
              <dt className="text-muted">Status</dt>
              <dd className="text-ink">Enabled</dd>
            </div>
          </dl>
          <Button type="button" variant="danger" onClick={handleOptOut} disabled={isSaving}>
            {isSaving ? 'Disabling...' : 'Disable Coach'}
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          <dl className="text-sm space-y-1">
            <div className="flex gap-2">
              <dt className="text-muted">Status</dt>
              <dd className="text-ink">Available — not opted in</dd>
            </div>
          </dl>
          <Button type="button" onClick={handleOptIn} disabled={isSaving}>
            {isSaving ? 'Enabling...' : 'Enable Coach'}
          </Button>
        </div>
      )}

      {error && <p className="mt-3 text-sm text-danger">{error}</p>}
    </Card>
  );
}
