'use client';

import { useQuery } from '@tanstack/react-query';
import { getCoachPlanDaysOrNull } from '@/lib/api';
import { formatCoachRunSummary } from '@/lib/coachDistance';
import { dayTypeLabel } from '@/lib/coachMemoryDisplay';
import { useCoachFeature } from '@/hooks/useCoachFeature';
import { useSettings } from '@/lib/settings';
import { Card } from '@/components/ui/Card';
import { Button } from '@/components/ui/Button';

function localDateIso(d = new Date()): string {
  const y = d.getFullYear();
  const m = String(d.getMonth() + 1).padStart(2, '0');
  const day = String(d.getDate()).padStart(2, '0');
  return `${y}-${m}-${day}`;
}

export function CoachTodayCard() {
  const { showCoachNav } = useCoachFeature();
  const { unitPreference } = useSettings();
  const todayIso = localDateIso();
  const timezoneOffsetMinutes = -new Date().getTimezoneOffset();

  const { data } = useQuery({
    queryKey: ['coach-plan-days', todayIso, timezoneOffsetMinutes],
    queryFn: () => getCoachPlanDaysOrNull(todayIso, todayIso, timezoneOffsetMinutes),
    enabled: showCoachNav,
    staleTime: 60_000,
  });

  if (!showCoachNav) {
    return null;
  }

  const day = data?.days.find((d) => d.date === todayIso);
  if (!day) {
    return null;
  }

  const isRest = day.dayType === 'rest' || !day.run;
  const note = day.run?.notes?.trim() || null;

  return (
    <Card>
      <div className="flex items-start justify-between gap-3 mb-3">
        <div>
          <h2 className="text-lg font-semibold text-ink">Today</h2>
          <p className="text-xs uppercase tracking-wide text-muted mt-1">
            {isRest ? 'Rest' : dayTypeLabel(day.dayType)}
          </p>
        </div>
        <Button href="/coach" variant="ghost" size="sm">
          Week
        </Button>
      </div>

      {isRest ? (
        <div className="space-y-1">
          <p className="text-sm text-muted">Rest</p>
          {note && <p className="text-sm text-ink">{note}</p>}
        </div>
      ) : (
        day.run && (
          <div className="space-y-1 text-sm">
            <p className="text-muted">
              {formatCoachRunSummary(
                day.run.distanceM,
                day.run.structure,
                day.run.paceOrHr,
                unitPreference
              )}
            </p>
            {day.run.shoeName && (
              <p className="text-muted">{day.run.shoeName}</p>
            )}
            {note && <p className="text-ink">{note}</p>}
          </div>
        )
      )}
    </Card>
  );
}
