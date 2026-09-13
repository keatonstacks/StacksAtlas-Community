import type { Device } from '../../models/Device';

export type DeviceGroupExpansion = {
  action: 'expand' | 'collapse';
  id: number;
};

export type DeviceGroupBy =
  | 'none'
  | 'node'
  | 'client'
  | 'location'
  | 'uplink'
  | 'status'
  | 'vendor';

export const HUB_GROUP_BY_OPTIONS: DeviceGroupBy[] = [
  'none',
  'node',
  'client',
  'location',
  'uplink',
  'status',
  'vendor',
];

export const NODE_GROUP_BY_OPTIONS: DeviceGroupBy[] = [
  'none',
  'uplink',
  'status',
  'vendor',
  'location',
];

export function normalizeDeviceGroupBy(value: string | null | undefined, isHub: boolean): DeviceGroupBy {
  const allowed = isHub ? HUB_GROUP_BY_OPTIONS : NODE_GROUP_BY_OPTIONS;
  return allowed.includes(value as DeviceGroupBy) ? (value as DeviceGroupBy) : 'none';
}

export function deviceGroupByStorageKey(isHub: boolean): string {
  return isHub ? 'device_grid_groupby_hub' : 'device_grid_groupby_node';
}

export function resolveUplinkGroupKey(
  device: Device,
  parentNameById?: Record<string, string>,
): string {
  if (!device.attachmentParentDeviceId && !device.attachmentParentMac) {
    return 'NO UPLINK';
  }
  const parentId = device.attachmentParentDeviceId;
  if (parentId && parentNameById?.[parentId]) {
    return parentNameById[parentId].toUpperCase();
  }
  if (device.attachmentParentName?.trim()) {
    return device.attachmentParentName.trim().toUpperCase();
  }
  if (device.attachmentParentMac?.trim()) {
    return device.attachmentParentMac.trim().toUpperCase();
  }
  if (parentId) {
    return parentId.toUpperCase();
  }
  return 'NO UPLINK';
}

export function resolveLocationGroupKey(device: Device): string {
  const location = device.location?.trim();
  if (location) return location.toUpperCase();

  const parts = [device.building?.trim(), device.room?.trim()].filter(Boolean);
  if (parts.length > 0) return parts.join(' / ').toUpperCase();

  return 'UNASSIGNED LOCATION';
}

export function resolveDeviceGroupKey(
  device: Device,
  groupBy: DeviceGroupBy,
  nodesMap?: Record<string, string>,
  parentNameById?: Record<string, string>,
): string {
  if (groupBy === 'node') {
    const rawNodeId = device.nodeId || '';
    const friendlyName = nodesMap?.[rawNodeId];
    return friendlyName ? friendlyName.toUpperCase() : (rawNodeId.toUpperCase() || 'LOCAL NODE');
  }
  if (groupBy === 'client') {
    return device.client?.toUpperCase() || 'UNASSIGNED CLIENT';
  }
  if (groupBy === 'location') {
    return resolveLocationGroupKey(device);
  }
  if (groupBy === 'uplink') {
    return resolveUplinkGroupKey(device, parentNameById);
  }
  if (groupBy === 'status') {
    return (device.status || 'unknown').toUpperCase();
  }
  if (groupBy === 'vendor') {
    return device.vendor?.toUpperCase() || 'UNASSIGNED VENDOR';
  }
  return 'UNASSIGNED';
}

export function buildDeviceGroups(
  devices: Device[],
  groupBy: DeviceGroupBy,
  nodesMap?: Record<string, string>,
  parentNameById?: Record<string, string>,
): Record<string, Device[]> | null {
  if (groupBy === 'none') return null;

  const map: Record<string, Device[]> = {};

  devices.forEach((device) => {
    const groupKey = resolveDeviceGroupKey(device, groupBy, nodesMap, parentNameById);

    if (!map[groupKey]) {
      map[groupKey] = [];
    }
    map[groupKey].push(device);
  });

  return map;
}
