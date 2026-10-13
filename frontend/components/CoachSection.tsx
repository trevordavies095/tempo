'use client';

import { useEffect, useState } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import {
  COACH_ANTHROPIC_MODELS,
  deleteCoachProvider,
  getCoachProvider,
  optInCoach,
  optOutCoach,
  putCoachProvider,
  testCoachProvider,
  type CoachProviderStatus,
} from '@/lib/api';
import { coachSettingsQueryKey, useCoachFeature } from '@/hooks/useCoachFeature';
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

export function CoachSection() {
  const queryClient = useQueryClient();
  const {
    available,
    optedIn,
    providerConnected,
    modelId: settingsModelId,
    isLoading,
    isError,
    error: loadError,
  } = useCoachFeature();

  const [isSaving, setIsSaving] = useState(false);
  const [isTesting, setIsTesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [apiKey, setApiKey] = useState('');
  const [modelId, setModelId] = useState(COACH_ANTHROPIC_MODELS[0].id);
  const [provider, setProvider] = useState<CoachProviderStatus | null>(null);
  const [providerLoading, setProviderLoading] = useState(false);

  useEffect(() => {
    if (!available) {
      setProvider(null);
      return;
    }

    let cancelled = false;
    const load = async () => {
      setProviderLoading(true);
      try {
        const status = await getCoachProvider();
        if (!cancelled) {
          setProvider(status);
          if (status.modelId) {
            setModelId(status.modelId);
          } else if (settingsModelId) {
            setModelId(settingsModelId);
          }
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Failed to load provider status');
        }
      } finally {
        if (!cancelled) {
          setProviderLoading(false);
        }
      }
    };

    void load();
    return () => {
      cancelled = true;
    };
  }, [available, settingsModelId]);

  const invalidateCoach = async () => {
    await queryClient.invalidateQueries({ queryKey: coachSettingsQueryKey });
    await queryClient.invalidateQueries({ queryKey: ['coach-home'] });
  };

  if (!isLoading && !available) {
    return null;
  }

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

  const handleSaveProvider = async () => {
    setIsSaving(true);
    setError(null);
    try {
      const next = await putCoachProvider(apiKey, modelId);
      setProvider(next);
      setApiKey('');
      await invalidateCoach();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save Anthropic key');
    } finally {
      setIsSaving(false);
    }
  };

  const handleTest = async () => {
    setIsTesting(true);
    setError(null);
    try {
      const next = await testCoachProvider(apiKey.trim() || undefined);
      if (providerConnected || next.connected) {
        setProvider(await getCoachProvider());
      }
      await invalidateCoach();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to test connection');
      if (providerConnected) {
        try {
          setProvider(await getCoachProvider());
        } catch {
          /* keep prior status */
        }
      }
    } finally {
      setIsTesting(false);
    }
  };

  const handleRemoveKey = async () => {
    setIsSaving(true);
    setError(null);
    try {
      await deleteCoachProvider();
      setProvider({ connected: false, modelId: null, lastProbeAt: null, lastError: null });
      setApiKey('');
      await invalidateCoach();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to remove key');
    } finally {
      setIsSaving(false);
    }
  };

  const busy = isSaving || isTesting || providerLoading;

  return (
    <Card>
      <h3 className="text-lg font-semibold text-ink mb-2">AI Coach</h3>
      <p className="text-sm text-muted mb-4">
        Optional weekly coaching loop using your own Anthropic API key. Off until you opt in.
        Disabling hides Coach without deleting coach data. The key is stored encrypted and never
        shown again. Tempo AI Coach is not a doctor and does not provide medical advice.
      </p>

      {isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : isError ? (
        <p className="text-sm text-danger">
          {loadError instanceof Error ? loadError.message : 'Failed to load Coach settings'}
        </p>
      ) : (
        <div className="space-y-6">
          <div className="space-y-3">
            <dl className="text-sm space-y-1">
              <div className="flex gap-2">
                <dt className="text-muted">Coach</dt>
                <dd className="text-ink">{optedIn ? 'Enabled' : 'Available — not opted in'}</dd>
              </div>
            </dl>
            {optedIn ? (
              <Button type="button" variant="danger" onClick={handleOptOut} disabled={busy}>
                {isSaving ? 'Disabling...' : 'Disable Coach'}
              </Button>
            ) : (
              <Button type="button" onClick={handleOptIn} disabled={busy}>
                {isSaving ? 'Enabling...' : 'Enable Coach'}
              </Button>
            )}
          </div>

          <div className="border-t border-border pt-4 space-y-3">
            <h4 className="text-sm font-semibold text-ink">Anthropic connection</h4>
            <p className="text-sm text-muted">
              Save your key without probing. Use Test connection to verify auth without sending
              workout data.
            </p>

            {providerLoading ? (
              <div className="text-muted text-sm">Loading provider...</div>
            ) : (
              <>
                <dl className="text-sm space-y-1">
                  <div className="flex gap-2">
                    <dt className="text-muted">Provider</dt>
                    <dd className="text-ink">
                      {provider?.connected || providerConnected ? 'Connected' : 'Not connected'}
                    </dd>
                  </div>
                  {(provider?.modelId || settingsModelId) && (
                    <div className="flex gap-2">
                      <dt className="text-muted">Model</dt>
                      <dd className="text-ink">{provider?.modelId ?? settingsModelId}</dd>
                    </div>
                  )}
                  {provider?.lastProbeAt && (
                    <div className="flex gap-2">
                      <dt className="text-muted">Last probe</dt>
                      <dd className="text-ink">{formatTimestamp(provider.lastProbeAt)}</dd>
                    </div>
                  )}
                  {provider?.lastError && (
                    <div className="flex gap-2">
                      <dt className="text-muted">Last error</dt>
                      <dd className="text-danger">{provider.lastError}</dd>
                    </div>
                  )}
                </dl>

                <label className="block">
                  <span className="text-sm text-muted mb-1 block">Model</span>
                  <select
                    value={modelId}
                    onChange={(event) => setModelId(event.target.value)}
                    className={fieldClass}
                    disabled={busy}
                  >
                    {COACH_ANTHROPIC_MODELS.map((model) => (
                      <option key={model.id} value={model.id}>
                        {model.label}
                      </option>
                    ))}
                  </select>
                </label>

                <label className="block">
                  <span className="sr-only">Anthropic API key</span>
                  <input
                    type="password"
                    autoComplete="off"
                    value={apiKey}
                    onChange={(event) => setApiKey(event.target.value)}
                    placeholder={
                      provider?.connected || providerConnected
                        ? 'Replace API key'
                        : 'Anthropic API key'
                    }
                    className={fieldClass}
                    disabled={busy}
                  />
                </label>

                <div className="flex flex-wrap gap-2">
                  <Button
                    type="button"
                    onClick={handleSaveProvider}
                    disabled={busy || apiKey.trim().length === 0}
                  >
                    {isSaving ? 'Saving...' : 'Save key'}
                  </Button>
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={handleTest}
                    disabled={busy || (apiKey.trim().length === 0 && !providerConnected)}
                  >
                    {isTesting ? 'Testing...' : 'Test connection'}
                  </Button>
                  {(provider?.connected || providerConnected) && (
                    <Button
                      type="button"
                      variant="danger"
                      onClick={handleRemoveKey}
                      disabled={busy}
                    >
                      Remove key
                    </Button>
                  )}
                </div>
              </>
            )}
          </div>
        </div>
      )}

      {error && <p className="mt-3 text-sm text-danger">{error}</p>}
    </Card>
  );
}
