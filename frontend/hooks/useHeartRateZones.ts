import { useState, useEffect } from 'react';
import type { HeartRateZoneSettings, HeartRateCalculationMethod } from '@/lib/api';

const ZONE_PERCENTAGES = [
  { min: 0.50, max: 0.60 }, // Zone 1: 50-60%
  { min: 0.60, max: 0.70 }, // Zone 2: 60-70%
  { min: 0.70, max: 0.80 }, // Zone 3: 70-80%
  { min: 0.80, max: 0.90 }, // Zone 4: 80-90%
  { min: 0.90, max: 1.00 }, // Zone 5: 90-100%
];

export interface ZoneRange {
  min: number;
  max: number;
}

/**
 * Calculates heart rate zones based on the selected calculation method
 */
export function calculateHeartRateZones(
  method: HeartRateCalculationMethod,
  age: number,
  restingHr: number,
  maxHr: number
): ZoneRange[] {
  if (method === 'Custom') {
    return [];
  }

  let calculatedZones: ZoneRange[] = [];

  if (method === 'AgeBased') {
    const maxHeartRate = 220 - age;
    calculatedZones = ZONE_PERCENTAGES.map((p) => ({
      min: Math.round(maxHeartRate * p.min),
      max: Math.round(maxHeartRate * p.max),
    }));
  } else if (method === 'Karvonen') {
    const heartRateReserve = maxHr - restingHr;
    calculatedZones = ZONE_PERCENTAGES.map((p) => ({
      min: Math.round(heartRateReserve * p.min + restingHr),
      max: Math.round(heartRateReserve * p.max + restingHr),
    }));
  }

  return calculatedZones;
}

/**
 * Hook for managing heart rate zone state and calculations
 */
export function useHeartRateZones(initialSettings: HeartRateZoneSettings | null) {
  const [calculationMethod, setCalculationMethod] = useState<HeartRateCalculationMethod>('AgeBased');
  const [age, setAge] = useState<number>(30);
  const [restingHr, setRestingHr] = useState<number>(60);
  const [maxHr, setMaxHr] = useState<number>(190);
  const [customZones, setCustomZones] = useState<ZoneRange[]>([
    { min: 95, max: 114 },
    { min: 114, max: 133 },
    { min: 133, max: 152 },
    { min: 152, max: 171 },
    { min: 171, max: 190 },
  ]);

  // Initialize from settings
  useEffect(() => {
    if (initialSettings) {
      setCalculationMethod(initialSettings.calculationMethod);
      if (initialSettings.age !== null) setAge(initialSettings.age);
      if (initialSettings.restingHeartRateBpm !== null) setRestingHr(initialSettings.restingHeartRateBpm);
      if (initialSettings.maxHeartRateBpm !== null) setMaxHr(initialSettings.maxHeartRateBpm);
      if (initialSettings.zones && initialSettings.zones.length === 5) {
        setCustomZones(initialSettings.zones.map((z) => ({ min: z.minBpm, max: z.maxBpm })));
      }
    }
  }, [initialSettings]);

  // Recalculate zones when method or inputs change (for preview only)
  const calculatedZones = calculateHeartRateZones(calculationMethod, age, restingHr, maxHr);
  const displayZones = calculationMethod === 'Custom' ? customZones : calculatedZones;

  const updateCustomZone = (index: number, field: 'min' | 'max', value: number) => {
    const updated = [...customZones];
    updated[index] = { ...updated[index], [field]: value };
    setCustomZones(updated);
  };

  return {
    calculationMethod,
    setCalculationMethod,
    age,
    setAge,
    restingHr,
    setRestingHr,
    maxHr,
    setMaxHr,
    customZones,
    setCustomZones,
    displayZones,
    updateCustomZone,
  };
}

