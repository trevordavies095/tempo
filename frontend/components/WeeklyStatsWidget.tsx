'use client';

import { useQuery } from '@tanstack/react-query';
import { useState, useEffect } from 'react';
import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer } from 'recharts';
import { getWeeklyStats } from '@/lib/api';
import { useSettings } from '@/lib/settings';

export default function WeeklyStatsWidget() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const { unitPreference } = useSettings();
  
  // Detect dark mode
  const [isDark, setIsDark] = useState(false);
  useEffect(() => {
    const checkDarkMode = () => {
      setIsDark(document.documentElement.classList.contains('dark'));
    };
    checkDarkMode();
    const observer = new MutationObserver(checkDarkMode);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['class'],
    });
    return () => observer.disconnect();
  }, []);

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['weeklyStats', timezoneOffsetMinutes],
    queryFn: () => getWeeklyStats(timezoneOffsetMinutes),
  });

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

  // Convert miles to the preferred unit
  // Backend returns miles, convert to km if metric is preferred
  const convertMiles = (miles: number) => {
    if (unitPreference === 'metric') {
      return miles * 1.609344; // Convert to km
    }
    return miles;
  };

  // Prepare data for chart: [M, T, W, T, F, S, S]
  // Ensure all values are numbers and convert to preferred unit
  const chartData = [
    { day: 'M', value: convertMiles(Number(data.dailyMiles[0]) || 0) },
    { day: 'T', value: convertMiles(Number(data.dailyMiles[1]) || 0) },
    { day: 'W', value: convertMiles(Number(data.dailyMiles[2]) || 0) },
    { day: 'T', value: convertMiles(Number(data.dailyMiles[3]) || 0) },
    { day: 'F', value: convertMiles(Number(data.dailyMiles[4]) || 0) },
    { day: 'S', value: convertMiles(Number(data.dailyMiles[5]) || 0) },
    { day: 'S', value: convertMiles(Number(data.dailyMiles[6]) || 0) },
  ];

  // Calculate max value for Y-axis (round up to nearest whole number, minimum 1)
  const maxValue = Math.max(...chartData.map(d => d.value), 0);
  const yAxisMax = maxValue === 0 ? 1 : Math.max(1, Math.ceil(maxValue));

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
        This Week
      </h2>
      <div style={{ width: '100%', height: '200px', minHeight: '200px' }}>
        <ResponsiveContainer width="100%" height={200}>
          <BarChart data={chartData} margin={{ top: 5, right: 5, left: 5, bottom: 5 }}>
            <XAxis
              dataKey="day"
              tick={{ fill: 'currentColor' }}
              style={{ fontSize: '12px' }}
            />
            <YAxis
              domain={[0, yAxisMax]}
              tick={{ fill: 'currentColor' }}
              style={{ fontSize: '12px' }}
              width={40}
              allowDecimals={false}
              label={{ value: unitPreference === 'metric' ? 'km' : 'mi', angle: -90, position: 'insideLeft', style: { textAnchor: 'middle', fill: 'currentColor' } }}
            />
            <Bar
              dataKey="value"
              fill={isDark ? '#60a5fa' : '#3b82f6'}
              radius={[4, 4, 0, 0]}
              isAnimationActive={false}
            />
          </BarChart>
        </ResponsiveContainer>
      </div>
    </div>
  );
}

