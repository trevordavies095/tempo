import { formatDuration } from '@/lib/format';

const ZONE_FILL_CLASSES = [
  'bg-hr-zone-1',
  'bg-hr-zone-2',
  'bg-hr-zone-3',
  'bg-hr-zone-4',
  'bg-hr-zone-5',
] as const;

export interface ZoneTime {
  zone: number;
  timeS: number;
}

/** Integer percents of sum(timeS) via largest remainder; timeS === 0 always gets 0%. */
function zoneTimePercents(times: Array<{ timeS: number }>): number[] {
  const sum = times.reduce((acc, z) => acc + z.timeS, 0);
  if (sum <= 0) {
    return times.map(() => 0);
  }

  const eligibleIdx: number[] = [];
  for (let i = 0; i < times.length; i++) {
    if (times[i].timeS > 0) eligibleIdx.push(i);
  }

  const floors = new Array(times.length).fill(0);
  const fracs: { i: number; frac: number }[] = [];
  for (const i of eligibleIdx) {
    const exact = (times[i].timeS / sum) * 100;
    floors[i] = Math.floor(exact);
    fracs.push({ i, frac: exact - floors[i] });
  }

  let rem = 100 - floors.reduce((a: number, b: number) => a + b, 0);
  fracs.sort((a, b) => b.frac - a.frac);
  for (const item of fracs) {
    if (rem <= 0) break;
    floors[item.i] += 1;
    rem -= 1;
  }
  return floors;
}

/** Z1–Z5 stacked time-in-zone bar with a per-zone duration and percent breakdown. Expects five zones in order. */
export function HeartRateZoneTimeBar({ zoneTimes }: { zoneTimes: ZoneTime[] }) {
  const zonePercents = zoneTimePercents(zoneTimes);

  return (
    <div className="space-y-1.5 pt-0.5">
      <div className="flex h-2 w-full overflow-hidden rounded-sm bg-canvas">
        {zoneTimes.map((z, i) => {
          const pct = zonePercents[i];
          if (pct <= 0) return null;
          return (
            <div
              key={z.zone}
              className={`h-full ${ZONE_FILL_CLASSES[i]}`}
              style={{ width: `${pct}%` }}
              title={`Zone ${z.zone}`}
            />
          );
        })}
      </div>
      {zoneTimes.map((z, i) => (
        <div key={z.zone} className="flex justify-between items-center gap-2">
          <span className="text-xs text-muted inline-flex items-center gap-1.5">
            <span
              className={`size-2 shrink-0 rounded-sm ${ZONE_FILL_CLASSES[i]}`}
              aria-hidden
            />
            Zone {z.zone}
          </span>
          <span className="text-sm font-semibold text-ink tabular-nums">
            {formatDuration(z.timeS)}
            <span className="text-muted font-normal ml-2">{zonePercents[i]}%</span>
          </span>
        </div>
      ))}
    </div>
  );
}
