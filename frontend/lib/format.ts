/**
 * Format distance from meters to miles
 * @param meters Distance in meters
 * @returns Formatted string like "6.5 mi"
 */
export function formatDistance(meters: number): string {
  const miles = meters / 1609.344;
  return `${miles.toFixed(2)} mi`;
}

/**
 * Format duration from seconds to HH:MM:SS
 * @param seconds Duration in seconds
 * @returns Formatted string like "1:30:45"
 */
export function formatDuration(seconds: number): string {
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const secs = seconds % 60;

  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
  }
  return `${minutes}:${secs.toString().padStart(2, '0')}`;
}

/**
 * Format pace from seconds per km (stored in DB) to M:SS /mi (displayed in imperial)
 * @param secondsPerKm Pace in seconds per kilometer (from database)
 * @returns Formatted string like "8:30 /mi"
 */
export function formatPace(secondsPerKm: number): string {
  // Convert from seconds/km to seconds/mile
  const secondsPerMile = secondsPerKm * 1.609344;
  const minutes = Math.floor(secondsPerMile / 60);
  const seconds = Math.floor(secondsPerMile % 60);
  return `${minutes}:${seconds.toString().padStart(2, '0')} /mi`;
}

/**
 * Format date from ISO string to readable format
 * @param dateString ISO date string
 * @returns Formatted string like "Jan 15, 2024"
 */
export function formatDate(dateString: string): string {
  const date = new Date(dateString);
  return date.toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });
}

/**
 * Format date and time from ISO string
 * @param dateString ISO date string
 * @returns Formatted string like "Jan 15, 2024 at 10:30 AM"
 */
export function formatDateTime(dateString: string): string {
  const date = new Date(dateString);
  return date.toLocaleString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
    hour12: true,
  });
}

/**
 * Format elevation from meters to feet
 * @param meters Elevation in meters
 * @returns Formatted string like "492 ft"
 */
export function formatElevation(meters: number): string {
  const feet = meters * 3.28084;
  return `${Math.round(feet)} ft`;
}

/**
 * Get workout display name, using time-based fallback if name is not provided
 * @param name Workout name (nullable)
 * @param startedAt ISO date string of workout start time
 * @returns Workout name or time-based name (Morning Run, Afternoon Run, Evening Run, Night Run)
 */
export function getWorkoutDisplayName(name: string | null, startedAt: string): string {
  // Return name if provided and not empty
  if (name && name.trim().length > 0) {
    return name;
  }

  // Determine time-based name from workout start time
  const date = new Date(startedAt);
  const hours = date.getHours();

  if (hours >= 5 && hours < 12) {
    return 'Morning Run';
  } else if (hours >= 12 && hours < 17) {
    return 'Afternoon Run';
  } else if (hours >= 17 && hours < 21) {
    return 'Evening Run';
  } else {
    // 21:00-4:59
    return 'Night Run';
  }
}

