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

  // Calculate day of year for progress bar
  const now = new Date();
  const currentYear = now.getFullYear();
  const startOfYear = new Date(currentYear, 0, 1);
  const dayOfYear = Math.floor((now.getTime() - startOfYear.getTime()) / (1000 * 60 * 60 * 24)) + 1;
  const daysInYear = ((currentYear % 4 === 0 && currentYear % 100 !== 0) || currentYear % 400 === 0) ? 366 : 365;
  const yearProgress = (dayOfYear / daysInYear) * 100;

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

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Yearly Total
      </h2>
      
      <div className="mb-4">
        <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          {formatDistance(currentYearMeters, unitPreference)}
        </div>
      </div>

      <div className="relative mt-2">
        <div className="w-full h-2 bg-gray-200 dark:bg-gray-700 rounded-full overflow-hidden relative">
          <div
            className="h-full bg-teal-500 rounded-full transition-all duration-300"
            style={{ width: `${Math.min(yearProgress, 100)}%` }}
          />
        </div>
        <div
          className="absolute top-2 left-0 flex items-center"
          style={{ transform: `translateX(${Math.min(yearProgress, 100)}%)` }}
        >
          <div className="w-0.5 h-3 bg-gray-900 dark:bg-gray-100 -mt-1"></div>
          <span className="ml-1 text-xs font-medium text-gray-700 dark:text-gray-300 whitespace-nowrap">
            TODAY
          </span>
        </div>
      </div>
    </div>
  );
}

