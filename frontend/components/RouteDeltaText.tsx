import type { ReactNode } from 'react';
import type { RouteDelta } from '@/lib/routeDelta';

interface RouteDeltaTextProps {
  /** From `routeTimeDelta` or `routePaceDelta`; renders nothing when null. */
  delta: RouteDelta | null;
  className?: string;
  /** Trailing content, such as a direction glyph. */
  children?: ReactNode;
}

/** A route comparison difference in its tone's colour: "This run −1:12 faster". */
export function RouteDeltaText({ delta, className = '', children }: RouteDeltaTextProps) {
  if (!delta) {
    return null;
  }
  return (
    <span className={`${className} ${delta.className}`.trim()}>
      {delta.text}
      {children}
    </span>
  );
}
