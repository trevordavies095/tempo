# Settings and Preferences

Configure Tempo to match your preferences and needs.

## Overview

Tempo settings allow you to customize:
- Unit preferences (metric/imperial)
- Heart rate zones
- Relative effort calculation

## Unit Preferences

Choose between metric and imperial units for all measurements.

### Metric Units

- Distance: Kilometers (km)
- Elevation: Meters (m)
- Pace: Minutes per kilometer (min/km)
- Splits: 1 kilometer intervals

### Imperial Units

- Distance: Miles (mi)
- Elevation: Feet (ft)
- Pace: Minutes per mile (min/mi)
- Splits: 1 mile intervals

### Changing Unit Preference

1. Navigate to Settings
2. Select your preferred unit system
3. Save changes

**Note**: Changing unit preferences will trigger recalculation of splits for all workouts. This may take a few moments.

## Heart Rate Zones

Heart rate zones determine how your workouts are analyzed and how relative effort is calculated.

### Zone Calculation Methods

Tempo supports three methods for calculating heart rate zones:

#### Age-Based Method

Uses your age to estimate maximum heart rate:
- Formula: `220 - age`
- Zones calculated as percentages of max HR
- Simple and requires only your age

#### Karvonen Method

Uses your age and resting heart rate:
- Formula: `((220 - age) - resting HR) × zone% + resting HR`
- More accurate than age-based
- Requires knowing your resting heart rate

#### Custom Method

Manually set zone boundaries:
- Define exact heart rate ranges for each zone
- Most flexible option
- Requires understanding of your personal zones

### Zone Definitions

Standard five-zone system:

- **Zone 1 (Recovery)**: 50-60% of max HR - Very light effort
- **Zone 2 (Aerobic)**: 60-70% of max HR - Easy, conversational pace
- **Zone 3 (Tempo)**: 70-80% of max HR - Moderate, sustainable effort
- **Zone 4 (Threshold)**: 80-90% of max HR - Hard, challenging effort
- **Zone 5 (Maximum)**: 90-100% of max HR - Maximum effort, unsustainable

### Configuring Heart Rate Zones

1. Navigate to Settings
2. Select your zone calculation method
3. Enter required information (age, resting HR, or custom zones)
4. Choose whether to recalculate relative effort for existing workouts
5. Save changes

### Recalculating Relative Effort

When you change heart rate zones, you can:
- **Recalculate All Workouts** - Updates relative effort for all existing workouts (may take time)
- **Apply to New Workouts Only** - New workouts use new zones, existing workouts keep old scores

## Relative Effort

Relative effort is automatically calculated based on:
- Heart rate data from your workouts
- Your configured heart rate zones
- Time spent in each zone

### Understanding Relative Effort

- **Low (0-50)**: Easy/recovery runs
- **Moderate (50-100)**: Tempo or steady-state runs
- **High (100-150)**: Hard intervals or tempo runs
- **Very High (150+)**: Maximum effort, races, or intense intervals

### Recalculating Relative Effort

You can manually recalculate relative effort:
1. Go to Settings
2. Find the "Recalculate Relative Effort" section
3. See how many workouts are eligible for recalculation
4. Click "Recalculate All" to update all workouts

**Note**: This operation may take several minutes for large workout collections.

## Settings Management

### Viewing Settings

All settings are accessible from the Settings page:
- Unit preferences
- Heart rate zone configuration
- Relative effort management

### Saving Changes

- Changes are saved immediately when you click "Save"
- Some changes (like unit preferences) may trigger background recalculation
- You'll see confirmation when changes are applied

## Best Practices

### Heart Rate Zones

- Use the Karvonen method if you know your resting heart rate
- Custom zones are best if you've had a professional assessment
- Age-based is fine for general use
- Recalculate relative effort when changing zones to maintain consistency

### Unit Preferences

- Choose based on your familiarity and local conventions
- All historical data will be displayed in your chosen units
- Splits automatically adjust to match your preference

## Troubleshooting

### Settings Not Saving

If settings don't save:
- Check that you're logged in
- Verify all required fields are filled
- Check for error messages
- Refresh the page and try again

### Recalculation Not Working

If recalculation doesn't complete:
- Check that you have workouts with heart rate data
- Verify heart rate zones are configured correctly
- Wait for the process to complete (may take time)
- Check API logs for errors

## Next Steps

- [Import workouts](../user-guide/importing-workouts.md) to see your settings in action
- [View analytics](../user-guide/analytics.md) to understand how settings affect metrics
- [Explore the dashboard](../user-guide/viewing-workouts.md) to see your configured preferences

