import { queryOptions, type QueryClient, type QueryKey } from '@tanstack/react-query';
import {
  getAvailablePeriods,
  getBestEfforts,
  getDefaultShoe,
  getHeartRateZones,
  getInsights,
  getRelativeEffortStats,
  getShoes,
  getSimilarRoutes,
  getThresholds,
  getWeeklyRecap,
  getWeeklyStats,
  getWorkout,
  getWorkoutMedia,
  getWorkouts,
  getWorkoutTimeSeries,
  getYearlyStats,
  getYearlyWeeklyStats,
  type BestEffortsParams,
  type WorkoutsListParams,
} from '@/lib/api';
import { browserTimezoneOffsetMinutes } from '@/lib/localCalendar';

/**
 * Every server read that a data change can make stale, as TanStack Query options (key + fetch), and the one
 * rule for which of them a change affects: {@link invalidateAfter}. Components read with
 * `useQuery(workoutQueries.detail(id))` (spreading extra options as needed) and mutations report what
 * happened, never which keys to clear. Reads no change affects (version, basemaps, import-job polling) stay
 * inline where they're used.
 */

const SIMILAR_ROUTES_STALE_MS = 5 * 60 * 1000;

/** Key roots, shared by the builders and {@link invalidateAfter}. Each read's key starts with its root. */
const roots = {
  workoutLists: ['workouts'],
  workout: ['workout'],
  timeSeries: ['workout-time-series'],
  media: ['workout-media'],
  similarRoutes: ['similar-routes'],
  insights: ['insights'],
  bestEfforts: ['bestEfforts'],
  weeklyRecap: ['weeklyRecap'],
  weeklyStats: ['weeklyStats'],
  yearlyStats: ['yearlyStats'],
  yearlyWeeklyStats: ['yearlyWeeklyStats'],
  availablePeriods: ['availablePeriods'],
  relativeEffortStats: ['relativeEffortStats'],
  shoes: ['shoes'],
  defaultShoe: ['default-shoe'],
  thresholds: ['thresholds'],
  heartRateZones: ['heart-rate-zones'],
} as const satisfies Record<string, QueryKey>;

export const workoutQueries = {
  list: (params: WorkoutsListParams) =>
    queryOptions({ queryKey: [...roots.workoutLists, params], queryFn: () => getWorkouts(params) }),
  detail: (id: string) => queryOptions({ queryKey: [...roots.workout, id], queryFn: () => getWorkout(id) }),
  timeSeries: (id: string) =>
    queryOptions({
      queryKey: [...roots.timeSeries, id],
      queryFn: () => getWorkoutTimeSeries(id),
      staleTime: 5 * 60 * 1000,
    }),
  media: (id: string) => queryOptions({ queryKey: [...roots.media, id], queryFn: () => getWorkoutMedia(id) }),
  similarRoutes: (id: string) =>
    queryOptions({
      queryKey: [...roots.similarRoutes, id],
      queryFn: () => getSimilarRoutes(id),
      staleTime: SIMILAR_ROUTES_STALE_MS,
      gcTime: 2 * SIMILAR_ROUTES_STALE_MS,
      retry: 1,
      refetchOnWindowFocus: false,
    }),
};

/** Calendar-bucketed stats use the browser's timezone offset, which is part of their keys. */
export const statsQueries = {
  weeklyRecap: () => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({ queryKey: [...roots.weeklyRecap, offset], queryFn: () => getWeeklyRecap(offset) });
  },
  weekly: () => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({ queryKey: [...roots.weeklyStats, offset], queryFn: () => getWeeklyStats(offset) });
  },
  yearly: () => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({ queryKey: [...roots.yearlyStats, offset], queryFn: () => getYearlyStats(offset) });
  },
  yearlyWeekly: (periodEndDate: string | null) => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({
      queryKey: [...roots.yearlyWeeklyStats, periodEndDate, offset],
      queryFn: () => getYearlyWeeklyStats(periodEndDate || undefined, offset),
    });
  },
  availablePeriods: () => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({
      queryKey: [...roots.availablePeriods, offset],
      queryFn: () => getAvailablePeriods(offset),
    });
  },
  relativeEffort: () => {
    const offset = browserTimezoneOffsetMinutes();
    return queryOptions({
      queryKey: [...roots.relativeEffortStats, offset],
      queryFn: () => getRelativeEffortStats(offset),
    });
  },
  /** Best per distance; `{}` is all time. */
  bestEfforts: (params: BestEffortsParams = {}) =>
    queryOptions({ queryKey: [...roots.bestEfforts, 'list', params], queryFn: () => getBestEfforts(params) }),
  /** PRs set on or after `params.since`, with the best each one beat. */
  bestEffortPrs: (params: BestEffortsParams) =>
    queryOptions({ queryKey: [...roots.bestEfforts, 'prs', params], queryFn: () => getBestEfforts(params) }),
  insights: () => queryOptions({ queryKey: [...roots.insights], queryFn: getInsights }),
};

