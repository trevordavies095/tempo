'use client';

import { formatDistance, formatPace, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { useState } from 'react';

export default function UnitPreferenceSection() {
  const { unitPreference, setUnitPreference } = useSettings();
  const [showTooltip, setShowTooltip] = useState(false);

  return (
    <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100">
          Unit Preference
        </h2>
        <div className="relative">
          <button
            type="button"
            onClick={() => setShowTooltip(!showTooltip)}
            className="text-gray-400 hover:text-gray-600 dark:text-gray-500 dark:hover:text-gray-300 focus:outline-none"
            aria-label="About units"
          >
            <svg
              className="w-5 h-5"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
              xmlns="http://www.w3.org/2000/svg"
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                strokeWidth={2}
                d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
              />
            </svg>
          </button>
          {showTooltip && (
            <>
              {/* Backdrop to close on outside click */}
              <div
                className="fixed inset-0 z-[5]"
                onClick={() => setShowTooltip(false)}
              />
              <div className="absolute right-0 top-8 z-10 w-80 max-w-[calc(100vw-2rem)] bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800 rounded-lg p-4 shadow-lg">
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
                  <li>To update splits for existing workouts, use the "Recalculate Splits" button in the Data Recalculation section.</li>
                </ul>
                <button
                  type="button"
                  onClick={() => setShowTooltip(false)}
                  className="mt-2 text-xs text-blue-600 dark:text-blue-400 hover:underline"
                >
                  Close
                </button>
              </div>
            </>
          )}
        </div>
      </div>
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
  );
}

