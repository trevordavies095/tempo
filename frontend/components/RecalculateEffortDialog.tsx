'use client';

import { Button } from '@/components/ui/Button';
import { Dialog } from '@/components/ui/Dialog';

interface RecalculateEffortDialogProps {
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  workoutCount: number | null;
  isLoading?: boolean;
}

export function RecalculateEffortDialog({
  open,
  onClose,
  onConfirm,
  workoutCount,
  isLoading = false,
}: RecalculateEffortDialogProps) {
  const handleConfirm = () => {
    if (!isLoading) {
      onConfirm();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Recalculate Relative Effort"
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
        This will update{' '}
        <strong className="text-ink">
          {workoutCount !== null ? workoutCount : 'all qualifying'} workout
          {workoutCount !== 1 ? 's' : ''}
        </strong>{' '}
        with new relative effort values based on your current heart rate zone configuration.
      </p>
      <p className="text-sm text-danger font-medium">
        This action cannot be undone. All qualifying workouts will be updated.
      </p>
    </Dialog>
  );
}
