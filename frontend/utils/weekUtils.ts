/**
 * Utilities for calculating week boundaries and week numbers
 * based on the 52-week rolling window system.
 * 
 * Week 52 is the most recent complete week (ending on Sunday).
 * Week 1 starts 51 weeks before week 52.
 */

export interface WeekRange {
  weekStart: string; // ISO date string (YYYY-MM-DD)
  weekEnd: string;   // ISO date string (YYYY-MM-DD)
}

/**
 * Gets the boundaries of week 52 (the most recent complete week).
 * Week 52 ends on the most recent Sunday, and starts on the Monday before that.
 * 
 * @returns Object with week52Start and week52End dates
 */
function getWeek52Boundaries(): { week52Start: Date; week52End: Date } {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const daysSinceMonday = ((today.getDay() - 1 + 7) % 7);
  const mostRecentMonday = new Date(today);
  mostRecentMonday.setDate(today.getDate() - daysSinceMonday);
  
  const week52End = new Date(mostRecentMonday);
  week52End.setDate(mostRecentMonday.getDate() + 6);
  if (daysSinceMonday === 0) {
    // If today is Monday, week 52 ended yesterday (last Sunday)
    week52End.setDate(mostRecentMonday.getDate() - 1);
  }
  
  const week52Start = new Date(week52End);
  week52Start.setDate(week52End.getDate() - 6);
  
  return { week52Start, week52End };
}

/**
 * Calculates the week number (1-52) for a given week start date.
 * Week 1 starts 51 weeks before week 52.
 * 
 * @param weekStartDate - The start date of the week
 * @returns Week number (1-52) or null if date is outside the 52-week window
 */
export function calculateWeekNumber(weekStartDate: Date): number | null {
  const { week52Start } = getWeek52Boundaries();
  
  // Week 1 starts 51 weeks before week 52
  const week1Start = new Date(week52Start);
  week1Start.setDate(week52Start.getDate() - 51 * 7);
  
  // Calculate which week number (1-52) the given week is
  const diffTime = weekStartDate.getTime() - week1Start.getTime();
  const diffDays = Math.floor(diffTime / (1000 * 60 * 60 * 24));
  const weekNum = Math.floor(diffDays / 7) + 1;
  
  // Clamp to valid range
  if (weekNum < 1 || weekNum > 52) {
    return null;
  }
  
  return weekNum;
}

/**
 * Generates an interval string (YYYYWW) and year offset for a given week.
 * Used for creating URL hash parameters.
 * 
 * @param weekStart - ISO date string of the week start
 * @returns Object with interval string and yearOffset, or null if invalid
 */
export function generateIntervalFromWeek(weekStart: string): { interval: string; yearOffset: number } | null {
  const weekStartDate = new Date(weekStart);
  const year = weekStartDate.getFullYear();
  
  const weekNum = calculateWeekNumber(weekStartDate);
  if (weekNum === null) {
    return null;
  }
  
  const interval = `${year}${Math.min(52, Math.max(1, weekNum)).toString().padStart(2, '0')}`;
  const yearOffset = new Date().getFullYear() - year;
  
  return { interval, yearOffset };
}

