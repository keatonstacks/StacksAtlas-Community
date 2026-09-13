import { describe, expect, it } from 'vitest';
import { filterSecurityRisks } from './reportsSecurityFilter';

const telnetRisk = {
  deviceName: 'Switch-01',
  reason: 'SA-001::Critical::Unencrypted Telnet Enabled::Telnet plaintext::Use SSH.',
  level: 'Critical' as const,
  findingId: 'SA-001',
  title: 'Unencrypted Telnet Enabled',
  description: 'Telnet transmits data in plaintext.',
  severity: 'Critical',
};

const httpRisk = {
  deviceName: 'Camera-02',
  reason: 'SA-002::Medium::Unencrypted HTTP Interface::HTTP admin::Enable HTTPS.',
  level: 'Warning' as const,
  findingId: 'SA-002',
  title: 'Unencrypted HTTP Interface',
  description: 'HTTP management interface.',
  severity: 'Medium',
};

describe('filterSecurityRisks', () => {
  const risks = [telnetRisk, httpRisk];

  it('finds Telnet finding by title search', () => {
    const filtered = filterSecurityRisks(risks, { search: 'telnet', severities: [] });
    expect(filtered).toHaveLength(1);
    expect(filtered[0].title).toBe('Unencrypted Telnet Enabled');
  });

  it('filters by severity chips', () => {
    const filtered = filterSecurityRisks(risks, { search: '', severities: ['Critical'] });
    expect(filtered).toHaveLength(1);
    expect(filtered[0].findingId).toBe('SA-001');
  });

  it('combines search and severity', () => {
    const filtered = filterSecurityRisks(risks, { search: 'camera', severities: ['Medium'] });
    expect(filtered).toHaveLength(1);
    expect(filtered[0].deviceName).toBe('Camera-02');
  });
});
