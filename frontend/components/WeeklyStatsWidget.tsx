'use client';

import { useQuery } from '@tanstack/react-query';
import { useState, useEffect } from 'react';
import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer } from 'recharts';
import { getWeeklyStats } from '@/lib/api';

export default function WeeklyStatsWidget() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  
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

  // Prepare data for chart: [M, T, W, T, F, S, S]
  // Ensure all values are numbers
  const chartData = [
    { day: 'M', miles: Number(data.dailyMiles[0]) || 0 },
    { day: 'T', miles: Number(data.dailyMiles[1]) || 0 },
    { day: 'W', miles: Number(data.dailyMiles[2]) || 0 },
    { day: 'T', miles: Number(data.dailyMiles[3]) || 0 },
    { day: 'F', miles: Number(data.dailyMiles[4]) || 0 },
    { day: 'S', miles: Number(data.dailyMiles[5]) || 0 },
    { day: 'S', miles: Number(data.dailyMiles[6]) || 0 },
  ];

  // Calculate max value for Y-axis (round up to nearest whole number, minimum 1)
  const maxMiles = Math.max(...chartData.map(d => d.miles), 0);
  const yAxisMax = maxMiles === 0 ? 1 : Math.max(1, Math.ceil(maxMiles));

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
            />
            <Bar
              dataKey="miles"
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

