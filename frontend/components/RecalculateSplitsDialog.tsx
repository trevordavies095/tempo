'use client';

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
  if (!open) return null;

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

  const handleConfirm = () => {
    if (!isLoading) {
      onConfirm();
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4"
      onClick={handleBackdropClick}
    >
      <div className="bg-white dark:bg-gray-900 rounded-lg shadow-xl max-w-md w-full p-6 border border-gray-200 dark:border-gray-800">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Recalculate Splits
        </h2>
        
        <div className="mb-6">
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-3">
            This will recalculate splits for <strong className="text-gray-900 dark:text-gray-100">
              {workoutCount !== null ? workoutCount : 'all'} workout{workoutCount !== 1 ? 's' : ''}
            </strong> based on your current unit preference (1 km splits for metric, 1 mile splits for imperial).
          </p>
          <p className="text-sm text-red-600 dark:text-red-400 font-medium">
            This action cannot be undone. All workouts with route data will be updated.
          </p>
        </div>

        <div className="flex gap-3 justify-end">
          <button
            onClick={onClose}
            disabled={isLoading}
            className="px-4 py-2 rounded-lg font-medium transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            disabled={isLoading}
            className="px-4 py-2 rounded-lg font-medium transition-colors bg-orange-600 text-white hover:bg-orange-700 dark:bg-orange-500 dark:hover:bg-orange-600 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isLoading ? 'Recalculating...' : 'Confirm'}
          </button>
        </div>
      </div>
    </div>
  );
}

