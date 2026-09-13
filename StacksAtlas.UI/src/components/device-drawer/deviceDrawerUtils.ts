export function downloadRdpFile(ip: string): void {
  const content = `full address:s:${ip}\nprompt for credentials:i:1\nnegotiate security layer:i:1`;
  const blob = new Blob([content], { type: 'application/x-rdp' });
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `Connect-${ip}.rdp`;
  document.body.appendChild(a);
  a.click();
  window.URL.revokeObjectURL(url);
  document.body.removeChild(a);
}

export function securityGradeLabel(grade?: number | string | null): string {
  const normalized = normalizeSecurityGrade(grade);
  if (normalized === 2) return 'CRITICAL';
  if (normalized === 1) return 'WARN';
  return 'HEALTHY';
}

/** API may send 0/1/2 or Green/Yellow/Red depending on serializer context. */
export function normalizeSecurityGrade(grade?: number | string | null): number {
  if (grade === null || grade === undefined) return 0;
  if (typeof grade === 'number') return grade;
  const g = String(grade).toLowerCase();
  if (g === 'red' || g === '2') return 2;
  if (g === 'yellow' || g === '1') return 1;
  return 0;
}

export {
  collectSecurityFindings,
  collectSecurityIssues,
  parseSecurityFinding,
  securitySeverityColor,
  type ParsedSecurityFinding,
  type SecurityFindingSeverity,
} from '../../utils/securityFindings';

export function deviceDisplayName(name?: string | null, hostname?: string | null, ip?: string): string {
  return name?.trim() || hostname?.trim() || ip || 'Unknown device';
}
