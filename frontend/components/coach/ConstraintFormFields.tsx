'use client';

import {
  COACH_GOAL_DISTANCES,
  COACH_WEEKDAY_LABELS,
  createCoachConstraint,
  listCoachConstraints,
  retireCoachConstraint,
  type CoachConstraint,
  type CoachConstraintIntent,
  type CoachConstraintKind,
  type CoachConstraintRequest,
  type CoachStandingPreset,
} from '@/lib/api';

const fieldClass =
  'w-full px-3 py-2 border border-border rounded-tempo bg-canvas text-ink focus:outline-none focus:ring-2 focus:ring-volt';

export type ConstraintDraft = CoachConstraintRequest;

type ConstraintFormFieldsProps = {
  value: ConstraintDraft;
  onChange: (next: ConstraintDraft) => void;
};

function isLongDayConstraint(c: Pick<CoachConstraint, 'kind' | 'preset'>): boolean {
  return c.kind === 'standing_schedule' && c.preset === 'long_day';
}

/** First active long_day weekday (ISO 1–7), or null when none. */
export function readLongRunDay(constraints: CoachConstraint[]): number | null {
  const row = constraints.find((c) => !c.isRetired && isLongDayConstraint(c));
  const day = row?.weekdays?.[0];
  return day != null && day >= 1 && day <= 7 ? day : null;
}

/** Retire every active long_day row, then create one when isoWeekday is set. */
export async function setLongRunDay(isoWeekday: number | null): Promise<void> {
  const active = await listCoachConstraints('active');
  const longDays = active.filter(isLongDayConstraint);
  for (const row of longDays) {
    await retireCoachConstraint(row.id);
  }
  if (isoWeekday != null && isoWeekday >= 1 && isoWeekday <= 7) {
    await createCoachConstraint({
      kind: 'standing_schedule',
      preset: 'long_day',
      weekdays: [isoWeekday],
    });
  }
}

export function isFixedDatesConstraint(c: CoachConstraint): boolean {
  return !isLongDayConstraint(c);
}

export function emptyConstraintDraft(kind: CoachConstraintKind = 'blackout'): ConstraintDraft {
  if (kind === 'secondary_race') {
    return {
      kind,
      startDate: '',
      distance: '5K',
      intent: 'tune_up',
      note: '',
    };
  }
  if (kind === 'standing_schedule') {
    return {
      kind,
      preset: 'no_run_on',
      weekdays: [1],
      note: '',
    };
  }
  return {
    kind: 'blackout',
    startDate: '',
    endDate: '',
    note: '',
  };
}

function standingScheduleLabel(preset: string | null | undefined, weekdays: number[] | null | undefined): string {
  const days = (weekdays ?? [])
    .map((d) => {
      const full = COACH_WEEKDAY_LABELS[d] ?? String(d);
      return full.slice(0, 3);
    })
    .join(', ');
  if (preset === 'no_quality_on') {
    return days ? `No quality on ${days}` : 'No quality on';
  }
  if (preset === 'no_run_on') {
    return days ? `No run on ${days}` : 'No run on';
  }
  if (preset === 'long_day') {
    const full = weekdays?.[0] != null ? COACH_WEEKDAY_LABELS[weekdays[0]] ?? String(weekdays[0]) : '?';
    return `Long run on ${full}`;
  }
  return days ? `Schedule: ${days}` : 'Standing schedule';
}

export function formatConstraintSummary(c: ConstraintDraft): string {
  if (c.kind === 'secondary_race') {
    return `B-race ${c.distance ?? ''} on ${c.startDate ?? '?'} (${c.intent ?? 'tune_up'})`;
  }
  if (c.kind === 'blackout') {
    const range =
      c.endDate && c.endDate !== c.startDate
        ? `${c.startDate}–${c.endDate}`
        : (c.startDate ?? '?');
    return `Blackout ${range}${c.note ? ` — ${c.note}` : ''}`;
  }
  return standingScheduleLabel(c.preset, c.weekdays);
}

export function coachConstraintToRequest(c: CoachConstraint): CoachConstraintRequest {
  return {
    kind: c.kind as CoachConstraintRequest['kind'],
    startDate: c.startDate,
    endDate: c.endDate,
    distance: c.distance,
    intent: c.intent as CoachConstraintRequest['intent'],
    preset: c.preset as CoachConstraintRequest['preset'],
    weekdays: c.weekdays,
    note: c.note,
  };
}

export function describeCoachConstraint(c: CoachConstraint): string {
  if (c.kind === 'secondary_race') {
    return `B-race ${c.distance} on ${c.startDate} (${c.intent})`;
  }
  if (c.kind === 'blackout') {
    const range =
      c.endDate && c.endDate !== c.startDate
        ? `${c.startDate}–${c.endDate}`
        : c.startDate;
    return `Blackout ${range}${c.note ? ` — ${c.note}` : ''}`;
  }
  const base = standingScheduleLabel(c.preset, c.weekdays);
  return c.note ? `${base} — ${c.note}` : base;
}

