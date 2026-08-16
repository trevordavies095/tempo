import { useState, useEffect } from 'react';
import { getWorkoutDisplayName, formatDateTime } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import { useWorkoutMutations } from '@/hooks/useWorkoutMutations';
import { CropWorkoutDialog } from './CropWorkoutDialog';
import { Button } from '@/components/ui/Button';
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
              className="flex-1 px-3 py-2 text-3xl font-bold border border-border rounded-tempo bg-raised text-ink placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-volt disabled:opacity-50 disabled:cursor-not-allowed"
              autoFocus
              onKeyDown={(e) => {
                if (e.key === 'Enter') {
                  handleSaveName();
                } else if (e.key === 'Escape') {
                  handleCancelName();
                }
              }}
            />
            <Button
              onClick={handleSaveName}
              disabled={updateWorkoutMutation.isPending}
            >
              Save
            </Button>
            <Button
              variant="secondary"
              onClick={handleCancelName}
              disabled={updateWorkoutMutation.isPending}
            >
              Cancel
            </Button>
            {updateWorkoutMutation.isPending && (
              <span className="text-sm text-muted">Saving...</span>
            )}
            {updateWorkoutMutation.isError && (
              <span className="text-sm text-danger">
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
            className="flex items-center gap-2 group hover:text-ink dark:hover:text-volt transition-colors text-left"
            type="button"
          >
            <h2 className="text-3xl font-bold text-ink group-hover:text-ink dark:group-hover:text-volt">
              {getWorkoutDisplayName(workout.name, workout.startedAt)}
            </h2>
            <IconPencil className="w-5 h-5 opacity-0 group-hover:opacity-50 flex-shrink-0" />
          </button>
        )}
        <div className="relative">
          <button
            onClick={() => setIsMenuOpen(!isMenuOpen)}
            className="p-1.5 text-muted hover:text-ink hover:bg-canvas rounded-tempo transition-colors"
            type="button"
            aria-label="More options"
            aria-expanded={isMenuOpen}
          >
            <IconDotsVertical className="w-5 h-5" />
          </button>
          {isMenuOpen && (
            <div className="absolute right-0 mt-1 w-48 bg-raised rounded-tempo shadow-lg border border-border z-10">
              <div className="py-1">
                {workout.route && (
                  <button
                    onClick={() => {
                      setIsMenuOpen(false);
                      setIsCropDialogOpen(true);
                    }}
                    disabled={cropWorkoutMutation.isPending}
                    className="w-full text-left px-4 py-2 text-sm text-ink hover:bg-canvas disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
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
                  className="w-full text-left px-4 py-2 text-sm text-danger hover:bg-canvas disabled:opacity-50 disabled:cursor-not-allowed transition-colors flex items-center gap-2"
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
          <span className="text-xs text-danger">
            {deleteWorkoutMutation.error instanceof Error ? deleteWorkoutMutation.error.message : 'Failed to delete workout'}
          </span>
        </div>
      )}
      {cropWorkoutMutation.isError && (
        <div className="mb-2">
          <span className="text-xs text-danger">
            {cropWorkoutMutation.error instanceof Error ? cropWorkoutMutation.error.message : 'Failed to crop workout'}
          </span>
        </div>
      )}
      <p className="text-base text-muted">
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

