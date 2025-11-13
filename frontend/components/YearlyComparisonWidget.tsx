'use client';

import { useQuery } from '@tanstack/react-query';
import { getYearlyStats } from '@/lib/api';
import { formatDistance } from '@/lib/format';
import { useSettings } from '@/lib/settings';

export default function YearlyComparisonWidget() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const { unitPreference } = useSettings();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['yearlyStats', timezoneOffsetMinutes],
    queryFn: () => getYearlyStats(timezoneOffsetMinutes),
  });

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Yearly Total
        </h2>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">Loading...</p>
        </div>
      </div>
    );
  }

  if (isError) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Yearly Total
        </h2>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-red-600 dark:text-red-400">
            Error: {error instanceof Error ? error.message : 'Failed to load stats'}
          </p>
        </div>
      </div>
    );
  }

  if (!data) {
    return null;
  }

  // Convert miles to meters for formatDistance (which expects meters)
  const currentYearMeters = data.currentYear * 1609.344;
  const previousYearMeters = data.previousYear * 1609.344;

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Yearly Total
      </h2>
      <div className="space-y-4">
        <div>
          <div className="flex items-baseline justify-between mb-1">
            <span className="text-sm text-gray-600 dark:text-gray-400">
              {data.currentYearLabel}
            </span>
            <span className="text-2xl font-bold text-gray-900 dark:text-gray-100">
              {formatDistance(currentYearMeters, unitPreference)}
            </span>
          </div>
        </div>
        <div>
          <div className="flex items-baseline justify-between mb-1">
            <span className="text-sm text-gray-600 dark:text-gray-400">
              {data.previousYearLabel}
            </span>
            <span className="text-xl font-semibold text-gray-700 dark:text-gray-300">
              {formatDistance(previousYearMeters, unitPreference)}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

