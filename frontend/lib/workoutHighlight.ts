'use client';

import { useCallback, useState } from 'react';

export type WorkoutSplit = {
  idx: number;
  distanceM: number;
  durationS: number;
  startElapsedS: number;
  endElapsedS: number;
};

export type WorkoutHighlight = {
  splitIdx: number | null;
  elapsedSeconds: number | null;
};

function sortedSplits(splits: WorkoutSplit[]): WorkoutSplit[] {
  return [...splits].sort((a, b) => a.idx - b.idx);
}

/** End-of-split elapsed time from stored StartElapsedS / EndElapsedS bounds. */
export function highlightFromSplit(
  splits: WorkoutSplit[],
  splitIdx: number
): WorkoutHighlight {
  const ordered = sortedSplits(splits);
  const split = ordered.find((s) => s.idx === splitIdx);
  return {
    splitIdx,
    elapsedSeconds: split ? split.endElapsedS : null,
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
  for (let i = 0; i < ordered.length; i++) {
    const split = ordered[i];
    const isLast = i === ordered.length - 1;
    if (
      elapsedSeconds >= split.startElapsedS &&
      (elapsedSeconds < split.endElapsedS || isLast)
    ) {
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

  for (let i = 0; i < ordered.length; i++) {
    const split = ordered[i];
    const isLast = i === ordered.length - 1;
    const nextDist = distCum + split.distanceM;
    const window = Math.max(0, split.endElapsedS - split.startElapsedS);

    if (distanceM <= nextDist || isLast) {
      const frac =
        split.distanceM > 0
          ? Math.min(1, Math.max(0, (distanceM - distCum) / split.distanceM))
          : 1;
      return {
        splitIdx: split.idx,
        elapsedSeconds: split.startElapsedS + frac * window,
      };
    }

    distCum = nextDist;
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

  for (let i = 0; i < ordered.length; i++) {
    const split = ordered[i];
    const isLast = i === ordered.length - 1;
    const window = Math.max(0, split.endElapsedS - split.startElapsedS);

    if (elapsedSeconds <= split.endElapsedS || isLast) {
      const frac =
        window > 0
          ? Math.min(
              1,
              Math.max(0, (elapsedSeconds - split.startElapsedS) / window)
            )
          : 1;
      return distCum + frac * split.distanceM;
    }

    distCum += split.distanceM;
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
