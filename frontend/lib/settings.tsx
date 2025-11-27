'use client';

import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { getUnitPreference, updateUnitPreference } from './api';

export type UnitPreference = 'metric' | 'imperial';

const STORAGE_KEY = 'tempo-unit-preference';
const DEFAULT_UNIT: UnitPreference = 'metric';

interface SettingsContextType {
  unitPreference: UnitPreference;
  setUnitPreference: (unit: UnitPreference) => void;
  isHydrated: boolean;
}

const SettingsContext = createContext<SettingsContextType | undefined>(undefined);

export function SettingsProvider({ children }: { children: ReactNode }) {
  const [unitPreference, setUnitPreferenceState] = useState<UnitPreference>(DEFAULT_UNIT);
  const [isHydrated, setIsHydrated] = useState(false);

  // Load preference from backend API, fallback to localStorage
  useEffect(() => {
    const loadPreference = async () => {
      try {
        // Try to load from backend API first
        try {
          const response = await getUnitPreference();
          if (response.unitPreference === 'metric' || response.unitPreference === 'imperial') {
            setUnitPreferenceState(response.unitPreference);
            // Also save to localStorage for backward compatibility
            try {
              localStorage.setItem(STORAGE_KEY, response.unitPreference);
            } catch (e) {
              // Ignore localStorage errors
            }
            return;
          }
        } catch (apiError) {
          // Backend API failed, fall back to localStorage
          console.warn('Failed to load unit preference from backend, using localStorage:', apiError);
        }

        // Fallback to localStorage
        try {
          const stored = localStorage.getItem(STORAGE_KEY);
          if (stored === 'metric' || stored === 'imperial') {
            setUnitPreferenceState(stored);
          }
        } catch (localStorageError) {
          console.error('Failed to load unit preference from localStorage:', localStorageError);
        }
      } catch (error) {
        console.error('Failed to load unit preference:', error);
      } finally {
        setIsHydrated(true);
      }
    };

    loadPreference();
  }, []);

  // Save preference to both backend API and localStorage when it changes
  const setUnitPreference = async (unit: UnitPreference) => {
    try {
      // Update state immediately for responsive UI
      setUnitPreferenceState(unit);

      // Save to localStorage for backward compatibility
      try {
        localStorage.setItem(STORAGE_KEY, unit);
      } catch (localStorageError) {
        console.warn('Failed to save unit preference to localStorage:', localStorageError);
      }

      // Save to backend API
      try {
        await updateUnitPreference(unit);
      } catch (apiError) {
        console.warn('Failed to save unit preference to backend:', apiError);
        // Don't throw - localStorage is saved, so preference is still persisted
      }
    } catch (error) {
      console.error('Failed to save unit preference:', error);
    }
  };

  return (
    <SettingsContext.Provider value={{ unitPreference, setUnitPreference, isHydrated }}>
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

