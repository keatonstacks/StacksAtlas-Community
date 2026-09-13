export type SecurityFindingSeverity = 'Critical' | 'High' | 'Medium' | 'Low' | 'Info' | 'Unknown';

export interface ParsedSecurityFinding {
  raw: string;
  id?: string;
  severity: SecurityFindingSeverity;
  title: string;
  description: string;
  mitigation: string;
}

export interface StructuredSecurityRiskInput {
  reason: string;
  level?: string;
  findingId?: string | null;
  title?: string | null;
  description?: string | null;
  mitigation?: string | null;
  severity?: string | null;
}

function normalizeFindingSeverity(value: string): SecurityFindingSeverity {
  const v = value.toLowerCase();
  if (v === 'critical') return 'Critical';
  if (v === 'high') return 'High';
  if (v === 'medium') return 'Medium';
  if (v === 'low') return 'Low';
  if (v === 'info') return 'Info';
  return 'Unknown';
}

const LEGACY_MITIGATIONS: Record<string, string> = {
  'Unencrypted Telnet Enabled':
    'Disable Telnet and use SSH for remote administration. Block port 23 at the network edge if the device cannot be reconfigured.',
  'Insecure FTP Enabled':
    'Disable FTP and switch to SFTP, FTPS, or HTTPS. Restrict port 21 to management VLANs only.',
  'Unencrypted HTTP Interface':
    'Enable HTTPS/TLS on the management interface or restrict HTTP access to trusted management networks only.',
  'Plaintext HTTP Management':
    'Enable HTTPS on the device if supported. Otherwise isolate the management interface on a dedicated control VLAN.',
  'End-of-Life Operating System':
    'Plan replacement or network isolation. Do not expose end-of-life systems to untrusted networks or the internet.',
  'Unauthenticated RTSP Stream':
    'Require authentication on RTSP streams and restrict port 554 to authorized viewers or VLANs.',
  'Remote Desktop Exposure':
    'Confirm remote access is required. Use strong passwords, MFA where available, and firewall rules limiting source IPs.',
  'SNMP Service Exposed':
    'Disable SNMP if unused. Otherwise use SNMPv3 and change default community strings immediately.',
  'Intelligence Finding':
    'Review the deep-scan finding and apply vendor guidance. Re-scan after remediation to confirm the exposure is closed.',
};

/** Parses structured SA-001::Critical::... lines and legacy [Critical] Title: desc strings. */
export function parseSecurityFinding(raw: string): ParsedSecurityFinding {
  const pipe = raw.split('::');
  if (pipe.length >= 5) {
    return {
      raw,
      id: pipe[0],
      severity: normalizeFindingSeverity(pipe[1]),
      title: pipe[2],
      description: pipe[3],
      mitigation: pipe.slice(4).join('::'),
    };
  }

  const legacy = raw.match(/^\[(\w+)\]\s*([^:]+):\s*(.+)$/);
  if (legacy) {
    const title = legacy[2].trim();
    return {
      raw,
      severity: normalizeFindingSeverity(legacy[1]),
      title,
      description: legacy[3].trim(),
      mitigation: LEGACY_MITIGATIONS[title] ?? 'Review this exposure and apply vendor or network hardening guidance.',
    };
  }

  return {
    raw,
    severity: 'Unknown',
    title: 'Security finding',
    description: raw,
    mitigation: 'Review this exposure and apply vendor or network hardening guidance.',
  };
}

export function toParsedSecurityFinding(risk: StructuredSecurityRiskInput): ParsedSecurityFinding {
  if (risk.title?.trim()) {
    return {
      raw: risk.reason,
      id: risk.findingId ?? undefined,
      severity: normalizeFindingSeverity(risk.severity ?? risk.level ?? 'Unknown'),
      title: risk.title,
      description: risk.description?.trim() ?? '',
      mitigation: risk.mitigation?.trim() ?? '',
    };
  }
  return parseSecurityFinding(risk.reason);
}

export function securitySeverityColor(
  severity: SecurityFindingSeverity
): 'error' | 'warning' | 'info' | 'success' | 'default' {
  if (severity === 'Critical') return 'error';
  if (severity === 'High') return 'error';
  if (severity === 'Medium') return 'warning';
  if (severity === 'Low' || severity === 'Info') return 'info';
  return 'default';
}

export function formatFindingSummaryLine(finding: ParsedSecurityFinding): string {
  if (finding.id) {
    return `${finding.id} · ${finding.severity} · ${finding.title}`;
  }
  return `${finding.severity} · ${finding.title}`;
}

/** Compact lines for grid hover tooltips  -  title/severity only, not full description. */
export function summarizeSecurityIssuesForTooltip(
  issues: string[] | null | undefined,
  maxItems = 3,
): { lines: string[]; overflow: number } {
  if (!issues?.length) return { lines: [], overflow: 0 };

  const parsed = issues.map(parseSecurityFinding);
  return {
    lines: parsed.slice(0, maxItems).map(formatFindingSummaryLine),
    overflow: Math.max(0, parsed.length - maxItems),
  };
}

export function formatFindingForCopy(finding: ParsedSecurityFinding, deviceName?: string): string {
  const lines = [
    deviceName ? `Device: ${deviceName}` : '',
    finding.id ? `ID: ${finding.id}` : '',
    `Severity: ${finding.severity}`,
    `Finding: ${finding.title}`,
    `What we found: ${finding.description}`,
    `Recommended action: ${finding.mitigation}`,
  ].filter(Boolean);
  return lines.join('\n');
}

export function collectSecurityIssues(device: {
  securityIssues?: string[] | null;
  deepScanIssues?: string[] | null;
}): string[] {
  const merged = [...(device.securityIssues ?? [])];
  for (const issue of device.deepScanIssues ?? []) {
    if (!merged.includes(issue)) merged.push(issue);
  }
  return merged;
}

export function collectSecurityFindings(device: {
  securityIssues?: string[] | null;
  deepScanIssues?: string[] | null;
}): ParsedSecurityFinding[] {
  return collectSecurityIssues(device).map(parseSecurityFinding);
}
