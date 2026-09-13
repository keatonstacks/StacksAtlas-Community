import { describe, expect, it } from 'vitest';
import type { Device } from '../../models/Device';
import { getDeviceExportValue, resolveInventoryExportColumns } from './deviceBulkExport';
import { DEFAULT_COLUMN_ORDER } from './types';

function baseDevice(overrides: Partial<Device> = {}): Device {
  return {
    id: '00000000-0000-0000-0000-000000000001',
    name: 'Test Device',
    ipAddress: '192.168.1.10',
    hostname: null,
    nodeId: 'daaf4a56',
    macAddress: '00A1595689A4',
    openPorts: [80, 443],
    vendor: 'Vendor',
    status: 'online',
    type: 'Unknown',
    model: 'Model',
    confidenceScore: 90,
    location: 'Room A',
    firstSeen: '2026-01-01T00:00:00Z',
    lastSeen: '2026-06-29T21:21:30.658-04:00',
    scanCount: 1,
    totalSweepsSeen: 1,
    totalSweepsOnline: 1,
    uptimePercent: 99.5,
    flapCount: 0,
    lastStateChangeUtc: null,
    averageLatencyMs: null,
    lastSweepId: null,
    managedByUserId: null,
    managedByUsername: 'admin',
    securityGrade: 1,
    ...overrides,
  };
}

describe('deviceBulkExport', () => {
  it('maps security column to grade label', () => {
    expect(getDeviceExportValue('security', baseDevice({ securityGrade: 0 }))).toBe('HEALTHY');
    expect(getDeviceExportValue('security', baseDevice({ securityGrade: 1 }))).toBe('WARN');
    expect(getDeviceExportValue('security', baseDevice({ securityGrade: 2 }))).toBe('CRITICAL');
  });

  it('maps managedBy column to username', () => {
    expect(getDeviceExportValue('managedBy', baseDevice())).toBe('admin');
    expect(getDeviceExportValue('managedBy', baseDevice({ managedByUsername: null }))).toBe('');
  });

  it('resolves node friendly name when nodesMap is provided', () => {
    const nodesMap = { daaf4a56: 'Natick Apartment' };
    expect(getDeviceExportValue('nodeId', baseDevice(), nodesMap)).toBe('Natick Apartment');
    expect(getDeviceExportValue('nodeId', baseDevice({ nodeId: '' }), nodesMap)).toBe('LOCAL');
  });

  it('formats MAC, status, ports, and last seen consistently', () => {
    const device = baseDevice();
    expect(getDeviceExportValue('macAddress', device)).toBe('00:A1:59:56:89:A4');
    expect(getDeviceExportValue('status', device)).toBe('Online');
    expect(getDeviceExportValue('openPorts', device)).toBe('80, 443');
    expect(getDeviceExportValue('lastSeen', device)).toBe('2026-06-30T01:21:30.658Z');
  });

  it('preserves special characters in device names', () => {
    const name = getDeviceExportValue('name', baseDevice({ name: '55" OLED TV' }));
    expect(name).toBe('55" OLED TV');
  });

  it('resolveInventoryExportColumns respects visibility and column order', () => {
    const visibility = Object.fromEntries(
      DEFAULT_COLUMN_ORDER.map((key) => [key, key === 'name' || key === 'status' || key === 'ipAddress']),
    ) as Record<import('./types').ColumnKey, boolean>;

    const columnOrder = ['ipAddress', 'status', 'name'] as import('./types').ColumnKey[];
    const visible = resolveInventoryExportColumns(columnOrder, visibility, false, 'visible');
    expect(visible).toEqual(['ipAddress', 'status', 'name']);

    const all = resolveInventoryExportColumns(columnOrder, visibility, false, 'all');
    expect(all).toEqual(DEFAULT_COLUMN_ORDER);
  });
});
