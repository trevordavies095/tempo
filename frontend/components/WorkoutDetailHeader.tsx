import { useState, useEffect } from 'react';
import Link from 'next/link';
import { getWorkoutDisplayName, formatDateTime } from '@/lib/format';
import type { WorkoutDetail } from '@/lib/api';
import { useWorkoutMutations } from '@/hooks/useWorkoutMutations';

interface WorkoutDetailHeaderProps {
  workout: WorkoutDetail;
}

export default function WorkoutDetailHeader({ workout }: WorkoutDetailHeaderProps) {
  const [isMenuOpen, setIsMenuOpen] = useState(false);
  const { deleteWorkoutMutation, handleDeleteWorkout } = useWorkoutMutations(workout.id);

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

  return (
    <div className="w-full mb-4">
      <div className="flex items-center gap-2 mb-1" data-menu-container>
        <h1 className="text-3xl font-bold text-gray-900 dark:text-gray-100">
          {getWorkoutDisplayName(workout.name, workout.startedAt)}
        </h1>
        <div className="relative">
          <button
            onClick={() => setIsMenuOpen(!isMenuOpen)}
            className="p-1.5 text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200 hover:bg-gray-100 dark:hover:bg-gray-800 rounded-md transition-colors"
            type="button"
            aria-label="More options"
            aria-expanded={isMenuOpen}
          >
            <svg
              className="w-5 h-5"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M12 5v.01M12 12v.01M12 19v.01M12 6a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2zm0 7a1 1 0 110-2 1 1 0 010 2z"
              />
            </svg>
          </button>
          {isMenuOpen && (
            <div className="absolute right-0 mt-1 w-48 bg-white dark:bg-gray-800 rounded-md shadow-lg border border-gray-200 dark:border-gray-700 z-10">
              <div className="py-1">
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
                      <svg className="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4"></circle>
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
                      </svg>
                      Deleting...
                    </>
                  ) : (
                    <>
                      <svg
                        className="w-4 h-4"
                        fill="none"
                        stroke="currentColor"
                        viewBox="0 0 24 24"
                        xmlns="http://www.w3.org/2000/svg"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16"
                        />
                      </svg>
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
      <p className="text-base text-gray-600 dark:text-gray-400">
        {formatDateTime(workout.startedAt)}
      </p>
    </div>
  );
}

