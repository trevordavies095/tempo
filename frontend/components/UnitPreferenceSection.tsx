import { formatDistance, formatPace, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';

export default function UnitPreferenceSection() {
  const { unitPreference, setUnitPreference } = useSettings();

  return (
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
  );
}

