/**
 * Thin horizontal bar for a 0–100 value. Purely visual; callers render the number as text.
 * `tone="muted"` is for background information that should not compete with the page's accent.
 */
export function ProgressBar({
  percent,
  label,
  tone = 'accent',
}: {
  percent: number;
  label: string;
  tone?: 'accent' | 'muted';
}) {
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
      <div
        className={`h-full rounded-full ${tone === 'muted' ? 'bg-muted' : 'bg-ink dark:bg-volt'}`}
        style={{ width: `${clamped}%` }}
      />
    </div>
  );
}
