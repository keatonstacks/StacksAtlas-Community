import { toParsedSecurityFinding, type SecurityFindingSeverity } from './securityFindings';
import type { ReportsSecurityRisk } from '../components/reports/types';

export type ReportsSeverityFilter = Extract<SecurityFindingSeverity, 'Critical' | 'High' | 'Medium'>;

export interface ReportsSecurityFilterState {
  search: string;
  severities: ReportsSeverityFilter[];
}

function searchableText(risk: ReportsSecurityRisk): string {
  const finding = toParsedSecurityFinding(risk);
  return [
    risk.deviceName,
    risk.findingId,
    risk.title,
    risk.description,
    risk.reason,
    finding.id,
    finding.title,
    finding.description,
  ]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
}

export function filterSecurityRisks(
  risks: ReportsSecurityRisk[],
  { search, severities }: ReportsSecurityFilterState
): ReportsSecurityRisk[] {
  const query = search.trim().toLowerCase();

  return risks.filter((risk) => {
    const finding = toParsedSecurityFinding(risk);

    if (severities.length > 0 && !severities.includes(finding.severity as ReportsSeverityFilter)) {
      return false;
    }

    if (!query) return true;
    return searchableText(risk).includes(query);
  });
}
