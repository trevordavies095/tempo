import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useRouter } from 'next/navigation';
import { updateWorkout, deleteWorkout } from '@/lib/api';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';

export function useWorkoutMutations(workoutId: string) {
  const router = useRouter();
  const queryClient = useQueryClient();

  const updateWorkoutMutation = useMutation({
    mutationFn: (updates: { runType?: string | null; notes?: string | null }) =>
      updateWorkout(workoutId, updates),
    onSuccess: () => {
      invalidateWorkoutQueries(queryClient, workoutId);
    },
  });

  const deleteWorkoutMutation = useMutation({
    mutationFn: () => deleteWorkout(workoutId),
    onSuccess: () => {
      invalidateWorkoutQueries(queryClient, workoutId);
      router.push('/dashboard');
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
    handleDeleteWorkout,
  };
}

