# Understanding Analytics and Metrics

Learn about the metrics and analytics available in Tempo.

## Overview

Tempo provides comprehensive analytics to help you understand and improve your running performance. This guide explains all available metrics and how to interpret them.

## Workout Metrics

### Distance

- **Total Distance** - The complete distance covered during the workout
- Units: Kilometers (metric) or Miles (imperial), based on your preference
- Calculated from GPS coordinates

### Time Metrics

- **Duration** - Total elapsed time from start to finish
- **Moving Time** - Time spent actually moving (excludes pauses)
- **Average Pace** - Average speed throughout the workout
- **Best Pace** - Fastest pace achieved during the workout

### Elevation

- **Elevation Gain** - Total upward elevation change
- **Elevation Loss** - Total downward elevation change
- **Min Elevation** - Lowest point on the route
- **Max Elevation** - Highest point on the route
- **Elevation Profile** - Visual chart showing elevation changes

### Heart Rate

- **Average Heart Rate** - Mean heart rate throughout the workout
- **Max Heart Rate** - Highest heart rate recorded
- **Heart Rate Zones** - Time spent in each zone (based on your zone configuration)
- **Heart Rate Chart** - Time-series visualization

### Relative Effort

- **Score** - Calculated intensity score based on heart rate zones
- **Intensity** - Workout intensity classification
- Helps compare workout difficulty across different activities
- Automatically recalculated when heart rate zones change

## Statistics Dashboards

### Weekly Statistics

View your performance for the current week:
- **Total Distance** - Sum of all workouts
- **Total Time** - Combined duration
- **Number of Workouts** - Count of activities
- **Average Pace** - Weighted average across all workouts
- **Total Relative Effort** - Cumulative intensity

### Yearly Statistics

Annual overview including:
- **Total Distance** - Year-to-date distance
- **Total Time** - Year-to-date time
- **Number of Workouts** - Activities this year
- **Trends** - Comparison with previous periods
- **Milestones** - Distance and time goals

### Relative Effort Statistics

Track workout intensity over time:
- **Weekly Totals** - Sum of relative effort per week
- **Trends** - Intensity patterns over time
- **Distribution** - How effort is distributed across workouts
- **Comparisons** - Week-over-week or month-over-month changes

## Time-Series Data

### Heart Rate Chart

Visualize heart rate throughout the workout:
- Shows heart rate at each point in time
- Helps identify intensity zones
- Useful for pacing analysis

### Pace Chart

See pace variations over time:
- Identifies pace consistency
- Shows acceleration and deceleration patterns
- Helps analyze performance segments

### Elevation Profile

Visual representation of elevation changes:
- Correlates with pace and heart rate
- Identifies challenging segments
- Useful for route planning

## Splits Analysis

Distance-based splits provide detailed segment analysis:

- **Split Number** - Sequential split identifier
- **Distance** - Distance covered in this split
- **Time** - Time taken for the split
- **Pace** - Average pace for the split
- **Elevation** - Elevation change in the split
- **Heart Rate** - Average heart rate (if available)

Splits help identify:
- Consistent pacing
- Performance variations
- Impact of elevation on pace
- Heart rate response to effort

## Interpreting Metrics

### Pace Analysis

- **Consistent Pace** - Steady pacing throughout (good for endurance)
- **Negative Splits** - Faster in the second half (good pacing strategy)
- **Positive Splits** - Slower in the second half (may indicate fatigue)

### Heart Rate Analysis

- **Zone Distribution** - Time in each zone indicates workout type
- **Heart Rate Drift** - Increasing HR at same pace (indicates fatigue)
- **Recovery** - HR drop rate after intense segments

### Elevation Impact

- **Uphill Segments** - Typically slower pace, higher heart rate
- **Downhill Segments** - Faster pace, but may increase injury risk
- **Flat Segments** - Best for consistent pacing

### Relative Effort

- **Low Effort** - Easy/recovery runs
- **Moderate Effort** - Tempo or steady-state runs
- **High Effort** - Intervals or races

## Tips for Analysis

1. **Compare Similar Workouts** - Compare runs on the same route to track progress
2. **Look for Trends** - Identify patterns over weeks and months
3. **Correlate Metrics** - Understand relationships between pace, heart rate, and elevation
4. **Set Goals** - Use metrics to set and track performance goals
5. **Identify Weaknesses** - Use data to find areas for improvement

## Next Steps

- [Configure heart rate zones](settings.md) for accurate relative effort calculation
- [View your workouts](viewing-workouts.md) to explore metrics
- [Set unit preferences](settings.md) for your preferred measurement system

