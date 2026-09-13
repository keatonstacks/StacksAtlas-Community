import { describe, expect, it } from 'vitest';
import { formatFindingForCopy, parseSecurityFinding, summarizeSecurityIssuesForTooltip, toParsedSecurityFinding } from './securityFindings';

describe('securityFindings', () => {
  const telnetRaw =
    'SA-001::Critical::Unencrypted Telnet Enabled::Telnet transmits data in plaintext, exposing credentials to anyone on the network.::Disable Telnet and use SSH for remote administration. Block port 23 at the network edge if the device cannot be reconfigured.';

  it('parses structured SA issue strings', () => {
    const parsed = parseSecurityFinding(telnetRaw);
    expect(parsed.id).toBe('SA-001');
    expect(parsed.severity).toBe('Critical');
    expect(parsed.title).toBe('Unencrypted Telnet Enabled');
    expect(parsed.description).toContain('plaintext');
    expect(parsed.mitigation).toContain('SSH');
  });

  it('prefers API structured fields when present', () => {
    const parsed = toParsedSecurityFinding({
      reason: telnetRaw,
      level: 'Critical',
      findingId: 'SA-001',
      title: 'Unencrypted Telnet Enabled',
      description: 'Telnet transmits data in plaintext.',
      mitigation: 'Use SSH.',
      severity: 'Critical',
    });
    expect(parsed.title).toBe('Unencrypted Telnet Enabled');
    expect(parsed.mitigation).toBe('Use SSH.');
  });

  it('formats copy-friendly text', () => {
    const parsed = parseSecurityFinding(telnetRaw);
    const text = formatFindingForCopy(parsed, 'Switch-01');
    expect(text).toContain('Device: Switch-01');
    expect(text).toContain('Finding: Unencrypted Telnet Enabled');
    expect(text).not.toContain('SA-001::Critical');
  });

  it('summarizes findings for compact grid tooltips', () => {
    const eolRaw =
      'SA-004::Critical::End-of-Life Operating System::Legacy OS detected (Microsoft Windows 7 SP0 - SP1, Windows Server 2008 SP1, Windows Server 2008 R2, Windows 8, or Windows 8.1 Update 1). These systems no longer receive security patches.::Plan replacement or network isolation. Do not expose EOL systems to untrusted networks or the internet.';
    const { lines } = summarizeSecurityIssuesForTooltip([telnetRaw, eolRaw]);
    expect(lines[0]).toBe('SA-001 · Critical · Unencrypted Telnet Enabled');
    expect(lines[1]).toBe('SA-004 · Critical · End-of-Life Operating System');
    expect(lines[0]).not.toContain('Microsoft Windows');
    expect(lines[1]).not.toContain('Plan replacement');
  });
});
