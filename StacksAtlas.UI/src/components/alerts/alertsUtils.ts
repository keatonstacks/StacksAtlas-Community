import type { FilterState } from '../FilterScopeToolbar';
import type { AlertEvent, AlertsTabId, AlertsUrlParams, GroupedEvent, SystemEvent } from './types';

export const getRelativeTime = (dateString: string) => {
  const now = new Date();
  const past = new Date(dateString);
  const diffInMs = now.getTime() - past.getTime();
  const diffInMins = Math.floor(diffInMs / 60000);
  if (diffInMins < 1) return 'Just now';
  if (diffInMins < 60) return `${diffInMins}m ago`;
  const diffInHours = Math.floor(diffInMins / 60);
  if (diffInHours < 24) return `${diffInHours}h ago`;
  return past.toLocaleDateString();
};

export const normalizeIpAddress = (ip?: string) => {
  if (!ip) return '';
  const trimmed = ip.trim();
  if (trimmed.toLowerCase().startsWith('::ffff:')) return trimmed.slice(7);
  const lastColon = trimmed.lastIndexOf(':');
  if (lastColon > 0 && trimmed.includes('.')) return trimmed.slice(lastColon + 1);
  return trimmed;
};

export const isAuditOnlyAlert = (item: AlertEvent) =>
  item.alertType === 'NodeDecoupled' ||
  !!item.errorMessage?.toLowerCase().includes('fleet audit') ||
  !!item.errorMessage?.toLowerCase().includes('historical replay') ||
  !!item.errorMessage?.toLowerCase().includes('notification not dispatched');

export const getIdString = (id: unknown): string => {
  if (!id) return '';
  if (typeof id === 'string') return id === '[object Object]' ? '' : id;
  const obj = id as Record<string, unknown>;
  if (obj.$oid) return String(obj.$oid);
  if (typeof obj.toHexString === 'function') return (obj.toHexString as () => string)();
  if (obj.hex) return String(obj.hex);
  if (obj.timestamp && obj.increment) {
    if (obj.value) return String(obj.value);
    return `${obj.timestamp}-${obj.machine}-${obj.pid}-${obj.increment}`;
  }
  return String(id);
};

const scopeMatch = (value: string | undefined, filter: string) => {
  if (!filter) return true;
  return (value || '').toLowerCase().includes(filter.toLowerCase());
};

export const matchesScopeFilter = (
  item: { client?: string; building?: string; room?: string; nodeId?: string },
  filters: FilterState,
) => {
  if (filters.nodeIds.length > 0 && item.nodeId && !filters.nodeIds.includes(item.nodeId)) {
    return false;
  }
  if (!scopeMatch(item.client, filters.client)) return false;
  if (!scopeMatch(item.building, filters.building)) return false;
  if (!scopeMatch(item.room, filters.room)) return false;
  return true;
};

export const groupSystemEvents = (events: SystemEvent[], deletedIds: Set<string>): GroupedEvent[] => {
  const groups: Record<string, GroupedEvent> = {};

  events.forEach((evt) => {
    const safeId = getIdString(evt.id);
    if (!safeId || deletedIds.has(safeId)) return;

    const gKey = `${evt.deviceIp || 'system'}-${evt.type}-${evt.message}`;
    if (!groups[gKey]) {
      groups[gKey] = {
        ...evt,
        groupKey: gKey,
        count: 1,
        lastSeen: evt.timestamp,
        allIds: [safeId],
      };
    } else {
      groups[gKey].count += 1;
      if (!groups[gKey].allIds.includes(safeId)) {
        groups[gKey].allIds.push(safeId);
      }
    }
  });

  return Object.values(groups);
};

export const parseAlertsTab = (raw: string | null): AlertsTabId => {
  if (raw === 'history' || raw === 'audit' || raw === 'live') return raw;
  return 'live';
};

export const parseAlertsUrlParams = (search: string): AlertsUrlParams => {
  const params = new URLSearchParams(search);
  const incomingNodeId = params.get('nodeId');
  const incomingSeverity = params.get('severity');

  return {
    tab: parseAlertsTab(params.get('tab')),
    search: params.get('search') || '',
    severity: incomingSeverity
      ? incomingSeverity.charAt(0).toUpperCase() + incomingSeverity.slice(1).toLowerCase()
      : 'All',
    filters: {
      nodeIds: incomingNodeId ? [incomingNodeId] : [],
      client: params.get('client') || '',
      building: params.get('building') || '',
      room: params.get('room') || '',
    },
  };
};

export const buildAlertsPageUrl = (opts?: {
  tab?: AlertsTabId;
  nodeId?: string;
  client?: string;
  building?: string;
  room?: string;
  search?: string;
  severity?: string;
}): string => {
  const params = new URLSearchParams();
  if (opts?.tab && opts.tab !== 'live') params.set('tab', opts.tab);
  if (opts?.nodeId) params.set('nodeId', opts.nodeId);
  if (opts?.client) params.set('client', opts.client);
  if (opts?.building) params.set('building', opts.building);
  if (opts?.room) params.set('room', opts.room);
  if (opts?.search) params.set('search', opts.search);
  if (opts?.severity && opts.severity !== 'All') params.set('severity', opts.severity);
  const qs = params.toString();
  return qs ? `/alerts?${qs}` : '/alerts';
};
