'use client';

import { Button } from '@/components/ui/Button';
import type { CoachExchangeReply, CoachExchangeVerdict } from '@/lib/api';

function verdictLabel(verdict: CoachExchangeVerdict): string {
  switch (verdict) {
    case 'on_track':
      return 'On track';
    case 'noise':
      return 'Noise';
    case 'watch':
      return 'Watch';
    case 'concern':
      return 'Concern';
    default:
      return verdict;
  }
}

export function CoachExchangeReplyView({
  reply,
  onAskForThisChange,
}: {
  reply: CoachExchangeReply;
  onAskForThisChange?: () => void;
}) {
  const flags = reply.safetyFlags?.filter((f) => f.trim().length > 0) ?? [];
  return (
    <div className="space-y-3 mt-3">
      {flags.length > 0 && (
        <div className="rounded-tempo border border-danger/40 bg-danger/5 p-3 space-y-1">
          <p className="text-xs font-medium uppercase tracking-wide text-danger">Safety</p>
          <ul className="list-disc pl-4 space-y-1">
            {flags.map((flag) => (
              <li key={flag} className="text-sm text-ink">
                {flag}
              </li>
            ))}
          </ul>
        </div>
      )}
      <p className="text-xs uppercase tracking-wide text-muted">{verdictLabel(reply.verdict)}</p>
      <div>
        <p className="text-xs font-medium text-muted mb-1">What happened</p>
        <p className="text-sm text-ink whitespace-pre-wrap">{reply.whatHappened}</p>
      </div>
      <div>
        <p className="text-xs font-medium text-muted mb-1">Why it looks like that</p>
        <p className="text-sm text-ink whitespace-pre-wrap">{reply.whyItLooksLikeThat}</p>
      </div>
      <div>
        <p className="text-xs font-medium text-muted mb-1">What it means next</p>
        <p className="text-sm text-ink whitespace-pre-wrap">{reply.whatItMeansNext}</p>
      </div>
      {reply.suggestedChange && (
        <div className="rounded-tempo border border-border p-3 space-y-2">
          <p className="text-xs font-medium text-muted">Suggested change</p>
          <p className="text-sm text-ink">{reply.suggestedChange.summary}</p>
          <p className="text-xs text-muted">{reply.suggestedChange.note}</p>
          {onAskForThisChange && (
            <Button type="button" size="sm" onClick={onAskForThisChange}>
              Ask for this change
            </Button>
          )}
        </div>
      )}
    </div>
  );
}
