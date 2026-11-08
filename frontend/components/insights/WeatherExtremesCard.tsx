import Link from 'next/link';
import type { InsightsWeather } from '@/lib/api';
import type { UnitPreference } from '@/lib/settings';
import { formatDate, getWorkoutDisplayName } from '@/lib/format';
import { Card } from '@/components/ui/Card';
import { EmptyState } from '@/components/ui/EmptyState';

type Tile = {
  key: string;
  label: string;
  value: string;
  detail?: string;
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

/** Tiles in the iOS order; extremes the server returned as null are left out. */
function buildTiles(weather: InsightsWeather, unit: UnitPreference): Tile[] {
  const tiles: Tile[] = [];
  const { coldest, hottest, windiest, mostHumid, wettest, mostEpic, foggiest, snowiest } = weather;

  if (coldest) {
    tiles.push({ key: 'coldest', label: 'Coldest', value: `${coldest.temperature}${coldest.temperatureUnit}`, ...coldest });
  }
  if (hottest) {
    tiles.push({ key: 'hottest', label: 'Hottest', value: `${hottest.temperature}${hottest.temperatureUnit}`, ...hottest });
  }
  if (windiest) {
    tiles.push({
      key: 'windiest',
      label: 'Windiest',
      value: `${windiest.windSpeed} ${windiest.windSpeedUnit}`,
      detail: windiest.windDirectionCardinal ? `From ${windiest.windDirectionCardinal}` : undefined,
      ...windiest,
    });
  }
  if (mostHumid) {
    tiles.push({ key: 'mostHumid', label: 'Most Humid', value: `${mostHumid.humidity}%`, ...mostHumid });
  }
  if (wettest) {
    tiles.push({ key: 'wettest', label: 'Wettest', value: formatPrecipitation(wettest.precipitation, unit), ...wettest });
  }
  if (mostEpic) {
    tiles.push({ key: 'mostEpic', label: 'Wildest Weather', value: mostEpic.condition, ...mostEpic });
  }
  if (foggiest) {
    tiles.push({ key: 'foggiest', label: 'Foggiest', value: foggiest.condition, ...foggiest });
  }
  if (snowiest) {
    tiles.push({
      key: 'snowiest',
      label: 'Snowiest',
      value: snowiest.condition,
      detail:
        snowiest.precipitation != null && snowiest.precipitation > 0
          ? formatPrecipitation(snowiest.precipitation, unit)
          : undefined,
      ...snowiest,
    });
  }

  return tiles;
}

export function WeatherExtremesCard({
  weather,
  unitPreference,
}: {
  weather: InsightsWeather | null | undefined;
  unitPreference: UnitPreference;
}) {
  const tiles = weather ? buildTiles(weather, unitPreference) : [];

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Weather Extremes</h2>
      {tiles.length === 0 ? (
        <EmptyState
          title="Not enough weather data yet"
          description="Weather extremes appear once enough of your runs have weather recorded."
        />
      ) : (
        <ul className="grid grid-cols-1 gap-3 sm:grid-cols-2 md:grid-cols-4">
          {tiles.map((tile) => (
            <li key={tile.key}>
              <Link
                href={`/dashboard/${tile.workoutId}`}
                className="flex h-full flex-col gap-1 rounded-tempo border border-border bg-canvas p-4 transition-colors hover:border-ink dark:hover:border-volt focus:outline-none focus:ring-2 focus:ring-volt"
              >
                <span className="text-xs font-medium uppercase tracking-wide text-muted">{tile.label}</span>
                <span className="text-xl font-semibold text-ink tabular-nums">{tile.value}</span>
                {tile.detail ? <span className="text-xs text-muted">{tile.detail}</span> : null}
                <span className="mt-auto pt-2 text-sm text-ink truncate">
                  {getWorkoutDisplayName(tile.workoutName ?? null, tile.date)}
                </span>
                <span className="text-xs text-muted">{formatDate(tile.date)}</span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}
