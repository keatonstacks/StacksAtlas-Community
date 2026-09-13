import type { FilterState } from '../FilterScopeToolbar';

export type { FilterState };

export type AlertsTabId = 'live' | 'history' | 'audit';

export const ALERTS_TABS: { id: AlertsTabId; label: string }[] = [
  { id: 'live', label: 'Live Events' },
  { id: 'history', label: 'Alert History' },
  { id: 'audit', label: 'Audit / Replay' },
];

export interface AlertsDevice {
  id: string;
  name: string;
  ipAddress: string;
  macAddress?: string;
  lastSeen?: string;
}

export interface SystemEvent {
  id: unknown;
  timestamp: string;
  type: string;
  message: string;
  severity: 'Info' | 'Warning' | 'Critical';
  deviceIp?: string;
  subnet?: string;
  nodeId?: string;
  nodeName?: string;
  client?: string;
  building?: string;
  room?: string;
}

export interface AlertEvent {
  id: string;
  triggeredAt: string;
  deviceId: string;
  deviceName: string;
  deviceIp: string;
  alertType: string;
  sentToEmails: string[];
  sentToWebhooks: string[];
  success: boolean;
  errorMessage?: string;
  nodeId?: string;
  nodeName?: string;
  client?: string;
  building?: string;
  room?: string;
}

export interface GroupedEvent extends SystemEvent {
  groupKey: string;
  count: number;
  lastSeen: string;
  allIds: string[];
}

export type HistorySortKey = 'triggeredAt' | 'alertType' | 'deviceName' | 'success';

export interface AlertsUrlParams {
  tab: AlertsTabId;
  search: string;
  severity: string;
  filters: FilterState;
}
