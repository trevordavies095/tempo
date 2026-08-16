'use client';

import { formatDistance, formatPace, formatElevation } from '@/lib/format';
import { useSettings } from '@/lib/settings';
import { useState } from 'react';
import { IconInfoCircle } from '@tabler/icons-react';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

export default function UnitPreferenceSection() {
  const { unitPreference, setUnitPreference } = useSettings();
  const [showTooltip, setShowTooltip] = useState(false);

  return (
    <Card>
      <div className="flex items-center gap-2 mb-4">
        <h2 className="text-lg font-semibold text-ink">Unit Preference</h2>
        <div className="relative">
          <button
            type="button"
            onClick={() => setShowTooltip(!showTooltip)}
            className="text-muted hover:text-ink focus:outline-none"
            aria-label="About units"
          >
            <IconInfoCircle className="w-5 h-5" />
          </button>
          {showTooltip && (
            <>
              <div
                className="fixed inset-0 z-[5]"
                onClick={() => setShowTooltip(false)}
              />
              <div className="absolute right-0 top-8 z-10 w-80 max-w-[calc(100vw-2rem)] bg-raised border border-border rounded-tempo p-4 shadow-lg">
                <h3 className="text-sm font-semibold text-ink mb-2">
                  About Units
                </h3>
                <ul className="text-sm text-muted space-y-1 list-disc list-inside">
                  <li>
                    <strong className="text-ink">Metric:</strong> Distances in kilometers (km), pace per kilometer, elevation in meters (m)
                  </li>
                  <li>
                    <strong className="text-ink">Imperial:</strong> Distances in miles (mi), pace per mile, elevation in feet (ft)
                  </li>
                  <li>Your preference is saved and will persist across sessions.</li>
                  <li>New workouts will be imported with splits based on your current unit preference (1 km splits for metric, 1 mile splits for imperial).</li>
                  <li>To update splits for existing workouts, use the &quot;Recalculate Splits&quot; button in the Data Recalculation section.</li>
                </ul>
                <button
                  type="button"
                  onClick={() => setShowTooltip(false)}
                  className="mt-2 text-xs text-ink underline"
                >
                  Close
                </button>
              </div>
            </>
          )}
        </div>
      </div>
      <p className="text-sm text-muted mb-4">
        Choose how distances, paces, and elevations are displayed throughout the app.
      </p>

      <div className="flex gap-3 mb-6">
        <Button
          type="button"
          variant={unitPreference === 'metric' ? 'primary' : 'secondary'}
          onClick={() => setUnitPreference('metric')}
        >
          Metric
        </Button>
        <Button
          type="button"
          variant={unitPreference === 'imperial' ? 'primary' : 'secondary'}
          onClick={() => setUnitPreference('imperial')}
        >
          Imperial
        </Button>
      </div>

      <div className="bg-canvas p-4 rounded-tempo border border-border">
        <h3 className="text-sm font-semibold text-ink mb-3">Preview</h3>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 text-sm">
          <div>
            <div className="text-muted mb-1">Distance</div>
            <div className="text-ink font-medium">
              {formatDistance(10000, unitPreference)}
            </div>
          </div>
          <div>
            <div className="text-muted mb-1">Pace</div>
            <div className="text-ink font-medium">
              {formatPace(300, unitPreference)}
            </div>
          </div>
          <div>
            <div className="text-muted mb-1">Elevation</div>
            <div className="text-ink font-medium">
              {formatElevation(150, unitPreference)}
            </div>
          </div>
        </div>
      </div>
    </Card>
  );
}
