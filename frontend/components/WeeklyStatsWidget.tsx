'use client';

import { useQuery } from '@tanstack/react-query';
import { getWeeklyStats } from '@/lib/api';
import { formatDistance } from '@/lib/format';
import { useSettings } from '@/lib/settings';

export default function WeeklyStatsWidget() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const { unitPreference } = useSettings();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['weeklyStats', timezoneOffsetMinutes],
    queryFn: () => getWeeklyStats(timezoneOffsetMinutes),
  });

  // Calculate current day of week (0=Monday, 6=Sunday)
  const now = new Date();
  const currentDayOfWeek = (now.getDay() + 6) % 7;

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          This Week
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
          This Week
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
  const totalWeeklyMiles = data.dailyMiles.reduce((sum, miles) => sum + (Number(miles) || 0), 0);
  const totalWeeklyMeters = totalWeeklyMiles * 1609.344;

  // Convert miles to the preferred unit for bars
  const convertMiles = (miles: number) => {
    if (unitPreference === 'metric') {
      return miles * 1.609344; // Convert to km
    }
    return miles;
  };

  const days = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];
  const dailyValues = data.dailyMiles.map(miles => convertMiles(Number(miles) || 0));
  const maxValue = Math.max(...dailyValues, 0.1); // Minimum 0.1 to avoid division by zero
  const scaleMax = maxValue * 1.1; // Scale goes 10% higher than max value

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
        This Week
      </h2>
      
      <div className="mb-6">
        <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          {formatDistance(totalWeeklyMeters, unitPreference)}
        </div>
      </div>

      <div className="relative pt-2">
        <div className="flex justify-between items-end gap-1" style={{ height: '80px', marginBottom: '24px' }}>
          {days.map((day, index) => {
            const value = dailyValues[index];
            const barHeightPercent = scaleMax > 0 ? (value / scaleMax) * 100 : 0;
            // Ensure minimum 5% height for any non-zero value to make small bars visible
            const barHeight = value > 0 ? Math.max(barHeightPercent, 5) : 0;
            return (
              <div key={index} className="flex flex-col items-center justify-end relative flex-1" style={{ height: '100%' }}>
                <div
                  className="w-3/4 bg-gray-300 dark:bg-gray-600 rounded-t"
                  style={{ height: `${barHeight}%`, minHeight: value > 0 ? '4px' : '0' }}
                />
              </div>
            );
          })}
        </div>
        <div className="flex justify-between items-center relative">
          {days.map((day, index) => (
            <div key={index} className="flex flex-col items-center relative flex-1">
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300">
                {day}
              </span>
              {index === currentDayOfWeek && (
                <div className="absolute top-5 w-0 h-0 border-l-[6px] border-r-[6px] border-b-[8px] border-l-transparent border-r-transparent border-b-orange-500"></div>
              )}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

