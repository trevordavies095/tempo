'use client';

import Link from 'next/link';
import dynamic from 'next/dynamic';
import { type WorkoutListItem } from '@/lib/api';
import { formatDistance, formatDuration, formatPace, formatDateTime, getWorkoutDisplayName } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { Card } from '@/components/ui/Card';

// Dynamically import WorkoutMap to avoid SSR issues with Leaflet
const WorkoutMap = dynamic(() => import('./WorkoutMap'), {
  ssr: false,
});

interface WorkoutCardProps {
  workout: WorkoutListItem;
}

function getRunTypeBadgeColor(runType: string | null): string {
  switch (runType) {
    case 'Easy Run':
      return 'bg-canvas text-ink border-border';
    case 'Race':
      return 'bg-danger/15 text-danger border-danger/30';
    case 'Workout':
      return 'bg-volt text-on-volt border-volt';
    case 'Long Run':
      return 'bg-ink text-inverse border-ink';
    default:
      return 'bg-canvas text-muted border-border';
  }
}

function getRunTypeLabel(runType: string | null): string {
  return runType || 'None';
}

export default function WorkoutCard({ workout }: WorkoutCardProps) {
  const { unitPreference } = useSettings();

  return (
    <Link href={`/dashboard/${workout.id}`} className="block w-full">
      <Card className="hover:opacity-95 transition-opacity">
        {/* Header: Name, Date/Time, Run Type */}
        <div className="flex items-start justify-between mb-4">
          <div className="flex-1 min-w-0">
            <h3 className="text-xl font-semibold text-ink mb-1">
              {getWorkoutDisplayName(workout.name, workout.startedAt)}
            </h3>
            <p className="text-sm text-muted">
              {formatDateTime(workout.startedAt)}
            </p>
          </div>
          <span
            className={`ml-4 px-3 py-1 text-xs font-medium rounded-full border ${getRunTypeBadgeColor(workout.runType)}`}
          >
            {getRunTypeLabel(workout.runType)}
          </span>
        </div>

        {/* Metrics: Distance, Pace, Time */}
        <div className="grid grid-cols-3 gap-4 mb-4">
          <div>
            <div className="text-2xl font-bold text-ink">
              {formatDistance(workout.distanceM, unitPreference)}
            </div>
            <div className="text-xs text-muted uppercase tracking-wide mt-1">
              Distance
            </div>
          </div>
          <div>
            <div className="text-2xl font-bold text-ink">
              {formatPace(workout.avgPaceS, unitPreference)}
            </div>
            <div className="text-xs text-muted uppercase tracking-wide mt-1">
              Pace
            </div>
          </div>
          <div>
            <div className="text-2xl font-bold text-ink">
              {formatDuration(workout.durationS)}
            </div>
            <div className="text-xs text-muted uppercase tracking-wide mt-1">
              Time
            </div>
          </div>
        </div>

        {/* Map */}
        {workout.route ? (
          <div className="mt-4">
            <WorkoutMap route={workout.route} workoutId={workout.id} height="h-48" interactive={false} />
          </div>
        ) : (
          <div className="mt-4 flex items-center justify-center h-48 bg-canvas rounded-tempo border border-border">
            <p className="text-muted">No route data available</p>
          </div>
        )}
      </Card>
    </Link>
  );
}
