'use client';

import { useState, type ReactNode } from 'react';
import { Dialog } from '@/components/ui/Dialog';
import { Button } from '@/components/ui/Button';

const fieldClass =
  'w-full rounded-tempo border border-border bg-canvas px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-volt';

export function AskForChangesDialog({
  open,
  onClose,
  note,
  onNoteChange,
  whatsSentLine,
  inspectContent,
  sending,
  progressLabel,
  onSend,
  notePlaceholder = 'e.g. Travel moved Thursday — shift quality to Friday',
  noteLabel = 'What changed?',
}: {
  open: boolean;
  onClose: () => void;
  note: string;
  onNoteChange: (value: string) => void;
  whatsSentLine: string;
  inspectContent?: ReactNode;
  sending: boolean;
  progressLabel?: string | null;
  onSend: () => void;
  notePlaceholder?: string;
  noteLabel?: string;
}) {
  const [inspectOpen, setInspectOpen] = useState(false);

  return (
    <Dialog
      open={open}
      onClose={() => {
        if (sending) {
          return;
        }
        setInspectOpen(false);
        onClose();
      }}
      title="Ask for changes"
      footer={
        <>
          <Button
            type="button"
            variant="secondary"
            disabled={sending}
            onClick={() => {
              setInspectOpen(false);
              onClose();
            }}
          >
            Cancel
          </Button>
          <Button
            type="button"
            disabled={sending || !note.trim()}
            onClick={onSend}
          >
            {sending ? 'Sending…' : 'Send to coach'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <label className="block text-sm text-muted space-y-1">
          <span>{noteLabel}</span>
          <textarea
            className={fieldClass + ' min-h-[96px]'}
            value={note}
            onChange={(e) => onNoteChange(e.target.value)}
            placeholder={notePlaceholder}
            disabled={sending}
          />
        </label>
        <p className="text-sm text-muted">
          {whatsSentLine}{' '}
          {inspectContent != null && (
            <button
              type="button"
              className="underline text-ink"
              onClick={() => setInspectOpen((o) => !o)}
            >
              {inspectOpen ? "Hide what's sent" : "See what's sent"}
            </button>
          )}
        </p>
        {inspectOpen && inspectContent != null && (
          <div className="rounded-tempo border border-border bg-canvas p-3 text-xs text-muted space-y-2 max-h-48 overflow-auto">
            {inspectContent}
          </div>
        )}
        {progressLabel && (
          <p className="text-sm text-muted" aria-live="polite">
            {progressLabel}
          </p>
        )}
      </div>
    </Dialog>
  );
}
