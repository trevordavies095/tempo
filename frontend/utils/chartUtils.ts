/**
 * Chart utility functions for calculating Y-axis ticks and ranges
 */

/**
 * Rounds a number to a "nice" value for chart display
 * @param num - Number to round
 * @returns Rounded number (rounds to nearest 10, 50, or 100 depending on magnitude)
 */
export function roundToNice(num: number): number {
  if (num <= 50) return Math.ceil(num / 10) * 10;
  if (num <= 200) return Math.ceil(num / 50) * 50;
  return Math.ceil(num / 100) * 100;
}

/**
 * Calculates Y-axis ticks for a chart given a maximum value
 * @param maxValue - Maximum value to display
 * @param tickCount - Desired number of ticks (default: 3)
 * @returns Array of tick values including 0
 */
export function calculateYAxisTicks(maxValue: number, tickCount: number = 3): number[] {
  // Calculate max value for Y-axis (add some padding)
  const yAxisMax = Math.ceil(maxValue * 1.2);
  const niceMax = roundToNice(yAxisMax);

  // Generate ticks that include 0 and divide the range nicely
  const yAxisTicks = [0];

  if (niceMax <= 100) {
    // For smaller ranges, use increments of 25 or 50
    const step = niceMax <= 50 ? 25 : 50;
    for (let val = step; val <= niceMax; val += step) {
      yAxisTicks.push(val);
    }
  } else {
    // For larger ranges, divide into equal parts with nice numbers
    const step = roundToNice(niceMax / tickCount);
    for (let val = step; val <= niceMax; val += step) {
      yAxisTicks.push(val);
    }
  }

  // Ensure we include the actual max value if it's not already in ticks
  // This helps align data points with Y-axis labels
  if (maxValue > 0 && !yAxisTicks.includes(maxValue)) {
    // Find the closest tick and add the actual value if it's significantly different
    const closestTick = yAxisTicks.reduce((prev, curr) =>
      Math.abs(curr - maxValue) < Math.abs(prev - maxValue) ? curr : prev
    );
    if (Math.abs(closestTick - maxValue) > maxValue * 0.1) {
      // Add the actual max value if it's more than 10% different from closest tick
      yAxisTicks.push(maxValue);
      yAxisTicks.sort((a, b) => a - b);
    }
  }

  // Ensure we have at least 3 ticks including 0
  if (yAxisTicks.length < 3) {
    const step = niceMax / 2;
    yAxisTicks.length = 1; // Keep 0
    yAxisTicks.push(Math.round(step));
    yAxisTicks.push(niceMax);
  }

  return yAxisTicks;
}

/**
 * Calculates the maximum Y-axis value for a chart
 * @param maxValue - Maximum data value
 * @returns Rounded maximum value for Y-axis
 */
export function calculateYAxisMax(maxValue: number): number {
  const yAxisMax = Math.ceil(maxValue * 1.2);
  return roundToNice(yAxisMax);
}

