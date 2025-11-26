'use client';

import { useSettings } from '@/lib/settings';
import { formatDistance, formatPace, formatElevation } from '@/lib/format';
import { 
  getHeartRateZones, 
  updateHeartRateZones,
  recalculateAllRelativeEffort,
  getQualifyingWorkoutCount,
  type HeartRateZoneSettings,
  type HeartRateCalculationMethod,
  type UpdateHeartRateZoneSettingsRequest
} from '@/lib/api';
import { RecalculateEffortDialog } from '@/components/RecalculateEffortDialog';
import Link from 'next/link';
import { useEffect, useState } from 'react';

export default function SettingsPage() {
  const { unitPreference, setUnitPreference } = useSettings();
  
  // Heart Rate Zones state
  const [hrZones, setHrZones] = useState<HeartRateZoneSettings | null>(null);
  const [calculationMethod, setCalculationMethod] = useState<HeartRateCalculationMethod>('AgeBased');
  const [age, setAge] = useState<number>(30);
  const [restingHr, setRestingHr] = useState<number>(60);
  const [maxHr, setMaxHr] = useState<number>(190);
  const [customZones, setCustomZones] = useState<Array<{ min: number; max: number }>>([
    { min: 95, max: 114 },
    { min: 114, max: 133 },
    { min: 133, max: 152 },
    { min: 152, max: 171 },
    { min: 171, max: 190 },
  ]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saveSuccess, setSaveSuccess] = useState(false);
  
  // Recalculate Relative Effort state
  const [showRecalcDialog, setShowRecalcDialog] = useState(false);
  const [recalcWorkoutCount, setRecalcWorkoutCount] = useState<number | null>(null);
  const [isRecalculating, setIsRecalculating] = useState(false);
  const [recalcError, setRecalcError] = useState<string | null>(null);
  const [recalcSuccess, setRecalcSuccess] = useState(false);

  // Load heart rate zones on mount
  useEffect(() => {
    const loadHrZones = async () => {
      try {
        const settings = await getHeartRateZones();
        setHrZones(settings);
        setCalculationMethod(settings.calculationMethod);
        if (settings.age !== null) setAge(settings.age);
        if (settings.restingHeartRateBpm !== null) setRestingHr(settings.restingHeartRateBpm);
        if (settings.maxHeartRateBpm !== null) setMaxHr(settings.maxHeartRateBpm);
        if (settings.zones && settings.zones.length === 5) {
          setCustomZones(settings.zones.map(z => ({ min: z.minBpm, max: z.maxBpm })));
        }
      } catch (error) {
        console.error('Failed to load heart rate zones:', error);
      } finally {
        setIsLoading(false);
      }
    };
    loadHrZones();
  }, []);

  // Recalculate zones when method or inputs change (for preview only)
  useEffect(() => {
    if (calculationMethod === 'Custom') return; // Don't recalculate custom zones
    
    const zonePercentages = [
      { min: 0.50, max: 0.60 }, // Zone 1: 50-60%
      { min: 0.60, max: 0.70 }, // Zone 2: 60-70%
      { min: 0.70, max: 0.80 }, // Zone 3: 70-80%
      { min: 0.80, max: 0.90 }, // Zone 4: 80-90%
      { min: 0.90, max: 1.00 }, // Zone 5: 90-100%
    ];

    let calculatedZones: Array<{ min: number; max: number }> = [];

    if (calculationMethod === 'AgeBased') {
      const maxHeartRate = 220 - age;
      calculatedZones = zonePercentages.map(p => ({
        min: Math.round(maxHeartRate * p.min),
        max: Math.round(maxHeartRate * p.max),
      }));
    } else if (calculationMethod === 'Karvonen') {
      const heartRateReserve = maxHr - restingHr;
      calculatedZones = zonePercentages.map(p => ({
        min: Math.round((heartRateReserve * p.min) + restingHr),
        max: Math.round((heartRateReserve * p.max) + restingHr),
      }));
    }

    if (calculatedZones.length === 5) {
      setCustomZones(calculatedZones);
    }
  }, [calculationMethod, age, restingHr, maxHr]);

  const handleSaveHrZones = async () => {
    setIsSaving(true);
    setSaveError(null);
    setSaveSuccess(false);

    try {
      const request: UpdateHeartRateZoneSettingsRequest = {
        calculationMethod,
        age: calculationMethod === 'AgeBased' ? age : null,
        restingHeartRateBpm: calculationMethod === 'Karvonen' ? restingHr : null,
        maxHeartRateBpm: calculationMethod === 'Karvonen' ? maxHr : null,
        zones: calculationMethod === 'Custom' 
          ? customZones.map((z, i) => ({ zoneNumber: i + 1, minBpm: z.min, maxBpm: z.max }))
          : undefined,
      };

      const updated = await updateHeartRateZones(request);
      setHrZones(updated);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
      
      // If this is first time setup, show confirmation dialog
      if (updated.isFirstTimeSetup) {
        // Fetch workout count first
        try {
          const countResponse = await getQualifyingWorkoutCount();
          setRecalcWorkoutCount(countResponse.count);
        } catch (error) {
          // If we can't get the count, still show dialog with null count
          setRecalcWorkoutCount(null);
        }
        setShowRecalcDialog(true);
      }
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : 'Failed to save heart rate zones');
    } finally {
      setIsSaving(false);
    }
  };

  const handleRecalculateClick = async () => {
    // Fetch workout count before showing dialog
    try {
      const countResponse = await getQualifyingWorkoutCount();
      setRecalcWorkoutCount(countResponse.count);
      setShowRecalcDialog(true);
    } catch (error) {
      setRecalcError('Failed to get workout count');
      // Still show dialog with null count
      setRecalcWorkoutCount(null);
      setShowRecalcDialog(true);
    }
  };

  const handleRecalculateConfirm = async () => {
    setIsRecalculating(true);
    setRecalcError(null);
    setRecalcSuccess(false);
    setShowRecalcDialog(false);

    try {
      const response = await recalculateAllRelativeEffort();
      setRecalcWorkoutCount(response.totalQualifyingWorkouts);
      setRecalcSuccess(true);
      setTimeout(() => {
        setRecalcSuccess(false);
        setRecalcWorkoutCount(null);
      }, 5000);
    } catch (error) {
      setRecalcError(error instanceof Error ? error.message : 'Failed to recalculate relative effort');
    } finally {
      setIsRecalculating(false);
    }
  };

  const updateCustomZone = (index: number, field: 'min' | 'max', value: number) => {
    const updated = [...customZones];
    updated[index] = { ...updated[index], [field]: value };
    setCustomZones(updated);
  };

  return (
    <div className="flex min-h-screen items-start justify-center bg-zinc-50 dark:bg-black">
      <main className="flex min-h-screen w-full max-w-4xl flex-col items-start py-16 px-8">
        <div className="w-full mb-8">
          <Link
            href="/dashboard"
            className="text-blue-600 dark:text-blue-400 hover:underline mb-4 inline-block"
          >
            ← Back to Dashboard
          </Link>
          <h1 className="text-4xl font-bold text-gray-900 dark:text-gray-100 mb-2">
            Settings
          </h1>
          <p className="text-lg text-gray-600 dark:text-gray-400">
            Configure your preferences
          </p>
        </div>

        <div className="w-full space-y-8">
          {/* Unit Preference */}
          <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
            <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Unit Preference
            </h2>
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
              Choose how distances, paces, and elevations are displayed throughout the app.
            </p>

            <div className="flex gap-4 mb-6">
              <button
                onClick={() => setUnitPreference('metric')}
                className={`px-6 py-3 rounded-lg font-medium transition-colors ${
                  unitPreference === 'metric'
                    ? 'bg-blue-600 text-white dark:bg-blue-500'
                    : 'bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700'
                }`}
              >
                Metric
              </button>
              <button
                onClick={() => setUnitPreference('imperial')}
                className={`px-6 py-3 rounded-lg font-medium transition-colors ${
                  unitPreference === 'imperial'
                    ? 'bg-blue-600 text-white dark:bg-blue-500'
                    : 'bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700'
                }`}
              >
                Imperial
              </button>
            </div>

            {/* Preview */}
            <div className="bg-gray-50 dark:bg-gray-800 p-4 rounded-lg">
              <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                Preview
              </h3>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-sm">
                <div>
                  <div className="text-gray-600 dark:text-gray-400 mb-1">Distance</div>
                  <div className="text-gray-900 dark:text-gray-100 font-medium">
                    {formatDistance(10000, unitPreference)}
                  </div>
                </div>
                <div>
                  <div className="text-gray-600 dark:text-gray-400 mb-1">Pace</div>
                  <div className="text-gray-900 dark:text-gray-100 font-medium">
                    {formatPace(300, unitPreference)}
                  </div>
                </div>
                <div>
                  <div className="text-gray-600 dark:text-gray-400 mb-1">Elevation</div>
                  <div className="text-gray-900 dark:text-gray-100 font-medium">
                    {formatElevation(150, unitPreference)}
                  </div>
                </div>
              </div>
            </div>
          </div>

          {/* Heart Rate Zones */}
          <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
            <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Heart Rate Zones
            </h2>
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-6">
              Configure your heart rate zones for training analysis. Choose a calculation method or set custom zones.
            </p>

            {isLoading ? (
              <div className="text-gray-600 dark:text-gray-400">Loading...</div>
            ) : (
              <>
                {/* Calculation Method Selection */}
                <div className="mb-6">
                  <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
                    Calculation Method
                  </label>
                  <div className="space-y-2">
                    <label className="flex items-center">
                      <input
                        type="radio"
                        name="calculationMethod"
                        value="AgeBased"
                        checked={calculationMethod === 'AgeBased'}
                        onChange={(e) => setCalculationMethod(e.target.value as HeartRateCalculationMethod)}
                        className="mr-2"
                      />
                      <span className="text-sm text-gray-700 dark:text-gray-300">
                        220 - Age (Default)
                      </span>
                    </label>
                    <label className="flex items-center">
                      <input
                        type="radio"
                        name="calculationMethod"
                        value="Karvonen"
                        checked={calculationMethod === 'Karvonen'}
                        onChange={(e) => setCalculationMethod(e.target.value as HeartRateCalculationMethod)}
                        className="mr-2"
                      />
                      <span className="text-sm text-gray-700 dark:text-gray-300">
                        Karvonen (Heart Rate Reserve)
                      </span>
                    </label>
                    <label className="flex items-center">
                      <input
                        type="radio"
                        name="calculationMethod"
                        value="Custom"
                        checked={calculationMethod === 'Custom'}
                        onChange={(e) => setCalculationMethod(e.target.value as HeartRateCalculationMethod)}
                        className="mr-2"
                      />
                      <span className="text-sm text-gray-700 dark:text-gray-300">
                        Custom Zones
                      </span>
                    </label>
                  </div>
                </div>

                {/* Input Fields Based on Method */}
                {calculationMethod === 'AgeBased' && (
                  <div className="mb-6">
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                      Age
                    </label>
                    <input
                      type="number"
                      min="1"
                      max="120"
                      value={age}
                      onChange={(e) => setAge(parseInt(e.target.value) || 30)}
                      className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                    />
                    <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                      Max HR will be calculated as 220 - age = {220 - age} BPM
                    </p>
                  </div>
                )}

                {calculationMethod === 'Karvonen' && (
                  <div className="mb-6 space-y-4">
                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                        Resting Heart Rate (BPM)
                      </label>
                      <input
                        type="number"
                        min="30"
                        max="120"
                        value={restingHr}
                        onChange={(e) => setRestingHr(parseInt(e.target.value) || 60)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                      />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-2">
                        Maximum Heart Rate (BPM)
                      </label>
                      <input
                        type="number"
                        min="60"
                        max="250"
                        value={maxHr}
                        onChange={(e) => setMaxHr(parseInt(e.target.value) || 190)}
                        className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                      />
                      <p className="text-xs text-gray-500 dark:text-gray-400 mt-1">
                        Heart Rate Reserve = {maxHr} - {restingHr} = {maxHr - restingHr} BPM
                      </p>
                    </div>
                  </div>
                )}

                {calculationMethod === 'Custom' && (
                  <div className="mb-6">
                    <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
                      Custom Zone Boundaries (BPM)
                    </label>
                    <div className="space-y-3">
                      {customZones.map((zone, index) => (
                        <div key={index} className="flex items-center gap-3">
                          <span className="text-sm font-medium text-gray-700 dark:text-gray-300 w-16">
                            Zone {index + 1}:
                          </span>
                          <input
                            type="number"
                            min="30"
                            max="250"
                            value={zone.min}
                            onChange={(e) => updateCustomZone(index, 'min', parseInt(e.target.value) || 0)}
                            className="w-24 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                            placeholder="Min"
                          />
                          <span className="text-gray-500 dark:text-gray-400">-</span>
                          <input
                            type="number"
                            min="30"
                            max="250"
                            value={zone.max}
                            onChange={(e) => updateCustomZone(index, 'max', parseInt(e.target.value) || 0)}
                            className="w-24 px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-lg bg-white dark:bg-gray-800 text-gray-900 dark:text-gray-100"
                            placeholder="Max"
                          />
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Zone Preview */}
                <div className="bg-gray-50 dark:bg-gray-800 p-4 rounded-lg mb-6">
                  <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-3">
                    Zone Preview
                  </h3>
                  <div className="space-y-2">
                    {customZones.map((zone, index) => (
                      <div key={index} className="flex items-center justify-between text-sm">
                        <span className="text-gray-700 dark:text-gray-300 font-medium">
                          Zone {index + 1}
                        </span>
                        <span className="text-gray-600 dark:text-gray-400">
                          {zone.min} - {zone.max} BPM
                        </span>
                      </div>
                    ))}
                  </div>
                </div>

                {/* Save Button and Messages */}
                <div className="flex flex-col gap-4">
                  <div className="flex items-center gap-4">
                    <button
                      onClick={handleSaveHrZones}
                      disabled={isSaving}
                      className={`px-6 py-3 rounded-lg font-medium transition-colors ${
                        isSaving
                          ? 'bg-gray-400 text-white cursor-not-allowed'
                          : 'bg-blue-600 text-white hover:bg-blue-700 dark:bg-blue-500 dark:hover:bg-blue-600'
                      }`}
                    >
                      {isSaving ? 'Saving...' : 'Save Heart Rate Zones'}
                    </button>
                    {saveSuccess && (
                      <span className="text-sm text-green-600 dark:text-green-400">
                        Settings saved successfully!
                      </span>
                    )}
                    {saveError && (
                      <span className="text-sm text-red-600 dark:text-red-400">
                        {saveError}
                      </span>
                    )}
                  </div>
                  
                  {/* Recalculate Relative Effort Button */}
                  {hrZones && (
                    <div className="flex flex-col gap-2">
                      <button
                        onClick={handleRecalculateClick}
                        disabled={isRecalculating || isSaving}
                        className={`px-6 py-3 rounded-lg font-medium transition-colors w-fit ${
                          isRecalculating || isSaving
                            ? 'bg-gray-400 text-white cursor-not-allowed'
                            : 'bg-orange-600 text-white hover:bg-orange-700 dark:bg-orange-500 dark:hover:bg-orange-600'
                        }`}
                      >
                        {isRecalculating ? 'Recalculating...' : 'Recalculate Relative Effort'}
                      </button>
                      {recalcSuccess && (
                        <span className="text-sm text-green-600 dark:text-green-400">
                          Successfully recalculated relative effort for {recalcWorkoutCount} workout{recalcWorkoutCount !== 1 ? 's' : ''}!
                        </span>
                      )}
                      {recalcError && (
                        <span className="text-sm text-red-600 dark:text-red-400">
                          {recalcError}
                        </span>
                      )}
                    </div>
                  )}
                </div>
              </>
            )}
          </div>

          {/* Info Section */}
          <div className="bg-blue-50 dark:bg-blue-900/20 p-6 rounded-lg border border-blue-200 dark:border-blue-800">
            <h3 className="text-sm font-semibold text-blue-900 dark:text-blue-200 mb-2">
              About Units
            </h3>
            <ul className="text-sm text-blue-800 dark:text-blue-300 space-y-1 list-disc list-inside">
              <li>
                <strong>Metric:</strong> Distances in kilometers (km), pace per kilometer, elevation in meters (m)
              </li>
              <li>
                <strong>Imperial:</strong> Distances in miles (mi), pace per mile, elevation in feet (ft)
              </li>
              <li>Your preference is saved locally in your browser and will persist across sessions.</li>
              <li>Splits will be calculated and displayed based on your unit preference (1 km splits for metric, 1 mile splits for imperial).</li>
            </ul>
          </div>
        </div>
      </main>
      
      {/* Recalculate Relative Effort Dialog */}
      <RecalculateEffortDialog
        open={showRecalcDialog}
        onClose={() => setShowRecalcDialog(false)}
        onConfirm={handleRecalculateConfirm}
        workoutCount={recalcWorkoutCount}
        isLoading={isRecalculating}
      />
    </div>
  );
}

