/** Accepted workout run types, in display order. Mirrors the API's runType enum. */
export const RUN_TYPES = [
  'Easy Run',
  'Recovery',
  'Walk-Run',
  'Long Run',
  'Workout',
  'Threshold',
  'Intervals',
  'Hills',
  'Fartlek',
  'Race',
] as const;

export type RunType = (typeof RUN_TYPES)[number];

export function isRunType(value: string | null | undefined): value is RunType {
  return (RUN_TYPES as readonly string[]).includes(value ?? '');
}

const EASY_BADGE = 'bg-canvas text-ink border-border';
const QUALITY_BADGE = 'bg-volt text-on-volt border-volt';

/** Badge classes per run type. Typed as a full Record so a new run type without a style fails the build. */
export const RUN_TYPE_BADGE: Record<RunType, string> = {
  'Easy Run': EASY_BADGE,
  Recovery: EASY_BADGE,
  'Walk-Run': EASY_BADGE,
  'Long Run': 'bg-ink text-inverse border-ink',
  Workout: QUALITY_BADGE,
  Threshold: QUALITY_BADGE,
  Intervals: QUALITY_BADGE,
  Hills: QUALITY_BADGE,
  Fartlek: QUALITY_BADGE,
  Race: 'bg-danger/15 text-danger border-danger/30',
};
