import type { Device } from "../../models/Device";
import { normalizeSecurityGrade, securityGradeLabel } from "../device-drawer/deviceDrawerUtils";
import { formatMacForCsv } from "../../utils/csvFormatUtils";
import {
  type ColumnKey,
  columnLabels,
  DEFAULT_COLUMN_ORDER,
  filterColumnsForPortable,
} from "./types";

export interface DeviceCsvExportOptions {
  portable?: boolean;
  /** Hub node id → friendly site name */
  nodesMap?: Record<string, string>;
  /** Attachment parent device id → display name */
  parentNameById?: Record<string, string>;
  /** Column keys to export; when omitted, exports all registry columns. */
  columnKeys?: ColumnKey[];
}

export function resolveInventoryExportColumns(
  columnOrder: ColumnKey[],
  visibility: Record<ColumnKey, boolean>,
  portable: boolean,
  mode: 'visible' | 'all',
): ColumnKey[] {
  const ordered = portable ? filterColumnsForPortable(columnOrder) : columnOrder;
  if (mode === 'all') {
    return portable ? filterColumnsForPortable(DEFAULT_COLUMN_ORDER) : DEFAULT_COLUMN_ORDER;
  }
  const visible = ordered.filter((key) => visibility[key]);
  return visible.length > 0 ? visible : (portable ? filterColumnsForPortable(DEFAULT_COLUMN_ORDER) : DEFAULT_COLUMN_ORDER);
}

function csvEscape(value: string): string {
  return `"${value.replace(/"/g, '""')}"`;
}

function formatMacForExport(mac?: string | null): string {
  return formatMacForCsv(mac);
}

function formatLastSeenForExport(value?: string | null): string {
  if (!value) return "";
  const parsed = new Date(value);
  if (Number.isNaN(parsed.getTime())) return value;
  return parsed.toISOString();
}

function formatStatusForExport(status?: string | null): string {
  if (!status) return "";
  const lower = status.toLowerCase();
  if (lower === "online") return "Online";
  if (lower === "offline") return "Offline";
  return status;
}

/** Resolve grid column keys to exportable string values. */
export function getDeviceExportValue(
  key: ColumnKey,
  device: Device,
  nodesMap?: Record<string, string>,
  parentNameById?: Record<string, string>,
): string {
  switch (key) {
    case "security": {
      const grade = normalizeSecurityGrade(device.securityGrade);
      return securityGradeLabel(grade);
    }
    case "managedBy":
      return device.managedByUsername ?? "";
    case "nodeId":
      if (!device.nodeId) return "LOCAL";
      return nodesMap?.[device.nodeId] ?? device.nodeId;
    case "macAddress":
      return formatMacForExport(device.macAddress);
    case "lastSeen":
      return formatLastSeenForExport(device.lastSeen);
    case "status":
      return formatStatusForExport(device.status);
    case "uptimePercent":
      return String(device.uptimePercent ?? 0);
    case "stabilityScore":
      return String(device.stabilityScore ?? 0);
    case "openPorts":
      return (device.openPorts ?? []).join(", ");
    case "warrantyExpiresUtc":
      return device.warrantyExpiresUtc ? device.warrantyExpiresUtc.slice(0, 10) : "";
    case "attachmentKind":
      return device.attachmentKind || "Unknown";
    case "attachmentPort":
      return device.attachmentPort ?? "";
    case "attachmentParent":
      if (!device.attachmentParentDeviceId) return "";
      return device.attachmentParentName
        ?? parentNameById?.[device.attachmentParentDeviceId]
        ?? device.attachmentParentMac
        ?? device.attachmentParentDeviceId;
    default: {
      const raw = device[key as keyof Device];
      if (raw === null || raw === undefined) return "";
      if (Array.isArray(raw)) return raw.join(", ");
      return String(raw);
    }
  }
}

function formatExportCell(
  key: ColumnKey,
  device: Device,
  nodesMap?: Record<string, string>,
  parentNameById?: Record<string, string>,
): string {
  return csvEscape(getDeviceExportValue(key, device, nodesMap, parentNameById));
}

/** Export a device list to CSV. */
export function exportDevicesToCsv(
  devices: Device[],
  filenamePrefix = "devices",
  options: DeviceCsvExportOptions = {},
) {
  const { portable = false, nodesMap, parentNameById, columnKeys } = options;
  const exportKeys: ColumnKey[] = columnKeys?.length
    ? columnKeys
    : (portable ? filterColumnsForPortable(DEFAULT_COLUMN_ORDER) : DEFAULT_COLUMN_ORDER);
  const headers = exportKeys.map((key) => columnLabels[key]);

  const rows = devices.map((device) =>
    exportKeys
      .map((key) => formatExportCell(key, device, nodesMap, parentNameById))
      .join(","),
  );

  const csvContent = [headers.join(","), ...rows].join("\n");
  const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.setAttribute("href", url);
  link.setAttribute(
    "download",
    `${filenamePrefix}_${new Date().toISOString().split("T")[0]}.csv`,
  );
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

const REMOVED_EXPORT_HEADERS_FULL = [
  "Name",
  "Node",
  "Node ID",
  "IP Address",
  "MAC Address",
  "Removed UTC",
  "Removed By",
  "Rediscovery Hits",
  "Last Rediscovery UTC",
  "Last Rediscovery IP",
] as const;

const REMOVED_EXPORT_HEADERS_PORTABLE = [
  "Name",
  "IP Address",
  "MAC Address",
  "Removed UTC",
  "Removed By",
  "Rediscovery Hits",
  "Last Rediscovery UTC",
  "Last Rediscovery IP",
] as const;

/** Export tombstone / removed-from-fleet devices with governance columns. */
export function exportRemovedDevicesToCsv(
  devices: Device[],
  filenamePrefix = "removed_devices",
  options: DeviceCsvExportOptions = {},
) {
  const { portable = false, nodesMap } = options;
  const headers = portable ? REMOVED_EXPORT_HEADERS_PORTABLE : REMOVED_EXPORT_HEADERS_FULL;

  const rows = devices.map((device) => {
    const nodeLabel = device.nodeId
      ? (nodesMap?.[device.nodeId] ?? device.nodeId)
      : "LOCAL";

    const values = portable
      ? [
          device.name || "",
          device.ipAddress || "",
          formatMacForExport(device.macAddress),
          formatLastSeenForExport(device.removedUtc),
          device.removedBy || "",
          String(device.rediscoveryHitCount ?? 0),
          formatLastSeenForExport(device.lastRediscoveryAttemptUtc),
          device.lastRediscoveryIp || "",
        ]
      : [
          device.name || "",
          nodeLabel,
          device.nodeId || "",
          device.ipAddress || "",
          formatMacForExport(device.macAddress),
          formatLastSeenForExport(device.removedUtc),
          device.removedBy || "",
          String(device.rediscoveryHitCount ?? 0),
          formatLastSeenForExport(device.lastRediscoveryAttemptUtc),
          device.lastRediscoveryIp || "",
        ];

    return values.map(csvEscape).join(",");
  });

  const csvContent = [headers.join(","), ...rows].join("\n");
  const blob = new Blob([csvContent], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.setAttribute("href", url);
  link.setAttribute(
    "download",
    `${filenamePrefix}_${new Date().toISOString().split("T")[0]}.csv`,
  );
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}
