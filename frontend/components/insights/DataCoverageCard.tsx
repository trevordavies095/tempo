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

/** Background information about the data behind the page: a compact strip of small, muted meters. */
export function DataCoverageCard({ metadata }: { metadata: InsightsMetadata }) {
  const keys = Object.keys(metadata.dataAvailability).sort((a, b) => {
    const ia = CATEGORY_ORDER.indexOf(a);
    const ib = CATEGORY_ORDER.indexOf(b);
    return (ia === -1 ? CATEGORY_ORDER.length : ia) - (ib === -1 ? CATEGORY_ORDER.length : ib);
  });

  if (keys.length === 0) return null;

  return (
    <Card>
      <div className="mb-4 flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
        <h2 className="text-sm font-semibold text-ink">Data Coverage</h2>
        <p className="text-xs text-muted">Share of your runs that recorded each kind of data</p>
      </div>
      <ul className="grid grid-cols-2 gap-x-6 gap-y-4 sm:grid-cols-3 lg:grid-cols-6">
        {keys.map((key) => {
          const category = metadata.dataAvailability[key];
          const label = labelFor(key);
          return (
            <li key={key} title={`${category.count.toLocaleString()} runs`}>
              <div className="mb-1.5 flex items-baseline justify-between gap-2 text-xs">
                <span className="font-medium text-ink">{label}</span>
                <span className="text-muted tabular-nums">{Math.round(category.percentage)}%</span>
              </div>
              <ProgressBar percent={category.percentage} label={`${label} coverage`} tone="muted" />
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
