'use client';

import { useAppearance } from '@/contexts/AppearanceContext';
import type { AppearancePreference } from '@/lib/appearance';

const OPTIONS: { value: AppearancePreference; label: string }[] = [
  { value: 'system', label: 'System' },
  { value: 'dark', label: 'Dark' },
  { value: 'light', label: 'Light' },
];

export default function AppearanceSection() {
  const { preference, setPreference } = useAppearance();

  return (
    <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
      <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Appearance
      </h2>
      <p className="text-sm text-gray-600 dark:text-gray-400 mb-4">
        Dark-first command center. System follows your operating system. Stored
        on this browser only.
      </p>

      <div className="flex flex-wrap gap-4">
        {OPTIONS.map(({ value, label }) => (
          <button
            key={value}
            type="button"
            onClick={() => setPreference(value)}
            className={`px-6 py-3 rounded-lg font-medium transition-colors ${
              preference === value
                ? 'bg-volt text-ink'
                : 'bg-gray-100 text-gray-700 dark:bg-gray-800 dark:text-gray-300 hover:bg-gray-200 dark:hover:bg-gray-700'
            }`}
          >
            {label}
          </button>
        ))}
      </div>
    </div>
  );
}
