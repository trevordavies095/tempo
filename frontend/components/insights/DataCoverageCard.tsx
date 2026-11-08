import type { InsightsMetadata } from '@/lib/api';
import { Card } from '@/components/ui/Card';
import { ProgressBar } from '@/components/insights/ProgressBar';

const CATEGORY_LABELS: Record<string, string> = {
  weather: 'Weather',
  heartRate: 'Heart Rate',
  elevation: 'Elevation',
  calories: 'Calories',
  cadence: 'Cadence',
  power: 'Power',
};

const CATEGORY_ORDER = Object.keys(CATEGORY_LABELS);

function labelFor(key: string): string {
  return CATEGORY_LABELS[key] ?? key.replace(/([A-Z])/g, ' $1').replace(/^./, (c) => c.toUpperCase());
}

export function DataCoverageCard({ metadata }: { metadata: InsightsMetadata }) {
  const keys = Object.keys(metadata.dataAvailability).sort((a, b) => {
    const ia = CATEGORY_ORDER.indexOf(a);
    const ib = CATEGORY_ORDER.indexOf(b);
    return (ia === -1 ? CATEGORY_ORDER.length : ia) - (ib === -1 ? CATEGORY_ORDER.length : ib);
  });
  const days = metadata.daysBetweenFirstAndLatestWorkout;

  return (
    <Card>
      <h2 className="text-lg font-semibold text-ink mb-4">Data Coverage</h2>
      <dl className="grid grid-cols-2 gap-4 mb-6">
        <div>
          <dt className="text-xs font-medium uppercase tracking-wide text-muted">Total Workouts</dt>
          <dd className="text-2xl font-semibold text-ink tabular-nums">
            {metadata.totalWorkouts.toLocaleString()}
          </dd>
        </div>
        <div>
          <dt className="text-xs font-medium uppercase tracking-wide text-muted">Days Tracked</dt>
          <dd className="text-2xl font-semibold text-ink tabular-nums">
            {days != null ? days.toLocaleString() : '—'}
          </dd>
        </div>
      </dl>
      <ul className="space-y-3">
        {keys.map((key) => {
          const category = metadata.dataAvailability[key];
          return (
            <li key={key}>
              <div className="flex items-baseline justify-between gap-3 text-sm mb-1">
                <span className="font-medium text-ink">{labelFor(key)}</span>
                <span className="text-muted tabular-nums">
                  {category.count.toLocaleString()} runs · {category.percentage}%
                </span>
              </div>
              <ProgressBar percent={category.percentage} label={`${labelFor(key)} coverage`} />
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
