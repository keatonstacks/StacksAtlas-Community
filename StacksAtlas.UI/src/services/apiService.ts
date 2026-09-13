import API_BASE_URL from '../config/api';
import type {
  ApplianceVersionInfo,
  PendingHubUpdateDto,
  UpdateApplyResult,
  UpdateChannel,
  UpdateCheckResult,
  UpdateDepotStageResult,
  UpdateDepotStatus,
  UpdateScheduleDto,
} from '../models/Updates';

export type {
  ApplianceVersionInfo,
  PendingHubUpdateDto,
  UpdateApplyResult,
  UpdateChannel,
  UpdateCheckResult,
  UpdateDepotStageResult,
  UpdateDepotStatus,
  UpdateScheduleDto,
} from '../models/Updates';

export type FreshStartScope =
  | 'ApplianceOnly'
  | 'FleetInventoryOnly'
  | 'ApplianceAndFleet'
  | 'FactoryReset';

export type SnapshotStoreKind = 'Appliance' | 'Fleet';

export interface OpenAvcLinkSummary {
  stacksAtlasDeviceId: string;
  openAvcDeviceId: string;
  driverName?: string;
  driverId?: string;
}

export interface OpenAvcDrawerContext {
  configured: boolean;
  integrationEnabled: boolean;
  integrationStatus?: 'disabled' | 'online' | 'offline' | 'auth_failed' | 'hub_relay';
  openAvcReachable?: boolean;
  openAvcAuthValid?: boolean;
  linked: boolean;
  hubRelay?: boolean;
  openAvcDeviceId?: string;
  driverId?: string;
  driverName?: string;
  deviceName?: string;
  suggestion?: {
    openAvcDeviceId: string;
    name?: string;
    ip?: string;
    driverId?: string;
    driverName?: string;
    matchReason?: string;
  };
  commands?: { id: string; label: string }[];
  macros?: { id: string; name: string }[];
  allMacros?: { id: string; name: string }[];
  pinnedMacroIds?: string[];
  hasMacroPins?: boolean;
  state?: Record<string, string>;
  displayState?: Record<string, string>;
  lastMessage?: string;
  lastError?: string;
  lastRawResponse?: string;
  parsedSerial?: string;
  readings?: { label: string; value: string }[];
  availableDevices?: {
    id: string;
    name?: string;
    ip?: string;
    driverId?: string;
    driverName?: string;
  }[];
}

/* eslint-disable @typescript-eslint/no-explicit-any */
const extractId = (id: any): string => {
  if (!id) return "";
  if (typeof id === 'string' && id !== "[object Object]") return id;
  try {
    if (id?.$oid) return id.$oid;
    if (id?.id?.$oid) return id.id.$oid;
    if (id?.id && typeof id.id === 'string') return id.id;
    if (id.timestamp && id.machine) {
      return `${id.timestamp}-${id.machine}-${id.pid}-${id.increment}`;
    }
    const jsonStr = JSON.stringify(id);
    const match = jsonStr.match(/[0-9a-fA-F]{24}/);
    if (match) return match[0];
  } catch (e) {
    console.error("ID Parsing Error:", e);
  }
  return "";
};

// --- TOKEN MANAGEMENT ---
let authToken: string | null = localStorage.getItem('token');

const setToken = (token: string | null) => {
  authToken = token;
};

const fetchJson = async (endpoint: string, options?: RequestInit) => {
  const baseUrl = API_BASE_URL.endsWith('/') ? API_BASE_URL.slice(0, -1) : API_BASE_URL;
  const cleanEndpoint = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;

  const headers: HeadersInit = {
    'Content-Type': 'application/json',
    ...(options?.headers || {}),
  };

  if (authToken) {
    (headers as any)['Authorization'] = `Bearer ${authToken}`;
  }

  // Cache busting for GET requests
  let finalUrl = `${baseUrl}${cleanEndpoint}`;
  if (options?.method === 'GET' || !options?.method) {
    const separator = finalUrl.includes('?') ? '&' : '?';
    finalUrl = `${finalUrl}${separator}_t=${new Date().getTime()}`;
  }

  const response = await fetch(finalUrl, {
    ...options,
    headers,
  });

  if (response.status === 401) {
    // Session expired or server restarted with new JWT key.
    // Clear the stale token and redirect to login  -  but only once
    // (multiple parallel requests will all 401 simultaneously).
    if (authToken) {
      authToken = null;
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      // Redirect only if we're not already on the login page
      if (!window.location.hash.includes('login') && !window.location.pathname.includes('login')) {
        window.location.reload();
      }
    }
    throw new Error("UNAUTHORIZED");
  }

  const text = await response.text();

  if (!response.ok) {
    let errorMessage = `API Error: ${response.status} ${response.statusText}`;
    if (text) {
      try {
        const jsonBody = JSON.parse(text);
        if (jsonBody.message) errorMessage = jsonBody.message;
        else if (jsonBody.title) errorMessage = jsonBody.title;
        else if (jsonBody.errors) {
          const first = Object.values(jsonBody.errors).flat()[0];
          if (typeof first === 'string') errorMessage = first;
        }
      } catch {
        errorMessage = text;
      }
    }
    throw new Error(errorMessage);
  }

  if (!text || response.status === 204) {
    return null;
  }

  try {
    return JSON.parse(text);
  } catch {
    return text;
  }
};

