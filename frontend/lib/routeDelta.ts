/**
 * Route comparison differences told from the viewed run's side, matching the iOS Similar Runs
 * section and screen.
 *
 * The server sends `timeDifferenceS` and `paceDifferenceS` as match − subject. The command center
 * shows subject − match, prefixed "This run": "This run −0:40 faster" means this run took 40
 * seconds less than the match. Green when this run is faster, red when slower, muted "Same time"
 * (or "Same pace") when equal. The minus sign is U+2212.
 */

export type RouteDeltaTone = 'faster' | 'slower' | 'same';

export interface RouteDelta {
  /** This run − the match, in whole seconds (per km or per mile for pace). */
  seconds: number;
  tone: RouteDeltaTone;
  /** "This run +1:12 slower", "This run −0:40 faster", "Same time". */
  text: string;
  /** Text colour class for the tone. */
  className: string;
}

const TONE_CLASS: Record<RouteDeltaTone, string> = {
  faster: 'text-hr-zone-1',
  slower: 'text-danger',
  same: 'text-muted',
};

/** m:ss with minutes uncapped. */
function minutesSeconds(totalSeconds: number): string {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

function buildDelta(seconds: number, suffix: string, sameText: string): RouteDelta {
  if (seconds === 0) {
    return { seconds: 0, tone: 'same', text: sameText, className: TONE_CLASS.same };
  }
  const tone: RouteDeltaTone = seconds < 0 ? 'faster' : 'slower';
  const sign = seconds < 0 ? '−' : '+';
  return {
    seconds,
    tone,
    text: `This run ${sign}${minutesSeconds(Math.abs(seconds))}${suffix} ${tone}`,
    className: TONE_CLASS[tone],
  };
}

/**
 * This run − the match, in whole seconds, from the server's match − subject difference. The Time
 * Difference sort key: missing differences count as zero.
 */
export function thisRunTimeDifferenceS(timeDifferenceS: number | null | undefined): number {
  if (timeDifferenceS === undefined || timeDifferenceS === null) {
    return 0;
  }
  return Math.round(-timeDifferenceS) || 0;
}

/** The time difference from this run's side; null when the server sent none. */
export function routeTimeDelta(timeDifferenceS: number | null | undefined): RouteDelta | null {
  if (timeDifferenceS === undefined || timeDifferenceS === null) {
    return null;
  }
  return buildDelta(thisRunTimeDifferenceS(timeDifferenceS), '', 'Same time');
}

/**
 * The pace difference from this run's side in the user's unit ("This run +0:08 /km slower");
 * null when the server sent none.
 */
export function routePaceDelta(
  paceDifferenceS: number | null | undefined,
  unit: 'metric' | 'imperial'
): RouteDelta | null {
  if (paceDifferenceS === undefined || paceDifferenceS === null) {
    return null;
  }
  const perUnit = unit === 'imperial' ? -paceDifferenceS * 1.609344 : -paceDifferenceS;
  const seconds = Math.round(perUnit) || 0;
  return buildDelta(seconds, unit === 'imperial' ? ' /mi' : ' /km', 'Same pace');
}
