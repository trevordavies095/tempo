'use client';

import { useSettings } from '@/lib/settings';
import { formatDistance, formatPace, formatElevation } from '@/lib/format';
import Link from 'next/link';

export default function SettingsPage() {
  const { unitPreference, setUnitPreference } = useSettings();

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
    </div>
  );
}

