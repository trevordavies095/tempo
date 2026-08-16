'use client';

import Image from 'next/image';
import { useSettings } from '@/lib/settings';
import {
  type WeatherData,
  getWeatherSymbol,
  formatTemperature,
  formatWindSpeed,
  formatWindDirection,
  getFeelsLikeTemperature,
  getHumidity,
  isNightTime,
} from '@/lib/weather';
import { Card } from '@/components/ui/Card';

interface WeatherDisplayProps {
  weather: WeatherData;
  workoutStartTime?: string;
  /** Skip Card chrome when nested in another overview surface. */
  embedded?: boolean;
}

export function WeatherDisplay({
  weather,
  workoutStartTime,
  embedded = false,
}: WeatherDisplayProps) {
  const { unitPreference } = useSettings();

  const isNight = workoutStartTime ? isNightTime(workoutStartTime) : false;
  const symbolFilename = getWeatherSymbol(weather.weatherCode, isNight);
  const symbolPath = `/weather-symbols/${symbolFilename}`;
  const conditionText = weather.condition || 'Unknown';
  const feelsLike = getFeelsLikeTemperature(weather);
  const humidity = getHumidity(weather);

  const body = (
    <>
      <h3
        className={
          embedded
            ? 'text-xs font-medium text-muted mb-2 uppercase tracking-wide'
            : 'text-lg font-semibold text-ink mb-3'
        }
      >
        Weather
      </h3>

      <div className="flex items-center gap-3 mb-3 min-w-0">
        <div className="relative w-12 h-12 shrink-0">
          <Image
            src={symbolPath}
            alt={conditionText}
            fill
            className="object-contain"
            unoptimized
          />
        </div>
        <p className="text-sm font-medium text-ink min-w-0">{conditionText}</p>
      </div>

      <dl className="space-y-1.5 min-w-0">
        <div className="flex justify-between items-baseline gap-3">
          <dt className="text-xs text-muted shrink-0">Temperature</dt>
          <dd className="text-sm font-semibold text-ink text-right">
            {formatTemperature(weather.temperature, unitPreference)}
          </dd>
        </div>
        <div className="flex justify-between items-baseline gap-3">
          <dt className="text-xs text-muted shrink-0">Humidity</dt>
          <dd className="text-sm font-semibold text-ink text-right">
            {humidity !== undefined && humidity !== null
              ? `${Math.round(humidity)}%`
              : 'N/A'}
          </dd>
        </div>
        <div className="flex justify-between items-baseline gap-3">
          <dt className="text-xs text-muted shrink-0">Feels like</dt>
          <dd className="text-sm font-semibold text-ink text-right">
            {formatTemperature(feelsLike, unitPreference)}
          </dd>
        </div>
        <div className="flex justify-between items-baseline gap-3">
          <dt className="text-xs text-muted shrink-0">Wind</dt>
          <dd className="text-sm font-semibold text-ink text-right">
            {formatWindSpeed(weather.windSpeed, unitPreference)}
            {weather.windDirection !== undefined && weather.windDirection !== null
              ? ` ${formatWindDirection(weather.windDirection)}`
              : ''}
          </dd>
        </div>
      </dl>
    </>
  );

  if (embedded) {
    return <div>{body}</div>;
  }

  return <Card>{body}</Card>;
}
