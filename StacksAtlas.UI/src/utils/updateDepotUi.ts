import type { UpdateDepotStatus } from '../models/Updates';
import { compareSemver } from './fleetVersionDrift';

export function formatBytes(bytes: number): string {
  if (bytes <= 0) return '0 B';
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function listDepotVersionRows(status: UpdateDepotStatus | null | undefined): {
  channel: string;
  version: string;
  artifactCount: number;
  totalBytes: number;
  hasManifest: boolean;
  hasSignature: boolean;
}[] {
  if (!status?.channels?.length) return [];
  return status.channels.flatMap((channel) =>
    channel.versions.map((version) => ({
      channel: channel.channel,
      version: version.version,
      artifactCount: version.artifacts?.length ?? 0,
      totalBytes: version.totalBytes ?? 0,
      hasManifest: !!version.hasManifest,
      hasSignature: !!version.hasSignature,
    })),
  );
}

export function depotStatusSummaryLabel(status: UpdateDepotStatus | null | undefined): string {
  const rows = listDepotVersionRows(status);
  if (rows.length === 0) return 'Depot empty (nothing staged yet)';
  if (rows.length === 1) {
    const row = rows[0];
    return `Staged ${row.channel} v${row.version} (${row.artifactCount} artifact${row.artifactCount === 1 ? '' : 's'}, ${formatBytes(row.totalBytes)})`;
  }
  return `${rows.length} staged package(s) across ${status?.channels.length ?? 0} channel(s)`;
}

/** Highest staged semver for a channel (stable/preview), or null if empty / unknown.
 * Prefers packages with a manifest (matches Hub TryGetLatestStagedRelease).
 */
export function latestStagedVersionForChannel(
  status: UpdateDepotStatus | null | undefined,
  channel: string | null | undefined,
): string | null {
  if (!status?.channels?.length || !channel?.trim()) return null;
  const want = channel.trim().toLowerCase();
  const entry = status.channels.find((c) => (c.channel || '').trim().toLowerCase() === want);
  if (!entry?.versions?.length) return null;

  const candidates = entry.versions.filter((row) => row.hasManifest && row.version?.trim());
  const pool = candidates.length > 0 ? candidates : entry.versions.filter((row) => row.version?.trim());

  let best: string | null = null;
  for (const row of pool) {
    const version = row.version!.trim();
    if (!best) {
      best = version;
      continue;
    }
    const cmp = compareSemver(version, best);
    if (cmp !== null && cmp > 0) best = version;
    else if (cmp === null && version.localeCompare(best, undefined, { numeric: true }) > 0) best = version;
  }
  return best;
}
