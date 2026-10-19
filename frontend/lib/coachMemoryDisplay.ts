import type {
  CoachLayer1Patch,
  CoachLayer2EpisodeDraft,
  CoachLayer3RuleProposal,
  CoachWeekApplyRecord,
  CoachWeeklyCommit,
} from '@/lib/api';
import { formatDuration, formatPace } from '@/lib/format';
import type { UnitPreference } from '@/lib/settings';

const L1_LABELS: Record<string, string> = {
  'paces.easySecPerKm': 'Easy pace',
  'paces.tempoSecPerKm': 'Tempo pace',
  'paces.thresholdSecPerKm': 'Threshold pace',
  'paces.intervalSecPerKm': 'Interval pace',
  'hrZones.maxHeartRateBpm': 'Max heart rate',
  'hrZones.restingHeartRateBpm': 'Resting heart rate',
  'raceStatus.targetTimeS': 'Goal time',
  'raceStatus.notes': 'Goal notes',
  trainingPhase: 'Training phase',
  runDaysPerWeek: 'Run days per week',
};

const TARGET_LAYER_LABELS: Record<string, string> = {
  data_interpretation_notes: 'Data interpretation',
  key_learnings: 'Key learnings',
  coaching_preferences: 'Coaching preferences',
};

const OPERATION_LABELS: Record<string, string> = {
  promote: 'Promote',
  revise: 'Revise',
  retire: 'Retire',
};

