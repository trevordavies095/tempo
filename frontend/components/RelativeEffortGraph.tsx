'use client';

import { useQuery } from '@tanstack/react-query';
import { LineChart, Line, Area, AreaChart, XAxis, YAxis, ResponsiveContainer, Tooltip, ComposedChart, ReferenceArea } from 'recharts';
import { getRelativeEffortStats } from '@/lib/api';

export default function RelativeEffortGraph() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['relativeEffortStats', timezoneOffsetMinutes],
    queryFn: () => getRelativeEffortStats(timezoneOffsetMinutes),
  });

  // Calculate current day of week (0=Monday, 6=Sunday)
  const now = new Date();
  const currentDayOfWeek = (now.getDay() + 6) % 7;

  if (isLoading) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Relative Effort
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
          Relative Effort
        </h2>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-red-600 dark:text-red-400">
            Error: {error instanceof Error ? error.message : 'Failed to load relative effort'}
          </p>
        </div>
      </div>
    );
  }

  if (!data) {
    return null;
  }

  // If no data available, show empty state
  const hasData = data.currentWeek.some(val => val > 0) || data.previousWeeks.some(val => val > 0);
  if (!hasData) {
    return (
      <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
        <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
          Relative Effort
        </h2>
        <div className="h-64 flex items-center justify-center">
          <p className="text-sm text-gray-600 dark:text-gray-400">
            No relative effort data available. Relative effort is calculated from heart rate zones.
          </p>
        </div>
      </div>
    );
  }

  const days = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];
  
  // Prepare chart data - mark which days have data (where effort changed from previous day)
  const chartData = data.currentWeek.map((value, index) => {
    const prevValue = index > 0 ? data.currentWeek[index - 1] : 0;
    const hasData = value !== prevValue; // Day has data if cumulative effort changed
    return {
      day: days[index],
      dayIndex: index,
      cumulativeEffort: value,
      rangeMin: data.rangeMin,
      rangeMax: data.rangeMax,
      hasData,
    };
  });

  // Calculate max value for Y-axis (add some padding)
  const maxEffort = Math.max(...data.currentWeek, data.rangeMax, 1);
  const yAxisMax = Math.ceil(maxEffort * 1.2);
  
  // Generate Y-axis ticks that align with data values
  // Find a nice round number for the max (round up to nearest 50 or 100)
  const roundToNice = (num: number): number => {
    if (num <= 50) return Math.ceil(num / 10) * 10;
    if (num <= 200) return Math.ceil(num / 50) * 50;
    return Math.ceil(num / 100) * 100;
  };
  
  const niceMax = roundToNice(yAxisMax);
  const tickCount = 3;
  
  // Generate ticks that include 0 and divide the range nicely
  // Also ensure we include the actual max data value if it's significant
  const yAxisTicks = [0];
  if (niceMax <= 100) {
    // For smaller ranges, use increments of 25 or 50
    const step = niceMax <= 50 ? 25 : 50;
    for (let val = step; val <= niceMax; val += step) {
      yAxisTicks.push(val);
    }
  } else {
    // For larger ranges, divide into 3-4 equal parts with nice numbers
    const step = roundToNice(niceMax / tickCount);
    for (let val = step; val <= niceMax; val += step) {
      yAxisTicks.push(val);
    }
  }
  
  // Ensure we include the actual max effort value if it's not already in ticks
  // This helps align data points with Y-axis labels
  if (maxEffort > 0 && !yAxisTicks.includes(maxEffort)) {
    // Find the closest tick and add the actual value if it's significantly different
    const closestTick = yAxisTicks.reduce((prev, curr) => 
      Math.abs(curr - maxEffort) < Math.abs(prev - maxEffort) ? curr : prev
    );
    if (Math.abs(closestTick - maxEffort) > maxEffort * 0.1) {
      // Add the actual max value if it's more than 10% different from closest tick
      yAxisTicks.push(maxEffort);
      yAxisTicks.sort((a, b) => a - b);
    }
  }
  
  // Ensure we have at least 3 ticks including 0
  if (yAxisTicks.length < 3) {
    const step = niceMax / 2;
    yAxisTicks.length = 1; // Keep 0
    yAxisTicks.push(Math.round(step));
    yAxisTicks.push(niceMax);
  }
  
  const adjustedYAxisMax = niceMax;

  // Determine status relative to range
  const currentTotal = data.currentWeekTotal;
  const status = currentTotal < data.rangeMin 
    ? 'below' 
    : currentTotal > data.rangeMax 
    ? 'above' 
    : 'within';

  // Calculate week-over-week comparison
  const lastWeekTotal = data.previousWeeks[0] || 0;
  const weekOverWeekChange = lastWeekTotal > 0 
    ? ((currentTotal - lastWeekTotal) / lastWeekTotal) * 100 
    : 0;
  const weekOverWeekDirection = weekOverWeekChange > 0 ? '↑' : weekOverWeekChange < 0 ? '↓' : '';

  // Custom tooltip
  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      return (
        <div className="bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 rounded-lg shadow-lg p-2">
          <p className="text-sm font-medium text-gray-900 dark:text-gray-100">
            {data.day}: {data.cumulativeEffort}
          </p>
          <p className="text-xs text-gray-500 dark:text-gray-400">
            Range: {data.rangeMin} - {data.rangeMax}
          </p>
        </div>
      );
    }
    return null;
  };

  // Custom XAxis tick with current day indicator
  const CustomTick = ({ x, y, payload, index }: any) => {
    const isCurrentDay = index === currentDayOfWeek;
    return (
      <g transform={`translate(${x},${y})`}>
        <text
          x={0}
          y={0}
          dy={16}
          textAnchor="middle"
          fill="currentColor"
          className="text-sm font-medium text-gray-700 dark:text-gray-300"
        >
          {payload.value}
        </text>
        {isCurrentDay && (
          <polygon
            points="0,24 -6,32 6,32"
            fill="#f97316"
            className="text-orange-500"
          />
        )}
      </g>
    );
  };

  return (
    <div className="bg-white dark:bg-gray-800 rounded-lg border border-gray-200 dark:border-gray-700 p-6">
      <h2 className="text-lg font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Relative Effort
      </h2>
      
      <div className="mb-4">
        <div className="text-2xl font-bold text-gray-900 dark:text-gray-100">
          {data.currentWeekTotal}
        </div>
        <div className="flex items-center gap-2 mt-1">
          <div className="text-xs text-gray-500 dark:text-gray-400">
            {status === 'within' && 'Within range'}
            {status === 'above' && 'Above range'}
            {status === 'below' && 'Below range'}
          </div>
          {lastWeekTotal > 0 && (
            <div className={`text-xs ${weekOverWeekChange > 0 ? 'text-green-600 dark:text-green-400' : weekOverWeekChange < 0 ? 'text-red-600 dark:text-red-400' : 'text-gray-500 dark:text-gray-400'}`}>
              {weekOverWeekDirection} {Math.abs(weekOverWeekChange).toFixed(0)}% vs last week
            </div>
          )}
        </div>
        <div className="text-xs text-gray-500 dark:text-gray-400 mt-1">
          3-week avg: {data.threeWeekAverage.toFixed(0)} (range: {data.rangeMin} - {data.rangeMax})
        </div>
      </div>

      <div className="relative">
        <div style={{ height: '180px', marginBottom: '0px' }}>
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chartData} margin={{ top: 0, right: 0, left: -20, bottom: 20 }}>
            <defs>
              <linearGradient id="rangeGradient" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="rgba(156, 163, 175, 0.25)" stopOpacity={0.25} />
                <stop offset="100%" stopColor="rgba(156, 163, 175, 0.1)" stopOpacity={0.1} />
              </linearGradient>
            </defs>
            {/* Range band using ReferenceArea - horizontal band showing suggested range */}
            <ReferenceArea 
              y1={data.rangeMin} 
              y2={data.rangeMax}
              fill="url(#rangeGradient)"
              stroke="none"
            />
            {/* Cumulative effort points - only dots for days with data, no line */}
            <Line
              type="monotone"
              dataKey="cumulativeEffort"
              stroke="none"
              connectNulls={false}
              dot={(props: any) => {
                // Only show dot if this day has data
                if (props.payload?.hasData) {
                  return <circle cx={props.cx} cy={props.cy} r={4} fill="#3b82f6" />;
                }
                return null;
              }}
              activeDot={(props: any) => {
                if (props.payload?.hasData) {
                  return <circle cx={props.cx} cy={props.cy} r={6} fill="#3b82f6" />;
                }
                return null;
              }}
            />
            <XAxis 
              dataKey="day" 
              type="category"
              axisLine={false}
              tickLine={false}
              tick={<CustomTick />}
              padding={{ left: 0, right: 0 }}
            />
            <YAxis 
              domain={[0, adjustedYAxisMax]}
              axisLine={false}
              tickLine={false}
              tick={{ fill: '#6b7280', fontSize: 12 }}
              width={50}
              tickMargin={5}
              ticks={yAxisTicks}
              allowDecimals={false}
            />
            <Tooltip content={<CustomTooltip />} />
          </ComposedChart>
        </ResponsiveContainer>
        </div>
      </div>
    </div>
  );
}

