import { QueryClient, type QueryKey } from '@tanstack/react-query';
import { beforeEach, describe, expect, it } from 'vitest';
import {
  invalidateAfter,
  settingsQueries,
  shoeQueries,
  statsQueries,
  workoutQueries,
  type DataChange,
} from '@/lib/queries';

const W = 'workout-1';
const OTHER = 'workout-2';

/** One cached read per kind, plus another Workout's reads to prove per-Workout changes stay scoped. */
const reads = {
  workoutList: workoutQueries.list({ page: 1 }).queryKey,
  workout: workoutQueries.detail(W).queryKey,
  otherWorkout: workoutQueries.detail(OTHER).queryKey,
  timeSeries: workoutQueries.timeSeries(W).queryKey,
  otherTimeSeries: workoutQueries.timeSeries(OTHER).queryKey,
  media: workoutQueries.media(W).queryKey,
  otherMedia: workoutQueries.media(OTHER).queryKey,
  similarRoutes: workoutQueries.similarRoutes(W).queryKey,
  otherSimilarRoutes: workoutQueries.similarRoutes(OTHER).queryKey,
  insights: statsQueries.insights().queryKey,
  bestEfforts: statsQueries.bestEfforts().queryKey,
  bestEffortPrs: statsQueries.bestEffortPrs({ since: '2026-01-01' }).queryKey,
  weeklyRecap: statsQueries.weeklyRecap().queryKey,
  weeklyStats: statsQueries.weekly().queryKey,
  yearlyStats: statsQueries.yearly().queryKey,
  yearlyWeeklyStats: statsQueries.yearlyWeekly(null).queryKey,
  availablePeriods: statsQueries.availablePeriods().queryKey,
  relativeEffortStats: statsQueries.relativeEffort().queryKey,
  shoes: shoeQueries.list('active').queryKey,
  retiredShoes: shoeQueries.list('retired').queryKey,
  defaultShoe: shoeQueries.defaultShoe().queryKey,
  thresholds: settingsQueries.thresholds().queryKey,
  heartRateZones: settingsQueries.heartRateZones().queryKey,
  version: ['version'],
} satisfies Record<string, QueryKey>;

type Read = keyof typeof reads;

const ACROSS_WORKOUTS: Read[] = [
  'workoutList',
  'insights',
  'bestEfforts',
  'bestEffortPrs',
  'weeklyRecap',
  'weeklyStats',
  'yearlyStats',
  'yearlyWeeklyStats',
  'availablePeriods',
  'relativeEffortStats',
  'shoes',
  'retiredShoes',
  'thresholds',
  'similarRoutes',
  'otherSimilarRoutes',
];

const cases: { change: DataChange; stale: Read[]; removed?: Read[] }[] = [
  { change: { kind: 'workoutEdited', workoutId: W }, stale: [...ACROSS_WORKOUTS, 'workout'] },
  {
    change: { kind: 'workoutDeleted', workoutId: W },
    stale: ACROSS_WORKOUTS.filter((r) => r !== 'similarRoutes'),
    removed: ['workout', 'timeSeries', 'media', 'similarRoutes'],
  },
  { change: { kind: 'workoutCropped', workoutId: W }, stale: [...ACROSS_WORKOUTS, 'workout', 'timeSeries'] },
  { change: { kind: 'workoutMediaChanged', workoutId: W }, stale: ['workoutList', 'workout', 'media'] },
  { change: { kind: 'workoutsImported' }, stale: ACROSS_WORKOUTS },
  { change: { kind: 'workoutsRecalculated' }, stale: [...ACROSS_WORKOUTS, 'workout', 'otherWorkout'] },
  {
    change: { kind: 'bestEffortsRecalculated' },
    stale: ['bestEfforts', 'bestEffortPrs', 'insights', 'thresholds', 'workout', 'otherWorkout'],
  },
  { change: { kind: 'shoesChanged' }, stale: ['shoes', 'retiredShoes', 'defaultShoe', 'workout', 'otherWorkout'] },
  { change: { kind: 'defaultShoeChanged' }, stale: ['shoes', 'retiredShoes', 'defaultShoe'] },
  { change: { kind: 'heartRateZonesSaved' }, stale: ['heartRateZones'] },
  { change: { kind: 'restoreCompleted' }, stale: Object.keys(reads) as Read[] },
];

describe('invalidateAfter', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient();
    for (const key of Object.values(reads)) {
      queryClient.setQueryData(key, { cached: true });
    }
  });

  const stateOf = (read: Read) => {
    const query = queryClient.getQueryCache().find({ queryKey: reads[read], exact: true });
    if (!query) return 'removed';
    return query.state.isInvalidated ? 'stale' : 'fresh';
  };

  it.each(cases)('$change.kind', ({ change, stale, removed = [] }) => {
    invalidateAfter(queryClient, change);

    const expected = Object.fromEntries(
      (Object.keys(reads) as Read[]).map((read) => [
        read,
        removed.includes(read) ? 'removed' : stale.includes(read) ? 'stale' : 'fresh',
      ])
    );
    const actual = Object.fromEntries((Object.keys(reads) as Read[]).map((read) => [read, stateOf(read)]));
    expect(actual).toEqual(expected);
  });
});

describe('query keys', () => {
  it('shares the Personal Records PR request between Insights cards', () => {
    expect(statsQueries.bestEffortPrs({ since: '2026-01-01' }).queryKey).toEqual(
      statsQueries.bestEffortPrs({ since: '2026-01-01' }).queryKey
    );
  });

  it('keys calendar stats by the browser timezone offset', () => {
    // TZ is America/New_York in vitest.config.ts
    const offset = -new Date().getTimezoneOffset();
    expect(statsQueries.weeklyRecap().queryKey).toEqual(['weeklyRecap', offset]);
  });
});
