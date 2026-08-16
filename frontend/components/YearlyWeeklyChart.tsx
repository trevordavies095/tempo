'use client';

import { useQuery } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { BarChart, Bar, XAxis, YAxis, ResponsiveContainer, Tooltip, Cell } from 'recharts';
import { getYearlyWeeklyStats, getAvailablePeriods } from '@/lib/api';
import { formatDistance } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { formatDateRange } from '@/utils/dateUtils';
import PeriodSelector from '@/components/PeriodSelector';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

interface YearlyWeeklyChartProps {
  selectedPeriodEndDate: string | null;
  onPeriodChange: (periodEndDate: string | null) => void;
  selectedWeek: { weekStart: string; weekEnd: string } | null;
  onWeekSelect: (week: { weekStart: string; weekEnd: string } | null) => void;
}

export default function YearlyWeeklyChart({
  selectedPeriodEndDate,
  onPeriodChange,
  selectedWeek,
  onWeekSelect,
}: YearlyWeeklyChartProps) {
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const { unitPreference } = useSettings();
  const [hoveredIndex, setHoveredIndex] = useState<number | null>(null);

  const { data: availablePeriods, isLoading: isLoadingPeriods, isError: isErrorPeriods } = useQuery({
    queryKey: ['availablePeriods', timezoneOffsetMinutes],
    queryFn: () => getAvailablePeriods(timezoneOffsetMinutes),
  });

  // Set default period to most recent if not set
  useEffect(() => {
    if (!selectedPeriodEndDate && availablePeriods && availablePeriods.length > 0) {
      onPeriodChange(availablePeriods[0].periodEndDate);
    }
  }, [availablePeriods, selectedPeriodEndDate, onPeriodChange]);

  const { data: weeklyDataResponse, isLoading, isError, error } = useQuery({
    queryKey: ['yearlyWeeklyStats', selectedPeriodEndDate, timezoneOffsetMinutes],
    queryFn: () => getYearlyWeeklyStats(selectedPeriodEndDate || undefined, timezoneOffsetMinutes),
    // Allow query to run even when selectedPeriodEndDate is null (backend will use default)
    enabled: true,
  });

  // Convert distance to user's preferred unit
  const convertDistance = (distanceM: number) => {
    if (unitPreference === 'metric') {
      return distanceM / 1000; // Convert to km
    }
    return distanceM / 1609.344; // Convert to miles
  };

  // Format chart data
  const weeklyData = weeklyDataResponse?.weeks || [];
  const chartData = weeklyData.map((week) => {
    const distance = convertDistance(week.distanceM);
    const weekStartDate = new Date(week.weekStart);
    const month = weekStartDate.toLocaleDateString('en-US', { month: 'short' });
    const weekNum = week.weekNumber;
    
    // Create label: show month for first week of each month, otherwise just week number
    const isFirstWeekOfMonth = weekNum === 1 || 
      (weeklyData[weekNum - 2] && 
       new Date(weeklyData[weekNum - 2].weekStart).getMonth() !== weekStartDate.getMonth());
    
    return {
      weekNumber: weekNum,
      weekStart: week.weekStart,
      weekEnd: week.weekEnd,
      distance,
      label: isFirstWeekOfMonth ? month : '',
      isSelected: selectedWeek?.weekStart === week.weekStart && selectedWeek?.weekEnd === week.weekEnd,
    };
  });

  const selectedWeekData = chartData.find(
    w => w.weekStart === selectedWeek?.weekStart && w.weekEnd === selectedWeek?.weekEnd
  );

  const handleBarClick = (week: { weekStart: string; weekEnd: string }) => {
    // Toggle selection: deselect if clicking the same week, otherwise select the new week
    if (week.weekStart === selectedWeek?.weekStart && week.weekEnd === selectedWeek?.weekEnd) {
      onWeekSelect(null);
    } else {
      onWeekSelect({ weekStart: week.weekStart, weekEnd: week.weekEnd });
    }
  };


  if (isLoading) {
    return (
      <Card>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-ink">
            Activities
          </h2>
          <PeriodSelector
            availablePeriods={availablePeriods}
            selectedPeriodEndDate={selectedPeriodEndDate}
            onPeriodChange={onPeriodChange}
            isLoading={isLoadingPeriods}
            isError={isErrorPeriods}
          />
        </div>
        <EmptyState title="Loading..." />
      </Card>
    );
  }

  if (isError) {
    return (
      <Card>
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-lg font-semibold text-ink">
            Activities
          </h2>
          <PeriodSelector
            availablePeriods={availablePeriods}
            selectedPeriodEndDate={selectedPeriodEndDate}
            onPeriodChange={onPeriodChange}
            isLoading={isLoadingPeriods}
            isError={isErrorPeriods}
          />
        </div>
        <EmptyState
          title="Could not load weekly stats"
          description={error instanceof Error ? error.message : 'Failed to load stats'}
        />
      </Card>
    );
  }

  return (
    <Card>
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-ink">
            {selectedWeek
              ? `Activities for ${formatDateRange(selectedWeek.weekStart, selectedWeek.weekEnd)}`
              : 'Activities'}
          </h2>
          {selectedWeekData && (
            <div className="flex items-center gap-4 mt-2">
              <div className="text-sm text-muted">
                {formatDistance(selectedWeekData.distance * (unitPreference === 'metric' ? 1000 : 1609.344), unitPreference)}
              </div>
            </div>
          )}
        </div>
        <PeriodSelector
          availablePeriods={availablePeriods}
          selectedPeriodEndDate={selectedPeriodEndDate}
          onPeriodChange={onPeriodChange}
          isLoading={isLoadingPeriods}
          isError={isErrorPeriods}
        />
      </div>

      <div className="mt-4">
        <ResponsiveContainer width="100%" height={100}>
          <BarChart
            data={chartData}
            margin={{ top: 5, right: 5, left: 5, bottom: 0 }}
          >
            <XAxis
              dataKey="weekNumber"
              tick={{ fontSize: 10, fill: 'var(--muted)' }}
              tickFormatter={(value, index) => {
                const item = chartData[index];
                return item?.label || '';
              }}
              interval={0}
              angle={-45}
              textAnchor="end"
              height={40}
            />
            <YAxis
              tick={{ fontSize: 12, fill: 'var(--muted)' }}
              tickFormatter={(value) => {
                if (unitPreference === 'metric') {
                  return `${value} km`;
                }
                return `${value} mi`;
              }}
            />
            <Tooltip
              content={({ active, payload }) => {
                if (active && payload && payload[0]) {
                  const data = payload[0].payload;
                  return (
                    <div className="bg-raised border border-border rounded-tempo p-2 shadow-lg">
                      <p className="text-sm font-medium text-ink">
                        Week {data.weekNumber}
                      </p>
                      <p className="text-xs text-muted">
                        {formatDateRange(data.weekStart, data.weekEnd)}
                      </p>
                      <p className="text-sm text-ink mt-1">
                        {formatDistance(data.distance * (unitPreference === 'metric' ? 1000 : 1609.344), unitPreference)}
                      </p>
                    </div>
                  );
                }
                return null;
              }}
            />
            <Bar
              dataKey="distance"
              radius={[2, 2, 0, 0]}
              cursor="pointer"
            >
              {chartData.map((entry, index) => {
                let fillColor = 'var(--volt)';
                if (entry.isSelected) {
                  fillColor = 'var(--ink)';
                } else if (hoveredIndex === index) {
                  fillColor = 'var(--muted)';
                }
                
                return (
                  <Cell
                    key={`cell-${index}`}
                    fill={fillColor}
                    onClick={() => handleBarClick({ weekStart: entry.weekStart, weekEnd: entry.weekEnd })}
                    style={{ cursor: 'pointer' }}
                    onMouseEnter={() => setHoveredIndex(index)}
                    onMouseLeave={() => setHoveredIndex(null)}
                  />
                );
              })}
            </Bar>
          </BarChart>
        </ResponsiveContainer>
      </div>
    </Card>
  );
}
