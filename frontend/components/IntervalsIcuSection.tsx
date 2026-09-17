'use client';

import { useEffect, useState } from 'react';
import {
  connectIntervalsIcu,
  disconnectIntervalsIcu,
  enableIntervalsIcu,
  getIntervalsIcuConnection,
  syncIntervalsIcu,
  type IntervalsIcuConnectionStatus,
} from '@/lib/api';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

const SYNC_POLL_MS = 1000;
const SYNC_POLL_TIMEOUT_MS = 20_000;

function statusTickChanged(
  previous: IntervalsIcuConnectionStatus,
  next: IntervalsIcuConnectionStatus
): boolean {
  return (
    next.lastSyncAttemptAt !== previous.lastSyncAttemptAt ||
    next.lastSuccessfulSyncAt !== previous.lastSuccessfulSyncAt ||
    next.lastError !== previous.lastError ||
    next.enabled !== previous.enabled
  );
}

async function waitForSyncTick(
  previous: IntervalsIcuConnectionStatus
): Promise<IntervalsIcuConnectionStatus> {
  const deadline = Date.now() + SYNC_POLL_TIMEOUT_MS;
  let latest = previous;

  while (Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, SYNC_POLL_MS));
    latest = await getIntervalsIcuConnection();
    if (statusTickChanged(previous, latest)) {
      return latest;
    }
  }

  return latest;
}

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
  const [isSyncing, setIsSyncing] = useState(false);
  const [isReplacing, setIsReplacing] = useState(false);
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

  const handleReplaceKey = async () => {
    setIsReplacing(true);
    setError(null);
    try {
      const next = await connectIntervalsIcu(apiKey);
      setStatus(next);
      setApiKey('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to replace key');
    } finally {
      setIsReplacing(false);
    }
  };

  const handleReEnable = async () => {
    setIsSaving(true);
    setError(null);
    try {
      setStatus(await enableIntervalsIcu());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to re-enable');
    } finally {
      setIsSaving(false);
    }
  };

  const handleSyncNow = async () => {
    setIsSyncing(true);
    setError(null);
    try {
      const previous = status ?? (await getIntervalsIcuConnection());
      const outcome = await syncIntervalsIcu();
      if (outcome === 'idle') {
        setStatus(await getIntervalsIcuConnection());
        return;
      }

      setStatus(await waitForSyncTick(previous));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to request sync');
    } finally {
      setIsSyncing(false);
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
          <label className="block">
            <span className="sr-only">Replace intervals.icu API key</span>
            <input
              type="password"
              autoComplete="off"
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              placeholder="Replace API key"
              className={fieldClass}
            />
          </label>
          <div className="flex flex-wrap gap-2">
            {status.enabled === false && (
              <Button
                type="button"
                onClick={handleReEnable}
                disabled={isSaving || isSyncing || isReplacing}
              >
                {isSaving ? 'Re-enabling...' : 'Re-enable'}
              </Button>
            )}
            <Button
              type="button"
              onClick={handleReplaceKey}
              disabled={isSaving || isSyncing || isReplacing || apiKey.trim().length === 0}
            >
              {isReplacing ? 'Replacing...' : 'Replace key'}
            </Button>
            <Button
              type="button"
              onClick={handleSyncNow}
              disabled={isSaving || isSyncing || isReplacing || status.enabled === false}
            >
              {isSyncing ? 'Syncing...' : 'Sync now'}
            </Button>
            <Button
              type="button"
              variant="danger"
              onClick={handleDisconnect}
              disabled={isSaving || isSyncing || isReplacing}
            >
              {isSaving && status.enabled !== false ? 'Disconnecting...' : 'Disconnect'}
            </Button>
          </div>
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
