'use client';

import { useCallback, useState } from 'react';

export type WorkoutSplit = {
  idx: number;
  distanceM: number;
  durationS: number;
};

export type WorkoutHighlight = {
  splitIdx: number | null;
  elapsedSeconds: number | null;
};

function sortedSplits(splits: WorkoutSplit[]): WorkoutSplit[] {
  return [...splits].sort((a, b) => a.idx - b.idx);
}

/** End-of-split elapsed time: sum of durations through `splitIdx`. */
export function highlightFromSplit(
  splits: WorkoutSplit[],
  splitIdx: number
): WorkoutHighlight {
  const ordered = sortedSplits(splits);
  let elapsed = 0;
  let found = false;
  for (const split of ordered) {
    elapsed += split.durationS;
    if (split.idx === splitIdx) {
      found = true;
      break;
    }
  }

  return {
    splitIdx,
    elapsedSeconds: found ? elapsed : null,
  };
}

export function highlightFromElapsed(
  splits: WorkoutSplit[],
  elapsedSeconds: number
): WorkoutHighlight {
  if (splits.length === 0) {
    return { splitIdx: null, elapsedSeconds };
  }

  const ordered = sortedSplits(splits);
  let cumulative = 0;
  for (const split of ordered) {
    cumulative += split.durationS;
    if (elapsedSeconds <= cumulative) {
      return { splitIdx: split.idx, elapsedSeconds };
    }
  }

  return {
    splitIdx: ordered[ordered.length - 1].idx,
    elapsedSeconds,
  };
}

export function highlightFromRouteDistance(
  splits: WorkoutSplit[],
  distanceM: number,
  totals?: { totalDistanceM: number; totalDurationS: number }
): WorkoutHighlight {
  if (splits.length === 0) {
    if (
      !totals ||
      totals.totalDistanceM <= 0 ||
      totals.totalDurationS <= 0
    ) {
      return { splitIdx: null, elapsedSeconds: null };
    }

    const frac = Math.min(1, Math.max(0, distanceM / totals.totalDistanceM));
    return {
      splitIdx: null,
      elapsedSeconds: frac * totals.totalDurationS,
    };
  }

  const ordered = sortedSplits(splits);
  let distCum = 0;
  let timeCum = 0;

  for (let i = 0; i < ordered.length; i++) {
    const split = ordered[i];
    const isLast = i === ordered.length - 1;
    const nextDist = distCum + split.distanceM;

    if (distanceM <= nextDist || isLast) {
      const frac =
        split.distanceM > 0
          ? Math.min(1, Math.max(0, (distanceM - distCum) / split.distanceM))
          : 1;
      return {
        splitIdx: split.idx,
        elapsedSeconds: timeCum + frac * split.durationS,
      };
    }

    distCum = nextDist;
    timeCum += split.durationS;
  }

  return { splitIdx: null, elapsedSeconds: null };
}

export function routeDistanceFromElapsed(
  splits: WorkoutSplit[],
  elapsedSeconds: number,
  totals?: { totalDistanceM: number; totalDurationS: number }
): number | null {
  if (splits.length === 0) {
    if (
      !totals ||
      totals.totalDistanceM <= 0 ||
      totals.totalDurationS <= 0
    ) {
      return null;
    }

    const frac = Math.min(
      1,
      Math.max(0, elapsedSeconds / totals.totalDurationS)
    );
    return frac * totals.totalDistanceM;
  }

  const ordered = sortedSplits(splits);
  let distCum = 0;
  let timeCum = 0;

  for (let i = 0; i < ordered.length; i++) {
    const split = ordered[i];
    const isLast = i === ordered.length - 1;
    const nextTime = timeCum + split.durationS;

    if (elapsedSeconds <= nextTime || isLast) {
      const frac =
        split.durationS > 0
          ? Math.min(
              1,
              Math.max(0, (elapsedSeconds - timeCum) / split.durationS)
            )
          : 1;
      return distCum + frac * split.distanceM;
    }

    distCum += split.distanceM;
    timeCum = nextTime;
  }

  return distCum;
}

function sameHighlight(
  a: WorkoutHighlight | null,
  b: WorkoutHighlight | null
): boolean {
  if (a === b) {
    return true;
  }
  if (!a || !b) {
    return false;
  }
  return a.splitIdx === b.splitIdx && a.elapsedSeconds === b.elapsedSeconds;
}

export function useWorkoutHighlight(splits: WorkoutSplit[]) {
  const [highlight, setHighlight] = useState<WorkoutHighlight | null>(null);

  const setFromSplit = useCallback(
    (idx: number | null) => {
      setHighlight((prev) => {
        const next = idx === null ? null : highlightFromSplit(splits, idx);
        return sameHighlight(prev, next) ? prev : next;
      });
    },
    [splits]
  );

  const setFromElapsed = useCallback(
    (elapsedSeconds: number | null) => {
      setHighlight((prev) => {
        const next =
          elapsedSeconds === null
            ? null
            : highlightFromElapsed(splits, elapsedSeconds);
        return sameHighlight(prev, next) ? prev : next;
      });
    },
    [splits]
  );

  const setFromMap = useCallback((next: WorkoutHighlight | null) => {
    setHighlight((prev) => (sameHighlight(prev, next) ? prev : next));
  }, []);

  const clear = useCallback(() => {
    setHighlight((prev) => (prev === null ? prev : null));
  }, []);

  return { highlight, setFromSplit, setFromElapsed, setFromMap, clear };
}
