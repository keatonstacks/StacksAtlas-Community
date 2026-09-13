export interface AuditEvent {
  id: string;
  timestampUtc: string;
  action: string;
  actorUserId: string;
  actorUsername: string;
  actorRole: string;
  resourceType: string;
  resourceId?: string | null;
  outcome: string;
  clientIp?: string | null;
  detail?: string | null;
  nodeId?: string | null;
  nodeName?: string | null;
}

export type AuditOutcomeFilter = 'All' | 'Success' | 'Failed' | 'Denied';

export type AuditActionFilter =
  | 'All'
  | 'auth'
  | 'user'
  | 'api_token'
  | 'license'
  | 'settings'
  | 'webhook'
  | 'federation'
  | 'device'
  | 'snapshot'
  | 'security'
  | 'database';

export const AUDIT_ACTION_FILTERS: { value: AuditActionFilter; label: string }[] = [
  { value: 'All', label: 'All actions' },
  { value: 'auth', label: 'Authentication' },
  { value: 'user', label: 'Users & roles' },
  { value: 'api_token', label: 'API tokens' },
  { value: 'license', label: 'Licensing' },
  { value: 'settings', label: 'Settings' },
  { value: 'webhook', label: 'Webhooks' },
  { value: 'federation', label: 'Federation' },
  { value: 'device', label: 'Device governance' },
  { value: 'snapshot', label: 'Snapshots / reset' },
  { value: 'security', label: 'Security / portable' },
  { value: 'database', label: 'Database' },
];

export function formatAuditAction(action: string): string {
  const parts = action.split('.');
  if (parts.length < 2) return action;
  const verb = parts[parts.length - 1].replace(/_/g, ' ');
  const subject = parts.slice(0, -1).join(' ').replace(/_/g, ' ');
  return `${subject}: ${verb}`.replace(/\b\w/g, (c) => c.toUpperCase());
}

export function outcomeColor(outcome: string): 'success' | 'error' | 'warning' | 'default' {
  switch (outcome.toLowerCase()) {
    case 'success':
      return 'success';
    case 'failed':
      return 'error';
    case 'denied':
      return 'warning';
    default:
      return 'default';
  }
}
