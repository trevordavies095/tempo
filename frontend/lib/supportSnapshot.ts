/** Frozen support-snapshot text for Settings copy / older APIs without `snapshot`. */

export type SupportSnapshotFields = {
  version?: string | null;
  gitCommit?: string | null;
  buildDate?: string | null;
  logProfile?: string | null;
  environment?: string | null;
  apiImage?: string | null;
  frontendImage?: string | null;
};

function field(value: string | null | undefined): string {
  if (value == null || value.trim() === '') {
    return 'unknown';
  }
  return value;
}

/**
 * Builds the frozen support-snapshot block (Unix newlines, trailing newline).
 * Matches GET /version `snapshot` projection.
 */
export function formatSupportSnapshot(fields: SupportSnapshotFields): string {
  return [
    'Tempo support snapshot',
    '',
    `version: ${field(fields.version)}`,
    `gitCommit: ${field(fields.gitCommit)}`,
    `buildDate: ${field(fields.buildDate)}`,
    `logProfile: ${field(fields.logProfile)}`,
    `environment: ${field(fields.environment)}`,
    `apiImage: ${field(fields.apiImage)}`,
    `frontendImage: ${field(fields.frontendImage)}`,
    '',
  ].join('\n');
}

/** Prefer API `snapshot` when present; otherwise format from known fields. */
export function resolveSupportSnapshot(
  versionInfo: SupportSnapshotFields & { snapshot?: string | null }
): string {
  if (versionInfo.snapshot != null && versionInfo.snapshot.trim() !== '') {
    return versionInfo.snapshot;
  }
  return formatSupportSnapshot(versionInfo);
}
