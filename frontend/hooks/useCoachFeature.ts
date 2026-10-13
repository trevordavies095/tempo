'use client';

import { useQuery } from '@tanstack/react-query';
import { getCoachSettings } from '@/lib/api';
import { useAuth } from '@/contexts/AuthContext';

export const coachSettingsQueryKey = ['coach-settings'] as const;

export function useCoachFeature() {
  const { isAuthenticated, user } = useAuth();
  const enabled = isAuthenticated && !!user?.onboardingCompleted;

  const query = useQuery({
    queryKey: coachSettingsQueryKey,
    queryFn: getCoachSettings,
    staleTime: 60_000,
    enabled,
  });

  const available = query.data?.available === true;
  const optedIn = query.data?.optedIn === true;
  const providerConnected = query.data?.providerConnected === true;
  const modelId = query.data?.modelId ?? null;
  const showCoachNav = available && optedIn;

  return {
    ...query,
    available,
    optedIn,
    providerConnected,
    modelId,
    showCoachNav,
  };
}
