/** Accepted workout run types, in display order. Mirrors the API's runType enum. */
export const RUN_TYPES = ['Easy Run', 'Long Run', 'Workout', 'Race'] as const;

export type RunType = (typeof RUN_TYPES)[number];

export function isRunType(value: string | null | undefined): value is RunType {
  return (RUN_TYPES as readonly string[]).includes(value ?? '');
}

/** Badge classes per run type. Typed as a full Record so a new run type without a style fails the build. */
export const RUN_TYPE_BADGE: Record<RunType, string> = {
  'Easy Run': 'bg-canvas text-ink border-border',
  'Long Run': 'bg-ink text-inverse border-ink',
  Workout: 'bg-volt text-on-volt border-volt',
  Race: 'bg-danger/15 text-danger border-danger/30',
};
