import type { UnitPreference } from './settings';

const METERS_PER_MILE = 1609.344;
const ROUND_KM_TOLERANCE_M = 25;
const HALF_MILE_SNAP_TOLERANCE_MI = 0.05;

const LEADING_DISTANCE =
  /^(\d+(?:\.\d+)?)\s*(mi|miles?|km|kilometers?)\b(?:\s*[·•,:\-–—]+|\s+)?/i;

/** Snap planned meters the same way the API normalizer does (round-km keep, else half-mile). */
export function normalizePlannedDistanceM(distanceM: number): number {
  if (!(distanceM > 0)) {
    return distanceM;
  }

  const nearestKm = Math.round(distanceM / 1000) * 1000;
  if (nearestKm >= 1000 && Math.abs(distanceM - nearestKm) <= ROUND_KM_TOLERANCE_M) {
    return nearestKm;
  }

  const miles = distanceM / METERS_PER_MILE;
  const nearestHalfMile = Math.round(miles * 2) / 2;
  if (nearestHalfMile >= 0.5 && Math.abs(miles - nearestHalfMile) <= HALF_MILE_SNAP_TOLERANCE_MI) {
    return nearestHalfMile * METERS_PER_MILE;
  }

  return distanceM;
}

function formatSnappedValue(value: number, suffix: string): string {
  if (Number.isInteger(value) || Math.abs(value - Math.round(value)) < 1e-9) {
    return `${Math.round(value)} ${suffix}`;
  }
  return `${value.toFixed(1)} ${suffix}`;
}

/** Athlete-facing planned distance (avoids 4800 m → "2.98 mi"). */
export function formatCoachDistance(meters: number, unit: UnitPreference): string {
  const normalized = normalizePlannedDistanceM(meters);
  if (unit === 'imperial') {
    const miles = normalized / METERS_PER_MILE;
    const half = Math.round(miles * 2) / 2;
    if (Math.abs(miles - half) <= HALF_MILE_SNAP_TOLERANCE_MI) {
      return formatSnappedValue(half, 'mi');
    }
    return `${miles.toFixed(2)} mi`;
  }

  const km = normalized / 1000;
  const half = Math.round(km * 2) / 2;
  if (Math.abs(km - half) <= 0.05) {
    return formatSnappedValue(half, 'km');
  }
  return `${km.toFixed(2)} km`;
}

function distancesMatch(aM: number, bM: number): boolean {
  const avg = (Math.abs(aM) + Math.abs(bM)) / 2;
  if (avg < 1) {
    return Math.abs(aM - bM) < 1;
  }
  return Math.abs(aM - bM) <= Math.max(80, avg * 0.03);
}

/** Drop leading "3 mi …" when it restates distanceM; keep interval recipes. */
export function stripRedundantLeadingDistance(
  structure: string | null | undefined,
  distanceM: number
): string | null {
  if (!structure || !structure.trim()) {
    return null;
  }
  if (!(distanceM > 0)) {
    return structure.trim();
  }

  const trimmed = structure.trim();
  const match = LEADING_DISTANCE.exec(trimmed);
  if (!match) {
    return trimmed;
  }

  const amount = Number.parseFloat(match[1]!);
  if (!Number.isFinite(amount) || amount < 0) {
    return trimmed;
  }

  const unit = match[2]!.toLowerCase();
  const leadingM = unit.startsWith('mi') ? amount * METERS_PER_MILE : amount * 1000;
  if (!distancesMatch(leadingM, normalizePlannedDistanceM(distanceM))) {
    return trimmed;
  }

  const rest = trimmed.slice(match[0].length).trimStart();
  return rest.length > 0 ? rest : null;
}

/** One-line coach day summary: distance · structure · pace (no duplicated miles). */
export function formatCoachRunSummary(
  distanceM: number,
  structure: string | null | undefined,
  paceOrHr: string | null | undefined,
  unit: UnitPreference
): string {
  const parts: string[] = [formatCoachDistance(distanceM, unit)];
  const cleaned = stripRedundantLeadingDistance(structure, distanceM);
  if (cleaned) {
    parts.push(cleaned);
  }
  if (paceOrHr?.trim()) {
    parts.push(paceOrHr.trim());
  }
  return parts.join(' · ');
}
