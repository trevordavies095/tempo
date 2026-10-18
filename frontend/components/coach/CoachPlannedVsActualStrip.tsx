'use client';

import { useQuery } from '@tanstack/react-query';
import { getCoachPlanDaysOrNull, type WorkoutDetail } from '@/lib/api';
import { formatCoachDistance, formatCoachRunSummary } from '@/lib/coachDistance';
import { dayTypeLabel } from '@/lib/coachMemoryDisplay';
import { formatDuration } from '@/lib/format';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import { useSettings } from '@/lib/settings';

function localDateIsoFromUtc(isoUtc: string, timezoneOffsetMinutes: number): string {
  const utc = new Date(isoUtc);
  const localMs = utc.getTime() + timezoneOffsetMinutes * 60_000;
  const local = new Date(localMs);
  const y = local.getUTCFullYear();
  const m = String(local.getUTCMonth() + 1).padStart(2, '0');
  const day = String(local.getUTCDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function CoachPlannedVsActualStrip({ workout }: { workout: WorkoutDetail }) {
  const { showCoachNav } = useCoachFeature();
  const { unitPreference } = useSettings();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();
  const localDate = localDateIsoFromUtc(workout.startedAt, timezoneOffsetMinutes);

  const { data } = useQuery({
    queryKey: ['coach-plan-days', localDate, timezoneOffsetMinutes],
    queryFn: () => getCoachPlanDaysOrNull(localDate, localDate, timezoneOffsetMinutes),
    enabled: showCoachNav,
    staleTime: 60_000,
  });

  if (!showCoachNav) {
    return null;
  }

  const day = data?.days.find((d) => d.date === localDate);
  if (!day) {
    return null;
  }

  const isRest = day.dayType === 'rest' || !day.run;
  const plannedSummary = isRest
    ? 'Rest'
    : day.run
      ? formatCoachRunSummary(
          day.run.distanceM,
          day.run.structure,
          day.run.paceOrHr,
          unitPreference
        )
      : dayTypeLabel(day.dayType);

  const actualSummary = `${formatCoachDistance(workout.distanceM, unitPreference)} · ${formatDuration(workout.durationS)}`;

  return (
    <div className="w-full rounded-tempo border border-border px-4 py-3 mb-3 space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-sm font-medium text-ink">Planned vs actual</p>
        <p className="text-xs uppercase tracking-wide text-muted">
          {dayTypeLabel(day.dayType)}
        </p>
      </div>
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 text-sm">
        <div>
          <p className="text-xs text-muted mb-0.5">Planned</p>
          <p className="text-ink">{plannedSummary}</p>
        </div>
        <div>
          <p className="text-xs text-muted mb-0.5">Actual</p>
          <p className="text-ink">{actualSummary}</p>
        </div>
      </div>
      {day.adherenceHint?.message && (
        <p className="text-xs text-muted">{day.adherenceHint.message}</p>
      )}
    </div>
  );
}
