import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { getThresholds, updateThresholds, type ThresholdSettings } from '@/lib/api';

const THRESHOLDS_KEY = ['thresholds'];

/**
 * Loads the athlete's threshold pace and heart rate, and saves replacements
 */
export function useThresholds() {
  const queryClient = useQueryClient();

  const query = useQuery({
    queryKey: THRESHOLDS_KEY,
    queryFn: getThresholds,
  });

  const mutation = useMutation({
    mutationFn: (thresholds: ThresholdSettings) => updateThresholds(thresholds),
    onSuccess: (saved) => {
      queryClient.setQueryData(THRESHOLDS_KEY, saved);
    },
  });

  return { query, mutation };
}
