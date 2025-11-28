## User Story

**As a** runner who participates in races
**I want to** crop/trim the beginning or end of my workout
**So that** my recorded workout time matches the official race time

### Acceptance Criteria

1. **User can access crop functionality** from the workout detail page
2. **Interactive timeline interface** allows users to visually select start and end points
3. **Preview shows** the new duration and distance before applying
4. **Crop operation updates**:

- Workout `StartedAt` timestamp (if trimming start)
- Workout `DurationS` (reduced by trimmed time)
- Workout `DistanceM` (recalculated from cropped route)
- All aggregated stats (pace, elevation, heart rate averages, etc.)
- Time series data (remove points outside range, reindex `ElapsedSeconds` from 0)
- Route coordinates (remove points outside range)
- Splits (recalculated from cropped data)

5. **Original raw data preserved** (RawGpxData, RawFitData, RawFileData) for audit trail
6. **Relative Effort recalculated** after crop if heart rate zones are configured
7. **User can undo** the crop operation (restore from original data)

### Technical Viability Assessment

**✅ Highly Viable** - The codebase architecture supports this feature well:

#### Strengths

1. **Data Model Supports Cropping**:

- `WorkoutTimeSeries` uses `ElapsedSeconds` (relative to start) - easy to filter and reindex
- Route stored as GeoJSON LineString - can trim coordinate arrays
- Splits are recalculated from track points (existing `SplitRecalculationService` pattern)
- Raw data stored separately - can preserve original

2. **Existing Patterns to Follow**:

- `SplitRecalculationService` demonstrates recalculating derived data
- `RelativeEffortService` shows how to work with time series data
- PATCH endpoint pattern exists for workout updates
- Recalculation endpoints exist (`/recalculate-splits`, `/recalculate-effort`)

3. **Data Integrity**:

- Time series has composite index on `(WorkoutId, ElapsedSeconds)` for efficient queries
- Cascade deletes configured for related data
- Raw data preservation maintains audit trail

#### Implementation Considerations

1. **Backend Service** (`WorkoutCropService.cs`):

- Filter time series by `ElapsedSeconds` range
- Reindex remaining time series points (subtract trim start from `ElapsedSeconds`)
- Trim route GeoJSON coordinates array
- Recalculate workout aggregates (distance, pace, elevation, HR stats)
- Update `StartedAt` if trimming start
- Update `DurationS`
- Trigger split recalculation
- Trigger relative effort recalculation

2. **API Endpoint** (`POST /workouts/{id}/crop`):

- Accept `startTrimSeconds` and `endTrimSeconds` parameters
- Validate trim values don't exceed workout duration
- Call `WorkoutCropService`
- Return updated workout data

3. **Frontend Components**:

- `CropWorkoutDialog.tsx` - Modal with interactive timeline
- Timeline slider component (similar to video editor)
- Preview showing new duration/distance
- Confirmation before applying

4. **Edge Cases**:

- Workouts without time series data (fallback to route coordinates)
- Workouts without route data (cannot crop)
- Very short workouts (minimum duration validation)
- Cropping entire workout (should be prevented)

#### Potential Challenges

1. **Time Series Data Volume**: Large workouts may have thousands of time series points - batch deletion/update operations needed
2. **Route Coordinate Mapping**: Need to map time-based crop to coordinate indices (may require interpolation)
3. **Split Recalculation**: Must ensure splits recalculate correctly with cropped data
4. **UI Complexity**: Interactive timeline slider requires careful UX design

#### Estimated Complexity

- **Backend**: Medium (new service + endpoint, similar to existing recalculation services)
- **Frontend**: Medium-High (interactive timeline UI is more complex)
- **Testing**: Medium (need to test various crop scenarios, edge cases)

### Recommended Implementation Approach

1. **Phase 1**: Basic time-based cropping (specify seconds to trim)
2. **Phase 2**: Add interactive timeline UI
3. **Phase 3**: Add undo/restore functionality

### Files to Modify/Create

**Backend:**

- `api/Services/WorkoutCropService.cs` (new)
- `api/Endpoints/WorkoutsEndpoints.cs` (add crop endpoint)
- `api/Models/Workout.cs` (may need to track original duration for undo)

**Frontend:**

- `frontend/components/CropWorkoutDialog.tsx` (new)
- `frontend/components/WorkoutDetailHeader.tsx` (add crop button)
- `frontend/lib/api.ts` (add crop API function)
- `frontend/hooks/useWorkoutMutations.ts` (add crop mutation)

### Success Metrics

- Users can successfully crop workouts to match race times
- Cropped workouts maintain data integrity (splits, stats recalculate correctly)
- Original data preserved for audit trail
- No performance issues with large workouts