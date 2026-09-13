import type { Device } from '../models/Device';
import { formatMacForCsv, formatWarrantyDateForCsv } from './csvFormatUtils';

export const ASSET_CSV_HEADERS_PORTABLE = [
  'Name',
  'Device ID',
  'IP Address',
  'MAC Address',
  'Serial',
  'Asset Tag',
  'Firmware',
  'Warranty Expires',
] as const;

export const ASSET_CSV_HEADERS_HUB = [
  'Name',
  'Node ID',
  'Device ID',
  'IP Address',
  'MAC Address',
  'Serial',
  'Asset Tag',
  'Firmware',
  'Warranty Expires',
] as const;

function csvEscape(value: string): string {
  return `"${value.replace(/"/g, '""')}"`;
}

function formatWarranty(device: Device): string {
  return formatWarrantyDateForCsv(device.warrantyExpiresUtc);
}

function formatMac(device: Device): string {
  return formatMacForCsv(device.macAddress);
}

export function buildAssetCsvRows(devices: Device[], portable: boolean): string[] {
  const headers = portable ? ASSET_CSV_HEADERS_PORTABLE : ASSET_CSV_HEADERS_HUB;
  const rows = devices.map((device) => {
    const values = portable
      ? [
          device.name || '',
          device.id || '',
          device.ipAddress || '',
          formatMac(device),
          device.serialNumber || '',
          device.assetTag || '',
          device.firmwareVersion || '',
          formatWarranty(device),
        ]
      : [
          device.name || '',
          device.nodeId || '',
          device.id || '',
          device.ipAddress || '',
          formatMac(device),
          device.serialNumber || '',
          device.assetTag || '',
          device.firmwareVersion || '',
          formatWarranty(device),
        ];
    return values.map(csvEscape).join(',');
  });

  return [headers.join(','), ...rows];
}

export function downloadAssetCsv(devices: Device[], filenamePrefix: string, portable: boolean) {
  const csvContent = buildAssetCsvRows(devices, portable).join('\n');
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.setAttribute('href', url);
  link.setAttribute(
    'download',
    `${filenamePrefix}_${new Date().toISOString().split('T')[0]}.csv`,
  );
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

export function downloadAssetCsvTemplate(portable: boolean) {
  const headers = portable ? ASSET_CSV_HEADERS_PORTABLE : ASSET_CSV_HEADERS_HUB;
  const sample = portable
    ? ['Example Host', '00000000-0000-0000-0000-000000000001', '192.168.1.10', 'AA:BB:CC:DD:EE:FF', 'SN-001', 'RACK-1', '1.2.3', '2027-12-31']
    : ['Example Host', 'daaf4a56', '00000000-0000-0000-0000-000000000001', '192.168.1.10', 'AA:BB:CC:DD:EE:FF', 'SN-001', 'RACK-1', '1.2.3', '2027-12-31'];

  const csvContent = [headers.join(','), sample.map(csvEscape).join(',')].join('\n');
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.setAttribute('href', url);
  link.setAttribute('download', 'StacksAtlas_asset_import_template.csv');
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

/** Parse a simple CSV line respecting quoted commas. */
export function parseCsvLine(line: string): string[] {
  const cells: string[] = [];
  let current = '';
  let inQuotes = false;

  for (let i = 0; i < line.length; i++) {
    const c = line[i];
    if (c === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i++;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }
    if (c === ',' && !inQuotes) {
      cells.push(current);
      current = '';
      continue;
    }
    current += c;
  }

  cells.push(current);
  return cells;
}

const HEADER_ALIASES: Record<string, string> = {
  name: 'name',
  id: 'deviceId',
  'device id': 'deviceId',
  deviceid: 'deviceId',
  'stacksatlas id': 'deviceId',
  'stacksatlas device id': 'deviceId',
  'ip address': 'ip',
  ip: 'ip',
  'mac address': 'mac',
  mac: 'mac',
  'node id': 'node',
  node: 'node',
  serial: 'serial',
  'serial number': 'serial',
  'asset tag': 'assetTag',
  assettag: 'assetTag',
  firmware: 'firmware',
  'firmware version': 'firmware',
  'warranty expires': 'warranty',
  'warranty expiry': 'warranty',
  'warranty expires utc': 'warranty',
};

function normalizeHeader(raw: string): string {
  const key = raw.trim().replace(/^"|"$/g, '').toLowerCase();
  return HEADER_ALIASES[key] ?? key;
}

export function parseAssetCsv(text: string) {
  const records = text
    .replace(/\r\n/g, '\n')
    .split('\n')
    .filter((line) => line.trim().length > 0);

  if (records.length < 2) {
    return { rows: [] as Record<string, string | null>[], hasAssetColumns: false };
  }

  const headerKeys = parseCsvLine(records[0]).map(normalizeHeader);
  const hasAssetColumns = headerKeys.some((k) =>
    ['serial', 'assetTag', 'firmware', 'warranty'].includes(k),
  );

  const rows = records.slice(1).map((line) => {
    const cells = parseCsvLine(line);
    const row: Record<string, string | null> = {};
    headerKeys.forEach((key, idx) => {
      if (key === 'name') return;
      const value = idx < cells.length ? cells[idx].trim() : '';
      if (['deviceId', 'serial', 'assetTag', 'firmware', 'warranty', 'node', 'mac', 'ip'].includes(key)) {
        row[key] = value.length > 0 ? value : null;
      }
    });
    return row;
  });

  return { rows, hasAssetColumns };
}
