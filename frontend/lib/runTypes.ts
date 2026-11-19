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

/** The weekly recap's key for Workouts with no run type; shown to users as "Untagged". */
export const UNSPECIFIED_RUN_TYPE = 'Unspecified';

/** Display label for a run type value, mapping `Unspecified` to "Untagged". */
export function runTypeLabel(runType: string): string {
  return runType === UNSPECIFIED_RUN_TYPE ? 'Untagged' : runType;
}

/** How many run types the runs-by-type line lists before collapsing the rest into "+n more". */
export const RUNS_BY_TYPE_MAX_SHOWN = 3;

/**
 * One line of run counts by type, e.g. "3 Easy Run · 1 Long Run · 1 Workout", matching the iOS recap card.
 * Keeps the order the server sent (canonical run-type order, legacy values, then Unspecified), drops zero counts,
 * shows at most three types and appends "+n more" for the rest. Returns null when there are no runs.
 */
export function runsByTypeLine(runsByType: Record<string, number> | null | undefined): string | null {
  const entries = Object.entries(runsByType ?? {}).filter(([, count]) => count > 0);
  if (entries.length === 0) {
    return null;
  }
  const shown = entries
    .slice(0, RUNS_BY_TYPE_MAX_SHOWN)
    .map(([runType, count]) => `${count} ${runTypeLabel(runType)}`);
  const hidden = entries.length - RUNS_BY_TYPE_MAX_SHOWN;
  if (hidden > 0) {
    shown.push(`+${hidden} more`);
  }
  return shown.join(' · ');
}

/** The full runs-by-type list with nothing collapsed, for tooltips and screen readers. */
export function runsByTypeFullLine(runsByType: Record<string, number> | null | undefined): string | null {
  const entries = Object.entries(runsByType ?? {}).filter(([, count]) => count > 0);
  if (entries.length === 0) {
    return null;
  }
  return entries.map(([runType, count]) => `${count} ${runTypeLabel(runType)}`).join(' · ');
}
