## Summary

Shoe mileage displayed on the Settings page does not update immediately after importing a workout. The mileage only updates after manually refreshing the page.

## Steps to Reproduce

1. Navigate to Settings page
2. Note the current mileage for the default shoe (e.g., "Total: 50.0 km")
3. Navigate to Import page
4. Import an activity (single file or bulk import)
5. Verify the activity details show the default shoe is correctly assigned
6. Navigate back to Settings page
7. **Observed**: The shoe mileage still shows the old value (e.g., "Total: 50.0 km")
8. Refresh the page (F5 or browser refresh)
9. **Observed**: The shoe mileage now shows the updated value (e.g., "Total: 52.5 km")

## Expected Behavior

The shoe mileage should update immediately when navigating back to the Settings page after importing a workout, without requiring a manual page refresh.

## Root Cause

The issue is a **cache invalidation problem** in the frontend:

1. When workouts are imported via `FileUpload.tsx` or `BulkImport.tsx`, the `onSuccess` callback calls `invalidateWorkoutQueries()` which invalidates workout-related queries.

2. However, `invalidateWorkoutQueries()` (defined in `frontend/lib/queryUtils.ts`) does **not** invalidate the `['shoes']` query cache.

3. The `ShoeManagementSection` component uses `useQuery({ queryKey: ['shoes'] })` to fetch shoe data, which includes calculated mileage.

4. Since the `['shoes']` query cache is not invalidated after import, the component continues to display stale cached data until the page is refreshed.

## Technical Details

### Affected Files

- `frontend/components/FileUpload.tsx` (line 25): Calls `invalidateWorkoutQueries()` after import
- `frontend/components/BulkImport.tsx` (line 30): Calls `invalidateWorkoutQueries()` after import
- `frontend/lib/queryUtils.ts` (lines 10-29): `invalidateWorkoutQueries()` function does not invalidate `['shoes']` query
- `frontend/components/ShoeManagementSection.tsx` (line 52-55): Uses `useQuery({ queryKey: ['shoes'] })` to fetch shoe data

### Backend Behavior

The backend correctly calculates shoe mileage dynamically:
- `ShoeMileageService.GetTotalMileageAsync()` sums all workout distances assigned to a shoe
- The `GET /shoes` endpoint correctly returns updated mileage when queried
- The issue is purely a frontend cache invalidation problem

## Proposed Fix

Add shoe query invalidation to `invalidateWorkoutQueries()` function in `frontend/lib/queryUtils.ts`:

```typescript
export function invalidateWorkoutQueries(queryClient: QueryClient, workoutId?: string): void {
  // ... existing invalidations ...
  
  // Invalidate shoes query (mileage is calculated from workouts)
  queryClient.invalidateQueries({ queryKey: ['shoes'] });
}
```

This ensures that whenever workouts are modified (imported, updated, deleted), the shoe mileage cache is refreshed.

## Severity

**Medium** - The functionality works correctly, but requires a manual page refresh to see updated data. This creates a poor user experience and may cause confusion.

## Additional Notes

- The same issue likely affects shoe mileage updates when:
  - Updating a workout's shoe assignment
  - Deleting a workout
  - Changing a shoe's initial mileage
- However, these operations may already handle invalidation correctly (need to verify)
- The fix should be applied to ensure all workout-related mutations properly invalidate shoe queries