export function ConstraintFormFields({ value, onChange }: ConstraintFormFieldsProps) {
  const setKind = (kind: CoachConstraintKind) => {
    onChange(emptyConstraintDraft(kind));
  };

  const toggleWeekday = (day: number) => {
    const current = new Set(value.weekdays ?? []);
    if (current.has(day)) current.delete(day);
    else current.add(day);
    const next = Array.from(current).sort((a, b) => a - b);
    onChange({ ...value, weekdays: next });
  };

  const standingPreset = (value.preset === 'no_quality_on' ? 'no_quality_on' : 'no_run_on') as
    | 'no_quality_on'
    | 'no_run_on';

  return (
    <div className="space-y-3">
      <label className="block text-sm">
        <span className="font-medium text-ink">Kind</span>
        <select
          className={`${fieldClass} mt-1`}
          value={value.kind}
          onChange={(e) => setKind(e.target.value as CoachConstraintKind)}
        >
          <option value="secondary_race">B-race (secondary)</option>
          <option value="blackout">Blackout</option>
          <option value="standing_schedule">Standing schedule</option>
        </select>
      </label>

      {value.kind === 'secondary_race' && (
        <>
          <label className="block text-sm">
            <span className="font-medium text-ink">Date</span>
            <input
              type="date"
              className={`${fieldClass} mt-1`}
              value={value.startDate ?? ''}
              onChange={(e) => onChange({ ...value, startDate: e.target.value })}
            />
          </label>
          <label className="block text-sm">
            <span className="font-medium text-ink">Distance</span>
            <select
              className={`${fieldClass} mt-1`}
              value={value.distance ?? '5K'}
              onChange={(e) => onChange({ ...value, distance: e.target.value })}
            >
              {COACH_GOAL_DISTANCES.map((d) => (
                <option key={d} value={d}>
                  {d}
                </option>
              ))}
            </select>
          </label>
          <label className="block text-sm">
            <span className="font-medium text-ink">Intent</span>
            <select
              className={`${fieldClass} mt-1`}
              value={value.intent ?? 'tune_up'}
              onChange={(e) =>
                onChange({ ...value, intent: e.target.value as CoachConstraintIntent })
              }
            >
              <option value="tune_up">Tune-up</option>
              <option value="all_out">All-out</option>
              <option value="fun_run">Fun run</option>
            </select>
          </label>
        </>
      )}

      {value.kind === 'blackout' && (
        <>
          <label className="block text-sm">
            <span className="font-medium text-ink">Start date</span>
            <input
              type="date"
              className={`${fieldClass} mt-1`}
              value={value.startDate ?? ''}
              onChange={(e) => onChange({ ...value, startDate: e.target.value })}
            />
          </label>
          <label className="block text-sm">
            <span className="font-medium text-ink">End date (optional)</span>
            <input
              type="date"
              className={`${fieldClass} mt-1`}
              value={value.endDate ?? ''}
              onChange={(e) => onChange({ ...value, endDate: e.target.value || null })}
            />
          </label>
          <label className="block text-sm">
            <span className="font-medium text-ink">Reason (optional)</span>
            <input
              type="text"
              className={`${fieldClass} mt-1`}
              value={value.note ?? ''}
              onChange={(e) => onChange({ ...value, note: e.target.value })}
            />
          </label>
        </>
      )}

      {value.kind === 'standing_schedule' && (
        <>
          <label className="block text-sm">
            <span className="font-medium text-ink">Preset</span>
            <select
              className={`${fieldClass} mt-1`}
              value={standingPreset}
              onChange={(e) => {
                const preset = e.target.value as CoachStandingPreset;
                onChange({
                  ...value,
                  preset,
                  weekdays: value.weekdays?.length ? value.weekdays : [1],
                });
              }}
            >
              <option value="no_quality_on">No quality on</option>
              <option value="no_run_on">No run on</option>
            </select>
          </label>
          <fieldset className="text-sm">
            <legend className="font-medium text-ink mb-2">Weekdays</legend>
            <div className="flex flex-wrap gap-2">
              {Object.entries(COACH_WEEKDAY_LABELS).map(([num, label]) => {
                const day = Number(num);
                const selected = (value.weekdays ?? []).includes(day);
                return (
                  <label key={day} className="flex items-center gap-1 text-muted">
                    <input
                      type="checkbox"
                      name="weekday"
                      checked={selected}
                      onChange={() => toggleWeekday(day)}
                    />
                    {label.slice(0, 3)}
                  </label>
                );
              })}
            </div>
          </fieldset>
          <label className="block text-sm">
            <span className="font-medium text-ink">Note (optional)</span>
            <input
              type="text"
              className={`${fieldClass} mt-1`}
              value={value.note ?? ''}
              onChange={(e) => onChange({ ...value, note: e.target.value })}
            />
          </label>
        </>
      )}
    </div>
  );
}