const fetchBlob = async (endpoint: string, options?: RequestInit) => {
  const baseUrl = API_BASE_URL.endsWith('/') ? API_BASE_URL.slice(0, -1) : API_BASE_URL;
  const cleanEndpoint = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;

  const headers: HeadersInit = {
    ...(options?.headers || {}),
  };

  if (authToken) {
    (headers as any)['Authorization'] = `Bearer ${authToken}`;
  }

  const response = await fetch(`${baseUrl}${cleanEndpoint}`, {
    ...options,
    headers,
  });

  if (response.status === 401) {
    if (authToken) {
      authToken = null;
      localStorage.removeItem('token');
      localStorage.removeItem('user');
      if (!window.location.hash.includes('login') && !window.location.pathname.includes('login')) {
        window.location.reload();
      }
    }
    throw new Error("UNAUTHORIZED");
  }

  if (!response.ok) {
    throw new Error(`API Error: ${response.status} ${response.statusText}`);
  }

  return await response.blob();
};

export const ApiService = {
  // Auth
  setToken,
  login: (creds: any) => fetchJson('/api/auth/login', { method: 'POST', body: JSON.stringify(creds) }),
  setupAdmin: (data: any) => fetchJson('/api/auth/setup', { method: 'POST', body: JSON.stringify(data) }),
  ensurePortableSession: () => fetchJson('/api/auth/portable-session', { method: 'POST' }),
  checkAuth: () => fetchJson('/api/auth/validate'),
  checkAuthStatus: () => fetchJson('/api/auth/status'),
  getOnboardingStatus: () => fetchJson('/api/onboarding/status'),
  updateOnboarding: (data: {
    siteName?: string;
    confirmNetwork?: boolean;
    completeFoundation?: boolean;
    hubOnlySetup?: boolean;
    skipNetworkConfirm?: boolean;
  }) => fetchJson('/api/onboarding', { method: 'PUT', body: JSON.stringify(data) }),
  getLicenseHardwareId: () => fetchJson('/api/license/hardware-id'),
  getSsoConfig: () => fetchJson('/api/auth/sso/config'),
  getLicenseStatus: () => fetchJson('/api/license/status'),
  activateLicense: (key: string) => fetchJson('/api/license/activate', { method: 'POST', body: JSON.stringify({ licenseKey: key }) }),

  // Leases
  getLeaseHistoryByMac: (mac: string) => fetchJson(`/api/leases/mac/${encodeURIComponent(mac)}`),
  getLeaseHistoryByIp: (ip: string) => fetchJson(`/api/leases/ip/${ip}`),

  // Devices
  getDevices: async (params?: {
    skip?: number;
    take?: number;
    search?: string;
    status?: string;
    showArchived?: boolean;
    nodeId?: string;
    client?: string;
    building?: string;
    room?: string;
    vlanTags?: string[];
    attachmentKind?: string;
    attachmentPort?: string;
    attachmentParentId?: string;
  }) => {
    let url = '/api/devices';
    if (params) {
      const query = new URLSearchParams();
      if (params.skip !== undefined) query.append('skip', params.skip.toString());
      if (params.take !== undefined) query.append('take', params.take.toString());
      if (params.search !== undefined) query.append('search', params.search);
      if (params.status !== undefined) query.append('status', params.status);
      if (params.showArchived !== undefined) query.append('showArchived', params.showArchived.toString());
      if (params.nodeId !== undefined && params.nodeId !== 'all') query.append('nodeId', params.nodeId);
      if (params.client !== undefined && params.client !== 'all') query.append('client', params.client);
      if (params.building !== undefined && params.building !== 'all') query.append('building', params.building);
      if (params.room !== undefined && params.room !== 'all') query.append('room', params.room);
      params.vlanTags?.forEach(tag => query.append('vlanTag', tag));
      if (params.attachmentKind && params.attachmentKind !== 'All') {
        query.append('attachmentKind', params.attachmentKind);
      }
      if (params.attachmentPort?.trim()) {
        query.append('attachmentPort', params.attachmentPort.trim());
      }
      if (params.attachmentParentId) {
        query.append('attachmentParentId', params.attachmentParentId);
      }
      url += `?${query.toString()}`;
    }
    const data = await fetchJson(url);
    return data; // Return full object with devices and totalCount
  },
  getDiscoveryVlanTags: (nodeId?: string) => {
    const query = nodeId ? `?nodeId=${encodeURIComponent(nodeId)}` : '';
    return fetchJson(`/api/devices/vlan-tags${query}`);
  },
  getAttachmentParents: (params?: { nodeId?: string; excludeDeviceId?: string }) => {
    const query = new URLSearchParams();
    if (params?.nodeId) query.append('nodeId', params.nodeId);
    if (params?.excludeDeviceId) query.append('excludeDeviceId', params.excludeDeviceId);
    const suffix = query.toString() ? `?${query.toString()}` : '';
    return fetchJson(`/api/devices/attachment-parents${suffix}`) as Promise<
      { id: string; label: string; ipAddress?: string; type?: string; nodeId?: string }[]
    >;
  },
  getDevice: (rawId: string) => fetchJson(`/api/devices/${extractId(rawId)}`),
  updateDevice: (id: string, data: any) => fetchJson(`/api/devices/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  updateDeviceName: (id: string, name: string) => fetchJson(`/api/devices/${id}/name`, { method: 'PATCH', body: JSON.stringify({ name }) }),
  updateDeviceLocation: (id: string, location: string) => fetchJson(`/api/devices/${id}/location`, { method: 'PATCH', body: JSON.stringify({ location }) }),
  updateDeviceModel: (id: string, model: string) => fetchJson(`/api/devices/${id}/model`, { method: 'PATCH', body: JSON.stringify({ model }) }),
  updateDeviceVendor: (id: string, vendor: string) => fetchJson(`/api/devices/${id}/vendor`, { method: 'PATCH', body: JSON.stringify({ vendor }) }),
  updateDeviceHostname: (id: string, hostname: string) => fetchJson(`/api/devices/${extractId(id)}/hostname`, { method: 'PATCH', body: JSON.stringify({ hostname }) }),
  updateDeviceType: (id: string, type: string) => fetchJson(`/api/devices/${id}/type`, { method: 'PATCH', body: JSON.stringify({ type }) }),
  updateDeviceAsset: (
    rawId: string,
    asset: {
      serialNumber?: string | null;
      assetTag?: string | null;
      firmwareVersion?: string | null;
      warrantyExpiresUtc?: string | null;
    },
  ) => {
    const id = extractId(rawId);
    const body: Record<string, string | null> = {};
    if (asset.serialNumber !== undefined) body.serialNumber = asset.serialNumber;
    if (asset.assetTag !== undefined) body.assetTag = asset.assetTag;
    if (asset.firmwareVersion !== undefined) body.firmwareVersion = asset.firmwareVersion;
    if (asset.warrantyExpiresUtc !== undefined) body.warrantyExpiresUtc = asset.warrantyExpiresUtc;
    return fetchJson(`/api/devices/${id}/asset`, { method: 'PATCH', body: JSON.stringify(body) });
  },
  importDeviceAssets: (payload: { csv?: string; rows?: Array<{
    nodeId?: string | null;
    macAddress?: string | null;
    ipAddress?: string | null;
    serialNumber?: string | null;
    assetTag?: string | null;
    firmwareVersion?: string | null;
    warrantyExpires?: string | null;
  }> }) =>
    fetchJson('/api/devices/asset-import', { method: 'POST', body: JSON.stringify(payload) }),
  clearManualDeviceAssetFields: (rawId: string) =>
    fetchJson(`/api/devices/${extractId(rawId)}/asset/clear-manual`, { method: 'PATCH' }),
  updateDeviceAttachment: (
    rawId: string,
    attachment: {
      kind?: string | null;
      port?: string | null;
      parentDeviceId?: string | null;
    },
  ) => {
    const id = extractId(rawId);
    return fetchJson(`/api/devices/${id}/attachment`, {
      method: 'PATCH',
      body: JSON.stringify({
        kind: attachment.kind ?? 'Unknown',
        port: attachment.port ?? null,
        parentDeviceId: attachment.parentDeviceId ?? null,
      }),
    });
  },
  updateDeviceManager: (id: string, userId: string | null, username: string | null) =>
    fetchJson(`/api/devices/${id}/manager`, { method: 'PATCH', body: JSON.stringify({ userId, username }) }),
  updateDeviceAlerts: (id: string, enabled: boolean) =>
    fetchJson(`/api/devices/${id}/alerts`, { method: 'PATCH', body: JSON.stringify({ enabled }) }),
  runDevicePing: (id: string) => fetchJson(`/api/devices/${id}/ping`, { method: 'POST' }),
  deleteDevice: (rawId: any, hardDelete: boolean = false) => {
    const cleanId = extractId(rawId);
    let url = `/api/devices/${cleanId}`;
    if (hardDelete) {
      url += '?hard=true';
    }
    return fetchJson(url, {
      method: 'DELETE',
    });
  },
  restoreDevice: (rawId: any) => {
    const cleanId = extractId(rawId);
    return fetchJson(`/api/devices/${cleanId}/restore`, { method: 'POST' });
  },
  getRemovedDevices: () => fetchJson('/api/devices/removed'),
  restoreToFleet: (rawId: any) => {
    const cleanId = extractId(rawId);
    return fetchJson(`/api/devices/${cleanId}/restore-to-fleet`, { method: 'POST' });
  },
  resetMetrics: (rawId: any) => {
    const cleanId = extractId(rawId);
    return fetchJson(`/api/devices/${cleanId}/metrics/reset`, { method: 'POST' });
  },
  acknowledgeRisk: (rawId: any, issue: string) => {
    const cleanId = extractId(rawId);
    return fetchJson(`/api/devices/${cleanId}/risks/acknowledge`, {
      method: 'POST',
      body: JSON.stringify({ issue })
    });
  },
  wakeDevice: (id: string) => fetchJson(`/api/devices/${id}/wake`, { method: 'POST' }),

  // Dashboard Summary (lightweight)
  getDashboardSummary: () => fetchJson('/api/dashboard/summary'),

  // Deep Scan
  triggerDeepScan: (id: string) => fetchJson(`/api/scan/deep/${id}`, { method: 'POST' }),
  getDeepScanStatus: (id: string) => fetchJson(`/api/scan/deep/${id}/status`),
  getDeepScanResults: (id: string) => fetchJson(`/api/scan/deep/${id}`),

  // Events & Alerts
  getRecentEvents: (limit = 20, nodeIds?: string[]) => {
    let url = `/api/events/recent?limit=${limit}`;
    if (nodeIds && nodeIds.length > 0) {
      nodeIds.forEach(id => {
        if (id !== 'all') url += `&nodeId=${encodeURIComponent(id)}`;
      });
    }
    return fetchJson(url);
  },
  clearAllEvents: (nodeIds?: string[]) => {
    let url = '/api/events';
    if (nodeIds && nodeIds.length > 0) {
      const q = nodeIds.filter(id => id !== 'all').map(id => `nodeId=${encodeURIComponent(id)}`).join('&');
      if (q) url += `?${q}`;
    }
    return fetchJson(url, { method: 'DELETE' });
  },
  deleteEvent: (rawId: any) => {
    const cleanId = (typeof rawId === 'string' && rawId.includes('-')) ? rawId : extractId(rawId);
    if (!cleanId) return Promise.resolve({ Removed: false });
    return fetchJson(`/api/events/${cleanId}`, { method: 'DELETE' });
  },

  // Network & Reports
  getNetworkSettings: () => fetchJson('/api/settings/network'),
  updateNetworkSettings: (settings: {
    subnets?: NetworkScope[];
    interfaceConfigs?: NetworkInterfaceConfig[];
    interfaceRoleMappings?: InterfaceRoleMapping[];
    offlineStrikeThreshold?: number;
    enableInstantRecovery?: boolean;
    pingTimeoutMs?: number;
    maxParallelPings?: number;
    enableNmapDeepScan?: boolean;
    maxConcurrentDeepScans?: number;
  }) =>
    fetchJson('/api/settings/network', { method: 'PUT', body: JSON.stringify(settings) }),

  getReportSummary: (params?: { nodeIds?: string[], client?: string, building?: string, room?: string }) => {
    let url = '/api/reports/summary';
    if (params) {
      const query = new URLSearchParams();
      if (params.nodeIds && params.nodeIds.length > 0) {
        params.nodeIds.forEach(id => {
          if (id !== 'all') query.append('nodeId', id);
        });
      }
      if (params.client !== undefined && params.client !== 'all') query.append('client', params.client);
      if (params.building !== undefined && params.building !== 'all') query.append('building', params.building);
      if (params.room !== undefined && params.room !== 'all') query.append('room', params.room);
      const qs = query.toString();
      if (qs) url += `?${qs}`;
    }
    return fetchJson(url);
  },
  downloadInventoryCsv: async (params?: { nodeIds?: string[], client?: string, building?: string, room?: string }) => {
    let url = '/api/reports/inventory/csv';
    if (params) {
      const query = new URLSearchParams();
      if (params.nodeIds && params.nodeIds.length > 0) {
        params.nodeIds.forEach(id => {
          if (id !== 'all') query.append('nodeId', id);
        });
      }
      if (params.client !== undefined && params.client !== 'all') query.append('client', params.client);
      if (params.building !== undefined && params.building !== 'all') query.append('building', params.building);
      if (params.room !== undefined && params.room !== 'all') query.append('room', params.room);
      const qs = query.toString();
      if (qs) url += `?${qs}`;
    }
    const blob = await fetchBlob(url);
    const downloadUrl = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = downloadUrl;
    a.download = `StacksAtlas_Inventory_${new Date().toISOString().split('T')[0]}.csv`;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(downloadUrl);
    document.body.removeChild(a);
  },
  downloadAuditPdf: async (params?: { nodeIds?: string[], client?: string, building?: string, room?: string }) => {
    let url = '/api/reports/inventory/pdf';
    if (params) {
      const query = new URLSearchParams();
      if (params.nodeIds && params.nodeIds.length > 0) {
        params.nodeIds.forEach(id => {
          if (id !== 'all') query.append('nodeId', id);
        });
      }
      if (params.client !== undefined && params.client !== 'all') query.append('client', params.client);
      if (params.building !== undefined && params.building !== 'all') query.append('building', params.building);
      if (params.room !== undefined && params.room !== 'all') query.append('room', params.room);
      const qs = query.toString();
      if (qs) url += `?${qs}`;
    }
    const blob = await fetchBlob(url);
    const downloadUrl = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = downloadUrl;
    a.download = `StacksAtlas_AuditReport_${new Date().toISOString().split('T')[0]}.pdf`;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(downloadUrl);
    document.body.removeChild(a);
  },

  // Polling & Maintenance
  getPollingSettings: () => fetchJson('/api/settings/polling'),
  updatePollingSettings: (settings: { refreshIntervalSeconds: number }) =>
    fetchJson('/api/settings/polling', { method: 'PUT', body: JSON.stringify(settings) }),
  getDatabaseHealth: () => fetchJson('/api/health/database'),
  getSweepHistory: (limit: number = 50) => fetchJson(`/api/sweep/recent?limit=${limit}`),
  getSystemStatus: () => fetchJson('/api/sweep/status'),

  // User Management
  getUsers: () => fetchJson('/api/users'),
  getCurrentUser: () => fetchJson('/api/users/me'),
  createUser: (username: string, password: string, role: string, email?: string) =>
    fetchJson('/api/users', { method: 'POST', body: JSON.stringify({ username, password, role, email }) }),
  updateUser: (id: string, data: { email?: string; role?: string }) =>
    fetchJson(`/api/users/${id}`, { method: 'PATCH', body: JSON.stringify(data) }),
  resetUserPassword: (id: string, password: string) => fetchJson(`/api/users/${id}/password`, { method: 'PATCH', body: JSON.stringify({ newPassword: password }) }),
  deleteUser: (id: string) => fetchJson(`/api/users/${id}`, { method: 'DELETE' }),
  getApiKeys: () => fetchJson('/api/users/me/keys'),
  createApiKey: (label: string) => fetchJson('/api/users/me/keys', { method: 'POST', body: JSON.stringify({ label }) }),
  revokeApiKey: (keyId: string) => fetchJson(`/api/users/me/keys/${keyId}`, { method: 'DELETE' }),

  // Email Settings
  getEmailSettings: () => fetchJson('/api/settings/email'),
  updateEmailSettings: (settings: any) => fetchJson('/api/settings/email', { method: 'PUT', body: JSON.stringify(settings) }),
  sendTestEmail: (toAddress: string) => fetchJson('/api/settings/email/test', { method: 'POST', body: JSON.stringify({ toAddress }) }),

  // Alert Preferences
  updateAlertPreferences: (userId: string, prefs: AlertPreferences) =>
    fetchJson(`/api/users/${userId}/alerts`, { method: 'PATCH', body: JSON.stringify(prefs) }),
  getAlertPreferences: (userId: string) =>
    fetchJson(`/api/users/${userId}/alerts`),
  getAlertHistory: (limit = 50, nodeIds?: string[]) => {
    let url = `/api/users/alerts/history?limit=${limit}`;
    if (nodeIds && nodeIds.length > 0) {
      nodeIds.forEach(id => {
        if (id !== 'all') url += `&nodeId=${encodeURIComponent(id)}`;
      });
    }
    return fetchJson(url);
  },
  deleteAlert: (id: string) =>
    fetchJson(`/api/users/alerts/history/${id}`, { method: 'DELETE' }),
  clearAlertHistory: (nodeIds?: string[]) => {
    let url = '/api/users/alerts/history';
    if (nodeIds && nodeIds.length > 0) {
      const q = nodeIds.filter(id => id !== 'all').map(id => `nodeId=${encodeURIComponent(id)}`).join('&');
      if (q) url += `?${q}`;
    }
    return fetchJson(url, { method: 'DELETE' });
  },

  // Snapshots (Data Sovereignty)
  getSnapshots: () =>
    fetchJson('/api/snapshots').then((data: any) => ({
      snapshots: data.snapshots ?? data,
      hubDatabasePresent: data.hubDatabasePresent ?? false,
    })),
  createSnapshot: (label?: string) => fetchJson('/api/snapshots', { method: 'POST', body: JSON.stringify({ label }) }),
  deleteSnapshot: (fileName: string) => fetchJson(`/api/snapshots/${fileName}`, { method: 'DELETE' }),
  restoreSnapshot: (fileName: string) =>
    fetchJson(`/api/snapshots/restore/${encodeURIComponent(fileName)}`, { method: 'POST' }),
  triggerFreshStart: (scope: FreshStartScope) =>
    fetchJson('/api/snapshots/fresh-start', {
      method: 'POST',
      body: JSON.stringify({ scope }),
    }),
  downloadSnapshot: async (fileName: string) => {
    const blob = await fetchBlob(`/api/snapshots/download/${encodeURIComponent(fileName)}`);
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  },

  // Network Transparency
  getInterfaces: (scope: 'discovery' | 'policy' = 'discovery') =>
    fetchJson(`/api/network/interfaces?scope=${scope}`),
  getNetworkRoutingDiagnostics: () => fetchJson('/api/network/routing-diagnostics'),
  getActiveInterface: () => fetchJson('/api/network/active'),
  getScanningRange: () => fetchJson('/api/network/range'),
  getTopology: (nodeId?: string) => {
    const query = nodeId ? `?nodeId=${encodeURIComponent(nodeId)}` : '';
    return fetchJson(`/api/network/topology${query}`);
  },
  setScanningInterface: (id: string | null) => fetchJson('/api/network/interface', { method: 'POST', body: JSON.stringify({ id }) }),
  getScanStatus: () => fetchJson('/api/network/status'),
  getPtpStatus: () => fetchJson('/api/network/ptp'),

  // Database Controls
  getDatabaseStatus: () => fetchJson('/api/database/status'),
  testDatabaseConnection: (config: { provider: string, connectionString: string }) => fetchJson('/api/database/test', { method: 'POST', body: JSON.stringify(config) }),
  applyDatabaseSettings: (config: { provider: string, connectionString: string, migrateData: boolean }) => fetchJson('/api/database/apply', { method: 'POST', body: JSON.stringify(config) }),

  // System Control
  getSystemConfig: () => fetchJson('/api/system/config'),
  getSystemVersion: (): Promise<ApplianceVersionInfo> => fetchJson('/api/system/version'),
  getUpdateDepotStatus: (): Promise<UpdateDepotStatus> => fetchJson('/api/system/updates/depot/status'),
  stageUpdateDepot: (channel?: UpdateChannel): Promise<UpdateDepotStageResult> => {
    const query = channel ? `?channel=${encodeURIComponent(channel)}` : '';
    return fetchJson(`/api/system/updates/stage${query}`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },
  checkForUpdates: (channel?: UpdateChannel): Promise<UpdateCheckResult> => {
    const query = channel ? `?channel=${encodeURIComponent(channel)}` : '';
    return fetchJson(`/api/system/updates/check${query}`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },
  getUpdateSchedule: (): Promise<UpdateScheduleDto> => fetchJson('/api/system/updates/schedule'),
  putUpdateSchedule: (schedule: UpdateScheduleDto): Promise<UpdateScheduleDto> =>
    fetchJson('/api/system/updates/schedule', {
      method: 'PUT',
      body: JSON.stringify(schedule),
    }),
  getPendingHubUpdate: (): Promise<PendingHubUpdateDto> => fetchJson('/api/system/updates/pending-hub'),
  clearPendingHubUpdate: (): Promise<{ message: string }> =>
    fetchJson('/api/system/updates/pending-hub', { method: 'DELETE' }),
  notifyNodeUpdate: (nodeId: string, channel?: UpdateChannel): Promise<{ message: string; availableVersion?: string }> => {
    const query = channel ? `?channel=${encodeURIComponent(channel)}` : '';
    return fetchJson(`/api/federation/nodes/${encodeURIComponent(nodeId)}/notify-update${query}`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },
  triggerNodeUpdate: (nodeId: string, channel?: UpdateChannel): Promise<{ message: string; availableVersion?: string }> => {
    const query = channel ? `?channel=${encodeURIComponent(channel)}` : '';
    return fetchJson(`/api/federation/nodes/${encodeURIComponent(nodeId)}/trigger-update${query}`, {
      method: 'POST',
      body: JSON.stringify({}),
    });
  },
  applyUpdate: async (channel?: UpdateChannel): Promise<UpdateApplyResult> => {
    const query = channel ? `?channel=${encodeURIComponent(channel)}` : '';
    const baseUrl = API_BASE_URL.endsWith('/') ? API_BASE_URL.slice(0, -1) : API_BASE_URL;
    const controller = new AbortController();
    const timeout = window.setTimeout(() => controller.abort(), 35 * 60 * 1000);
    try {
      const headers: HeadersInit = { 'Content-Type': 'application/json' };
      if (authToken) {
        (headers as Record<string, string>)['Authorization'] = `Bearer ${authToken}`;
      }
      const response = await fetch(`${baseUrl}/api/system/updates/apply${query}`, {
        method: 'POST',
        headers,
        body: JSON.stringify({}),
        signal: controller.signal,
      });
      const text = await response.text();
      if (!response.ok) {
        let errorMessage = `API Error: ${response.status} ${response.statusText}`;
        if (text) {
          try {
            const jsonBody = JSON.parse(text);
            if (jsonBody.message) errorMessage = jsonBody.message;
          } catch {
            errorMessage = text;
          }
        }
        throw new Error(errorMessage);
      }
      return text ? JSON.parse(text) : ({} as UpdateApplyResult);
    } finally {
      window.clearTimeout(timeout);
    }
  },
  getHealth: (): Promise<{ status: string; timestamp: string }> => fetchJson('/api/health'),
  getSystemRuntimeInfo: () => fetchJson('/api/system/info'),
  closePortable: () => fetchJson('/api/system/portable/close', { method: 'POST' }),
  getAuthSettings: () => fetchJson('/api/system/auth'),
  updateAuthSettings: (settings: any) => fetchJson('/api/system/auth', { method: 'PUT', body: JSON.stringify(settings) }),
  testOidc: (settings: any) => fetchJson('/api/system/auth/test-oidc', { method: 'POST', body: JSON.stringify(settings) }),
  testLdap: (settings: any) => fetchJson('/api/system/auth/test-ldap', { method: 'POST', body: JSON.stringify(settings) }),
  downloadCertificate: async () => {
    const blob = await fetchBlob('/api/system/certificate');
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = 'StacksAtlas-Appliance.cer';
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  },
  getCertificateTrustStatus: () => fetchJson('/api/system/certificate/status'),
  trustApplianceCertificate: () => fetchJson('/api/system/certificate/trust', { method: 'POST' }),
  setDashboardPreference: (scheme: 'http' | 'https') =>
    fetchJson('/api/system/dashboard-preference', { method: 'PUT', body: JSON.stringify({ scheme }) }),
  updateSystemPorts: (ports: { 
    httpPort: number, 
    httpsPort: number,
    syslogEnabled?: boolean,
    syslogHost?: string,
    syslogPort?: number,
    syslogAppName?: string
  }) =>
    fetchJson('/api/system/ports', { method: 'POST', body: JSON.stringify(ports) }),
  restartSystem: () => fetchJson('/api/system/restart', { method: 'POST' }),
  getSystemLogs: (limit: number = 200) => fetchJson(`/api/system/logs?limit=${limit}`),
  getRemoteSystemLogs: (nodeId: string, limit: number = 200) => fetchJson(`/api/system/logs/remote?nodeId=${nodeId}&limit=${limit}`),
  getAuditEvents: (opts?: {
    skip?: number;
    take?: number;
    action?: string;
    nodeIds?: string[];
    since?: string;
    until?: string;
    hubOnly?: boolean;
  }) => {
    const params = new URLSearchParams({
      skip: String(opts?.skip ?? 0),
      take: String(opts?.take ?? 100),
    });
    if (opts?.action) params.set('action', opts.action);
    opts?.nodeIds?.forEach((id) => params.append('nodeId', id));
    if (opts?.since) params.set('since', opts.since);
    if (opts?.until) params.set('until', opts.until);
    if (opts?.hubOnly) params.set('hubOnly', 'true');
    return fetchJson(`/api/audit/events?${params.toString()}`);
  },
  toggleDebugLogging: (enabled: boolean, nodeId?: string) => {
    const params = new URLSearchParams({ enabled: String(enabled) });
    if (nodeId) params.set('nodeId', nodeId);
    return fetchJson(`/api/system/logging?${params.toString()}`, { method: 'POST' });
  },

  // Tools
  runTraceroute: (target: string, deviceId?: string) => {
    const params = new URLSearchParams({ target });
    if (deviceId) params.set('deviceId', deviceId);
    return fetchJson(`/api/tools/traceroute?${params.toString()}`);
  },

  // Security & Portability
  rotateEncryptionKey: (newPassword: string) => fetchJson('/api/security/rotate-key', { method: 'POST', body: JSON.stringify({ newPassword }) }),
  exportPortableDatabase: async (masterPassword: string) => {
    const response = await fetch(`${API_BASE_URL}/api/security/export-portable`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${authToken}`
      },
      body: JSON.stringify({ masterPassword })
    });
    if (response.status === 401) {
      if (authToken) {
        authToken = null;
        localStorage.removeItem('token');
        localStorage.removeItem('user');
        if (!window.location.hash.includes('login') && !window.location.pathname.includes('login')) {
          window.location.reload();
        }
      }
      throw new Error("UNAUTHORIZED");
    }
    if (!response.ok) throw new Error("Portable export failed");
    const blob = await response.blob();
    const url = window.URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `StacksAtlas_Portable_Backup_${new Date().toISOString().split('T')[0]}.db`;
    document.body.appendChild(a);
    a.click();
    window.URL.revokeObjectURL(url);
    document.body.removeChild(a);
  },
  importPortableDatabase: (file: File, masterPassword: string) => {
    const formData = new FormData();
    formData.append('file', file);
    formData.append('masterPassword', masterPassword);

    return fetch(`${API_BASE_URL}/api/security/import-portable`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${authToken}`
      },
      body: formData
    }).then(async res => {
      if (res.status === 401) {
        if (authToken) {
          authToken = null;
          localStorage.removeItem('token');
          localStorage.removeItem('user');
          if (!window.location.hash.includes('login') && !window.location.pathname.includes('login')) {
            window.location.reload();
          }
        }
        throw new Error("UNAUTHORIZED");
      }
      if (!res.ok) {
        const text = await res.text();
        throw new Error(text || "Import failed");
      }
      return res.json();
    });
  },

  // Federation
  getFederationStats: () => fetchJson('/api/federation/stats'),
  getFederationNodes: () => fetchJson('/api/federation/nodes'),
  updateFederationNode: (id: string, data: any) => fetchJson(`/api/federation/nodes/${id}`, { method: 'PATCH', body: JSON.stringify(data) }),
  getFederationSettings: () => fetchJson('/api/federation/settings'),
  getFederationBacklogStatus: () => fetchJson('/api/federation/backlog-status'),
  updateFederationSettings: (settings: any) => fetchJson('/api/federation/settings', { method: 'PATCH', body: JSON.stringify(settings) }),
  getTailscaleStatus: () => fetchJson('/api/federation/tailscale/status'),
  testTailscaleReachability: (draft?: {
    useTailscaleForHubConnection?: boolean;
    hubTailscaleMagicDns?: string;
    hubTailscaleIpv4?: string;
  }) => fetchJson('/api/federation/tailscale/test-reachability', {
    method: 'POST',
    body: JSON.stringify(draft ?? {}),
  }),
  getTailscaleTransportSummary: () => fetchJson('/api/federation/tailscale/transport-summary'),
  decoupleNode: () => fetchJson('/api/federation/decouple', { method: 'POST' }),
  deleteFederationNode: (id: string) => fetchJson(`/api/federation/nodes/${id}`, { method: 'DELETE' }),
  resetFederationSite: (id: string) =>
    fetchJson(`/api/federation/nodes/${encodeURIComponent(id)}/reset-site`, { method: 'POST' }),
  getEnrollmentString: (options?: { tailscale?: boolean }) => {
    const query = options?.tailscale ? '?tailscale=true' : '';
    return fetchJson(`/api/federation/nodes/enrollment-string${query}`);
  },
  enrollNodeMtls: (data: {
    enrollmentString: string;
    friendlyName: string;
    client?: string;
    building?: string;
    room?: string;
  }) => fetchJson('/api/federation/enroll-node-mtls', { method: 'POST', body: JSON.stringify(data) }),
  enrollNodeFromHub: (data: {
    nodeUrl: string;
    nodeAdminPassword: string;
    nodeId?: string;
    nodeName: string;
    client?: string;
    building?: string;
    room?: string;
  }) => fetchJson('/api/federation/nodes/enroll', { method: 'POST', body: JSON.stringify(data) }),
  sendNodeCommand: (nodeId: string, command: { commandType: string, targetId?: string, parameters?: Record<string, string> }) =>
    fetchJson(`/api/federation/nodes/${nodeId}/command`, { method: 'POST', body: JSON.stringify(command) }),
  
  // Webhooks
  getWebhooks: () => fetchJson('/api/webhooks'),
  createWebhook: (data: any) => fetchJson('/api/webhooks', { method: 'POST', body: JSON.stringify(data) }),
  updateWebhook: (id: string, data: any) => fetchJson(`/api/webhooks/${id}`, { method: 'PUT', body: JSON.stringify(data) }),
  deleteWebhook: (id: string) => fetchJson(`/api/webhooks/${id}`, { method: 'DELETE' }),
  testWebhook: (id: string) => fetchJson(`/api/webhooks/${id}/test`, { method: 'POST' }),

  // OpenAVC integration (1.8.5 spike)
  getOpenAvcSettings: () => fetchJson('/api/integrations/openavc/settings'),
  updateOpenAvcSettings: (settings: {
    enabled: boolean;
    baseUrl: string;
    username: string;
    password?: string;
  }) =>
    fetchJson('/api/integrations/openavc/settings', {
      method: 'PUT',
      body: JSON.stringify({
        enabled: settings.enabled,
        baseUrl: settings.baseUrl,
        username: settings.username,
        password: settings.password ?? '',
      }),
    }),
  testOpenAvcConnection: () =>
    fetchJson('/api/integrations/openavc/test', { method: 'POST' }) as Promise<{
      success: boolean;
      message?: string;
      version?: string;
    }>,
  getOpenAvcHealth: () =>
    fetchJson('/api/integrations/openavc/health') as Promise<{
      status: string;
      configured: boolean;
      reachable: boolean;
      authValid: boolean;
      message?: string;
    }>,
  getOpenAvcNetworkCandidates: () =>
    fetchJson('/api/integrations/openavc/candidates') as Promise<
      Array<{
        deviceId: string;
        ipAddress: string;
        hostname?: string;
        baseUrl: string;
        version?: string;
        source: string;
      }>
    >,
  getOpenAvcDeviceLink: (deviceId: string) =>
    fetchJson(`/api/integrations/openavc/link/${deviceId}`) as Promise<{
      linked: boolean;
      openAvcDeviceId?: string;
      integrationEnabled?: boolean;
      configured?: boolean;
      hubMode?: boolean;
    }>,
  saveOpenAvcDeviceLink: (deviceId: string, openAvcDeviceId: string) =>
    fetchJson(`/api/integrations/openavc/link/${deviceId}`, {
      method: 'PUT',
      body: JSON.stringify({ openAvcDeviceId }),
    }),
  deleteOpenAvcDeviceLink: (deviceId: string) =>
    fetchJson(`/api/integrations/openavc/link/${deviceId}`, { method: 'DELETE' }),
  saveOpenAvcMacroPins: (deviceId: string, macroIds: string[]) =>
    fetchJson(`/api/integrations/openavc/link/${deviceId}/macros`, {
      method: 'PUT',
      body: JSON.stringify({ macroIds }),
    }) as Promise<{ success: boolean; message?: string }>,
  sendOpenAvcDeviceCommand: (deviceId: string, command: string, params?: Record<string, unknown>) =>
    fetchJson(`/api/integrations/openavc/link/${deviceId}/command`, {
      method: 'POST',
      body: JSON.stringify({ command, params: params ?? null }),
    }) as Promise<{ success: boolean; message?: string; result?: unknown }>,
  getOpenAvcDrawerContext: (deviceId: string, ip?: string, hostname?: string) => {
    const params = new URLSearchParams();
    if (ip) params.set('ip', ip);
    if (hostname) params.set('hostname', hostname);
    const query = params.toString() ? `?${params.toString()}` : '';
    return fetchJson(`/api/integrations/openavc/context/${deviceId}${query}`) as Promise<OpenAvcDrawerContext>;
  },
  getOpenAvcLinks: () =>
    fetchJson('/api/integrations/openavc/links') as Promise<OpenAvcLinkSummary[]>,
  executeOpenAvcMacro: (deviceId: string, macroId: string) =>
    fetchJson(
      `/api/integrations/openavc/macros/${encodeURIComponent(macroId)}/execute?deviceId=${encodeURIComponent(deviceId)}`,
      { method: 'POST' },
    ) as Promise<{ success: boolean; message?: string; result?: unknown }>,
};

// Types
export interface AlertPreferences {
  alertEmail: string;
  alertsEnabled: boolean;
  webhookEnabled: boolean;
  preferredWebhookId: string | null;
  preferredWebhookIds: string[];
  alertOnDeviceDown: boolean;
  alertOnDeviceUp: boolean;
  alertOnNewDevice: boolean;
  alertSeverity: string;
  systemWideWebhooksActive?: boolean;
}

export type NetworkRole = 'Default' | 'HubCommunication' | 'AlertsAndSiem' | 'DiscoveryFallback';

export interface InterfaceRoleMapping {
  interfaceId: string;
  role: NetworkRole;
}

export interface NetworkInterfaceConfig {
  interfaceId: string;
  roles: NetworkRole[];
  defaultVlanTag?: string | null;
}

export interface FederationBacklogStatus {
  hubConnected: boolean;
  deepSleep: boolean;
  fullSyncRequired: boolean;
  disconnectedSinceUtc?: string | null;
  pendingTelemetryBatches: number;
  pendingDeviceChanges: number;
  pendingLogs: number;
  pendingEvents: number;
  pendingAlerts: number;
  maxPendingDeviceChanges: number;
  backlogSeverity: string;
  summary: string;
}

export interface NetworkScope {
  id?: string;
  cidr: string;
  interfaceId?: string | null;
  vlanTag?: string | null;
  enableArp: boolean;
  enablePing: boolean;
  enableMdns: boolean;
  enableUpnp: boolean;
}

export interface NetworkInterfaceInfo {
  id: string;
  name: string;
  description: string;
  ipAddress: string;
  subnetMask: string;
  gatewayAddress?: string;
  type: string;
  speed: number;
  status: string;
}

export interface InterfaceRpFilterStatus {
  interfaceName: string;
  interfaceId?: string | null;
  ipAddress?: string | null;
  rpFilterValue?: number | null;
  mode: string;
  usedForDiscovery: boolean;
}

export interface NetworkRoutingDiagnostic {
  isLinux: boolean;
  isMultiHomedProfile: boolean;
  physicalInterfaceCount: number;
  distinctDiscoveryInterfaceCount: number;
  rpFilterCheckApplicable: boolean;
  allRpFilterValue?: number | null;
  defaultRpFilterValue?: number | null;
  rpFilterMode: string;
  requiresRemediation: boolean;
  severity: string;
  summary: string;
  detail: string;
  remediationSteps: string[];
  sysctlCommands: string[];
  perInterface: InterfaceRpFilterStatus[];
}

export interface TailscaleStatus {
  cliInstalled: boolean;
  connected: boolean;
  hostname?: string;
  magicDnsName?: string;
  tailnetIpv4?: string;
  keyExpiryUtc?: string;
  keyExpired: boolean;
  keyExpiringSoon: boolean;
  backendState?: string;
  errorMessage?: string;
  detectedViaInterface?: boolean;
}

export interface TailscaleReachabilityProbe {
  label: string;
  host: string;
  port: number;
  reachable: boolean;
  latencyMs?: number;
  error?: string;
}

export interface TailscaleReachabilityResult {
  anyReachable: boolean;
  summary: string;
  probes: TailscaleReachabilityProbe[];
}

export interface TailscaleTransportSummary {
  discoveryInterface?: string;
  federationInterface?: string;
  useTailscaleForHubConnection: boolean;
  hubTailscaleMagicDns?: string;
  hubTailscaleIpv4?: string;
}

