import { useState, useEffect } from 'react';
import Link from 'next/link';
import { getWorkoutDisplayName, formatDateTime } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import { useWorkoutMutations } from '@/hooks/useWorkoutMutations';
import { CropWorkoutDialog } from './CropWorkoutDialog';
import { IconPencil, IconDotsVertical, IconCrop, IconTrash, IconLoader2 } from '@tabler/icons-react';

interface WorkoutDetailHeaderProps {
  workout: WorkoutDetail;
}

export default function WorkoutDetailHeader({ workout }: WorkoutDetailHeaderProps) {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const [isEditingName, setIsEditingName] = useState(false);
  const [nameValue, setNameValue] = useState<string>('');
  const [isCropDialogOpen, setIsCropDialogOpen] = useState(false);
  const { deleteWorkoutMutation, handleDeleteWorkout, updateWorkoutMutation, cropWorkoutMutation } = useWorkoutMutations(workout.id);

  // Close menu when clicking outside
  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      const target = event.target as HTMLElement;
      if (isMenuOpen && !target.closest('[data-menu-container]')) {
        setIsMenuOpen(false);
      }
    };

    if (isMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => {
        document.removeEventListener('mousedown', handleClickOutside);
      };
    }
  }, [isMenuOpen]);

  // Sync nameValue with workout.name when entering edit mode or data changes
  useEffect(() => {
    if (workout && isEditingName) {
      setNameValue(workout.name || '');
    }
  }, [workout, isEditingName]);

  const handleSaveName = () => {
    const trimmedName = nameValue.trim() || null;
    updateWorkoutMutation.mutate(
      { name: trimmedName },
      {
        onSuccess: () => {
          setIsEditingName(false);
        },
      }
    );
  };

  const handleCancelName = () => {
    setIsEditingName(false);
    setNameValue(workout.name || '');
  };

  const handleCropWorkout = (startTrimSeconds: number, endTrimSeconds: number) => {
    cropWorkoutMutation.mutate(
      { startTrimSeconds, endTrimSeconds },
      {
        onSuccess: () => {
          setIsCropDialogOpen(false);
        },
      }
    );
  };

  return (
    <div className="w-full mb-4">
      <div className="flex items-center gap-2 mb-1" data-menu-container>
        {isEditingName ? (
          <div className="flex-1 flex items-center gap-2">
            <input
              type="text"
              value={nameValue}
              onChange={(e) => setNameValue(e.target.value)}
              disabled={updateWorkoutMutation.isPending}
              placeholder="Enter activity name..."
              maxLength={200}
              className="flex-1 px-3 py-2 text-3xl font-bold border border-gray-300 dark:border-gray-600 rounded-md bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:opacity-50 disabled:cursor-not-allowed"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  handleSaveName();
                } else if (e.key === 'Escape') {
                  handleCancelName();
                }
              }}
            />
            <button
              onClick={handleSaveName}
              disabled={updateWorkoutMutation.isPending}
              className="px-3 py-2 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              type="button"
            >
              Save
            </button>
            <button
              onClick={handleCancelName}
              disabled={updateWorkoutMutation.isPending}
              className="px-3 py-2 text-sm font-medium text-gray-700 dark:text-gray-300 bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 rounded-md disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              type="button"
            >
              Cancel
            </button>
            {updateWorkoutMutation.isPending && (
              <span className="text-sm text-gray-500 dark:text-gray-400">Saving...</span>
            )}
            {updateWorkoutMutation.isError && (
              <span className="text-sm text-red-600 dark:text-red-400">
                Error: {updateWorkoutMutation.error instanceof Error ? updateWorkoutMutation.error.message : 'Failed to update'}
              </span>
            )}
          </div>
        ) : (
          <button
            onClick={() => {
              setNameValue(workout.name || '');
              setIsEditingName(true);
            }}
            className="flex items-center gap-2 group hover:text-blue-600 dark:hover:text-blue-400 transition-colors text-left"
            type="button"
          >
            <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100 group-hover:text-blue-600 dark:group-hover:text-blue-400">
              {getWorkoutDisplayName(workout.name, workout.startedAt)}
            </h1>
            <IconPencil className="w-5 h-5 opacity-0 group-hover:opacity-50 flex-shrink-0" />
          </button>
        )}
        <div className="relative">
          <button
            onClick={() => setIsMenuOpen(!isMenuOpen)}
            className="p-1.5 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-md transition-colors"
            type="button"
            aria-label="More options"
            aria-expanded={isMenuOpen}
          >
            <IconDotsVertical className="w-5 h-5" />
          </button>
          {isMenuOpen && (
            <div className="absolute right-0 mt-1 w-48 bg-white dark:bg-gray-800 rounded-md shadow-lg border border-gray-200 dark:border-gray-700 z-10">
              <div className="py-1">
                {workout.route && (
                  <button
                    onClick={() => {
                      setIsMenuOpen(false);
                      setIsCropDialogOpen(true);
                    }}
                    disabled={cropWorkoutMutation.isPending}
                    className="w-full text-left px-4 py-2 text-sm text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
                    type="button"
                  >
                    <IconCrop className="w-4 h-4" />
                    Crop Workout
                  </button>
                )}
                <button
                  onClick={() => {
                    setIsMenuOpen(false);
                    handleDeleteWorkout();
                  }}
                  disabled={deleteWorkoutMutation.isPending}
                  className="w-full text-left px-4 py-2 text-sm text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
                  type="button"
                >
                  {deleteWorkoutMutation.isPending ? (
                    <>
                      <IconLoader2 className="animate-spin h-4 w-4" />
                      Deleting...
                    </>
                  ) : (
                    <>
                      <IconTrash className="w-4 h-4" />
                      Delete Workout
                    </>
                  )}
                </button>
              </div>
            </div>
          )}
        </div>
      </div>
      {deleteWorkoutMutation.isError && (
        <div className="mb-2">
          <span className="text-xs text-red-600 dark:text-red-400">
            {deleteWorkoutMutation.error instanceof Error ? deleteWorkoutMutation.error.message : 'Failed to delete workout'}
          </span>
        </div>
      )}
      {cropWorkoutMutation.isError && (
        <div className="mb-2">
          <span className="text-xs text-red-600 dark:text-red-400">
            {cropWorkoutMutation.error instanceof Error ? cropWorkoutMutation.error.message : 'Failed to crop workout'}
          </span>
        </div>
      )}
      <p className="text-base text-gray-600 dark:text-gray-400">
        {formatDateTime(workout.startedAt)}
      </p>
      <CropWorkoutDialog
        open={isCropDialogOpen}
        onClose={() => setIsCropDialogOpen(false)}
        onConfirm={handleCropWorkout}
        workout={workout}
        isLoading={cropWorkoutMutation.isPending}
      />
    </div>
  );
}

