import Link from 'next/link';
import {
  IconCloudRain,
  IconCloudSnow,
  IconCloudStorm,
  IconDroplet,
  IconMist,
  IconSnowflake,
  IconSun,
  IconWind,
  type Icon,
} from '@tabler/icons-react';
import type { InsightsWeather } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';
import { formatDate, getWorkoutDisplayName } from '@/lib/format';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

type Tile = {
  key: string;
  label: string;
  icon: Icon;
  value: string;
  /** Short qualifier shown beside the value, e.g. the wind direction. */
  detail?: string;
  /** True when the value is a phrase rather than a number, so it is set smaller and may wrap. */
  isPhrase?: boolean;
  workoutId: string;
  workoutName?: string | null;
  date: string;
};

/** The server sends precipitation in millimetres regardless of unit preference. */
function formatPrecipitation(mm: number, unit: UnitPreference): string {
  if (unit === 'imperial') {
    return `${(mm / 25.4).toFixed(2)} in`;
  }
  return `${mm.toFixed(1)} mm`;
}

/** Short names for the WMO codes behind the condition tiles; other codes keep the server's wording. */
const SHORT_CONDITIONS: Record<number, string> = {
  45: 'Fog',
  48: 'Rime fog',
  71: 'Light snow',
  73: 'Snow',
  75: 'Heavy snow',
  77: 'Snow grains',
  85: 'Snow showers',
  86: 'Heavy snow showers',
  95: 'Thunderstorm',
  96: 'Thunderstorm, hail',
  99: 'Thunderstorm, heavy hail',
};

function shortCondition(weatherCode: number, condition: string): string {
  return SHORT_CONDITIONS[weatherCode] ?? condition;
}

/** Tiles in the iOS order; extremes the server returned as null are left out. */
function buildTiles(weather: InsightsWeather, unit: UnitPreference): Tile[] {
  const tiles: Tile[] = [];
  const { coldest, hottest, windiest, mostHumid, wettest, mostEpic, foggiest, snowiest } = weather;

  if (coldest) {
    tiles.push({
      ...coldest,
      key: 'coldest',
      label: 'Coldest',
      icon: IconSnowflake,
      value: `${Math.round(coldest.temperature)}${coldest.temperatureUnit}`,
    });
  }
  if (hottest) {
    tiles.push({
      ...hottest,
      key: 'hottest',
      label: 'Hottest',
      icon: IconSun,
      value: `${Math.round(hottest.temperature)}${hottest.temperatureUnit}`,
    });
  }
  if (windiest) {
    tiles.push({
      ...windiest,
      key: 'windiest',
      label: 'Windiest',
      icon: IconWind,
      value: `${Math.round(windiest.windSpeed)} ${windiest.windSpeedUnit}`,
      detail: windiest.windDirectionCardinal ?? undefined,
    });
  }
  if (mostHumid) {
    tiles.push({
      ...mostHumid,
      key: 'mostHumid',
      label: 'Most humid',
      icon: IconDroplet,
      value: `${Math.round(mostHumid.humidity)}%`,
    });
  }
  if (wettest) {
    tiles.push({
      ...wettest,
      key: 'wettest',
      label: 'Wettest',
      icon: IconCloudRain,
      value: formatPrecipitation(wettest.precipitation, unit),
    });
  }
  if (mostEpic) {
    tiles.push({
      ...mostEpic,
      key: 'mostEpic',
      label: 'Wildest',
      icon: IconCloudStorm,
      value: shortCondition(mostEpic.weatherCode, mostEpic.condition),
      isPhrase: true,
    });
  }
  if (foggiest) {
    tiles.push({
      ...foggiest,
      key: 'foggiest',
      label: 'Foggiest',
      icon: IconMist,
      value: shortCondition(foggiest.weatherCode, foggiest.condition),
      isPhrase: true,
    });
  }
  if (snowiest) {
    tiles.push({
      ...snowiest,
      key: 'snowiest',
      label: 'Snowiest',
      icon: IconCloudSnow,
      value: shortCondition(snowiest.weatherCode, snowiest.condition),
      isPhrase: true,
      detail:
        snowiest.precipitation != null && snowiest.precipitation > 0
          ? formatPrecipitation(snowiest.precipitation, unit)
          : undefined,
    });
  }

  return tiles;
}

export function WeatherExtremesCard({
  weather,
  unitPreference,
  className,
}: {
  weather: InsightsWeather | null | undefined;
  unitPreference: UnitPreference;
  className?: string;
}) {
  const tiles = weather ? buildTiles(weather, unitPreference) : [];

  return (
    <Card className={className}>
      <h2 className="text-lg font-semibold text-ink mb-4">Weather Extremes</h2>
      {tiles.length === 0 ? (
        <EmptyState
          title="Not enough weather data yet"
          description="Weather extremes appear once enough of your runs have weather recorded."
        />
      ) : (
        <ul className="grid auto-rows-fr grid-cols-2 gap-3">
          {tiles.map((tile) => {
            const TileIcon = tile.icon;
            return (
              <li key={tile.key}>
                <Link
                  href={`/dashboard/${tile.workoutId}`}
                  className="flex h-full flex-col rounded-tempo border border-border p-3 transition-colors hover:border-ink dark:hover:border-volt focus:outline-none focus-visible:ring-2 focus-visible:ring-ink dark:focus-visible:ring-volt"
                >
                  <span className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted">
                    <TileIcon size={16} stroke={1.75} aria-hidden="true" className="shrink-0" />
                    {tile.label}
                  </span>
                  <span className="mt-1.5 flex flex-wrap items-baseline gap-x-1.5">
                    <span
                      className={
                        tile.isPhrase
                          ? 'text-base font-semibold leading-snug text-ink'
                          : 'text-2xl font-semibold text-ink tabular-nums'
                      }
                    >
                      {tile.value}
                    </span>
                    {tile.detail ? <span className="text-xs text-muted tabular-nums">{tile.detail}</span> : null}
                  </span>
                  <span className="mt-auto block truncate pt-2 text-xs text-muted">
                    <span className="text-ink">{getWorkoutDisplayName(tile.workoutName ?? null, tile.date)}</span>
                    {' · '}
                    {formatDate(tile.date)}
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}
