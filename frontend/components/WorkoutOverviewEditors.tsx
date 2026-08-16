'use client';

import { useState } from 'react';
import type { WorkoutDetail } from '@/lib/api';
import { useWorkoutMutations } from '@/hooks/useWorkoutMutations';
import { ShoeSelector } from '@/components/ShoeSelector';
import { Button } from '@/components/ui/Button';

function PencilIcon({ className }: { className?: string }) {
  return (
    <svg
      className={className}
      fill="none"
      stroke="currentColor"
      viewBox="0 0 24 24"
      xmlns="http://www.w3.org/2000/svg"
    >
      <path
        strokeLinecap="round"
        strokeLinejoin="round"
        strokeWidth={2}
        d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z"
      />
    </svg>
  );
}

const fieldClass =
  'px-2 py-1.5 text-sm border border-border rounded-tempo bg-raised text-ink focus:outline-none focus:ring-2 focus:ring-volt disabled:opacity-50 disabled:cursor-not-allowed';

export function WorkoutOverviewEditors({ workout }: { workout: WorkoutDetail }) {
  const [isEditingRunType, setIsEditingRunType] = useState(false);
  const [isEditingRpe, setIsEditingRpe] = useState(false);
  const [isEditingNotes, setIsEditingNotes] = useState(false);
  const [isEditingShoe, setIsEditingShoe] = useState(false);
  const [notesValue, setNotesValue] = useState<string>('');
  const { updateWorkoutMutation } = useWorkoutMutations(workout.id);

  const mutationError =
    updateWorkoutMutation.error instanceof Error
      ? updateWorkoutMutation.error.message
      : 'Failed to update';

  const handleSaveNotes = () => {
    const trimmedNotes = notesValue.trim() || null;
    updateWorkoutMutation.mutate(
      { notes: trimmedNotes },
      {
        onSuccess: () => {
          setIsEditingNotes(false);
        },
      }
    );
  };

  const handleCancelNotes = () => {
    setIsEditingNotes(false);
    setNotesValue(workout.notes || '');
  };

  return (
    <div className="space-y-2.5">
      <div>
        <dt className="text-xs font-medium text-muted mb-1">Description</dt>
        <dd>
          {isEditingNotes ? (
            <div className="space-y-2">
              <textarea
                value={notesValue}
                onChange={(e) => setNotesValue(e.target.value)}
                disabled={updateWorkoutMutation.isPending}
                placeholder="Add a description..."
                rows={3}
                className={`${fieldClass} w-full placeholder:text-muted resize-y`}
                autoFocus
              />
              <div className="flex items-center gap-2">
                <Button
                  size="sm"
                  onClick={handleSaveNotes}
                  disabled={updateWorkoutMutation.isPending}
                >
                  Save
                </Button>
                <Button
                  size="sm"
                  variant="secondary"
                  onClick={handleCancelNotes}
                  disabled={updateWorkoutMutation.isPending}
                >
                  Cancel
                </Button>
                {updateWorkoutMutation.isPending && (
                  <span className="text-xs text-muted">Saving...</span>
                )}
                {updateWorkoutMutation.isError && (
                  <span className="text-xs text-danger">Error: {mutationError}</span>
                )}
              </div>
            </div>
          ) : (
            <button
              onClick={() => {
                setNotesValue(workout.notes || '');
                setIsEditingNotes(true);
              }}
              className="flex items-start gap-2 w-full text-left hover:text-ink dark:hover:text-volt transition-colors group"
              type="button"
            >
              <div className="flex-1 min-w-0">
                {workout.notes ? (
                  <p className="text-sm text-ink whitespace-pre-wrap">{workout.notes}</p>
                ) : (
                  <p className="text-sm text-muted italic">Add a description...</p>
                )}
              </div>
              <PencilIcon className="w-4 h-4 opacity-0 group-hover:opacity-50 mt-0.5 flex-shrink-0" />
            </button>
          )}
        </dd>
      </div>

      <div>
        <dt className="text-xs font-medium text-muted mb-1">Run Type</dt>
        <dd className="text-sm text-ink">
          {isEditingRunType ? (
            <div className="flex items-center gap-2">
              <select
                value={workout.runType || ''}
                onChange={(e) => {
                  const newValue = e.target.value === '' ? null : e.target.value;
                  updateWorkoutMutation.mutate(
                    { runType: newValue },
                    {
                      onSuccess: () => {
                        setIsEditingRunType(false);
                      },
                    }
                  );
                }}
                disabled={updateWorkoutMutation.isPending}
                className={fieldClass}
                autoFocus
              >
                <option value="">None</option>
                <option value="Easy Run">Easy Run</option>
                <option value="Race">Race</option>
                <option value="Workout">Workout</option>
                <option value="Long Run">Long Run</option>
              </select>
              <button
                onClick={() => setIsEditingRunType(false)}
                className="text-muted hover:text-ink"
                type="button"
                disabled={updateWorkoutMutation.isPending}
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </button>
            </div>
          ) : (
            <button
              onClick={() => setIsEditingRunType(true)}
              className="flex items-center gap-1 hover:text-ink dark:hover:text-volt transition-colors"
              type="button"
            >
              <span>{workout.runType || 'None'}</span>
              <PencilIcon className="w-4 h-4 opacity-50" />
            </button>
          )}
          {updateWorkoutMutation.isPending && (
            <span className="ml-2 text-xs text-muted">Saving...</span>
          )}
          {updateWorkoutMutation.isError && (
            <span className="ml-2 text-xs text-danger">Error: {mutationError}</span>
          )}
        </dd>
      </div>

      <div>
        <dt className="text-xs font-medium text-muted mb-1">RPE (1–10)</dt>
        <dd className="text-sm text-ink">
          {isEditingRpe ? (
            <div className="flex items-center gap-2">
              <select
                value={workout.rpe != null ? String(workout.rpe) : ''}
                onChange={(e) => {
                  const newValue = e.target.value === '' ? null : Number(e.target.value);
                  updateWorkoutMutation.mutate(
                    { rpe: newValue },
                    {
                      onSuccess: () => {
                        setIsEditingRpe(false);
                      },
                    }
                  );
                }}
                disabled={updateWorkoutMutation.isPending}
                className={fieldClass}
                autoFocus
              >
                <option value="">None</option>
                {[1, 2, 3, 4, 5, 6, 7, 8, 9, 10].map((n) => (
                  <option key={n} value={n}>
                    {n}
                  </option>
                ))}
              </select>
              <button
                onClick={() => setIsEditingRpe(false)}
                className="text-muted hover:text-ink"
                type="button"
                disabled={updateWorkoutMutation.isPending}
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </button>
            </div>
          ) : (
            <button
              onClick={() => setIsEditingRpe(true)}
              className="flex items-center gap-1 hover:text-ink dark:hover:text-volt transition-colors"
              type="button"
            >
              <span>{workout.rpe != null ? String(workout.rpe) : 'None'}</span>
              <PencilIcon className="w-4 h-4 opacity-50" />
            </button>
          )}
        </dd>
      </div>

      <div>
        <dt className="text-xs font-medium text-muted mb-1">Shoe</dt>
        <dd className="text-sm text-ink">
          {isEditingShoe ? (
            <div className="flex items-center gap-2">
              <ShoeSelector
                value={workout.shoeId}
                assignedShoe={workout.shoe}
                onChange={(shoeId) => {
                  updateWorkoutMutation.mutate(
                    { shoeId },
                    {
                      onSuccess: () => {
                        setIsEditingShoe(false);
                      },
                    }
                  );
                }}
                className={fieldClass}
              />
              <button
                onClick={() => setIsEditingShoe(false)}
                className="text-muted hover:text-ink"
                type="button"
                disabled={updateWorkoutMutation.isPending}
              >
                <svg className="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={2}
                    d="M6 18L18 6M6 6l12 12"
                  />
                </svg>
              </button>
            </div>
          ) : (
            <button
              onClick={() => setIsEditingShoe(true)}
              className="flex items-center gap-1 hover:text-ink dark:hover:text-volt transition-colors"
              type="button"
            >
              <span>{workout.shoe ? `${workout.shoe.brand} ${workout.shoe.model}` : 'None'}</span>
              <PencilIcon className="w-4 h-4 opacity-50" />
            </button>
          )}
          {updateWorkoutMutation.isPending && (
            <span className="ml-2 text-xs text-muted">Saving...</span>
          )}
          {updateWorkoutMutation.isError && (
            <span className="ml-2 text-xs text-danger">Error: {mutationError}</span>
          )}
        </dd>
      </div>

      {workout.device && (
        <div>
          <dt className="text-xs font-medium text-muted mb-1">Device</dt>
          <dd className="text-sm text-ink">{workout.device}</dd>
        </div>
      )}

      {workout.source && (
        <div>
          <dt className="text-xs font-medium text-muted mb-1">Source</dt>
          <dd className="text-sm text-ink">{workout.source}</dd>
        </div>
      )}
    </div>
  );
}
