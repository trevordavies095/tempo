# Understanding Analytics and Metrics

How Tempo calculates and shows its metrics, on the Dashboard, the Workout overview, and Insights.

## Workout Metrics

### Distance

FIT files use the device's session distance; GPX distance is computed from the track points. Shown in km or miles per your unit preference. Crop recomputes it from the remaining track.

### Time

Tempo keeps three clocks:

- **Elapsed time** (`DurationS`) — wall clock, pauses included. Dashboard, weekly recap, and yearly sums use this clock. Duplicate detection uses it.
- **Timer time** (`TimerTimeS`) — device running time, from FIT `total_timer_time`.
- **Moving time** (`MovingTimeS`) — time moving by a speed threshold, when the source provides it.

How each screen picks a clock is under [Metrics](viewing-workouts.md#metrics).

**Average pace** (stored as seconds per km) is distance over timer time, else moving time, else elapsed time.

### Elevation Gain

Summed from the track's elevation samples, filtering noise: a continuous climb counts only if it rises at least `ElevationCalculation:NoiseThresholdMeters` (default 2.0 m) over at least `ElevationCalculation:MinDistanceMeters` (default 10.0 m). See [Configuration](../getting-started/configuration.md#storage-and-processing). Elevation loss, min, and max are not shown.

### Heart Rate, Cadence, Power

- Average and max from the time series when samples exist, otherwise from the file summary.
- Cadence is steps per minute (spm). FIT stride cadence is doubled on import.
- Power is watts, from FIT or GPX `TrackPointExtension`.
- Temperature, speed, grade, and vertical speed are stored in the time series but not charted.

### Time in Zones

Each heart-rate sample counts the seconds until the next sample (gaps over 10 s or negative count as 1 s) toward the zone it falls in. Zones 1–4 include their min and exclude their max; Zone 5 includes both. Samples outside all five zones are not counted. Shown on the Workout overview and in the weekly recap; needs an HR series. Recomputed when zones are saved with recalculation. Zone setup is in [Settings](settings.md#heart-rate-zones).

### Relative Effort

A heart-rate load score:

```
Relative Effort = round( Σ minutes in zone N × N )   for N = 1..5
```

Zone 1 earns 1 point per minute, Zone 5 earns 5. Time below Zone 1 earns nothing.

- Uses the HR time series. Without one, it falls back to the average HR (FIT session `avgHeartRate`, else the stored average) × elapsed minutes × that zone's weight. With no heart rate at all, it is empty.
- Calculated on import, crop, and duplicate upgrade. When you change zones, choose whether to recalculate existing Workouts (see [Settings](settings.md#saving-and-recalculating)).
- There are no fixed "easy" or "hard" bands; compare against your own weeks on the Dashboard card.

**Dashboard card:** cumulative Relative Effort Monday→Sunday for this week, over a band from the lowest to highest weekly total of the previous three complete weeks. The card says **Within range**, **Above range**, or **Below range** and shows the 3-week average.

**Implementation:** `api/Services/RelativeEffortService.cs`, `api/Services/WorkoutDerivations.cs`, `GET /stats/relative-effort` in `api/Endpoints/StatsEndpoints.cs`, `frontend/components/RelativeEffortGraph.tsx`.

### Best Efforts

The fastest time for a standard distance over any segment of a Workout, so a 5K best can come from the middle of a half marathon.

Distances: 400m, 1/2 mile, 1K, 1 mile, 2 mile, 5K, 10K, 15K, 10 mile, 20K, Half-Marathon, 30K, Marathon.

- Each Workout stores its own best per distance (`WorkoutBestEffort`), computed with a sliding window over the time series' cumulative distance, or from the route when there is no distance series. Only distances no longer than the Workout are tried.
- All-time and date-windowed bests are the fastest of those; ties go to the earliest Workout.
- Recomputed when a Workout's samples change (import, crop, duplicate upgrade) and after a Tempo restore. Not exported.
- Distances with no effort are omitted.

**Dashboard card:** all-time best per distance; each time links to its Workout. The refresh button (**Recalculate best efforts**) recomputes every Workout after confirmation.

**Implementation:** `api/Services/BestEffortService.cs`, `GET /stats/best-efforts` (`from` / `to` optional), `POST /stats/best-efforts/recalculate`, `frontend/components/BestEffortsChart.tsx`.

## Weekly Recap

The dashboard's **This Week** card is the weekly recap for the current Monday to Sunday week:

- **Header** — the week's dates (with "through" and today's weekday while the week is in progress, Monday to Saturday) and the year-to-date distance
- **Distance, Runs, Time, Elevation** — week-to-date totals in your units. Time is elapsed time.
- **Trend arrows** — each tile compares the week to the average of the previous three weeks: up when above, down when below, a dash only when exactly level, the same as the iOS app. No arrow when the previous three weeks have nothing to compare. While the current week has no runs yet, no arrows are shown; they appear with the week's first run.
- **Daily bars** — distance per day, Monday to Sunday, with a marker under today
- **Runs by type** — one line such as "3 Easy Run · 1 Long Run · 1 Workout", in run-type order with untagged runs ("Untagged") last. After three types the rest collapse into "+n more"; hover to see them all. Hidden when the week has no runs.
- **Time in Zone** — the Z1–Z5 bar for the week, as on the Workout overview. Hidden when no Workout this week has a heart-rate series.

Weeks follow the browser's timezone (`timezoneOffsetMinutes`).

**Implementation:** `frontend/components/WeeklyRecapCard.tsx`; `GET /stats/weekly-recap`, `/stats/weekly`, `/stats/yearly` in `api/Endpoints/StatsEndpoints.cs`.

## Insights

**Insights** in the navigation bar is an all-time view across your runs. It needs at least 5 Workouts; until then it shows **Unlock Insights** with "Complete at least 5 runs to see insights!" and a progress bar.

### Summary

**Total runs**, **Running since** (month of the first run, with the latest run's date), and **Records in {year}** (distances whose all-time best was set this year).

### Personal Records

Fastest time per standard distance; each time opens its Workout. Recalculate from the Dashboard's Best Efforts card.

Pick a range above the table:

- **All time** (default) — the same list as the Dashboard card
- **This year** — best per distance since January 1
- **Last 90 days** — best per distance from the last 90 days

In a ranged view, a time that is also an all-time record set inside the range gets a **New** marker. When it beat an earlier record, it also shows **Was m:ss on <date>** (linking to the previous record's Workout) and how much faster it was. Distances with no effort in the range are left out; if none have one, the card says "No records in this range". The range resets to All time when you leave the page.

### Weather Extremes

Up to eight tiles: Coldest, Hottest, Windiest, Most humid, Wettest, Wildest (thunderstorms), Foggiest, Snowiest. Each shows the value, the Workout name and date, and opens that Workout. Temperature, wind, and precipitation follow your unit preference. A tile appears only when a run qualifies (Snowiest needs a run in snow). The section needs weather on at least 3 runs and at least 25% of runs; otherwise it shows "Not enough weather data yet".

### Data Coverage

For weather, heart rate, elevation, calories, cadence, and power: what percentage of runs have it (hover for the count).

**Implementation:** `frontend/app/insights/page.tsx`, `frontend/components/insights/*`, `frontend/lib/bestEffortsRange.ts`, `api/Services/InsightsService.cs`, `api/Services/InsightsThresholds.cs`, `GET /stats/insights`.

## Splits

Distance splits are 1 km (metric) or 1 mile (imperial), built from track points at import; the last is partial. Each has distance, duration, pace, and average heart rate when the Workout has an HR series. Device laps replace them on the overview when present. See [Splits and Laps](viewing-workouts.md#splits-and-laps).

**Implementation:** `api/Services/TrackGeometry.cs`, `api/Services/SplitRecalculationService.cs`, `api/Services/SplitHeartRateService.cs`.

## Next Steps

- [Configure heart rate zones](settings.md#heart-rate-zones)
- [Explore the Workout overview](viewing-workouts.md)
