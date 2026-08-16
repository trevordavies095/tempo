'use client';

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react';
import {
  applyAppearance,
  DEFAULT_APPEARANCE,
  persistAppearance,
  readStoredAppearance,
  type AppearancePreference,
} from '@/lib/appearance';

interface AppearanceContextType {
  preference: AppearancePreference;
  setPreference: (preference: AppearancePreference) => void;
}

const AppearanceContext = createContext<AppearanceContextType | undefined>(
  undefined
);

export function AppearanceProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] =
    useState<AppearancePreference>(DEFAULT_APPEARANCE);

  useEffect(() => {
    const stored = readStoredAppearance();
    setPreferenceState(stored);
    applyAppearance(stored);

    const media = window.matchMedia('(prefers-color-scheme: dark)');
    const onSystemChange = () => {
      if (readStoredAppearance() === 'system') {
        applyAppearance('system');
      }
    };
    media.addEventListener('change', onSystemChange);
    return () => media.removeEventListener('change', onSystemChange);
  }, []);

  const setPreference = useCallback((next: AppearancePreference) => {
    persistAppearance(next);
    setPreferenceState(next);
  }, []);

  const value = useMemo(
    () => ({ preference, setPreference }),
    [preference, setPreference]
  );

  return (
    <AppearanceContext.Provider value={value}>
      {children}
    </AppearanceContext.Provider>
  );
}

export function useAppearance() {
  const context = useContext(AppearanceContext);
  if (!context) {
    throw new Error('useAppearance must be used within an AppearanceProvider');
  }
  return context;
}
