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

/**
 * Whether a Workout's laps are structured enough to show intensity tags: two or more distinct
 * non-null intensity values (e.g. warmup + interval + cooldown). Laps that all share one
 * intensity (intervals.icu marks every auto-lap `interval`) or carry none get no tags.
 */
export function showsIntensityTags(
  splits: ReadonlyArray<{ intensity?: string | null }>,
): boolean {
  const distinct = new Set<string>();
  for (const s of splits) {
    if (s.intensity) distinct.add(s.intensity);
  }
  return distinct.size >= 2;
}

/** Tag label for each split, in order: null on every row unless `showsIntensityTags`. */
export function intensityTags(
  splits: ReadonlyArray<{ intensity?: string | null }>,
): Array<string | null> {
  const show = showsIntensityTags(splits);
  return splits.map((s) => (show ? intensityTag(s.intensity) : null));
}

/** Tag label for a device lap's intensity; null for `active`, null, or unknown values. */
export function intensityTag(intensity: string | null | undefined): string | null {
  if (!intensity || intensity === 'active') return null;
  return INTENSITY_TAG[intensity as keyof typeof INTENSITY_TAG] ?? null;
}
