export type DeviceViewMode = 'active' | 'archive' | 'removed';

export type ColumnKey =
  | "name"
  | "ipAddress"
  | "hostname"
  | "macAddress"
  | "openPorts"
  | "vendor"
  | "status"
  | "type"
  | "model"
  | "location"
  | "lastSeen"
  | "uptimePercent"
  | "stabilityScore"
  | "managedBy"
  | "nodeId"
  | "client"
  | "building"
  | "room"
  | "security"
  | "discoveryVlanTag"
  | "serialNumber"
  | "firmwareVersion"
  | "assetTag"
  | "warrantyExpiresUtc"
  | "attachmentKind"
  | "attachmentPort"
  | "attachmentParent";

export const columnLabels: Record<ColumnKey, string> = {
  name: "Name",
  ipAddress: "IP Address",
  macAddress: "MAC Address",
  hostname: "Hostname",
  vendor: "Vendor",
  model: "Model",
  type: "Type",
  location: "Location",
  status: "Status",
  uptimePercent: "Availability %",
  stabilityScore: "Stability",
  lastSeen: "Last Seen",
  openPorts: "Ports",
  managedBy: "Manager",
  nodeId: "Node",
  client: "Client",
  building: "Building",
  room: "Room",
  security: "Security",
  discoveryVlanTag: "VLAN",
  serialNumber: "Serial",
  firmwareVersion: "Firmware",
  assetTag: "Asset Tag",
  warrantyExpiresUtc: "Warranty",
  attachmentKind: "Mode",
  attachmentPort: "Port / SSID",
  attachmentParent: "Uplink",
};

/** Federation / multi-site columns  -  hidden in portable mode. */
export const FEDERATION_COLUMN_KEYS: ColumnKey[] = [
  "nodeId",
  "client",
  "building",
  "room",
  "discoveryVlanTag",
];

export function filterColumnsForPortable(keys: ColumnKey[]): ColumnKey[] {
  return keys.filter((key) => !FEDERATION_COLUMN_KEYS.includes(key));
}

export const DEFAULT_COLUMN_ORDER: ColumnKey[] = [
  "status",
  "security",
  "name",
  "nodeId",
  "client",
  "building",
  "room",
  "ipAddress",
  "discoveryVlanTag",
  "vendor",
  "type",
  "openPorts",
  "stabilityScore",
  "uptimePercent",
  "hostname",
  "model",
  "location",
  "managedBy",
  "lastSeen",
  "macAddress",
  "serialNumber",
  "assetTag",
  "firmwareVersion",
  "warrantyExpiresUtc",
  "attachmentKind",
  "attachmentPort",
  "attachmentParent",
];

export const PORTABLE_DEFAULT_COLUMN_ORDER: ColumnKey[] = filterColumnsForPortable(DEFAULT_COLUMN_ORDER);

/** Core columns for a readable default grid  -  detail fields live in the drawer. */
export const ESSENTIAL_COLUMN_KEYS: ReadonlySet<ColumnKey> = new Set([
  "status",
  "security",
  "name",
  "nodeId",
  "ipAddress",
  "vendor",
  "type",
  "uptimePercent",
  "lastSeen",
]);

export type ColumnVisibilityMap = Record<ColumnKey, boolean>;

export function buildEssentialColumnVisibility(): ColumnVisibilityMap {
  return Object.fromEntries(
    (Object.keys(columnLabels) as ColumnKey[]).map((key) => [
      key,
      ESSENTIAL_COLUMN_KEYS.has(key),
    ]),
  ) as ColumnVisibilityMap;
}

export function buildShowAllColumnVisibility(): ColumnVisibilityMap {
  return Object.fromEntries(
    (Object.keys(columnLabels) as ColumnKey[]).map((key) => [key, true]),
  ) as ColumnVisibilityMap;
}
