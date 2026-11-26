import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { updateWorkout, deleteWorkout, cropWorkout } from '@/lib/api';
import { invalidateAfter } from '@/lib/queries';

export function useWorkoutMutations(workoutId: string) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const updateWorkoutMutation = useMutation({
    mutationFn: (updates: {
      runType?: string | null;
      notes?: string | null;
      name?: string | null;
      shoeId?: string | null;
      rpe?: number | null;
    }) => updateWorkout(workoutId, updates),
    onSuccess: () => {
      invalidateAfter(queryClient, { kind: 'workoutEdited', workoutId });
    },
  });

  const deleteWorkoutMutation = useMutation({
    mutationFn: () => deleteWorkout(workoutId),
    onSuccess: () => {
      // Leave first so the deleted Workout's own reads aren't refetched
      router.push('/dashboard');
      invalidateAfter(queryClient, { kind: 'workoutDeleted', workoutId });
    },
  });

  const cropWorkoutMutation = useMutation({
    mutationFn: ({ startTrimSeconds, endTrimSeconds }: { startTrimSeconds: number; endTrimSeconds: number }) =>
      cropWorkout(workoutId, startTrimSeconds, endTrimSeconds),
    onSuccess: () => {
      invalidateAfter(queryClient, { kind: 'workoutCropped', workoutId });
    },
  });

  const handleDeleteWorkout = () => {
    if (window.confirm('Are you sure you want to delete this workout? This action cannot be undone.')) {
      deleteWorkoutMutation.mutate();
    }
  };

  return {
    updateWorkoutMutation,
    deleteWorkoutMutation,
    cropWorkoutMutation,
    handleDeleteWorkout,
  };
}

