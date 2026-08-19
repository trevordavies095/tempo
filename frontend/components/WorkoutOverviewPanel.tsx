'use client';

import dynamic from 'next/dynamic';
import type { WorkoutDetail } from '@/lib/api';
import { useSettings } from '@/lib/settings';
import { useWorkoutHighlight } from '@/lib/workoutHighlight';
import WorkoutDetailSplits from '@/components/WorkoutDetailSplits';
import { RouteMatchesSummary } from '@/components/RouteMatchesSummary';
import { WorkoutOverviewEditors } from '@/components/WorkoutOverviewEditors';
import { WorkoutOverviewMetrics } from '@/components/WorkoutOverviewMetrics';
import { WorkoutOverviewMedia } from '@/components/WorkoutOverviewMedia';
import { Card } from '@/components/ui/Card';

const WorkoutMap = dynamic(() => import('@/components/WorkoutMap'), {
  ssr: false,
  loading: () => (
    <div className="flex items-center justify-center h-64 bg-canvas rounded-tempo border border-border">
      <p className="text-muted">Loading map...</p>
    </div>
  ),
});

const WorkoutTimeSeriesCharts = dynamic(
  () =>
    import('@/components/WorkoutTimeSeriesCharts').then(
      (mod) => mod.WorkoutTimeSeriesCharts
    ),
  {
    loading: () => (
      <div className="bg-raised border border-border rounded-tempo p-6">
        <p className="text-sm text-muted">Loading sensor data…</p>
      </div>
    ),
  }
);

export function WorkoutOverviewPanel({ workout }: { workout: WorkoutDetail }) {
  const { unitPreference } = useSettings();
  const { highlight, setFromSplit, setFromElapsed, setFromMap } =
    useWorkoutHighlight(workout.splits ?? []);
  const hasSplits = !!(workout.splits && workout.splits.length > 0);

  return (
    <div className="w-full space-y-3">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
        <Card className="space-y-2.5" padding>
          <WorkoutOverviewEditors workout={workout} />
          <WorkoutOverviewMedia workoutId={workout.id} />
        </Card>

        <Card className="space-y-2.5" padding>
          <WorkoutOverviewMetrics workout={workout} unitPreference={unitPreference} />
        </Card>
      </div>

      {workout.route && (
        <RouteMatchesSummary workoutId={workout.id} currentWorkout={workout} />
      )}

      {(hasSplits || workout.route) && (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          <WorkoutDetailSplits
            splits={workout.splits}
            unitPreference={unitPreference}
            hoveredSplitIdx={highlight?.splitIdx ?? null}
            onSplitHover={setFromSplit}
          />

          {workout.route && (
            <Card>
              <h2 className="text-lg font-semibold text-ink mb-2">Route Map</h2>
              <WorkoutMap
                key={workout.id}
                route={workout.route}
                workoutId={workout.id}
                splits={workout.splits}
                hoveredSplitIdx={highlight?.splitIdx ?? null}
                highlightElapsedSeconds={highlight?.elapsedSeconds ?? null}
                workoutDistanceM={workout.distanceM}
                workoutDurationS={workout.durationS}
                onHighlightFromMap={setFromMap}
              />
            </Card>
          )}
        </div>
      )}

      <WorkoutTimeSeriesCharts
        workoutId={workout.id}
        highlight={highlight}
        onElapsedChange={setFromElapsed}
      />
    </div>
  );
}
