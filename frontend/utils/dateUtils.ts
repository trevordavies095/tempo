/**
 * Date formatting utilities
 */

import { parseLocalDate } from '@/lib/localCalendar';

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
 * Format a range of two local `yyyy-MM-dd` dates
 * @param weekStart local date of week start
 * @param weekEnd local date of week end
 * @returns Formatted string like "Jan 15, 2024 - Jan 21, 2024"
 */
export function formatDateRange(weekStart: string, weekEnd: string): string {
  const format = (value: string) =>
    parseLocalDate(value)?.toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' }) ?? value;
  return `${format(weekStart)} - ${format(weekEnd)}`;
}

