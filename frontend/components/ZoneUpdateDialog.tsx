'use client';

import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';

interface ZoneUpdateDialogProps {
  open: boolean;
  onClose: () => void;
  onRecalculateAll: () => void;
  onKeepExisting: () => void;
  workoutCount: number | null;
  isLoading?: boolean;
}

export function ZoneUpdateDialog({
  open,
  onClose,
  onRecalculateAll,
  onKeepExisting,
  workoutCount,
  isLoading = false,
}: ZoneUpdateDialogProps) {
  const handleRecalculateAll = () => {
    if (!isLoading) {
      onRecalculateAll();
    }
  };

  const handleKeepExisting = () => {
    if (!isLoading) {
      onKeepExisting();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Heart Rate Zones Updated"
      footer={
        <Button variant="secondary" size="sm" onClick={onClose} disabled={isLoading}>
          Cancel
        </Button>
      }
    >
      <p className="text-sm text-muted mb-4">
        Your heart rate zones have been updated. How would you like to handle existing workouts?
      </p>

      <div className="space-y-3">
        <button
          type="button"
          onClick={handleRecalculateAll}
          disabled={isLoading}
          className="w-full px-4 py-3 rounded-tempo font-medium transition-colors text-left border-2 border-ink dark:border-volt bg-canvas hover:bg-raised disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-raised"
        >
          <div className="font-semibold text-ink mb-1">Recalculate All Workouts</div>
          <div className="text-xs text-muted">
            Update {workoutCount !== null ? workoutCount : 'all'} existing workout
            {workoutCount !== 1 ? 's' : ''} with new zones. This will change historical relative
            effort values.
          </div>
        </button>

        <button
          type="button"
          onClick={handleKeepExisting}
          disabled={isLoading}
          className="w-full px-4 py-3 rounded-tempo font-medium transition-colors text-left border-2 border-border bg-canvas hover:bg-raised disabled:opacity-50 disabled:cursor-not-allowed focus:outline-none focus:ring-2 focus:ring-volt focus:ring-offset-2 focus:ring-offset-raised"
        >
          <div className="font-semibold text-ink mb-1">Keep Existing Unchanged</div>
          <div className="text-xs text-muted">
            Only future workouts will use the new zones. Historical data remains unchanged.
          </div>
        </button>
      </div>

      {isLoading && (
        <p className="text-sm text-muted mt-4 text-center">Processing...</p>
      )}
    </Dialog>
  );
}
