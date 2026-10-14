'use client';

import {
  COACH_GOAL_DISTANCES,
  COACH_WEEKDAY_LABELS,
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
      preset: 'long_day',
      weekdays: [7],
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
  const days = (c.weekdays ?? [])
    .map((d) => COACH_WEEKDAY_LABELS[d] ?? String(d))
    .join(', ');
  return `${c.preset ?? 'schedule'}: ${days}`;
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
              value={value.preset ?? 'long_day'}
              onChange={(e) => {
                const preset = e.target.value as CoachStandingPreset;
                onChange({
                  ...value,
                  preset,
                  weekdays:
                    preset === 'long_day'
                      ? [(value.weekdays?.[0] ?? 7)]
                      : (value.weekdays ?? [1]),
                });
              }}
            >
              <option value="long_day">Long day</option>
              <option value="no_quality_on">No quality on</option>
              <option value="no_run_on">No run on</option>
            </select>
          </label>
          <fieldset className="text-sm">
            <legend className="font-medium text-ink mb-2">
              {value.preset === 'long_day' ? 'Weekday' : 'Weekdays'}
            </legend>
            <div className="flex flex-wrap gap-2">
              {Object.entries(COACH_WEEKDAY_LABELS).map(([num, label]) => {
                const day = Number(num);
                const selected = (value.weekdays ?? []).includes(day);
                return (
                  <label key={day} className="flex items-center gap-1 text-muted">
                    <input
                      type={value.preset === 'long_day' ? 'radio' : 'checkbox'}
                      name="weekday"
                      checked={selected}
                      onChange={() => {
                        if (value.preset === 'long_day') {
                          onChange({ ...value, weekdays: [day] });
                        } else {
                          toggleWeekday(day);
                        }
                      }}
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