export const shoeQueries = {
  list: (status: 'active' | 'retired') =>
    queryOptions({ queryKey: [...roots.shoes, status], queryFn: () => getShoes({ status }) }),
  defaultShoe: () => queryOptions({ queryKey: [...roots.defaultShoe], queryFn: getDefaultShoe }),
};

export const settingsQueries = {
  thresholds: () => queryOptions({ queryKey: [...roots.thresholds], queryFn: getThresholds }),
  heartRateZones: () => queryOptions({ queryKey: [...roots.heartRateZones], queryFn: getHeartRateZones }),
};

/** Something that changed on the server, as the mutation that caused it saw it. */
export type DataChange =
  /** Name, notes, run type, shoe or RPE edited. */
  | { kind: 'workoutEdited'; workoutId: string }
  | { kind: 'workoutDeleted'; workoutId: string }
  /** Route, splits, time series and derived metrics all change. */
  | { kind: 'workoutCropped'; workoutId: string }
  | { kind: 'workoutMediaChanged'; workoutId: string }
  /** New Workouts from a file, bulk or intervals.icu import. */
  | { kind: 'workoutsImported' }
  /** Bulk Relative Effort or splits recalculation across many Workouts. */
  | { kind: 'workoutsRecalculated' }
  | { kind: 'bestEffortsRecalculated' }
  /** A shoe was created, edited, retired, unretired or deleted. */
  | { kind: 'shoesChanged' }
  | { kind: 'defaultShoeChanged' }
  | { kind: 'heartRateZonesSaved' }
  /** A Tempo export restore: anything may have changed. */
  | { kind: 'restoreCompleted' };

/** Reads computed across Workouts: any added, removed or changed Workout can move them. */
const ACROSS_WORKOUTS: readonly QueryKey[] = [
  roots.workoutLists,
  roots.insights,
  roots.bestEfforts,
  roots.weeklyRecap,
  roots.weeklyStats,
  roots.yearlyStats,
  roots.yearlyWeeklyStats,
  roots.availablePeriods,
  roots.relativeEffortStats,
  roots.shoes,
  roots.thresholds,
  // Similar runs on other Workouts list this one
  roots.similarRoutes,
];

/** Which cached reads a change makes stale, and which it removes (a deleted Workout's own reads). */
export function affectedBy(change: DataChange): { stale: readonly QueryKey[]; removed: readonly QueryKey[] } {
  switch (change.kind) {
    case 'workoutEdited':
      return { stale: [...ACROSS_WORKOUTS, [...roots.workout, change.workoutId]], removed: [] };
    case 'workoutDeleted':
      return {
        stale: ACROSS_WORKOUTS,
        removed: [
          [...roots.workout, change.workoutId],
          [...roots.timeSeries, change.workoutId],
          [...roots.media, change.workoutId],
          [...roots.similarRoutes, change.workoutId],
        ],
      };
    case 'workoutCropped':
      return {
        stale: [...ACROSS_WORKOUTS, [...roots.workout, change.workoutId], [...roots.timeSeries, change.workoutId]],
        removed: [],
      };
    case 'workoutMediaChanged':
      // Lists and the Workout carry media ids for thumbnails
      return {
        stale: [roots.workoutLists, [...roots.workout, change.workoutId], [...roots.media, change.workoutId]],
        removed: [],
      };
    case 'workoutsImported':
      return { stale: ACROSS_WORKOUTS, removed: [] };
    case 'workoutsRecalculated':
      return { stale: [...ACROSS_WORKOUTS, roots.workout], removed: [] };
    case 'bestEffortsRecalculated':
      return { stale: [roots.bestEfforts, roots.insights, roots.thresholds, roots.workout], removed: [] };
    case 'shoesChanged':
      // A Workout shows its shoe's name and retired state
      return { stale: [roots.shoes, roots.defaultShoe, roots.workout], removed: [] };
    case 'defaultShoeChanged':
      return { stale: [roots.shoes, roots.defaultShoe], removed: [] };
    case 'heartRateZonesSaved':
      return { stale: [roots.heartRateZones], removed: [] };
    case 'restoreCompleted':
      // An empty key matches every query
      return { stale: [[]], removed: [] };
  }
}

/** Refreshes every cached read `change` affects; active queries refetch now, others on next use. */
export function invalidateAfter(queryClient: QueryClient, change: DataChange): void {
  const { stale, removed } = affectedBy(change);
  for (const queryKey of removed) {
    queryClient.removeQueries({ queryKey });
  }
  for (const queryKey of stale) {
    queryClient.invalidateQueries({ queryKey });
  }
}
