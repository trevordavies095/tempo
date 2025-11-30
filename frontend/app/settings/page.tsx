'use client';

import { useSettings } from '@/lib/settings';
import { 
  getHeartRateZones, 
  updateHeartRateZones,
  recalculateAllRelativeEffort,
  getQualifyingWorkoutCount,
  getQualifyingWorkoutCountForSplits,
  recalculateAllSplits,
  type HeartRateZoneSettings,
  type HeartRateCalculationMethod,
  type UpdateHeartRateZoneSettingsRequest
} from '@/lib/api';
import { RecalculateEffortDialog } from '@/components/RecalculateEffortDialog';
import { ZoneUpdateDialog } from '@/components/ZoneUpdateDialog';
import { RecalculateSplitsDialog } from '@/components/RecalculateSplitsDialog';
import UnitPreferenceSection from '@/components/UnitPreferenceSection';
import { ShoeManagementSection } from '@/components/ShoeManagementSection';
import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getVersion } from '@/lib/api';
import { useHeartRateZones, type ZoneRange } from '@/hooks/useHeartRateZones';
import { invalidateWorkoutQueries } from '@/lib/queryUtils';
import { AuthGuard } from '@/components/AuthGuard';

function SettingsPageContent() {
  const queryClient = useQueryClient();
  const [hrZones, setHrZones] = useState<HeartRateZoneSettings | null>(null);
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
  
  // Zone Update Dialog state (for subsequent updates)
  const [showZoneUpdateDialog, setShowZoneUpdateDialog] = useState(false);
  const [zoneUpdateWorkoutCount, setZoneUpdateWorkoutCount] = useState<number | null>(null);
  const [isZoneUpdating, setIsZoneUpdating] = useState(false);
  
  // Recalculate Splits state
  const [showRecalcSplitsDialog, setShowRecalcSplitsDialog] = useState(false);
  const [recalcSplitsWorkoutCount, setRecalcSplitsWorkoutCount] = useState<number | null>(null);
  const [isRecalculatingSplits, setIsRecalculatingSplits] = useState(false);
  const [recalcSplitsError, setRecalcSplitsError] = useState<string | null>(null);
  const [recalcSplitsSuccess, setRecalcSplitsSuccess] = useState(false);

  // Fetch version information
  const { data: versionInfo } = useQuery({
    queryKey: ['version'],
    queryFn: getVersion,
    staleTime: Infinity, // Version doesn't change during session
    retry: 1, // Only retry once if it fails
  });

  // Load heart rate zones on mount
  useEffect(() => {
    const loadHrZones = async () => {
      try {
        const settings = await getHeartRateZones();
        setHrZones(settings);
      } catch (error) {
        console.error('Failed to load heart rate zones:', error);
      } finally {
        setIsLoading(false);
      }
    };
    loadHrZones();
  }, []);

  const {
    calculationMethod,
    setCalculationMethod,
    age,
    setAge,
    restingHr,
    setRestingHr,
    maxHr,
    setMaxHr,
    customZones,
    displayZones,
    updateCustomZone,
  } = useHeartRateZones(hrZones);

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
          ? displayZones.map((z, i) => ({ zoneNumber: i + 1, minBpm: z.min, maxBpm: z.max }))
          : undefined,
      };

      const updated = await updateHeartRateZones(request);
      setHrZones(updated);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
      
      // If this is first time setup, show confirmation dialog for recalculation
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
      } else {
        // Subsequent update: show dialog with options
        try {
          const countResponse = await getQualifyingWorkoutCount();
          setZoneUpdateWorkoutCount(countResponse.count);
        } catch (error) {
          // If we can't get the count, still show dialog with null count
          setZoneUpdateWorkoutCount(null);
        }
        setShowZoneUpdateDialog(true);
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
      
      // Invalidate all workout queries to refresh the UI
      invalidateWorkoutQueries(queryClient);
      
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

  const handleZoneUpdateRecalculateAll = async () => {
    setIsZoneUpdating(true);
    setRecalcError(null);
    setRecalcSuccess(false);
    setShowZoneUpdateDialog(false);

    try {
      // Zones are already saved, just recalculate all workouts
      const response = await recalculateAllRelativeEffort();
      setRecalcWorkoutCount(response.totalQualifyingWorkouts);
      setRecalcSuccess(true);
      
      // Invalidate all workout queries to refresh the UI
      invalidateWorkoutQueries(queryClient);
      
      setTimeout(() => {
        setRecalcSuccess(false);
        setRecalcWorkoutCount(null);
      }, 5000);
    } catch (error) {
      setRecalcError(error instanceof Error ? error.message : 'Failed to recalculate relative effort');
    } finally {
      setIsZoneUpdating(false);
    }
  };

  const handleZoneUpdateKeepExisting = () => {
    // Zones are already saved, just close the dialog
    setShowZoneUpdateDialog(false);
  };

  const handleRecalculateSplitsClick = async () => {
    // Fetch workout count before showing dialog
    try {
      const countResponse = await getQualifyingWorkoutCountForSplits();
      setRecalcSplitsWorkoutCount(countResponse.count);
      setShowRecalcSplitsDialog(true);
    } catch (error) {
      setRecalcSplitsError('Failed to get workout count');
      // Still show dialog with null count
      setRecalcSplitsWorkoutCount(null);
      setShowRecalcSplitsDialog(true);
    }
  };

  const handleRecalculateSplitsConfirm = async () => {
    setIsRecalculatingSplits(true);
    setRecalcSplitsError(null);
    setRecalcSplitsSuccess(false);
    setShowRecalcSplitsDialog(false);

    try {
      const response = await recalculateAllSplits();
      setRecalcSplitsWorkoutCount(response.totalWorkouts);
      setRecalcSplitsSuccess(true);
      
      // Invalidate all workout queries to refresh the UI
      invalidateWorkoutQueries(queryClient);
      
      setTimeout(() => {
        setRecalcSplitsSuccess(false);
        setRecalcSplitsWorkoutCount(null);
      }, 5000);
    } catch (error) {
      setRecalcSplitsError(error instanceof Error ? error.message : 'Failed to recalculate splits');
    } finally {
      setIsRecalculatingSplits(false);
    }
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
          <UnitPreferenceSection />

          <ShoeManagementSection />

          {/* Recalculate Splits Button */}
          <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
            <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
              Recalculate Splits
            </h2>
            <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
              Recalculate splits for all existing workouts based on your current unit preference. New workouts will automatically use your current preference.
            </p>
            <div className="flex flex-col gap-2">
              <button
                onClick={handleRecalculateSplitsClick}
                disabled={isRecalculatingSplits}
                className={`px-6 py-3 rounded-lg font-medium transition-colors w-fit ${
                  isRecalculatingSplits
                    ? 'bg-gray-400 text-white cursor-not-allowed'
                    : 'bg-orange-600 text-white hover:bg-orange-700 dark:bg-orange-500 dark:hover:bg-orange-600'
                }`}
              >
                {isRecalculatingSplits ? 'Recalculating...' : 'Recalculate Splits'}
              </button>
              {recalcSplitsSuccess && (
                <span className="text-sm text-green-600 dark:text-green-400">
                  Successfully recalculated splits for {recalcSplitsWorkoutCount} workout{recalcSplitsWorkoutCount !== 1 ? 's' : ''}!
                </span>
              )}
              {recalcSplitsError && (
                <span className="text-sm text-red-600 dark:text-red-400">
                  {recalcSplitsError}
                </span>
              )}
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
                    {displayZones.map((zone, index) => (
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
              <li>Your preference is saved and will persist across sessions.</li>
              <li>New workouts will be imported with splits based on your current unit preference (1 km splits for metric, 1 mile splits for imperial).</li>
              <li>To update splits for existing workouts, use the "Recalculate Splits" button above.</li>
            </ul>
          </div>

          {/* Version Information */}
          {versionInfo && (
            <div className="mt-8 pt-6 border-t border-gray-200 dark:border-gray-800">
              <div className="text-center text-sm text-gray-500 dark:text-gray-400">
                <div className="font-mono">v{versionInfo.version}</div>
                {versionInfo.buildDate && versionInfo.buildDate !== 'unknown' && (
                  <div className="text-xs mt-1">
                    Built {new Date(versionInfo.buildDate).toLocaleDateString()}
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </main>
      
      {/* Recalculate Relative Effort Dialog (for first-time setup) */}
      <RecalculateEffortDialog
        open={showRecalcDialog}
        onClose={() => setShowRecalcDialog(false)}
        onConfirm={handleRecalculateConfirm}
        workoutCount={recalcWorkoutCount}
        isLoading={isRecalculating}
      />
      
      {/* Zone Update Dialog (for subsequent updates) */}
      <ZoneUpdateDialog
        open={showZoneUpdateDialog}
        onClose={() => setShowZoneUpdateDialog(false)}
        onRecalculateAll={handleZoneUpdateRecalculateAll}
        onKeepExisting={handleZoneUpdateKeepExisting}
        workoutCount={zoneUpdateWorkoutCount}
        isLoading={isZoneUpdating}
      />
      
      {/* Recalculate Splits Dialog */}
      <RecalculateSplitsDialog
        open={showRecalcSplitsDialog}
        onClose={() => setShowRecalcSplitsDialog(false)}
        onConfirm={handleRecalculateSplitsConfirm}
        workoutCount={recalcSplitsWorkoutCount}
        isLoading={isRecalculatingSplits}
      />
    </div>
  );
}

export default function SettingsPage() {
  return (
    <AuthGuard>
      <SettingsPageContent />
    </AuthGuard>
  );
}

