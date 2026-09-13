import type { FederatedNodeRow, HubFleetFilters, HubAlertItem } from './types';

export function formatHubIp(ip?: string): string {
  if (!ip) return ' - ';
  return ip.replace(/^.*:/, '');
}

export function filterFleetNodes(nodes: FederatedNodeRow[], filters: HubFleetFilters): FederatedNodeRow[] {
  const term = filters.search.toLowerCase().trim();
  return nodes.filter((n) => {
    if (filters.client && (n.client || '') !== filters.client) return false;
    if (filters.building && (n.building || '') !== filters.building) return false;
    if (!term) return true;
    return (
      n.name?.toLowerCase().includes(term) ||
      n.id?.toLowerCase().includes(term) ||
      n.ipAddress?.toLowerCase().includes(term) ||
      n.client?.toLowerCase().includes(term) ||
      n.building?.toLowerCase().includes(term) ||
      n.room?.toLowerCase().includes(term)
    );
  });
}

export function uniqueFilterValues(nodes: FederatedNodeRow[], key: 'client' | 'building'): string[] {
  const set = new Set<string>();
  for (const n of nodes) {
    const v = n[key]?.trim();
    if (v) set.add(v);
  }
  return Array.from(set).sort((a, b) => a.localeCompare(b));
}

export function fleetHealthPercent(online: number, total: number): number {
  return total > 0 ? Math.round((online / total) * 100) : 100;
}

export function formatDatabaseSizeMb(bytes?: number): string {
  if (bytes === undefined || bytes <= 0) return '0.00 MB';
  return `${(bytes / 1024 / 1024).toFixed(2)} MB`;
}

export function buildAlertClipboardText(alert: HubAlertItem): string {
  const isDown = alert.alertType === 'DeviceDown';
  const isUp = alert.alertType === 'DeviceUp';
  const isNew = alert.alertType === 'NewDeviceDiscovered';
  const isDecoupled = alert.alertType === 'NodeDecoupled';
  const isSiteReset = alert.alertType === 'SiteResetInitiated';

  return `[Anomaly Report]
EVENT: ${alert.alertType}
DEVICE: ${alert.deviceName || 'Unknown'} (${alert.deviceIp || 'No IP'})
SITE: ${alert.nodeName || 'Remote Node'}${alert.room ? ` - ${alert.room}` : ''}
TIME: ${new Date(alert.triggeredAt).toLocaleString()}
DETAILS: ${
    isDecoupled ? 'Node decoupled from fleet.' :
    isSiteReset ? 'Remote site reset initiated from Hub.' :
    isDown ? 'Device is OFFLINE.' :
    isUp ? 'Device is ONLINE.' :
    isNew ? 'Device was DISCOVERED.' : 'State changed.'
  }`;
}
