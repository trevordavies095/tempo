import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { updateThresholds, type ThresholdSettings } from '@/lib/api';
import { settingsQueries } from '@/lib/queries';

/**
 * Loads the athlete's threshold pace and heart rate, and saves replacements
 */
export function useThresholds() {
  const queryClient = useQueryClient();

  const query = useQuery(settingsQueries.thresholds());

  const mutation = useMutation({
    mutationFn: (thresholds: ThresholdSettings) => updateThresholds(thresholds),
    onSuccess: (saved) => {
      queryClient.setQueryData(settingsQueries.thresholds().queryKey, saved);
    },
  });

  return { query, mutation };
}
