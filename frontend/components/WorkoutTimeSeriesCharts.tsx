'use client';

import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  Line,
  LineChart,
  ReferenceDot,
  ReferenceLine,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts';
import { getWorkoutTimeSeries, type WorkoutTimeSeriesSample } from '@/lib/api';
import { formatDuration, formatElevation, formatPace } from '@/lib/format';
import { useSettings, type UnitPreference } from '@/lib/settings';
import type { WorkoutHighlight } from '@/lib/workoutHighlight';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

const TOKEN_FALLBACK = {
  volt: '#e8ff00',
  ink: '#1c1917',
  muted: '#57534e',
  danger: '#e05656',
} as const;

const CHART_POINT_CAP = 2500;
const MIN_SPEED_MPS = 0.3;
const MAX_PACE_S_PER_KM = 1200;

type ChartTokens = {
  volt: string;
  ink: string;
  muted: string;
  danger: string;
  isDark: boolean;
};

type ChartPoint = {
  elapsedSeconds: number;
  heartRateBpm: number | null;
  paceSeconds: number | null;
  elevation: number | null;
};

function readChartTokens(): ChartTokens {
  if (typeof document === 'undefined') {
    return { ...TOKEN_FALLBACK, isDark: true };
  }

  const styles = getComputedStyle(document.documentElement);
  const read = (name: string, fallback: string) =>
    styles.getPropertyValue(name).trim() || fallback;

  return {
    volt: read('--volt', TOKEN_FALLBACK.volt),
    ink: read('--ink', TOKEN_FALLBACK.ink),
    muted: read('--muted', TOKEN_FALLBACK.muted),
    danger: read('--danger', TOKEN_FALLBACK.danger),
    isDark: document.documentElement.classList.contains('dark'),
  };
}

function useChartTokens(): ChartTokens {
  const [tokens, setTokens] = useState<ChartTokens>(readChartTokens);

  useEffect(() => {
    const sync = () => setTokens(readChartTokens());
    sync();
    const observer = new MutationObserver(sync);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ['class'],
    });
    return () => observer.disconnect();
  }, []);

  return tokens;
}

function downsample<T>(items: T[], maxPoints: number): T[] {
  if (items.length <= maxPoints) {
    return items;
  }

  const step = items.length / maxPoints;
  const out: T[] = [];
  for (let i = 0; i < maxPoints; i++) {
    out.push(items[Math.min(items.length - 1, Math.floor(i * step))]);
  }
  return out;
}

function paceSecondsPerKmFromSpeed(speedMps: number): number | null {
  if (!Number.isFinite(speedMps) || speedMps < MIN_SPEED_MPS) {
    return null;
  }
  const pace = 1000 / speedMps;
  if (pace > MAX_PACE_S_PER_KM) {
    return null;
  }
  return pace;
}

function paceSecondsPerKmFromDistance(
  previous: WorkoutTimeSeriesSample | undefined,
  current: WorkoutTimeSeriesSample
): number | null {
  if (
    !previous ||
    previous.distanceM == null ||
    current.distanceM == null
  ) {
    return null;
  }

  const deltaM = current.distanceM - previous.distanceM;
  const deltaS = current.elapsedSeconds - previous.elapsedSeconds;
  if (deltaM <= 0 || deltaS <= 0) {
    return null;
  }

  return paceSecondsPerKmFromSpeed(deltaM / deltaS);
}

function toDisplayPaceSeconds(
  secondsPerKm: number,
  unit: UnitPreference
): number {
  return unit === 'imperial' ? secondsPerKm * 1.609344 : secondsPerKm;
}

function toDisplayElevation(
  meters: number,
  unit: UnitPreference
): number {
  return unit === 'imperial' ? meters * 3.28084 : meters;
}

function buildChartPoints(
  samples: WorkoutTimeSeriesSample[],
  unit: UnitPreference
): ChartPoint[] {
  return samples.map((sample, index) => {
    const previous = index > 0 ? samples[index - 1] : undefined;
    const pacePerKm =
      (sample.speedMps != null
        ? paceSecondsPerKmFromSpeed(sample.speedMps)
        : null) ?? paceSecondsPerKmFromDistance(previous, sample);

    return {
      elapsedSeconds: sample.elapsedSeconds,
      heartRateBpm: sample.heartRateBpm,
      paceSeconds:
        pacePerKm != null ? toDisplayPaceSeconds(pacePerKm, unit) : null,
      elevation:
        sample.elevationM != null
          ? toDisplayElevation(sample.elevationM, unit)
          : null,
    };
  });
}

