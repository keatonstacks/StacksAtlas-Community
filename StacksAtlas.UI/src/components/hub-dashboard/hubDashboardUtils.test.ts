import { describe, expect, it } from 'vitest';
import {
  filterFleetNodes,
  fleetHealthPercent,
  uniqueFilterValues,
  formatHubIp,
} from './hubDashboardUtils';
import type { FederatedNodeRow } from './types';

const nodes: FederatedNodeRow[] = [
  { id: '1', name: 'Alpha', client: 'Acme', building: 'HQ', ipAddress: '10.0.0.1' },
  { id: '2', name: 'Beta', client: 'Acme', building: 'Remote', ipAddress: '10.0.0.2' },
  { id: '3', name: 'Gamma', client: 'Globex', building: 'HQ', ipAddress: '10.0.0.3' },
];

describe('hubDashboardUtils', () => {
  it('filters by client and building', () => {
    const filtered = filterFleetNodes(nodes, { search: '', client: 'Acme', building: 'HQ' });
    expect(filtered).toHaveLength(1);
    expect(filtered[0].name).toBe('Alpha');
  });

  it('filters by search term across fields', () => {
    const filtered = filterFleetNodes(nodes, { search: 'remote', client: '', building: '' });
    expect(filtered).toHaveLength(1);
    expect(filtered[0].name).toBe('Beta');
  });

  it('collects unique filter values', () => {
    expect(uniqueFilterValues(nodes, 'client')).toEqual(['Acme', 'Globex']);
    expect(uniqueFilterValues(nodes, 'building')).toEqual(['HQ', 'Remote']);
  });

  it('computes fleet health percent', () => {
    expect(fleetHealthPercent(3, 4)).toBe(75);
    expect(fleetHealthPercent(0, 0)).toBe(100);
  });

  it('formats hub IP', () => {
    expect(formatHubIp('::ffff:192.168.1.1')).toBe('192.168.1.1');
    expect(formatHubIp(undefined)).toBe(' - ');
  });
});
