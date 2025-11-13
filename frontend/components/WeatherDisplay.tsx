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

interface WeatherDisplayProps {
  weather: WeatherData;
  workoutStartTime?: string; // ISO date string to determine day/night
}

export function WeatherDisplay({ weather, workoutStartTime }: WeatherDisplayProps) {
  const { unitPreference } = useSettings();

  // Determine if it's night time for symbol selection
  const isNight = workoutStartTime ? isNightTime(workoutStartTime) : false;
  const symbolFilename = getWeatherSymbol(weather.weatherCode, isNight);
  const symbolPath = `/weather-symbols/${symbolFilename}`;

  // Get condition text (prefer condition field, fallback to weatherCode description)
  const conditionText = weather.condition || 'Unknown';

  // Get feels like temperature
  const feelsLike = getFeelsLikeTemperature(weather);

  // Get normalized humidity (handles both humidity and relativeHumidity fields)
  const humidity = getHumidity(weather);

  return (
    <div className="bg-white dark:bg-gray-900 p-6 rounded-lg border border-gray-200 dark:border-gray-800">
      <h2 className="text-xl font-semibold text-gray-900 dark:text-gray-100 mb-4">
        Weather
      </h2>

      <div className="flex flex-col md:flex-row gap-6">
        {/* Weather Symbol and Condition */}
        <div className="flex flex-col items-center md:items-start">
          <div className="relative w-24 h-24 mb-3">
            <Image
              src={symbolPath}
              alt={conditionText}
              fill
              className="object-contain"
              unoptimized // Weather symbols are small PNGs, no need for optimization
            />
          </div>
          <div className="text-center md:text-left">
            <p className="text-lg font-medium text-gray-900 dark:text-gray-100">
              {conditionText}
            </p>
          </div>
        </div>

        {/* Weather Data Grid */}
        <div className="flex-1 grid grid-cols-2 gap-4">
          {/* Temperature */}
          <div className="flex flex-col">
            <span className="text-sm text-gray-600 dark:text-gray-400 mb-1">Temperature</span>
            <span className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {formatTemperature(weather.temperature, unitPreference)}
            </span>
          </div>

          {/* Humidity */}
          <div className="flex flex-col">
            <span className="text-sm text-gray-600 dark:text-gray-400 mb-1">Humidity</span>
            <span className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {humidity !== undefined && humidity !== null
                ? `${Math.round(humidity)}%`
                : 'N/A'}
            </span>
          </div>

          {/* Feels Like */}
          <div className="flex flex-col">
            <span className="text-sm text-gray-600 dark:text-gray-400 mb-1">Feels like</span>
            <span className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {formatTemperature(feelsLike, unitPreference)}
            </span>
          </div>

          {/* Wind Speed */}
          <div className="flex flex-col">
            <span className="text-sm text-gray-600 dark:text-gray-400 mb-1">Wind Speed</span>
            <span className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {formatWindSpeed(weather.windSpeed, unitPreference)}
            </span>
          </div>

          {/* Wind Direction */}
          <div className="flex flex-col col-span-2">
            <span className="text-sm text-gray-600 dark:text-gray-400 mb-1">Wind Direction</span>
            <span className="text-lg font-semibold text-gray-900 dark:text-gray-100">
              {weather.windDirection !== undefined && weather.windDirection !== null
                ? `${formatWindDirection(weather.windDirection)} (${Math.round(weather.windDirection)}°)`
                : 'N/A'}
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}

