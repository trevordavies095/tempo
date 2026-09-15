/**
 * Which duration seconds to show on cards / activities table.
 * Promotes timer only when it differs from elapsed. Never promotes moving.
 */
export function listDisplayDurationS(workout: {
  durationS: number;
  timerTimeS: number | null;
}): number {
  if (workout.timerTimeS != null && workout.timerTimeS !== workout.durationS) {
    return workout.timerTimeS;
  }
  return workout.durationS;
}

export type OverviewDurationDisplay = {
  heroLabel: 'Duration' | 'Moving Time';
  heroSeconds: number;
  showElapsedSubtitle: boolean;
  showMovingInAdditionalDetails: boolean;
};

/**
 * Overview hero clock: timer (as Duration) when it differs from elapsed,
 * else today's moving-vs-elapsed, else single Duration.
 */
export function overviewDurationDisplay(workout: {
  durationS: number;
  timerTimeS: number | null;
  movingTimeS: number | null;
}): OverviewDurationDisplay {
  const { durationS, timerTimeS, movingTimeS } = workout;

  if (timerTimeS != null && timerTimeS !== durationS) {
    return {
      heroLabel: 'Duration',
      heroSeconds: timerTimeS,
      showElapsedSubtitle: true,
      showMovingInAdditionalDetails:
        movingTimeS != null && movingTimeS !== timerTimeS,
    };
  }

  if (movingTimeS != null && movingTimeS !== durationS) {
    return {
      heroLabel: 'Moving Time',
      heroSeconds: movingTimeS,
      showElapsedSubtitle: true,
      showMovingInAdditionalDetails: false,
    };
  }

  return {
    heroLabel: 'Duration',
    heroSeconds: durationS,
    showElapsedSubtitle: false,
    showMovingInAdditionalDetails: false,
  };
}
