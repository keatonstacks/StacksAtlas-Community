import { describe, expect, it } from 'vitest';
import {
  formatScheduleLabel,
  isInMaintenanceWindow,
  shouldAutoApply,
  type UpdateScheduleConfig,
} from './updateSchedule';

const base: UpdateScheduleConfig = {
  enabled: true,
  dayOfWeek: 1,
  hour: 2,
  minute: 0,
  lastAppliedVersion: null,
};

describe('updateSchedule', () => {
  it('detects maintenance window on configured day/time', () => {
    const monday2am = new Date(2026, 5, 22, 2, 10); // Monday Jun 22 2026
    expect(monday2am.getDay()).toBe(1);
    expect(isInMaintenanceWindow(base, monday2am)).toBe(true);
    expect(isInMaintenanceWindow(base, new Date(2026, 5, 22, 3, 0))).toBe(false);
  });

  it('skips auto-apply when version already applied', () => {
    const now = new Date(2026, 5, 22, 2, 5);
    expect(shouldAutoApply({ ...base, lastAppliedVersion: '1.8.0' }, '1.8.0', now)).toBe(false);
    expect(shouldAutoApply(base, '1.8.1', now)).toBe(true);
  });

  it('formats schedule label', () => {
    expect(formatScheduleLabel(base)).toContain('Mondays at 02:00');
  });
});
