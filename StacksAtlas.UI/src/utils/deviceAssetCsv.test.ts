import { describe, expect, it } from 'vitest';
import { buildAssetCsvRows, parseAssetCsv, parseCsvLine } from './deviceAssetCsv';
import type { Device } from '../models/Device';

const sampleDevice: Device = {
  id: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
  name: 'Host',
  ipAddress: '10.0.0.1',
  hostname: null,
  nodeId: 'node-1',
  macAddress: 'AABBCCDDEEFF',
  openPorts: [],
  vendor: 'Acme',
  status: 'online',
  type: 'Workstation',
  model: 'X1',
  confidenceScore: 1,
  location: '',
  firstSeen: '',
  lastSeen: '',
  scanCount: 0,
  totalSweepsSeen: 0,
  totalSweepsOnline: 0,
  uptimePercent: 100,
  flapCount: 0,
  lastStateChangeUtc: null,
  averageLatencyMs: null,
  lastSweepId: null,
  managedByUserId: null,
  managedByUsername: null,
  alertsEnabled: true,
  serialNumber: 'SN-1',
  assetTag: 'TAG-1',
  firmwareVersion: '2.0',
  warrantyExpiresUtc: '2027-06-01T00:00:00Z',
};

describe('deviceAssetCsv', () => {
  it('buildAssetCsvRows includes Device ID and warranty', () => {
    const [header, row] = buildAssetCsvRows([sampleDevice], false);
    expect(header).toContain('Device ID');
    expect(header).toContain('Node ID');
    expect(row).toContain(sampleDevice.id);
    expect(row).toContain('"2027-06-01"');
    expect(row).toContain('"SN-1"');
    expect(row).toContain('"AA:BB:CC:DD:EE:FF"');
  });

  it('round-trips exported asset CSV through the import parser', () => {
    const [, row] = buildAssetCsvRows([sampleDevice], false);
    const header = 'Name,Node ID,Device ID,IP Address,MAC Address,Serial,Asset Tag,Firmware,Warranty Expires';
    const { rows, hasAssetColumns } = parseAssetCsv(`${header}\n${row}`);
    expect(hasAssetColumns).toBe(true);
    expect(rows[0].deviceId).toBe(sampleDevice.id);
    expect(rows[0].node).toBe('node-1');
    expect(rows[0].mac).toBe('AA:BB:CC:DD:EE:FF');
    expect(rows[0].serial).toBe('SN-1');
    expect(rows[0].assetTag).toBe('TAG-1');
    expect(rows[0].firmware).toBe('2.0');
    expect(rows[0].warranty).toBe('2027-06-01');
  });

  it('parseCsvLine handles quoted commas', () => {
    expect(parseCsvLine('"a,b",c')).toEqual(['a,b', 'c']);
  });

  it('parseAssetCsv maps device id and asset columns', () => {
    const csv = `Device ID,MAC Address,Asset Tag,Serial
a1b2c3d4-e5f6-7890-abcd-ef1234567890,AA:BB:CC:DD:EE:FF,RACK-9,SN-9`;

    const { rows, hasAssetColumns } = parseAssetCsv(csv);
    expect(hasAssetColumns).toBe(true);
    expect(rows[0].deviceId).toBe('a1b2c3d4-e5f6-7890-abcd-ef1234567890');
    expect(rows[0].assetTag).toBe('RACK-9');
    expect(rows[0].serial).toBe('SN-9');
  });
});
