import type { UnitPreference } from './settings';

/**
 * Weather data interface matching the structure from the API
 */
export interface WeatherData {
  source?: string;
  temperature?: number;
  condition?: string;
  humidity?: number;
  temperatureFeelsLike?: number;
  apparentTemperature?: number;
  windSpeed?: number;
  windDirection?: number;
  weatherCode?: number;
  pressure?: number;
  precipitation?: number;
  windGust?: number;
  precipitationProbability?: number;
  [key: string]: any; // Allow other fields from different sources
}

/**
 * Determine if a given datetime represents night time (6 PM to 6 AM)
 * @param dateTime ISO date string
 * @returns true if night time, false if day time
 */
export function isNightTime(dateTime: string): boolean {
  const date = new Date(dateTime);
  const hours = date.getHours();
  // Night time: 18:00 (6 PM) to 05:59 (5:59 AM)
  return hours >= 18 || hours < 6;
}

/**
 * Map WMO weather code to weather symbol filename
 * @param weatherCode WMO weather code (0-99)
 * @param isNight Whether it's night time
 * @returns Weather symbol filename
 */
export function getWeatherSymbol(weatherCode: number | undefined, isNight: boolean = false): string {
  if (weatherCode === undefined) {
    return 'wsymbol_0999_unknown.png';
  }

  // Map WMO codes to symbol files
  const symbolMap: Record<number, { day: string; night: string }> = {
    0: { day: 'wsymbol_0001_sunny.png', night: 'wsymbol_0008_clear_sky_night.png' },
    1: { day: 'wsymbol_0002_sunny_intervals.png', night: 'wsymbol_0041_partly_cloudy_night.png' },
    2: { day: 'wsymbol_0002_sunny_intervals.png', night: 'wsymbol_0041_partly_cloudy_night.png' },
    3: { day: 'wsymbol_0003_white_cloud.png', night: 'wsymbol_0042_cloudy_night.png' },
    // Code 5 (if used by Strava) - map to drizzle as it's between clear and rain
    5: { day: 'wsymbol_0048_drizzle.png', night: 'wsymbol_0066_drizzle_night.png' },
    45: { day: 'wsymbol_0007_fog.png', night: 'wsymbol_0064_fog_night.png' },
    48: { day: 'wsymbol_0007_fog.png', night: 'wsymbol_0064_fog_night.png' },
    51: { day: 'wsymbol_0048_drizzle.png', night: 'wsymbol_0066_drizzle_night.png' },
    53: { day: 'wsymbol_0048_drizzle.png', night: 'wsymbol_0066_drizzle_night.png' },
    55: { day: 'wsymbol_0048_drizzle.png', night: 'wsymbol_0066_drizzle_night.png' },
    56: { day: 'wsymbol_0050_freezing_rain.png', night: 'wsymbol_0068_freezing_rain_night.png' },
    57: { day: 'wsymbol_0050_freezing_rain.png', night: 'wsymbol_0068_freezing_rain_night.png' },
    61: { day: 'wsymbol_0009_light_rain_showers.png', night: 'wsymbol_0025_light_rain_showers_night.png' },
    63: { day: 'wsymbol_0018_cloudy_with_heavy_rain.png', night: 'wsymbol_0034_cloudy_with_heavy_rain_night.png' },
    65: { day: 'wsymbol_0018_cloudy_with_heavy_rain.png', night: 'wsymbol_0034_cloudy_with_heavy_rain_night.png' },
    66: { day: 'wsymbol_0050_freezing_rain.png', night: 'wsymbol_0068_freezing_rain_night.png' },
    67: { day: 'wsymbol_0050_freezing_rain.png', night: 'wsymbol_0068_freezing_rain_night.png' },
    71: { day: 'wsymbol_0011_light_snow_showers.png', night: 'wsymbol_0027_light_snow_showers_night.png' },
    73: { day: 'wsymbol_0020_cloudy_with_heavy_snow.png', night: 'wsymbol_0036_cloudy_with_heavy_snow_night.png' },
    75: { day: 'wsymbol_0020_cloudy_with_heavy_snow.png', night: 'wsymbol_0036_cloudy_with_heavy_snow_night.png' },
    77: { day: 'wsymbol_0011_light_snow_showers.png', night: 'wsymbol_0027_light_snow_showers_night.png' },
    80: { day: 'wsymbol_0009_light_rain_showers.png', night: 'wsymbol_0025_light_rain_showers_night.png' },
    81: { day: 'wsymbol_0018_cloudy_with_heavy_rain.png', night: 'wsymbol_0034_cloudy_with_heavy_rain_night.png' },
    82: { day: 'wsymbol_0018_cloudy_with_heavy_rain.png', night: 'wsymbol_0034_cloudy_with_heavy_rain_night.png' },
    85: { day: 'wsymbol_0011_light_snow_showers.png', night: 'wsymbol_0027_light_snow_showers_night.png' },
    86: { day: 'wsymbol_0020_cloudy_with_heavy_snow.png', night: 'wsymbol_0036_cloudy_with_heavy_snow_night.png' },
    95: { day: 'wsymbol_0024_thunderstorms.png', night: 'wsymbol_0040_thunderstorms_night.png' },
    96: { day: 'wsymbol_0024_thunderstorms.png', night: 'wsymbol_0040_thunderstorms_night.png' },
    99: { day: 'wsymbol_0024_thunderstorms.png', night: 'wsymbol_0040_thunderstorms_night.png' },
  };

  const symbol = symbolMap[weatherCode];
  if (symbol) {
    return isNight ? symbol.night : symbol.day;
  }

  // Default fallback
  return 'wsymbol_0999_unknown.png';
}

