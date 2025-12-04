import { QueryClient } from '@tanstack/react-query';

/**
 * Invalidates all workout-related queries after a mutation.
 * This includes workout lists, individual workouts, all stats queries, shoe queries, and best efforts.
 * 
 * @param queryClient - The React Query client instance
 * @param workoutId - Optional specific workout ID to invalidate
 */
export function invalidateWorkoutQueries(queryClient: QueryClient, workoutId?: string): void {
  // Invalidate all workout list queries (dashboard, activities page, home page)
  queryClient.invalidateQueries({ queryKey: ['workouts'] });
  
  // Invalidate workout queries
  if (workoutId) {
    // Invalidate specific workout if ID provided
    queryClient.invalidateQueries({ queryKey: ['workout', workoutId] });
  } else {
    // Invalidate all individual workout queries (for bulk operations like split recalculation)
    queryClient.invalidateQueries({ queryKey: ['workout'] });
  }
  
  // Invalidate stats queries
  queryClient.invalidateQueries({ queryKey: ['weeklyStats'] });
  queryClient.invalidateQueries({ queryKey: ['yearlyStats'] });
  queryClient.invalidateQueries({ queryKey: ['yearlyWeeklyStats'] });
  queryClient.invalidateQueries({ queryKey: ['availablePeriods'] });
  queryClient.invalidateQueries({ queryKey: ['relativeEffortStats'] });
  
  // Invalidate shoes query (mileage is calculated from workouts)
  queryClient.invalidateQueries({ queryKey: ['shoes'] });
  
  // Invalidate best efforts query (calculated from workout segments)
  queryClient.invalidateQueries({ queryKey: ['bestEfforts'] });
}

