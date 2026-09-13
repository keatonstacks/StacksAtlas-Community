import { describe, expect, it } from 'vitest';
import {
  buildAlertsPageUrl,
  isAuditOnlyAlert,
  matchesScopeFilter,
  parseAlertsUrlParams,
} from './alertsUtils';
import type { AlertEvent } from './types';

describe('alertsUtils', () => {
  it('flags audit-only alerts', () => {
    const audit: AlertEvent = {
      id: '1',
      triggeredAt: '',
      deviceId: '',
      deviceName: 'n',
      deviceIp: '',
      alertType: 'NodeDecoupled',
      sentToEmails: [],
      sentToWebhooks: [],
      success: true,
    };
    expect(isAuditOnlyAlert(audit)).toBe(true);
    expect(
      isAuditOnlyAlert({
        ...audit,
        alertType: 'DeviceDown',
        errorMessage: 'Historical replay  -  notification not dispatched',
      }),
    ).toBe(true);
  });

  it('matches client/building scope filters', () => {
    expect(
      matchesScopeFilter({ client: 'Acme Corp', building: 'HQ' }, {
        nodeIds: [],
        client: 'acme',
        building: 'hq',
        room: '',
      }),
    ).toBe(true);
    expect(
      matchesScopeFilter({ client: 'Other' }, {
        nodeIds: [],
        client: 'acme',
        building: '',
        room: '',
      }),
    ).toBe(false);
  });

  it('parses alerts URL params', () => {
    const parsed = parseAlertsUrlParams('?tab=audit&nodeId=abc&client=Acme&severity=critical');
    expect(parsed.tab).toBe('audit');
    expect(parsed.filters.nodeIds).toEqual(['abc']);
    expect(parsed.filters.client).toBe('Acme');
    expect(parsed.severity).toBe('Critical');
  });

  it('builds deep link URLs', () => {
    expect(buildAlertsPageUrl({ tab: 'history', nodeId: 'n1' })).toBe('/alerts?tab=history&nodeId=n1');
    expect(buildAlertsPageUrl()).toBe('/alerts');
  });
});
