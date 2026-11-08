'use client';

import { useQuery } from '@tanstack/react-query';
import { getWeeklyRecap, getWeeklyStats, getYearlyStats, type WeeklyRecapResponse } from '@/lib/api';
import { formatDistance, formatDuration, formatElevation } from '@/lib/format';
import { runsByTypeFullLine, runsByTypeLine } from '@/lib/runTypes';
import { recapThroughWeekday, recapTrend, type RecapTrend } from '@/lib/weeklyRecap';
import { useSettings, type UnitPreference } from '@/lib/settings';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';
import { HeartRateZoneTimeBar, type ZoneTime } from '@/components/HeartRateZoneTimeBar';

const METERS_PER_MILE = 1609.344;

/** Parses a `yyyy-MM-dd` date as a local calendar date (no UTC shift). */
function parseLocalDate(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  if (!match) return null;
  return new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3]));
}

/** "Nov 2 – 8" within one month, "Oct 27 – Nov 2" across months. */
function formatWeekRange(weekStart: string, weekEnd: string): string | null {
  const start = parseLocalDate(weekStart);
  const end = parseLocalDate(weekEnd);
  if (!start || !end) return null;
  const month = (d: Date) => d.toLocaleDateString(undefined, { month: 'short' });
  const endLabel = start.getMonth() === end.getMonth() ? `${end.getDate()}` : `${month(end)} ${end.getDate()}`;
  return `${month(start)} ${start.getDate()} – ${endLabel}`;
}

const TREND_STYLE: Record<RecapTrend, { glyph: string; className: string; label: string }> = {
  up: { glyph: '↑', className: 'text-hr-zone-1', label: 'Above the three-week average' },
  down: { glyph: '↓', className: 'text-danger', label: 'Below the three-week average' },
  flat: { glyph: '–', className: 'text-muted', label: 'Level with the three-week average' },
};

function TrendArrow({ trend }: { trend: RecapTrend | null }) {
  if (!trend) return null;
  const style = TREND_STYLE[trend];
  return (
    <span className={`text-sm font-bold ${style.className}`} title={style.label} role="img" aria-label={style.label}>
      {style.glyph}
    </span>
  );
}

function RecapTile({ label, value, trend }: { label: string; value: string; trend: RecapTrend | null }) {
  return (
    <div className="min-w-0">
      <div className="text-xs text-muted mb-1">{label}</div>
      <div className="flex items-baseline gap-1.5 min-w-0">
        <span className="text-lg font-bold text-ink tabular-nums truncate">{value}</span>
        <TrendArrow trend={trend} />
      </div>
    </div>
  );
}

/** Z1–Z5 current-week seconds, or null when the week has no heart-rate series. */
function currentZoneTimes(metrics: WeeklyRecapResponse['metrics']): ZoneTime[] | null {
  const raw = [
    metrics.zoneSecondsZ1?.current,
    metrics.zoneSecondsZ2?.current,
    metrics.zoneSecondsZ3?.current,
    metrics.zoneSecondsZ4?.current,
    metrics.zoneSecondsZ5?.current,
  ];
  if (raw.every((v) => v == null)) return null;
  const zones = raw.map((v, i) => ({ zone: i + 1, timeS: Math.max(0, Math.round(v ?? 0)) }));
  return zones.some((z) => z.timeS > 0) ? zones : null;
}

function RecapTiles({ recap, unitPreference }: { recap: WeeklyRecapResponse; unitPreference: UnitPreference }) {
  const { metrics } = recap;
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-2 gap-x-4 gap-y-3">
      <RecapTile
        label="Distance"
        value={formatDistance(metrics.distanceM.current, unitPreference)}
        trend={recapTrend(metrics.distanceM.current, metrics.distanceM.trailingAvg)}
      />
      <RecapTile
        label="Runs"
        value={String(Math.round(metrics.runs.current))}
        trend={recapTrend(metrics.runs.current, metrics.runs.trailingAvg)}
      />
      <RecapTile
        label="Time"
        value={formatDuration(Math.round(metrics.durationS.current))}
        trend={recapTrend(metrics.durationS.current, metrics.durationS.trailingAvg)}
      />
      <RecapTile
        label="Elevation"
        value={formatElevation(metrics.elevationGainM.current, unitPreference)}
        trend={recapTrend(metrics.elevationGainM.current, metrics.elevationGainM.trailingAvg)}
      />
    </div>
  );
}

function TilesSkeleton() {
  return (
    <div className="grid grid-cols-2 sm:grid-cols-4 md:grid-cols-2 gap-x-4 gap-y-3" aria-hidden>
      {[0, 1, 2, 3].map((i) => (
        <div key={i} className="space-y-1.5">
          <div className="h-3 w-14 rounded bg-canvas animate-pulse" />
          <div className="h-6 w-20 rounded bg-canvas animate-pulse" />
        </div>
      ))}
    </div>
  );
}

function InlineError({ message, onRetry, retrying }: { message: string; onRetry: () => void; retrying: boolean }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-2 rounded-tempo border border-border bg-canvas px-3 py-2">
      <p className="text-sm text-danger">{message}</p>
      <Button variant="secondary" size="sm" onClick={onRetry} disabled={retrying}>
        {retrying ? 'Retrying…' : 'Retry'}
      </Button>
    </div>
  );
}

const DAY_LABELS = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];