/**
 * Format temperature from Celsius to display string
 * @param celsius Temperature in Celsius
 * @param unit Unit preference ('metric' or 'imperial')
 * @returns Formatted string like "15°C" or "59°F"
 */
export function formatTemperature(celsius: number | undefined, unit: UnitPreference = 'metric'): string {
  if (celsius === undefined || celsius === null) {
    return 'N/A';
  }

  if (unit === 'imperial') {
    const fahrenheit = (celsius * 9) / 5 + 32;
    return `${Math.round(fahrenheit)}°F`;
  } else {
    return `${Math.round(celsius)}°C`;
  }
}

/**
 * Format wind speed from m/s to display string
 * @param mps Wind speed in meters per second
 * @param unit Unit preference ('metric' or 'imperial')
 * @returns Formatted string like "10 m/s" or "22 mph"
 */
export function formatWindSpeed(mps: number | undefined, unit: UnitPreference = 'metric'): string {
  if (mps === undefined || mps === null) {
    return 'N/A';
  }

  if (unit === 'imperial') {
    const mph = mps * 2.237;
    return `${mph.toFixed(1)} mph`;
  } else {
    return `${mps.toFixed(1)} m/s`;
  }
}

/**
 * Format wind direction from degrees to compass direction
 * @param degrees Wind direction in degrees (0-360)
 * @returns Compass direction string like "N", "NE", "E", etc.
 */
export function formatWindDirection(degrees: number | undefined): string {
  if (degrees === undefined || degrees === null) {
    return 'N/A';
  }

  // Normalize to 0-360
  const normalized = ((degrees % 360) + 360) % 360;

  // Compass directions (16 points)
  const directions = [
    'N', 'NNE', 'NE', 'ENE',
    'E', 'ESE', 'SE', 'SSE',
    'S', 'SSW', 'SW', 'WSW',
    'W', 'WNW', 'NW', 'NNW'
  ];

  // Each direction covers 22.5 degrees (360 / 16)
  const index = Math.round(normalized / 22.5) % 16;
  return directions[index];
}

/**
 * Get feels like temperature (prefers temperatureFeelsLike, falls back to apparentTemperature)
 * @param weather Weather data object
 * @returns Feels like temperature in Celsius, or undefined
 */
export function getFeelsLikeTemperature(weather: WeatherData): number | undefined {
  return weather.temperatureFeelsLike ?? weather.apparentTemperature;
}

/**
 * Get normalized humidity value from weather data.
 * Handles both 'humidity' and 'relativeHumidity' field names.
 * Normalizes decimal values (0.0-1.0) to percentages (0-100).
 * @param weather Weather data object
 * @returns Humidity as percentage (0-100), or undefined
 */
export function getHumidity(weather: WeatherData): number | undefined {
  // Check both field names for backward compatibility
  let humidity = weather.humidity ?? (weather as any).relativeHumidity;
  
  if (humidity === undefined || humidity === null) {
    return undefined;
  }

  const humidityNum = typeof humidity === 'number' ? humidity : parseFloat(String(humidity));
  
  if (isNaN(humidityNum)) {
    return undefined;
  }

  // If value is between 0.0 and 1.0 (exclusive), it's likely a decimal format
  // Convert to percentage by multiplying by 100
  if (humidityNum > 0.0 && humidityNum <= 1.0) {
    return humidityNum * 100.0;
  }

  // If value is already in 0-100 range, return as-is
  // Clamp to valid range (0-100) for safety
  if (humidityNum < 0) {
    return 0;
  }
  if (humidityNum > 100) {
    return 100;
  }

  return humidityNum;
}

