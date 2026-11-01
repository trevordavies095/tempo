'use client';

import { useEffect, useRef, useState } from 'react';
import {
  createApiKey,
  listApiKeys,
  revealApiKey,
  revokeApiKey,
  type ApiKeySummary,
} from '@/lib/api';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const COPIED_RESET_MS = 2000;

function formatDate(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString();
}

export function ApiKeySection() {
  const [activeKey, setActiveKey] = useState<ApiKeySummary | null>(null);
  // Plaintext stays in memory only while this page is open.
  const [plaintext, setPlaintext] = useState<string | null>(null);
  const [isVisible, setIsVisible] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isWorking, setIsWorking] = useState(false);
  const [copied, setCopied] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const copiedTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    const load = async () => {
      try {
        const keys = await listApiKeys();
        setActiveKey(keys.find((k) => k.revokedAt === null) ?? null);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Failed to load API key');
      } finally {
        setIsLoading(false);
      }
    };

    void load();
    return () => {
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
    };
  }, []);

  const ensurePlaintext = async (key: ApiKeySummary): Promise<string> => {
    if (plaintext) return plaintext;
    const revealed = await revealApiKey(key.id);
    setPlaintext(revealed);
    return revealed;
  };

  const handleGenerate = async () => {
    setIsWorking(true);
    setError(null);
    try {
      const created = await createApiKey();
      setActiveKey({
        id: created.id,
        label: created.label,
        keyPrefix: created.keyPrefix,
        createdAt: created.createdAt,
        revokedAt: null,
        canReveal: true,
      });
      setPlaintext(created.key);
      setIsVisible(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to generate API key');
    } finally {
      setIsWorking(false);
    }
  };

  const handleToggleVisible = async () => {
    if (!activeKey) return;
    if (isVisible) {
      setIsVisible(false);
      return;
    }

    setIsWorking(true);
    setError(null);
    try {
      await ensurePlaintext(activeKey);
      setIsVisible(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to show API key');
    } finally {
      setIsWorking(false);
    }
  };

  const handleCopy = async () => {
    if (!activeKey) return;
    setIsWorking(true);
    setError(null);
    try {
      await navigator.clipboard.writeText(await ensurePlaintext(activeKey));
      setCopied(true);
      if (copiedTimer.current) clearTimeout(copiedTimer.current);
      copiedTimer.current = setTimeout(() => setCopied(false), COPIED_RESET_MS);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to copy API key');
    } finally {
      setIsWorking(false);
    }
  };

  const handleRevoke = async () => {
    if (!activeKey) return;
    if (
      !window.confirm(
        'Revoke this API key? Anything using it (tempo-cli, Claude Desktop, scripts) will stop working immediately. You can generate a new key afterwards.'
      )
    ) {
      return;
    }

    setIsWorking(true);
    setError(null);
    try {
      await revokeApiKey(activeKey.id);
      setActiveKey(null);
      setPlaintext(null);
      setIsVisible(false);
      setCopied(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to revoke API key');
    } finally {
      setIsWorking(false);
    }
  };

  return (
    <Card>
      <h3 className="text-lg font-semibold text-ink mb-2">API key</h3>
      <p className="text-sm text-muted mb-4">
        For tempo-cli, Claude Desktop, and other automation. Send it as{' '}
        <code className="font-mono">Authorization: Bearer &lt;key&gt;</code>. Anyone with this key
        can read and change your Tempo data, so keep it private. You can have one key at a time.
      </p>

      {isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : activeKey ? (
        <div className="space-y-3">
          <div className="flex flex-wrap items-center gap-2">
            <code
              className="min-w-0 flex-1 break-all rounded-tempo border border-border bg-canvas px-3 py-2 font-mono text-sm text-ink"
              aria-label={isVisible ? 'API key' : 'API key (hidden)'}
            >
              {isVisible && plaintext ? plaintext : `${activeKey.keyPrefix}${'•'.repeat(16)}`}
            </code>
            {activeKey.canReveal && (
              <>
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  onClick={handleToggleVisible}
                  disabled={isWorking}
                >
                  {isVisible ? 'Hide' : 'Show'}
                </Button>
                <Button
                  type="button"
                  size="sm"
                  variant="secondary"
                  onClick={handleCopy}
                  disabled={isWorking}
                >
                  {copied ? 'Copied' : 'Copy'}
                </Button>
              </>
            )}
          </div>
          {!activeKey.canReveal && (
            <p className="text-sm text-muted">
              This key was created before Tempo could display keys, so it can&apos;t be shown. It
              still works. To get a key you can see, revoke this one and generate a new one.
            </p>
          )}
          <p className="text-sm text-muted">Created {formatDate(activeKey.createdAt)}</p>
          <Button type="button" variant="danger" onClick={handleRevoke} disabled={isWorking}>
            Revoke
          </Button>
        </div>
      ) : (
        <div className="space-y-3">
          <p className="text-sm text-ink">No API key yet.</p>
          <Button type="button" onClick={handleGenerate} disabled={isWorking}>
            {isWorking ? 'Generating...' : 'Generate key'}
          </Button>
        </div>
      )}

      {error && <p className="mt-3 text-sm text-danger">{error}</p>}
    </Card>
  );
}
