import { describe, expect, it } from 'vitest';
import { browserTimezoneOffsetMinutes, formatLocalDate, parseLocalDate } from '@/lib/localCalendar';

// TZ is America/New_York in vitest.config.ts
describe('localCalendar', () => {
  it('parses yyyy-MM-dd as that local day, where new Date() lands on the day before', () => {
    expect(new Date('2026-01-05').getDate()).toBe(4);

    const date = parseLocalDate('2026-01-05');
    expect([date?.getFullYear(), date?.getMonth(), date?.getDate(), date?.getHours()]).toEqual([2026, 0, 5, 0]);
  });

  it('returns null for anything that is not yyyy-MM-dd', () => {
    expect(parseLocalDate('Jan 5')).toBeNull();
    expect(parseLocalDate('')).toBeNull();
  });

  it('formats the local day, where toISOString() gives the UTC day', () => {
    const lateEvening = new Date(2026, 5, 15, 21, 30);
    expect(lateEvening.toISOString().slice(0, 10)).toBe('2026-06-16');
    expect(formatLocalDate(lateEvening)).toBe('2026-06-15');
  });

  it('round-trips', () => {
    expect(formatLocalDate(parseLocalDate('2027-12-31')!)).toBe('2027-12-31');
  });

  it('sends minutes east of UTC', () => {
    expect([-240, -300]).toContain(browserTimezoneOffsetMinutes());
  });
});
