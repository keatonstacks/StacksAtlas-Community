import type { Device } from '../models/Device';

export type AttachmentParentOption = {
  id: string;
  label: string;
  nodeId?: string;
};

export function resolveAttachmentParentScope(params: {
  filterNodeId?: string;
  isHub: boolean;
  selectedDevices: Pick<Device, 'nodeId'>[];
}): { nodeId?: string; mixedSites: boolean } {
  const filter = params.filterNodeId?.trim();
  if (filter) {
    return { nodeId: filter, mixedSites: false };
  }

  if (!params.isHub) {
    return { nodeId: undefined, mixedSites: false };
  }

  const nodeIds = [
    ...new Set(
      params.selectedDevices
        .map((d) => d.nodeId?.trim())
        .filter((id): id is string => Boolean(id)),
    ),
  ];

  if (nodeIds.length === 1) {
    return { nodeId: nodeIds[0], mixedSites: false };
  }

  if (nodeIds.length > 1) {
    return { nodeId: undefined, mixedSites: true };
  }

  return { nodeId: undefined, mixedSites: false };
}

export function formatAttachmentParentOptionLabel(
  baseLabel: string,
  nodeId: string | undefined,
  nodesMap: Record<string, string>,
  includeSitePrefix: boolean,
): string {
  const label = baseLabel.trim();
  if (!includeSitePrefix || !nodeId?.trim()) {
    return label;
  }

  const site = nodesMap[nodeId] || nodeId;
  return `${site} · ${label}`;
}

export function mapAttachmentParentRows(
  rows: {
    id: string;
    label?: string;
    ipAddress?: string;
    nodeId?: string;
  }[],
  nodesMap: Record<string, string>,
  includeSitePrefix: boolean,
): AttachmentParentOption[] {
  return (rows || []).map((row) => {
    const base = row.label?.trim() || row.ipAddress?.trim() || row.id;
    return {
      id: row.id,
      nodeId: row.nodeId,
      label: formatAttachmentParentOptionLabel(base, row.nodeId, nodesMap, includeSitePrefix),
    };
  });
}
