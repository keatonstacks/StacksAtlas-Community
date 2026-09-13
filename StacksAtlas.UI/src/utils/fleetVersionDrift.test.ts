import { describe, expect, it } from 'vitest';
import {
  classifyNodeVsHub,
  compareSemver,
  fleetDriftKindLabel,
  fleetDriftSummaryLabel,
  isActionableHubUpdateOffer,
  summarizeFleetVersionDrift,
  tryParseSemver,
} from './fleetVersionDrift';

describe('fleetVersionDrift', () => {
  it('parses major.minor.patch prefixes', () => {
    expect(tryParseSemver('1.9.2')).toEqual([1, 9, 2]);
    expect(tryParseSemver('1.9.2-preview')).toEqual([1, 9, 2]);
    expect(tryParseSemver('')).toBeNull();
    expect(tryParseSemver('not-a-version')).toBeNull();
  });

  it('compares semver triples', () => {
    expect(compareSemver('1.9.1', '1.9.2')).toBeLessThan(0);
    expect(compareSemver('1.9.2', '1.9.1')).toBeGreaterThan(0);
    expect(compareSemver('1.9.2', '1.9.2')).toBe(0);
    expect(compareSemver(null, '1.9.2')).toBeNull();
  });

  it('classifies node vs hub', () => {
    expect(classifyNodeVsHub('1.9.1', '1.9.2')).toBe('behind');
    expect(classifyNodeVsHub('1.9.2', '1.9.2')).toBe('current');
    expect(classifyNodeVsHub('1.9.3', '1.9.2')).toBe('ahead');
    expect(classifyNodeVsHub(null, '1.9.2')).toBe('unknown');
    expect(classifyNodeVsHub('1.9.1', null)).toBe('unknown');
    expect(classifyNodeVsHub('', '1.9.2')).toBe('unknown');
  });

  it('summarizes fleet drift counts', () => {
    const summary = summarizeFleetVersionDrift(
      [
        { id: 'a', name: 'Linux', version: '1.9.1' },
        { id: 'b', name: 'Mac', version: '1.9.2' },
        { id: 'c', name: 'Stub', version: null },
        { id: 'd', name: 'Preview', version: '1.9.3' },
      ],
      '1.9.2',
    );

    expect(summary.total).toBe(4);
    expect(summary.behind).toBe(1);
    expect(summary.current).toBe(1);
    expect(summary.ahead).toBe(1);
    expect(summary.unknown).toBe(1);
    expect(summary.rows[0].kind).toBe('behind');
    expect(summary.rows[2].kind).toBe('unknown');
  });

  it('builds operator-facing summary labels', () => {
    const mixed = summarizeFleetVersionDrift(
      [
        { id: 'a', version: '1.9.1' },
        { id: 'b', version: '1.9.2' },
        { id: 'c', version: null },
      ],
      '1.9.2',
    );
    expect(fleetDriftSummaryLabel(mixed, '1.9.2')).toContain('1 of 3 behind Hub 1.9.2');
    expect(fleetDriftSummaryLabel(mixed, '1.9.2')).toContain('1 unknown');

    const allCurrent = summarizeFleetVersionDrift(
      [{ id: 'a', version: '1.9.2' }],
      '1.9.2',
    );
    expect(fleetDriftSummaryLabel(allCurrent, '1.9.2')).toContain('all 1 site(s) at Hub 1.9.2');

    expect(fleetDriftKindLabel('behind')).toBe('Behind Hub');
    expect(fleetDriftKindLabel('unknown')).toBe('Version unknown');
  });

  it('gates actionable Hub update offers on staged vs node', () => {
    expect(isActionableHubUpdateOffer('1.9.3', '1.9.2')).toBe(true);
    expect(isActionableHubUpdateOffer('1.9.2', '1.9.2')).toBe(false);
    expect(isActionableHubUpdateOffer('1.9.1', '1.9.2')).toBe(false);
    expect(isActionableHubUpdateOffer('1.9.3', null)).toBe(true);
    expect(isActionableHubUpdateOffer(null, '1.9.2')).toBe(false);
    expect(isActionableHubUpdateOffer(' ', '1.9.2')).toBe(false);
  });
});
