'use client';

import { useState, type FormEvent } from 'react';
import { useThresholds } from '@/hooks/useThresholds';
import { useSettings, type UnitPreference } from '@/lib/settings';
import type { ThresholdSettings } from '@/lib/api';
import { formatPace, formatPaceInput, parsePaceInput } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

// Mirrors the API's accepted ranges
const MIN_PACE_S_PER_KM = 120;
const MAX_PACE_S_PER_KM = 900;
const MIN_HR_BPM = 60;
const MAX_HR_BPM = 230;

export default function ThresholdsSection() {
  const { unitPreference } = useSettings();
  const { query, mutation } = useThresholds();
  const [saveSuccess, setSaveSuccess] = useState(false);

  const handleSave = async (thresholds: ThresholdSettings) => {
    setSaveSuccess(false);
    await mutation.mutateAsync(thresholds);
    setSaveSuccess(true);
    setTimeout(() => setSaveSuccess(false), 3000);
  };

  return (
    <Card>
      <h3 className="text-lg font-semibold text-ink mb-4">Thresholds</h3>
      <p className="text-sm text-muted mb-6">
        Your lactate threshold pace and heart rate. Pace lets coaching classify short reps, where heart rate lags.
        Leave a field empty if you don&apos;t know it.
      </p>

      {query.isLoading ? (
        <div className="text-muted">Loading...</div>
      ) : query.isError || !query.data ? (
        <div className="text-sm text-danger">Failed to load thresholds</div>
      ) : (
        // Remount when saved values or units change so the inputs start from them
        <ThresholdsForm
          key={`${unitPreference}:${query.data.thresholdPaceS}:${query.data.thresholdHeartRateBpm}`}
          saved={query.data}
          unitPreference={unitPreference}
          isSaving={mutation.isPending}
          saveSuccess={saveSuccess}
          onSave={handleSave}
        />
      )}
    </Card>
  );
}

function ThresholdsForm({
  saved,
  unitPreference,
  isSaving,
  saveSuccess,
  onSave,
}: {
  saved: ThresholdSettings;
  unitPreference: UnitPreference;
  isSaving: boolean;
  saveSuccess: boolean;
  onSave: (thresholds: ThresholdSettings) => Promise<void>;
}) {
  const [paceInput, setPaceInput] = useState(
    saved.thresholdPaceS !== null ? formatPaceInput(saved.thresholdPaceS, unitPreference) : ''
  );
  const [hrInput, setHrInput] = useState(
    saved.thresholdHeartRateBpm !== null ? String(saved.thresholdHeartRateBpm) : ''
  );
  const [formError, setFormError] = useState<string | null>(null);

  const paceUnit = unitPreference === 'imperial' ? '/mi' : '/km';

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setFormError(null);

    let thresholdPaceS: number | null = null;
    if (paceInput.trim() !== '') {
      thresholdPaceS = parsePaceInput(paceInput, unitPreference);
      if (thresholdPaceS === null) {
        setFormError(`Enter threshold pace as M:SS ${paceUnit}`);
        return;
      }
      if (thresholdPaceS < MIN_PACE_S_PER_KM || thresholdPaceS > MAX_PACE_S_PER_KM) {
        setFormError(
          `Threshold pace must be between ${formatPace(MIN_PACE_S_PER_KM, unitPreference)} and ${formatPace(MAX_PACE_S_PER_KM, unitPreference)}`
        );
        return;
      }
    }

    let thresholdHeartRateBpm: number | null = null;
    if (hrInput.trim() !== '') {
      thresholdHeartRateBpm = Number(hrInput);
      if (!Number.isInteger(thresholdHeartRateBpm) || thresholdHeartRateBpm < MIN_HR_BPM || thresholdHeartRateBpm > MAX_HR_BPM) {
        setFormError(`Threshold heart rate must be a whole number between ${MIN_HR_BPM} and ${MAX_HR_BPM} BPM`);
        return;
      }
    }

    try {
      await onSave({ thresholdPaceS, thresholdHeartRateBpm });
    } catch (error) {
      setFormError(error instanceof Error ? error.message : 'Failed to save thresholds');
    }
  };

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4">
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        <div>
          <label htmlFor="threshold-pace" className="block text-sm font-medium text-ink mb-2">
            Threshold pace (M:SS {paceUnit})
          </label>
          <input
            id="threshold-pace"
            type="text"
            inputMode="numeric"
            placeholder={unitPreference === 'imperial' ? '6:50' : '4:15'}
            value={paceInput}
            onChange={(e) => setPaceInput(e.target.value)}
            className={fieldClass}
          />
        </div>
        <div>
          <label htmlFor="threshold-hr" className="block text-sm font-medium text-ink mb-2">
            Threshold heart rate (BPM)
          </label>
          <input
            id="threshold-hr"
            type="number"
            min={MIN_HR_BPM}
            max={MAX_HR_BPM}
            placeholder="170"
            value={hrInput}
            onChange={(e) => setHrInput(e.target.value)}
            className={fieldClass}
          />
        </div>
      </div>
      <div className="flex items-center gap-4">
        <Button type="submit" disabled={isSaving}>
          {isSaving ? 'Saving...' : 'Save Thresholds'}
        </Button>
        {saveSuccess && <span className="text-sm text-ink">Thresholds saved successfully!</span>}
        {formError && <span className="text-sm text-danger">{formError}</span>}
      </div>
    </form>
  );
}
