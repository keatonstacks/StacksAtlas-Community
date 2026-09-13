/**
 * Read-only fleet version drift helpers for Hub Settings → Federation (1.9.2 Slice 1).
 * Mirrors StacksAtlas.Core SemverUtility: major.minor.patch prefix only.
 */

export type FleetVersionDriftKind = 'unknown' | 'current' | 'behind' | 'ahead';

export interface FleetVersionDriftRow {
  nodeId: string;
  nodeName: string;
  version: string | null;
  kind: FleetVersionDriftKind;
}

export interface FleetVersionDriftSummary {
  total: number;
  known: number;
  behind: number;
  ahead: number;
  current: number;
  unknown: number;
  rows: FleetVersionDriftRow[];
}

const SEMVER_PREFIX = /^(\d+)\.(\d+)\.(\d+)/;

export function tryParseSemver(input: string | null | undefined): [number, number, number] | null {
  if (!input || !input.trim()) return null;
  const match = SEMVER_PREFIX.exec(input.trim());
  if (!match) return null;
  return [Number(match[1]), Number(match[2]), Number(match[3])];
}

/** Negative if left < right, 0 if equal, positive if left > right. Null if either unparsable. */
export function compareSemver(left: string | null | undefined, right: string | null | undefined): number | null {
  const a = tryParseSemver(left);
  const b = tryParseSemver(right);
  if (!a || !b) return null;
  if (a[0] !== b[0]) return a[0] - b[0];
  if (a[1] !== b[1]) return a[1] - b[1];
  return a[2] - b[2];
}

export function classifyNodeVsHub(
  nodeVersion: string | null | undefined,
  hubVersion: string | null | undefined,
): FleetVersionDriftKind {
  if (!tryParseSemver(nodeVersion) || !tryParseSemver(hubVersion)) return 'unknown';
  const cmp = compareSemver(nodeVersion, hubVersion);
  if (cmp === null) return 'unknown';
  if (cmp < 0) return 'behind';
  if (cmp > 0) return 'ahead';
  return 'current';
}

export function summarizeFleetVersionDrift(
  nodes: { id?: string; name?: string; version?: string | null }[],
  hubVersion: string | null | undefined,
): FleetVersionDriftSummary {
  const rows: FleetVersionDriftRow[] = nodes.map((node) => {
    const version = node.version?.trim() ? node.version.trim() : null;
    return {
      nodeId: node.id ?? '',
      nodeName: node.name?.trim() || 'Unnamed Node',
      version,
      kind: classifyNodeVsHub(version, hubVersion),
    };
  });

  return {
    total: rows.length,
    known: rows.filter((r) => r.kind !== 'unknown').length,
    behind: rows.filter((r) => r.kind === 'behind').length,
    ahead: rows.filter((r) => r.kind === 'ahead').length,
    current: rows.filter((r) => r.kind === 'current').length,
    unknown: rows.filter((r) => r.kind === 'unknown').length,
    rows,
  };
}

export function fleetDriftSummaryLabel(summary: FleetVersionDriftSummary, hubVersion: string | null | undefined): string {
  const hubLabel = tryParseSemver(hubVersion) ? hubVersion!.trim() : 'unknown';
  if (summary.total === 0) return `Fleet versions vs Hub ${hubLabel}: no enrolled sites`;
  if (summary.behind === 0 && summary.unknown === 0) {
    return `Fleet versions: all ${summary.total} site(s) at Hub ${hubLabel}`;
  }
  const parts: string[] = [];
  if (summary.behind > 0) parts.push(`${summary.behind} of ${summary.total} behind Hub ${hubLabel}`);
  if (summary.ahead > 0) parts.push(`${summary.ahead} ahead`);
  if (summary.unknown > 0) parts.push(`${summary.unknown} unknown`);
  if (summary.current > 0 && summary.behind > 0) parts.push(`${summary.current} current`);
  return `Fleet versions: ${parts.join(' · ')}`;
}

export function fleetDriftKindLabel(kind: FleetVersionDriftKind): string {
  switch (kind) {
    case 'behind':
      return 'Behind Hub';
    case 'ahead':
      return 'Ahead of Hub';
    case 'current':
      return 'Current';
    default:
      return 'Version unknown';
  }
}

/**
 * Mirrors UpdateFleetPolicy.IsActionableOffer: staged package must be newer than the Node
 * (or Node version unknown). Used to enable Hub Notify / Update now vs depot, not Hub self-version.
 */
export function isActionableHubUpdateOffer(
  availableVersion: string | null | undefined,
  currentVersion: string | null | undefined,
): boolean {
  if (!availableVersion?.trim()) return false;
  if (!currentVersion?.trim()) return true;
  const cmp = compareSemver(availableVersion, currentVersion);
  if (cmp === null) {
    return availableVersion.trim().toLowerCase() !== currentVersion.trim().toLowerCase();
  }
  return cmp > 0;
}
