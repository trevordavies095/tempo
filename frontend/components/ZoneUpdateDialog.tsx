'use client';

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
  if (!open) return null;

  const handleBackdropClick = (e: React.MouseEvent) => {
    if (e.target === e.currentTarget) {
      onClose();
    }
  };

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
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black bg-opacity-50 p-4"
      onClick={handleBackdropClick}
    >
      <div className="bg-white dark:bg-gray-900 rounded-lg shadow-xl max-w-md w-full p-6 border border-gray-200 dark:border-gray-800">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Heart Rate Zones Updated
        </h2>
        
        <div className="mb-6">
          <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
            Your heart rate zones have been updated. How would you like to handle existing workouts?
          </p>
          
          <div className="space-y-3">
            <button
              onClick={handleRecalculateAll}
              disabled={isLoading}
              className="w-full px-4 py-3 rounded-lg font-medium transition-colors text-left border-2 border-blue-600 dark:border-blue-500 bg-blue-50 dark:bg-blue-900/20 hover:bg-blue-100 dark:hover:bg-blue-900/30 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <div className="font-semibold text-blue-900 dark:text-blue-200 mb-1">
                Recalculate All Workouts
              </div>
              <div className="text-xs text-blue-700 dark:text-blue-300">
                Update {workoutCount !== null ? workoutCount : 'all'} existing workout{workoutCount !== 1 ? 's' : ''} with new zones. This will change historical relative effort values.
              </div>
            </button>
            
            <button
              onClick={handleKeepExisting}
              disabled={isLoading}
              className="w-full px-4 py-3 rounded-lg font-medium transition-colors text-left border-2 border-gray-300 dark:border-gray-600 bg-gray-50 dark:bg-gray-800 hover:bg-gray-100 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <div className="font-semibold text-gray-900 dark:text-gray-100 mb-1">
                Keep Existing Unchanged
              </div>
              <div className="text-xs text-gray-600 dark:text-gray-400">
                Only future workouts will use the new zones. Historical data remains unchanged.
              </div>
            </button>
          </div>
          
          {isLoading && (
            <p className="text-sm text-gray-500 dark:text-gray-400 mt-4 text-center">
              Processing...
            </p>
          )}
        </div>

        <div className="flex gap-3 justify-end">
          <button
            onClick={onClose}
            disabled={isLoading}
            className="px-4 py-2 rounded-lg font-medium transition-colors bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

