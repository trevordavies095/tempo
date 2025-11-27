/**
 * Date formatting utilities
 */

/**
 * Format activity date from ISO string to readable format
 * @param dateString ISO date string
 * @returns Formatted string like "Mon, 1/15/2024"
 */
export function formatActivityDate(dateString: string): string {
  const date = new Date(dateString);
  const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
  const dayName = days[date.getDay()];
  const month = date.getMonth() + 1;
  const day = date.getDate();
  const year = date.getFullYear();
  return `${dayName}, ${month}/${day}/${year}`;
}

/**
 * Format date range from two ISO date strings
 * @param weekStart ISO date string of week start
 * @param weekEnd ISO date string of week end
 * @returns Formatted string like "Jan 15, 2024 - Jan 21, 2024"
 */
export function formatDateRange(weekStart: string, weekEnd: string): string {
  const start = new Date(weekStart);
  const end = new Date(weekEnd);
  return `${start.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })} - ${end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`;
}

/**
 * Format overall date range from two ISO date strings
 * @param dateStart ISO date string of range start
 * @param dateEnd ISO date string of range end
 * @returns Formatted string like "Jan 15, 2024 - Jan 21, 2024"
 */
export function formatOverallDateRange(dateStart: string, dateEnd: string): string {
  const start = new Date(dateStart);
  const end = new Date(dateEnd);
  return `${start.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })} - ${end.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}`;
}

