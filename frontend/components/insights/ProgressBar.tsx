/** Thin horizontal bar for a 0–100 value. Purely visual; callers render the number as text. */
export function ProgressBar({ percent, label }: { percent: number; label: string }) {
  const clamped = Math.max(0, Math.min(100, percent));
  return (
    <div
      className="h-1.5 w-full overflow-hidden rounded-full bg-canvas"
      role="progressbar"
      aria-label={label}
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={clamped}
    >
      <div className="h-full rounded-full bg-ink dark:bg-volt" style={{ width: `${clamped}%` }} />
    </div>
  );
}
