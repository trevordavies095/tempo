/** Words for the Workout overview splits table. iOS uses the same words; keep them in sync. */

export type SplitIntensity =
  | 'active'
  | 'rest'
  | 'warmup'
  | 'cooldown'
  | 'recovery'
  | 'interval'
  | 'other';

const INTENSITY_TAG: Record<Exclude<SplitIntensity, 'active'>, string> = {
  warmup: 'Warm-up',
  rest: 'Rest',
  recovery: 'Recovery',
  interval: 'Interval',
  cooldown: 'Cool-down',
  other: 'Other',
};

/** "Laps" when every row is a device lap, otherwise "Splits" (including null kind and no rows). */
export function splitsTitle(splits: ReadonlyArray<{ kind?: string | null }>): 'Laps' | 'Splits' {
  return splits.length > 0 && splits.every((s) => s.kind === 'device_lap') ? 'Laps' : 'Splits';
}

/** Singular column header matching `splitsTitle`. */
export function splitsColumnHeader(title: 'Laps' | 'Splits'): 'Lap' | 'Split' {
  return title === 'Laps' ? 'Lap' : 'Split';
}

/** Tag label for a device lap's intensity; null for `active`, null, or unknown values. */
export function intensityTag(intensity: string | null | undefined): string | null {
  if (!intensity || intensity === 'active') return null;
  return INTENSITY_TAG[intensity as keyof typeof INTENSITY_TAG] ?? null;
}
