'use client';

import { useEffect, useState } from 'react';
import {
  connectIntervalsIcu,
  disconnectIntervalsIcu,
  getIntervalsIcuConnection,
  type IntervalsIcuConnectionStatus,
} from '@/lib/api';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

function formatTimestamp(value: string | null): string {
  if (!value) {
    return 'Never';
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return 'Never';
  }

  return date.toLocaleString();
}

export function IntervalsIcuSection() {
  const [status, setStatus] = useState<IntervalsIcuConnectionStatus | null>(null);
  const [apiKey, setApiKey] = useState('');
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const load = async () => {
      try {
        setStatus(await getIntervalsIcuConnection());
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load intervals.icu status');
      } finally {
        setIsLoading(false);
      }
    };

    void load();
  }, []);

  const handleConnect = async () => {
    setIsSaving(true);
    setError(null);
    try {
      const next = await connectIntervalsIcu(apiKey);
      setStatus(next);
      setApiKey('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to connect');
    } finally {
      setIsSaving(false);
    }
  };

  const handleDisconnect = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await disconnectIntervalsIcu();
      setStatus(await getIntervalsIcuConnection());
      setApiKey('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to disconnect');
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Card>
      <h3 className="text-lg font-semibold text-ink mb-2">intervals.icu</h3>
      <p className="text-sm text-muted mb-4">
        Optional. Connect Garmin (or another device) on intervals.icu, then paste your personal
        Developer API key. Tempo stores the key encrypted and never shows it again. Workout data
        transits intervals.icu.
      </p>

      {isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : status?.connected ? (
        <div className="space-y-3">
          <dl className="text-sm space-y-1">
            <div className="flex gap-2">
              <dt className="text-muted">Status</dt>
              <dd className="text-ink">{status.enabled ? 'Enabled' : 'Disabled'}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="text-muted">Last successful sync</dt>
              <dd className="text-ink">{formatTimestamp(status.lastSuccessfulSyncAt)}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="text-muted">Last attempt</dt>
              <dd className="text-ink">{formatTimestamp(status.lastSyncAttemptAt)}</dd>
            </div>
            {status.lastError && (
              <div className="flex gap-2">
                <dt className="text-muted">Last error</dt>
                <dd className="text-danger">{status.lastError}</dd>
              </div>
            )}
          </dl>
          <Button
            type="button"
            variant="danger"
            onClick={handleDisconnect}
            disabled={isSaving}
          >
            {isSaving ? 'Disconnecting...' : 'Disconnect'}
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          <label className="block">
            <span className="sr-only">intervals.icu API key</span>
            <input
              type="password"
              autoComplete="off"
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              placeholder="Personal API key"
              className={fieldClass}
            />
          </label>
          <Button
            type="button"
            onClick={handleConnect}
            disabled={isSaving || apiKey.trim().length === 0}
          >
            {isSaving ? 'Connecting...' : 'Connect'}
          </Button>
        </div>
      )}

      {error && <p className="mt-3 text-sm text-danger">{error}</p>}
    </Card>
  );
}