function DailyBars({ dailyMiles, unitPreference }: { dailyMiles: number[]; unitPreference: UnitPreference }) {
  // Calculate current day of week (0=Monday, 6=Sunday)
  const currentDayOfWeek = (new Date().getDay() + 6) % 7;
  const convertMiles = (miles: number) => (unitPreference === 'metric' ? miles * 1.609344 : miles);
  const dailyValues = DAY_LABELS.map((_, i) => convertMiles(Number(dailyMiles[i]) || 0));
  const maxValue = Math.max(...dailyValues, 0.1); // Minimum 0.1 to avoid division by zero
  const scaleMax = maxValue * 1.1; // Scale goes 10% higher than max value

  return (
    <div className="relative pt-2">
      <div className="flex justify-between items-end gap-1" style={{ height: '80px', marginBottom: '24px' }}>
        {DAY_LABELS.map((day, index) => {
          const value = dailyValues[index];
          const barHeightPercent = scaleMax > 0 ? (value / scaleMax) * 100 : 0;
          // Ensure minimum 5% height for any non-zero value to make small bars visible
          const barHeight = value > 0 ? Math.max(barHeightPercent, 5) : 0;
          return (
            <div key={index} className="flex flex-col items-center justify-end relative flex-1" style={{ height: '100%' }}>
              <div
                className="w-3/4 bg-ink dark:bg-volt rounded-t"
                style={{ height: `${barHeight}%`, minHeight: value > 0 ? '4px' : '0' }}
              />
            </div>
          );
        })}
      </div>
      <div className="flex justify-between items-center relative">
        {DAY_LABELS.map((day, index) => (
          <div key={index} className="flex flex-col items-center relative flex-1">
            <span className="text-sm font-medium text-ink">{day}</span>
            {index === currentDayOfWeek && (
              <div className="absolute top-5 w-0 h-0 border-l-[6px] border-r-[6px] border-b-[8px] border-l-transparent border-r-transparent border-b-volt"></div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function DailyBarsSkeleton() {
  return (
    <div className="pt-2" aria-hidden>
      <div className="h-[80px] mb-6 rounded bg-canvas animate-pulse" />
      <div className="h-5" />
    </div>
  );
}

function errorMessage(error: unknown, fallback: string): string {
  return error instanceof Error && error.message ? `${fallback} (${error.message})` : fallback;
}

/** Dashboard Weekly Recap: this week's four tiles against the trailing average, daily bars, runs by type and zone time. */
export default function WeeklyRecapCard() {
  // Get timezone offset in minutes (negative for timezones ahead of UTC)
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const { unitPreference } = useSettings();

  const recapQuery = useQuery({
    queryKey: ['weeklyRecap', timezoneOffsetMinutes],
    queryFn: () => getWeeklyRecap(timezoneOffsetMinutes),
  });

  const weeklyQuery = useQuery({
    queryKey: ['weeklyStats', timezoneOffsetMinutes],
    queryFn: () => getWeeklyStats(timezoneOffsetMinutes),
  });

  const { data: yearlyData } = useQuery({
    queryKey: ['yearlyStats', timezoneOffsetMinutes],
    queryFn: () => getYearlyStats(timezoneOffsetMinutes),
  });

  const recap = recapQuery.data;
  const weekStart = recap?.weekStart ?? weeklyQuery.data?.weekStart;
  const weekEnd = recap?.weekEnd ?? weeklyQuery.data?.weekEnd;
  const weekRange = weekStart && weekEnd ? formatWeekRange(weekStart, weekEnd) : null;
  const weekday = recapThroughWeekday(recap?.currentWeekIsPartial);
  const subtitle = [weekRange, weekday ? `through ${weekday}` : null].filter(Boolean).join(' · ');

  const typeLine = recap ? runsByTypeLine(recap.metrics.runsByType) : null;
  const typeLineFull = recap ? runsByTypeFullLine(recap.metrics.runsByType) : null;
  const zoneTimes = recap ? currentZoneTimes(recap.metrics) : null;

  return (
    <Card>
      <div className="flex items-start justify-between gap-3 mb-4">
        <div className="min-w-0">
          <h2 className="text-lg font-semibold text-ink">This Week</h2>
          {subtitle && <p className="text-xs text-muted mt-0.5">{subtitle}</p>}
        </div>
        {yearlyData && (
          <div className="text-right shrink-0">
            <div className="text-sm font-semibold text-ink tabular-nums">
              {formatDistance(yearlyData.currentYear * METERS_PER_MILE, unitPreference)}
            </div>
            <div className="text-xs text-muted">{new Date().getFullYear()} total</div>
          </div>
        )}
      </div>

      <div className="space-y-5">
        {recapQuery.isPending ? (
          <TilesSkeleton />
        ) : recapQuery.isError ? (
          <InlineError
            message={errorMessage(recapQuery.error, 'Could not load the weekly recap.')}
            onRetry={() => recapQuery.refetch()}
            retrying={recapQuery.isFetching}
          />
        ) : recap ? (
          <div className="space-y-3">
            <RecapTiles recap={recap} unitPreference={unitPreference} />
            {typeLine && (
              <p className="text-xs text-muted truncate" title={typeLineFull ?? undefined}>
                <span aria-hidden>{typeLine}</span>
                <span className="sr-only">Runs by type: {typeLineFull}</span>
              </p>
            )}
          </div>
        ) : null}

        {weeklyQuery.isPending ? (
          <DailyBarsSkeleton />
        ) : weeklyQuery.isError ? (
          <InlineError
            message={errorMessage(weeklyQuery.error, 'Could not load daily distance.')}
            onRetry={() => weeklyQuery.refetch()}
            retrying={weeklyQuery.isFetching}
          />
        ) : weeklyQuery.data ? (
          <DailyBars dailyMiles={weeklyQuery.data.dailyMiles} unitPreference={unitPreference} />
        ) : null}

        {zoneTimes && (
          <div>
            <h3 className="text-xs font-medium text-muted mb-2 uppercase tracking-wide">Time in Zone</h3>
            <HeartRateZoneTimeBar zoneTimes={zoneTimes} />
          </div>
        )}
      </div>
    </Card>
  );
}
