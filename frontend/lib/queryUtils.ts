import { QueryClient } from '@tanstack/react-query';

/**
 * Invalidates all workout-related queries after a mutation.
 * This includes workout lists, individual workouts, and all stats queries.
 * 
 * @param queryClient - The React Query client instance
 * @param workoutId - Optional specific workout ID to invalidate
 */
export function invalidateWorkoutQueries(queryClient: QueryClient, workoutId?: string): void {
  // Invalidate all workout list queries (dashboard, activities page, home page)
  queryClient.invalidateQueries({ queryKey: ['workouts'] });
  
  // Invalidate specific workout if ID provided
  if (workoutId) {
    queryClient.invalidateQueries({ queryKey: ['workout', workoutId] });
  }
  
  // Invalidate stats queries
  queryClient.invalidateQueries({ queryKey: ['weeklyStats'] });
  queryClient.invalidateQueries({ queryKey: ['yearlyStats'] });
  queryClient.invalidateQueries({ queryKey: ['yearlyWeeklyStats'] });
  queryClient.invalidateQueries({ queryKey: ['availablePeriods'] });
}

