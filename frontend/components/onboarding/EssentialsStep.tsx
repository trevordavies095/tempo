'use client';

import { useEffect, useState } from 'react';
import {
  createShoe,
  getHeartRateZones,
  setDefaultShoe,
  updateHeartRateZones,
  updateUnitPreference,
  type HeartRateCalculationMethod,
  type HeartRateZoneSettings,
  type UpdateHeartRateZoneSettingsRequest,
} from '@/lib/api';
import { useHeartRateZones } from '@/hooks/useHeartRateZones';
import { useSettings } from '@/lib/settings';
import { formatDistance, formatElevation, formatPace } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

type EssentialsStepProps = {
  onContinue: () => void;
};

export function EssentialsStep({ onContinue }: EssentialsStepProps) {
  const { unitPreference, setUnitPreference } = useSettings();
  const [hrZones, setHrZones] = useState<HeartRateZoneSettings | null>(null);
  const [isLoadingHr, setIsLoadingHr] = useState(true);
  const [unitsSaved, setUnitsSaved] = useState(false);
  const [hrSaved, setHrSaved] = useState(false);
  const [isSavingHr, setIsSavingHr] = useState(false);
  const [hrError, setHrError] = useState<string | null>(null);
  const [continueError, setContinueError] = useState<string | null>(null);
  const [isContinuing, setIsContinuing] = useState(false);

  const [shoeOpen, setShoeOpen] = useState(false);
  const [shoeBrand, setShoeBrand] = useState('');
  const [shoeModel, setShoeModel] = useState('');
  const [shoeMileageInput, setShoeMileageInput] = useState('');
  const [shoeSaving, setShoeSaving] = useState(false);
  const [shoeError, setShoeError] = useState<string | null>(null);
  const [shoeSuccess, setShoeSuccess] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const settings = await getHeartRateZones();
        if (!cancelled) {
          setHrZones(settings);
        }
      } catch (error) {
        console.error('Failed to load heart rate zones:', error);
      } finally {
        if (!cancelled) {
          setIsLoadingHr(false);
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, []);

  const {
    calculationMethod,
    setCalculationMethod,
    age,
    setAge,
    restingHr,
    setRestingHr,
    maxHr,
    setMaxHr,
    customZones,
    displayZones,
    updateCustomZone,
  } = useHeartRateZones(hrZones);

  const selectUnit = async (unit: 'metric' | 'imperial') => {
    await setUnitPreference(unit);
    setUnitsSaved(true);
  };

  const handleSaveHrZones = async () => {
    setIsSavingHr(true);
    setHrError(null);
    try {
      const request: UpdateHeartRateZoneSettingsRequest = {
        calculationMethod,
        age: calculationMethod === 'AgeBased' ? age : null,
        restingHeartRateBpm: calculationMethod === 'Karvonen' ? restingHr : null,
        maxHeartRateBpm: calculationMethod === 'Karvonen' ? maxHr : null,
        zones:
          calculationMethod === 'Custom'
            ? displayZones.map((z, i) => ({
                zoneNumber: i + 1,
                minBpm: z.min,
                maxBpm: z.max,
              }))
            : undefined,
      };
      const updated = await updateHeartRateZones(request);
      setHrZones(updated);
      setHrSaved(true);
    } catch (error) {
      setHrError(error instanceof Error ? error.message : 'Failed to save heart rate zones');
      setHrSaved(false);
    } finally {
      setIsSavingHr(false);
    }
  };

  const convertToMeters = (value: number, unit: 'metric' | 'imperial'): number =>
    unit === 'imperial' ? value * 1609.344 : value * 1000;

  const handleCreateShoe = async () => {
    if (!shoeBrand.trim() || !shoeModel.trim()) {
      setShoeError('Brand and model are required');
      return;
    }
    setShoeSaving(true);
    setShoeError(null);
    setShoeSuccess(null);
    try {
      const initialMileageM = shoeMileageInput
        ? convertToMeters(parseFloat(shoeMileageInput), unitPreference)
        : null;
      const shoe = await createShoe({
        brand: shoeBrand.trim(),
        model: shoeModel.trim(),
        initialMileageM,
      });
      await setDefaultShoe(shoe.id);
      setShoeSuccess(`${shoe.brand} ${shoe.model} saved as your default shoe.`);
      setShoeBrand('');
      setShoeModel('');
      setShoeMileageInput('');
    } catch (error) {
      setShoeError(error instanceof Error ? error.message : 'Failed to create shoe');
    } finally {
      setShoeSaving(false);
    }
  };

  const handleContinue = async () => {
    setContinueError(null);
    setIsContinuing(true);
    try {
      await updateUnitPreference(unitPreference);
      setUnitsSaved(true);
      if (!hrSaved) {
        setContinueError('Save heart rate zones before continuing.');
        setIsContinuing(false);
        return;
      }
      onContinue();
    } catch (error) {
      setContinueError(error instanceof Error ? error.message : 'Failed to save settings');
      setIsContinuing(false);
    }
  };

  const canContinue = unitsSaved && hrSaved && !isContinuing;

  return (
    <div className="w-full space-y-6">
      <Card className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold text-ink">Unit preference</h2>
          <p className="mt-1 text-sm text-muted">
            Choose how distances, paces, and elevations are shown. New imports use this for split
            boundaries.
          </p>
        </div>
        <div className="flex gap-3">
          <Button
            type="button"
            variant={unitPreference === 'metric' ? 'primary' : 'secondary'}
            onClick={() => void selectUnit('metric')}
          >
            Metric
          </Button>
          <Button
            type="button"
            variant={unitPreference === 'imperial' ? 'primary' : 'secondary'}
            onClick={() => void selectUnit('imperial')}
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
              <div className="text-ink font-medium">{formatPace(300, unitPreference)}</div>
            </div>
            <div>
              <div className="text-muted mb-1">Elevation</div>
              <div className="text-ink font-medium">
                {formatElevation(150, unitPreference)}
              </div>
            </div>
          </div>
        </div>
        {unitsSaved ? (
          <p className="text-sm text-ink">Unit preference saved.</p>
        ) : (
          <p className="text-sm text-muted">Select metric or imperial to continue.</p>
        )}
      </Card>

      <Card className="space-y-4">
        <div>
          <h2 className="text-lg font-semibold text-ink">Heart rate zones</h2>
          <p className="mt-1 text-sm text-muted">
            Configure zones before importing so relative effort can be calculated on intake.
          </p>
        </div>

        {isLoadingHr ? (
          <p className="text-sm text-muted">Loading…</p>
        ) : (
          <>
            <div>
              <label className="block text-sm font-medium text-ink mb-3">
                Calculation method
              </label>
              <div className="space-y-2">
                {(
                  [
                    ['AgeBased', '220 − Age (Default)'],
                    ['Karvonen', 'Karvonen (Heart Rate Reserve)'],
                    ['Custom', 'Custom Zones'],
                  ] as const
                ).map(([value, label]) => (
                  <label key={value} className="flex items-center">
                    <input
                      type="radio"
                      name="onboardingCalculationMethod"
                      value={value}
                      checked={calculationMethod === value}
                      onChange={(e) => {
                        setCalculationMethod(e.target.value as HeartRateCalculationMethod);
                        setHrSaved(false);
                      }}
                      className="mr-2 h-4 w-4 accent-[var(--volt)]"
                    />
                    <span className="text-sm text-ink">{label}</span>
                  </label>
                ))}
              </div>
            </div>

            {calculationMethod === 'AgeBased' && (
              <div>
                <label className="block text-sm font-medium text-ink mb-2">Age</label>
                <input
                  type="number"
                  min={1}
                  max={120}
                  value={age}
                  onChange={(e) => {
                    setAge(parseInt(e.target.value, 10) || 30);
                    setHrSaved(false);
                  }}
                  className={fieldClass}
                />
                <p className="text-xs text-muted mt-1">
                  Max HR will be calculated as 220 − age = {220 - age} BPM
                </p>
              </div>
            )}

            {calculationMethod === 'Karvonen' && (
              <div className="space-y-4">
                <div>
                  <label className="block text-sm font-medium text-ink mb-2">
                    Resting heart rate (BPM)
                  </label>
                  <input
                    type="number"
                    min={30}
                    max={120}
                    value={restingHr}
                    onChange={(e) => {
                      setRestingHr(parseInt(e.target.value, 10) || 60);
                      setHrSaved(false);
                    }}
                    className={fieldClass}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-ink mb-2">
                    Maximum heart rate (BPM)
                  </label>
                  <input
                    type="number"
                    min={60}
                    max={250}
                    value={maxHr}
                    onChange={(e) => {
                      setMaxHr(parseInt(e.target.value, 10) || 190);
                      setHrSaved(false);
                    }}
                    className={fieldClass}
                  />
                  <p className="text-xs text-muted mt-1">
                    Heart rate reserve = {maxHr} − {restingHr} = {maxHr - restingHr} BPM
                  </p>
                </div>
              </div>
            )}

            {calculationMethod === 'Custom' && (
              <div>
                <label className="block text-sm font-medium text-ink mb-3">
                  Custom zone boundaries (BPM)
                </label>
                <div className="space-y-3">
                  {customZones.map((zone, index) => (
                    <div key={index} className="flex items-center gap-3">
                      <span className="text-sm font-medium text-ink w-16">Zone {index + 1}:</span>
                      <input
                        type="number"
                        min={30}
                        max={250}
                        value={zone.min}
                        onChange={(e) => {
                          updateCustomZone(index, 'min', parseInt(e.target.value, 10) || 0);
                          setHrSaved(false);
                        }}
                        className={`w-24 ${fieldClass}`}
                        placeholder="Min"
                      />
                      <span className="text-muted">–</span>
                      <input
                        type="number"
                        min={30}
                        max={250}
                        value={zone.max}
                        onChange={(e) => {
                          updateCustomZone(index, 'max', parseInt(e.target.value, 10) || 0);
                          setHrSaved(false);
                        }}
                        className={`w-24 ${fieldClass}`}
                        placeholder="Max"
                      />
                    </div>
                  ))}
                </div>
              </div>
            )}

            <div className="bg-canvas border border-border p-4 rounded-tempo">
              <h3 className="text-sm font-semibold text-ink mb-3">Zone preview</h3>
              <div className="space-y-2">
                {displayZones.map((zone, index) => (
                  <div key={index} className="flex items-center justify-between text-sm">
                    <span className="text-ink font-medium">Zone {index + 1}</span>
                    <span className="text-muted">
                      {zone.min} – {zone.max} BPM
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div className="flex flex-wrap items-center gap-3">
              <Button onClick={() => void handleSaveHrZones()} disabled={isSavingHr}>
                {isSavingHr ? 'Saving…' : 'Save heart rate zones'}
              </Button>
              {hrSaved ? <span className="text-sm text-ink">Zones saved.</span> : null}
              {hrError ? (
                <span className="text-sm text-danger" role="alert">
                  {hrError}
                </span>
              ) : null}
            </div>
          </>
        )}
      </Card>

      <Card className="space-y-3">
        <button
          type="button"
          className="flex w-full items-center justify-between text-left"
          onClick={() => setShoeOpen((open) => !open)}
          aria-expanded={shoeOpen}
        >
          <div>
            <h2 className="text-lg font-semibold text-ink">Add a default shoe</h2>
            <p className="mt-1 text-sm text-muted">Optional — skip if you do not care yet.</p>
          </div>
          <span className="text-sm text-muted">{shoeOpen ? 'Hide' : 'Show'}</span>
        </button>

        {shoeOpen ? (
          <div className="space-y-3 border-t border-border pt-4">
            <div className="grid gap-3 sm:grid-cols-2">
              <div>
                <label className="block text-sm font-medium text-ink mb-2">Brand</label>
                <input
                  type="text"
                  value={shoeBrand}
                  onChange={(e) => setShoeBrand(e.target.value)}
                  className={fieldClass}
                  placeholder="e.g. Nike"
                />
              </div>
              <div>
                <label className="block text-sm font-medium text-ink mb-2">Model</label>
                <input
                  type="text"
                  value={shoeModel}
                  onChange={(e) => setShoeModel(e.target.value)}
                  className={fieldClass}
                  placeholder="e.g. Pegasus 40"
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-ink mb-2">
                Initial mileage ({unitPreference === 'imperial' ? 'miles' : 'km'}, optional)
              </label>
              <input
                type="number"
                min={0}
                step="0.1"
                value={shoeMileageInput}
                onChange={(e) => setShoeMileageInput(e.target.value)}
                className={fieldClass}
                placeholder={unitPreference === 'imperial' ? 'Miles already on shoe' : 'Kilometers already on shoe'}
              />
            </div>
            <div className="flex flex-wrap items-center gap-3">
              <Button
                type="button"
                onClick={() => void handleCreateShoe()}
                disabled={shoeSaving}
              >
                {shoeSaving ? 'Saving…' : 'Create and set as default'}
              </Button>
              {shoeSuccess ? <span className="text-sm text-ink">{shoeSuccess}</span> : null}
              {shoeError ? (
                <span className="text-sm text-danger" role="alert">
                  {shoeError}
                </span>
              ) : null}
            </div>
          </div>
        ) : null}
      </Card>

      <div className="flex flex-col gap-2">
        <Button onClick={() => void handleContinue()} disabled={!canContinue}>
          {isContinuing ? 'Continuing…' : 'Continue'}
        </Button>
        {!unitsSaved || !hrSaved ? (
          <p className="text-sm text-muted">
            Save unit preference and heart rate zones to continue.
          </p>
        ) : null}
        {continueError ? (
          <p className="text-sm text-danger" role="alert">
            {continueError}
          </p>
        ) : null}
      </div>
    </div>
  );
}