function hasSeries(
  points: ChartPoint[],
  key: 'heartRateBpm' | 'paceSeconds' | 'elevation'
): boolean {
  return points.some((point) => point[key] != null);
}

function ChartTooltip({
  active,
  payload,
  unitPreference,
  series,
}: {
  active?: boolean;
  payload?: Array<{ payload: ChartPoint }>;
  unitPreference: UnitPreference;
  series: 'heartRateBpm' | 'paceSeconds' | 'elevation';
}) {
  if (!active || !payload?.[0]) {
    return null;
  }

  const point = payload[0].payload;
  let valueLabel = '—';
  if (series === 'heartRateBpm' && point.heartRateBpm != null) {
    valueLabel = `${Math.round(point.heartRateBpm)} bpm`;
  } else if (series === 'paceSeconds' && point.paceSeconds != null) {
    const secondsPerKm =
      unitPreference === 'imperial'
        ? point.paceSeconds / 1.609344
        : point.paceSeconds;
    valueLabel = formatPace(secondsPerKm, unitPreference);
  } else if (series === 'elevation' && point.elevation != null) {
    const meters =
      unitPreference === 'imperial' ? point.elevation / 3.28084 : point.elevation;
    valueLabel = formatElevation(meters, unitPreference);
  }

  return (
    <div className="bg-raised border border-border rounded-tempo p-2 shadow-lg">
      <p className="text-xs text-muted">{formatDuration(point.elapsedSeconds)}</p>
      <p className="text-sm font-medium text-ink">{valueLabel}</p>
    </div>
  );
}

function nearestChartPoint(
  data: ChartPoint[],
  elapsedSeconds: number
): ChartPoint | null {
  if (data.length === 0) {
    return null;
  }

  let nearest = data[0];
  let best = Math.abs(data[0].elapsedSeconds - elapsedSeconds);
  for (let i = 1; i < data.length; i++) {
    const delta = Math.abs(data[i].elapsedSeconds - elapsedSeconds);
    if (delta < best) {
      best = delta;
      nearest = data[i];
    }
  }
  return nearest;
}

function elapsedFromChartEvent(state: {
  activeLabel?: string | number;
  activePayload?: Array<{ payload: ChartPoint }>;
}): number | null {
  const fromPayload = state.activePayload?.[0]?.payload?.elapsedSeconds;
  if (typeof fromPayload === 'number' && Number.isFinite(fromPayload)) {
    return fromPayload;
  }
  if (typeof state.activeLabel === 'number' && Number.isFinite(state.activeLabel)) {
    return state.activeLabel;
  }
  return null;
}

function SensorLineChart({
  data,
  dataKey,
  color,
  cursorColor,
  unitPreference,
  yTickFormatter,
  highlightElapsedSeconds,
  onElapsedChange,
  reversed = false,
}: {
  data: ChartPoint[];
  dataKey: 'heartRateBpm' | 'paceSeconds' | 'elevation';
  color: string;
  cursorColor: string;
  unitPreference: UnitPreference;
  yTickFormatter: (value: number) => string;
  highlightElapsedSeconds: number | null;
  onElapsedChange?: (elapsedSeconds: number | null) => void;
  reversed?: boolean;
}) {
  const cursorPoint =
    highlightElapsedSeconds == null
      ? null
      : nearestChartPoint(data, highlightElapsedSeconds);
  const cursorValue = cursorPoint?.[dataKey] ?? null;

  return (
    <ResponsiveContainer width="100%" height={180}>
      <LineChart
        data={data}
        margin={{ top: 8, right: 8, left: 0, bottom: 0 }}
        onMouseMove={(state) => {
          const elapsed = elapsedFromChartEvent(state);
          if (elapsed != null) {
            onElapsedChange?.(elapsed);
          }
        }}
        onClick={(state) => {
          const elapsed = elapsedFromChartEvent(state);
          if (elapsed != null) {
            onElapsedChange?.(elapsed);
          }
        }}
        onMouseLeave={() => onElapsedChange?.(null)}
      >
        <XAxis
          dataKey="elapsedSeconds"
          tick={{ fontSize: 11, fill: 'var(--muted)' }}
          tickFormatter={(value: number) => formatDuration(value)}
          minTickGap={48}
        />
        <YAxis
          tick={{ fontSize: 11, fill: 'var(--muted)' }}
          tickFormatter={yTickFormatter}
          width={56}
          reversed={reversed}
          domain={['auto', 'auto']}
        />
        <Tooltip
          content={
            <ChartTooltip unitPreference={unitPreference} series={dataKey} />
          }
        />
        {cursorPoint ? (
          <ReferenceLine
            x={cursorPoint.elapsedSeconds}
            stroke={cursorColor}
            strokeWidth={1.5}
            ifOverflow="extendDomain"
          />
        ) : null}
        {cursorPoint && cursorValue != null ? (
          <ReferenceDot
            x={cursorPoint.elapsedSeconds}
            y={cursorValue}
            r={4}
            fill={cursorColor}
            stroke="none"
            ifOverflow="extendDomain"
          />
        ) : null}
        <Line
          type="monotone"
          dataKey={dataKey}
          stroke={color}
          strokeWidth={1.75}
          dot={false}
          connectNulls
          isAnimationActive={false}
        />
      </LineChart>
    </ResponsiveContainer>
  );
}

