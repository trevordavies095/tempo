'use client';

import { useQuery } from '@tanstack/react-query';
import { Line, XAxis, YAxis, ResponsiveContainer, Tooltip, ComposedChart, ReferenceArea } from 'recharts';
import { getRelativeEffortStats } from '@/lib/api';
import { calculateYAxisTicks, calculateYAxisMax } from '@/utils/chartUtils';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

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
      <Card>
        <h2 className="text-lg font-semibold text-ink mb-4">
          Relative Effort
        </h2>
        <EmptyState title="Loading..." />
      </Card>
    );
  }

  if (isError) {
    return (
      <Card>
        <h2 className="text-lg font-semibold text-ink mb-4">
          Relative Effort
        </h2>
        <EmptyState
          title="Could not load relative effort"
          description={error instanceof Error ? error.message : 'Failed to load relative effort'}
        />
      </Card>
    );
  }

  if (!data) {
    return null;
  }

  // If no data available, show empty state
  const hasData = (data.currentWeek?.some(val => val > 0) || false) || (data.previousWeeks?.some(val => val > 0) || false);
  if (!hasData) {
    return (
      <Card>
        <h2 className="text-lg font-semibold text-ink mb-4">
          Relative Effort
        </h2>
        <EmptyState
          title="No relative effort data"
          description="Relative effort is calculated from heart rate zones."
        />
      </Card>
    );
  }

  const days = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];
  
  // Prepare chart data - mark which days have data (where effort changed from previous day)
  const chartData = (data.currentWeek || []).map((value, index) => {
    const prevValue = index > 0 ? (data.currentWeek?.[index - 1] || 0) : 0;
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

  // Calculate max value for Y-axis
  const maxEffort = Math.max(...(data.currentWeek || []), data.rangeMax || 0, 1);
  const adjustedYAxisMax = calculateYAxisMax(maxEffort);
  const yAxisTicks = calculateYAxisTicks(maxEffort, 3);

  // Determine status relative to range
  const currentTotal = data.currentWeekTotal || 0;
  const status = currentTotal < (data.rangeMin || 0)
    ? 'below' 
    : currentTotal > (data.rangeMax || 0)
    ? 'above' 
    : 'within';

  // Calculate week-over-week comparison
  const lastWeekTotal = data.previousWeeks?.[0] || 0;
  const weekOverWeekChange = lastWeekTotal > 0 
    ? ((currentTotal - lastWeekTotal) / lastWeekTotal) * 100 
    : 0;
  const weekOverWeekDirection = weekOverWeekChange > 0 ? '↑' : weekOverWeekChange < 0 ? '↓' : '';

  // Custom tooltip
  const CustomTooltip = ({ active, payload }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload;
      return (
        <div className="bg-raised border border-border rounded-tempo shadow-lg p-2">
          <p className="text-sm font-medium text-ink">
            {data.day}: {data.cumulativeEffort}
          </p>
          <p className="text-xs text-muted">
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
          fill="var(--ink)"
          className="text-sm font-medium"
        >
          {payload.value}
        </text>
        {isCurrentDay && (
          <polygon
            points="0,24 -6,32 6,32"
            fill="var(--volt)"
          />
        )}
      </g>
    );
  };

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">
        Relative Effort
      </h2>
      
      <div className="mb-4">
        <div className="text-2xl font-bold text-ink">
          {data.currentWeekTotal}
        </div>
        <div className="flex items-center gap-2 mt-1">
          <div className="text-xs text-muted">
            {status === 'within' && 'Within range'}
            {status === 'above' && 'Above range'}
            {status === 'below' && 'Below range'}
          </div>
          {lastWeekTotal > 0 && (
            <div className={`text-xs ${weekOverWeekChange > 0 ? 'text-ink' : weekOverWeekChange < 0 ? 'text-danger' : 'text-muted'}`}>
              {weekOverWeekDirection} {Math.abs(weekOverWeekChange).toFixed(0)}% vs last week
            </div>
          )}
        </div>
        <div className="text-xs text-muted mt-1">
          3-week avg: {(data.threeWeekAverage || 0).toFixed(0)} (range: {data.rangeMin || 0} - {data.rangeMax || 0})
        </div>
      </div>

      <div className="relative">
        <div style={{ height: '180px', marginBottom: '0px' }}>
          <ResponsiveContainer width="100%" height="100%">
            <ComposedChart data={chartData} margin={{ top: 0, right: 0, left: -20, bottom: 20 }}>
            <defs>
              <linearGradient id="rangeGradient" x1="0" y1="0" x2="0" y2="1">
                <stop offset="0%" stopColor="var(--muted)" stopOpacity={0.25} />
                <stop offset="100%" stopColor="var(--muted)" stopOpacity={0.1} />
              </linearGradient>
            </defs>
            {/* Range band using ReferenceArea - horizontal band showing suggested range */}
            <ReferenceArea 
              y1={data.rangeMin || 0} 
              y2={data.rangeMax || 0}
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
                  return <circle cx={props.cx} cy={props.cy} r={4} fill="var(--volt)" />;
                }
                return null;
              }}
              activeDot={(props: any) => {
                if (props.payload?.hasData) {
                  return <circle cx={props.cx} cy={props.cy} r={6} fill="var(--volt)" />;
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
              tick={{ fill: 'var(--muted)', fontSize: 12 }}
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
    </Card>
  );
}
