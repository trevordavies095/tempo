'use client';

import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';

interface RecalculateSplitsDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  workoutCount: number | null;
  isLoading?: boolean;
}

export function RecalculateSplitsDialog({
  open,
  onClose,
  onConfirm,
  workoutCount,
  isLoading = false,
}: RecalculateSplitsDialogProps) {
  const handleConfirm = () => {
    if (!isLoading) {
      onConfirm();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Recalculate Splits"
      footer={
        <>
          <Button variant="secondary" size="sm" onClick={onClose} disabled={isLoading}>
            Cancel
          </Button>
          <Button variant="danger" size="sm" onClick={handleConfirm} disabled={isLoading}>
            {isLoading ? 'Recalculating...' : 'Confirm'}
          </Button>
        </>
      }
    >
      <p className="text-sm text-muted mb-3">
        This will recalculate splits for{' '}
        <strong className="text-ink">
          {workoutCount !== null ? workoutCount : 'all'} workout
          {workoutCount !== 1 ? 's' : ''}
        </strong>{' '}
        based on your current unit preference (1 km splits for metric, 1 mile splits for imperial).
      </p>
      <p className="text-sm text-danger font-medium">
        This action cannot be undone. All workouts with route data will be updated.
      </p>
    </Dialog>
  );
}