function asNumber(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function formatPatchValue(
  fieldPath: string,
  value: unknown,
  unit: UnitPreference
): string {
  if (value === null || value === undefined) {
    return '—';
  }

  if (
    fieldPath.startsWith('paces.') &&
    fieldPath.endsWith('SecPerKm')
  ) {
    const n = asNumber(value);
    return n !== null ? formatPace(n, unit) : String(value);
  }

  if (fieldPath === 'raceStatus.targetTimeS') {
    const n = asNumber(value);
    return n !== null ? formatDuration(n) : String(value);
  }

  if (
    fieldPath === 'hrZones.maxHeartRateBpm' ||
    fieldPath === 'hrZones.restingHeartRateBpm'
  ) {
    const n = asNumber(value);
    return n !== null ? `${n} bpm` : String(value);
  }

  if (fieldPath === 'trainingPhase') {
    return typeof value === 'string' && value.trim() ? value : '—';
  }

  if (fieldPath === 'runDaysPerWeek') {
    const n = asNumber(value);
    return n !== null ? String(n) : String(value);
  }

  if (typeof value === 'string') {
    return value;
  }

  return String(value);
}

export function layer1FieldLabel(fieldPath: string): string {
  return L1_LABELS[fieldPath] ?? fieldPath;
}

export function formatLayer1PatchPrimary(
  patch: CoachLayer1Patch,
  unit: UnitPreference
): string {
  const label = layer1FieldLabel(patch.fieldPath);
  const from = formatPatchValue(patch.fieldPath, patch.oldValue, unit);
  const to = formatPatchValue(patch.fieldPath, patch.newValue, unit);
  return `${label}: ${from} → ${to}`;
}

export function targetLayerLabel(targetLayer: string): string {
  return TARGET_LAYER_LABELS[targetLayer] ?? targetLayer;
}

export function operationLabel(operation: string): string {
  return OPERATION_LABELS[operation.toLowerCase()] ?? operation;
}

export function categoryLabel(category: string): string {
  if (!category) {
    return 'Note';
  }
  return category.replaceAll('_', ' ');
}

const DAY_TYPE_LABELS: Record<string, string> = {
  rest: 'Rest',
  easy: 'Easy',
  quality: 'Quality',
  long: 'Long',
  race: 'Race',
  other: 'Other',
};

/** Athlete-facing day-type label (enum value stays in form values). */
export function dayTypeLabel(dayType: string): string {
  return DAY_TYPE_LABELS[dayType] ?? dayType.replaceAll('_', ' ');
}

const SOURCE_LABELS: Record<string, string> = {
  athlete: 'from you',
  coach: 'from your coach',
  system: 'from Tempo',
};

/** Athlete-facing source tag for memory items. */
export function memorySourceLabel(source: string): string {
  return SOURCE_LABELS[source] ?? source.replaceAll('_', ' ');
}

export function hasMemoryChannels(commit: CoachWeeklyCommit): boolean {
  return (
    (commit.layer1Patches?.length ?? 0) > 0 ||
    (commit.layer2EpisodeDrafts?.length ?? 0) > 0 ||
    (commit.layer3RuleProposals?.length ?? 0) > 0
  );
}

export function shouldShowMemoryCard(commit: CoachWeeklyCommit): boolean {
  return hasMemoryChannels(commit) || Boolean(commit.coachesNotes?.trim());
}

export interface AppliedMemoryLine {
  key: string;
  text: string;
}

export function buildAppliedMemoryLines(
  commit: CoachWeeklyCommit | null | undefined,
  applyRecord: CoachWeekApplyRecord | null | undefined,
  unit: UnitPreference,
  legacyMemoryProposals?: Array<{ layer?: string; path?: string; summary?: string }> | null
): AppliedMemoryLine[] {
  const lines: AppliedMemoryLine[] = [];

  if (applyRecord) {
    const patches = commit?.layer1Patches ?? [];
    const l1Indexes = applyRecord.acceptedLayer1PatchIndexes ?? [];
    l1Indexes.forEach((index, pos) => {
      const patch = patches[index];
      if (patch) {
        lines.push({
          key: `l1-${index}`,
          text: formatLayer1PatchPrimary(patch, unit),
        });
        return;
      }
      const path =
        applyRecord.appliedLayer1Paths?.[pos] ??
        applyRecord.appliedLayer1Paths?.[index];
      lines.push({
        key: `l1-path-${index}`,
        text: path ? `Athlete state: ${layer1FieldLabel(path)}` : `Athlete state #${index + 1}`,
      });
    });

    if (l1Indexes.length === 0) {
      (applyRecord.appliedLayer1Paths ?? []).forEach((path, i) => {
        lines.push({
          key: `l1-fallback-${i}`,
          text: `Athlete state: ${layer1FieldLabel(path)}`,
        });
      });
    }

    const episodes = commit?.layer2EpisodeDrafts ?? [];
    const l2Indexes = applyRecord.acceptedEpisodeIndexes ?? [];
    l2Indexes.forEach((index, pos) => {
      const draft = episodes[index] as CoachLayer2EpisodeDraft | undefined;
      if (draft) {
        lines.push({
          key: `l2-${index}`,
          text: `${categoryLabel(draft.category)} · ${draft.date}: ${draft.summary}`,
        });
        return;
      }
      const id = applyRecord.appliedEpisodeIds?.[pos];
      lines.push({
        key: `l2-id-${index}`,
        text: id ? `Note ${id}` : `Note #${index + 1}`,
      });
    });

    const rules = commit?.layer3RuleProposals ?? [];
    const l3Indexes = applyRecord.acceptedRuleProposalIndexes ?? [];
    l3Indexes.forEach((index, pos) => {
      const proposal = rules[index] as CoachLayer3RuleProposal | undefined;
      if (proposal) {
        const op = operationLabel(proposal.operation);
        const layer = targetLayerLabel(proposal.targetLayer);
        const text =
          proposal.ruleText?.trim() ||
          proposal.currentRuleText?.trim() ||
          proposal.targetRuleId ||
          '';
        lines.push({
          key: `l3-${index}`,
          text: `${op} · ${layer}${text ? `: ${text}` : ''}`,
        });
        return;
      }
      const id = applyRecord.appliedRuleIds?.[pos];
      lines.push({
        key: `l3-id-${index}`,
        text: id ? `Rule ${id}` : `Rule #${index + 1}`,
      });
    });

    if (l3Indexes.length === 0) {
      (applyRecord.appliedRuleIds ?? []).forEach((id, i) => {
        lines.push({ key: `l3-fallback-${i}`, text: `Rule ${id}` });
      });
    }

    if (applyRecord.archiveNotesAsRecap || applyRecord.recapEpisodeId) {
      lines.push({
        key: 'recap',
        text: applyRecord.recapEpisodeId
          ? `Recap note ${applyRecord.recapEpisodeId}`
          : "Kept Coach's Notes as a recap",
      });
    }
  }

  if (lines.length === 0 && legacyMemoryProposals && legacyMemoryProposals.length > 0) {
    legacyMemoryProposals.forEach((p, i) => {
      const layer = p.layer ? `${p.layer}` : 'Memory';
      const summary = p.summary || p.path || 'Accepted proposal';
      lines.push({ key: `legacy-${i}`, text: `${layer}: ${summary}` });
    });
  }

  return lines;
}

export function applyRecordHasAcceptedMemory(
  applyRecord: CoachWeekApplyRecord | null | undefined
): boolean {
  if (!applyRecord) {
    return false;
  }
  return (
    (applyRecord.acceptedLayer1PatchIndexes?.length ?? 0) > 0 ||
    (applyRecord.acceptedEpisodeIndexes?.length ?? 0) > 0 ||
    (applyRecord.acceptedRuleProposalIndexes?.length ?? 0) > 0 ||
    (applyRecord.appliedLayer1Paths?.length ?? 0) > 0 ||
    (applyRecord.appliedEpisodeIds?.length ?? 0) > 0 ||
    (applyRecord.appliedRuleIds?.length ?? 0) > 0 ||
    Boolean(applyRecord.archiveNotesAsRecap) ||
    Boolean(applyRecord.recapEpisodeId)
  );
}
