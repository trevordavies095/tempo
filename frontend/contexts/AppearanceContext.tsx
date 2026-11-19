'use client';

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useSyncExternalStore,
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

// The preference lives in localStorage, so it is read through an external store: the server
// render and hydration use the default, then the client reads storage once and caches it.
let preferenceSnapshot: AppearancePreference | null = null;
const preferenceListeners = new Set<() => void>();

function subscribePreference(listener: () => void) {
  preferenceListeners.add(listener);
  return () => {
    preferenceListeners.delete(listener);
  };
}

function getPreferenceSnapshot(): AppearancePreference {
  if (preferenceSnapshot === null) {
    preferenceSnapshot = readStoredAppearance();
  }
  return preferenceSnapshot;
}

function getServerPreferenceSnapshot(): AppearancePreference {
  return DEFAULT_APPEARANCE;
}

export function AppearanceProvider({ children }: { children: ReactNode }) {
  const preference = useSyncExternalStore(
    subscribePreference,
    getPreferenceSnapshot,
    getServerPreferenceSnapshot
  );

  useEffect(() => {
    applyAppearance(readStoredAppearance());

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
    preferenceSnapshot = next;
    preferenceListeners.forEach((listener) => listener());
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
