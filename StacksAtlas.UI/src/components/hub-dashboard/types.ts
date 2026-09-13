export interface FederatedNodeRow {
  id: string;
  name?: string;
  client?: string;
  building?: string;
  room?: string;
  ipAddress?: string;
  httpPort?: number;
  httpsPort?: number;
  os?: string;
  status?: string;
  version?: string;
  databaseSize?: number;
  lastSeenUtc?: string;
  deviceCount?: number;
  securityGrade?: number;
  stabilityScore?: number;
  syncUsers?: boolean;
  syncUserRegistry?: boolean;
  syncSsoSettings?: boolean;
  syncAlertSettings?: boolean;
  syncSiemSettings?: boolean;
  delegateAlertDispatch?: boolean;
  licenseTier?: string;
  isPortable?: boolean;
  overrideAlertSettings?: boolean;
  overrideSiemSettings?: boolean;
}

export interface HubFleetStats {
  totalNodes: number;
  onlineNodes: number;
  setupRequiredNodes: number;
  totalDevices: number;
  criticalAlerts: number;
}

export interface HubAlertItem {
  id: string;
  alertType?: string;
  triggeredAt: string;
  deviceId?: string;
  deviceName?: string;
  deviceIp?: string;
  nodeId?: string;
  nodeName?: string;
  room?: string;
}

export interface HubFleetFilters {
  search: string;
  client: string;
  building: string;
}

export interface HubToastState {
  open: boolean;
  message: string;
  severity: 'success' | 'error';
}