export function WorkoutTimeSeriesCharts({
  workoutId,
  highlight = null,
  onElapsedChange,
}: {
  workoutId: string;
  highlight?: WorkoutHighlight | null;
  onElapsedChange?: (elapsedSeconds: number | null) => void;
}) {
  const { unitPreference } = useSettings();
  const tokens = useChartTokens();
  const primary = tokens.isDark ? tokens.volt : tokens.ink;

  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['workout-time-series', workoutId],
    queryFn: () => getWorkoutTimeSeries(workoutId),
    staleTime: 5 * 60 * 1000,
  });

  const points = useMemo(
    () => downsample(buildChartPoints(data ?? [], unitPreference), CHART_POINT_CAP),
    [data, unitPreference]
  );

  const showHr = hasSeries(points, 'heartRateBpm');
  const showPace = hasSeries(points, 'paceSeconds');
  const showElev = hasSeries(points, 'elevation');
  const highlightElapsed = highlight?.elapsedSeconds ?? null;
  const cursorColor = tokens.danger;

  if (isLoading) {
    return (
      <Card>
        <p className="text-sm text-muted">Loading sensor data…</p>
      </Card>
    );
  }

  if (isError) {
    return (
      <Card>
        <EmptyState
          title="Could not load sensor data"
          description={
            error instanceof Error ? error.message : 'Failed to load time series'
          }
        />
      </Card>
    );
  }

  if (!showHr && !showPace && !showElev) {
    return (
      <Card>
        <EmptyState title="No sensor data" />
      </Card>
    );
  }

  return (
    <Card className="space-y-6">
      {showHr ? (
        <section>
          <h2 className="text-lg font-semibold text-ink mb-2">Heart rate</h2>
          <SensorLineChart
            data={points}
            dataKey="heartRateBpm"
            color={tokens.danger}
            cursorColor={cursorColor}
            unitPreference={unitPreference}
            highlightElapsedSeconds={highlightElapsed}
            onElapsedChange={onElapsedChange}
            yTickFormatter={(value) => `${Math.round(value)}`}
          />
        </section>
      ) : null}

      {showPace ? (
        <section>
          <h2 className="text-lg font-semibold text-ink mb-2">Pace</h2>
          <SensorLineChart
            data={points}
            dataKey="paceSeconds"
            color={primary}
            cursorColor={cursorColor}
            unitPreference={unitPreference}
            highlightElapsedSeconds={highlightElapsed}
            onElapsedChange={onElapsedChange}
            reversed
            yTickFormatter={(value) => {
              const secondsPerKm =
                unitPreference === 'imperial' ? value / 1.609344 : value;
              return formatPace(secondsPerKm, unitPreference);
            }}
          />
        </section>
      ) : null}

      {showElev ? (
        <section>
          <h2 className="text-lg font-semibold text-ink mb-2">Elevation</h2>
          <SensorLineChart
            data={points}
            dataKey="elevation"
            color={tokens.muted}
            cursorColor={cursorColor}
            unitPreference={unitPreference}
            highlightElapsedSeconds={highlightElapsed}
            onElapsedChange={onElapsedChange}
            yTickFormatter={(value) =>
              formatElevation(
                unitPreference === 'imperial' ? value / 3.28084 : value,
                unitPreference
              )
            }
          />
        </section>
      ) : null}
    </Card>
  );
}
