export interface UpdateScheduleConfig {
  enabled: boolean;
  dayOfWeek: number;
  hour: number;
  minute: number;
  lastAppliedVersion: string | null;
}

export const UPDATE_SCHEDULE_STORAGE_KEY = 'stacksatlas.updateSchedule.v1';

const DEFAULT_SCHEDULE: UpdateScheduleConfig = {
  enabled: false,
  dayOfWeek: 0,
  hour: 2,
  minute: 0,
  lastAppliedVersion: null,
};

export const WEEKDAY_LABELS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export function normalizeSchedule(raw: Partial<UpdateScheduleConfig> | null | undefined): UpdateScheduleConfig {
  if (!raw) return { ...DEFAULT_SCHEDULE };
  return {
    enabled: !!raw.enabled,
    dayOfWeek: typeof raw.dayOfWeek === 'number' ? Math.min(6, Math.max(0, raw.dayOfWeek)) : DEFAULT_SCHEDULE.dayOfWeek,
    hour: typeof raw.hour === 'number' ? Math.min(23, Math.max(0, raw.hour)) : DEFAULT_SCHEDULE.hour,
    minute: typeof raw.minute === 'number' ? Math.min(59, Math.max(0, raw.minute)) : DEFAULT_SCHEDULE.minute,
    lastAppliedVersion: raw.lastAppliedVersion ?? null,
  };
}

/** @deprecated Prefer ApiService.getUpdateSchedule  -  kept for migration/tests. */
export function loadUpdateSchedule(): UpdateScheduleConfig {
  try {
    const raw = localStorage.getItem(UPDATE_SCHEDULE_STORAGE_KEY);
    if (!raw) return { ...DEFAULT_SCHEDULE };
    return normalizeSchedule(JSON.parse(raw) as Partial<UpdateScheduleConfig>);
  } catch {
    return { ...DEFAULT_SCHEDULE };
  }
}

/** @deprecated Prefer ApiService.putUpdateSchedule. */
export function saveUpdateSchedule(config: UpdateScheduleConfig): void {
  localStorage.setItem(UPDATE_SCHEDULE_STORAGE_KEY, JSON.stringify(config));
}

/** True when local time is on the configured weekday within a 30-minute maintenance window. */
export function isInMaintenanceWindow(config: UpdateScheduleConfig, now = new Date()): boolean {
  if (!config.enabled) return false;
  if (now.getDay() !== config.dayOfWeek) return false;
  const start = config.hour * 60 + config.minute;
  const current = now.getHours() * 60 + now.getMinutes();
  return current >= start && current < start + 30;
}

export function formatScheduleLabel(config: UpdateScheduleConfig): string {
  const day = WEEKDAY_LABELS[config.dayOfWeek] ?? 'Sunday';
  const h = config.hour.toString().padStart(2, '0');
  const m = config.minute.toString().padStart(2, '0');
  return `${day}s at ${h}:${m} (local time)`;
}

export function shouldAutoApply(
  config: UpdateScheduleConfig,
  availableVersion: string | null,
  now = new Date(),
): boolean {
  if (!config.enabled || !availableVersion) return false;
  if (config.lastAppliedVersion === availableVersion) return false;
  return isInMaintenanceWindow(config, now);
}
