'use client';

import { createContext, useContext, useState, useEffect, ReactNode } from 'react';

export type UnitPreference = 'metric' | 'imperial';

const STORAGE_KEY = 'tempo-unit-preference';
const DEFAULT_UNIT: UnitPreference = 'metric';

interface SettingsContextType {
  unitPreference: UnitPreference;
  setUnitPreference: (unit: UnitPreference) => void;
}

const SettingsContext = createContext<SettingsContextType | undefined>(undefined);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [unitPreference, setUnitPreferenceState] = useState<UnitPreference>(DEFAULT_UNIT);
  const [isHydrated, setIsHydrated] = useState(false);

  // Load preference from localStorage on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored === 'metric' || stored === 'imperial') {
        setUnitPreferenceState(stored);
      }
    } catch (error) {
      console.error('Failed to load unit preference from localStorage:', error);
    } finally {
      setIsHydrated(true);
    }
  }, []);

  // Save preference to localStorage when it changes
  const setUnitPreference = (unit: UnitPreference) => {
    try {
      localStorage.setItem(STORAGE_KEY, unit);
      setUnitPreferenceState(unit);
    } catch (error) {
      console.error('Failed to save unit preference to localStorage:', error);
    }
  };

  return (
    <SettingsContext.Provider value={{ unitPreference, setUnitPreference }}>
      {children}
    </SettingsContext.Provider>
  );
}

export function useSettings() {
  const context = useContext(SettingsContext);
  if (context === undefined) {
    throw new Error('useSettings must be used within a SettingsProvider');
  }
  return context;
}

