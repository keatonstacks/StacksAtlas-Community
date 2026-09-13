import type { UpdateChannel, UpdateCheckResult, UpdateCheckStatus } from '../models/Updates';

export type CheckUiState = 'available' | 'uptodate' | 'unavailable' | 'blocked';

export function normalizeUpdateChannel(channel: string | undefined | null): UpdateChannel {
  const value = (channel ?? 'stable').trim().toLowerCase();
  return value === 'preview' ? 'preview' : 'stable';
}

export function resolveCheckUiState(result: UpdateCheckResult): CheckUiState {
  const status = result.status as UpdateCheckStatus | undefined;
  if (status === 'updateAvailable' || result.updateAvailable) {
    return 'available';
  }
  if (status === 'upToDate') {
    return 'uptodate';
  }
  if (status === 'unavailable') {
    return 'unavailable';
  }
  if (status === 'checkFailed') {
    return 'blocked';
  }

  const available = result.availableVersion?.trim();
  const current = result.currentVersion?.trim();
  if (available && current) {
    const parse = (v: string) => v.split('.').map((p) => parseInt(p, 10) || 0);
    const [aM, aN, aP] = parse(available);
    const [cM, cN, cP] = parse(current);
    const cmp = cM !== aM ? cM - aM : cN !== aN ? cN - aN : cP - aP;
    if (cmp >= 0) {
      return 'uptodate';
    }
  }

  const normalizedMessage = (result.message ?? '').toLowerCase();
  if (
    normalizedMessage.includes('latest published') ||
    normalizedMessage.includes('ahead of the published')
  ) {
    return 'uptodate';
  }
  if (
    normalizedMessage.includes('not published') ||
    normalizedMessage.includes('not available')
  ) {
    return 'unavailable';
  }

  return 'blocked';
}

export function isUpdateCriticalityStrong(criticality: string | null | undefined): boolean {
  return (criticality ?? '').trim().toLowerCase() === 'critical';
}

export const UPDATE_BANNER_SNOOZE_KEY = 'stacksatlas.updateBannerSnoozeUntil';

export function isUpdateBannerSnoozed(now = Date.now()): boolean {
  try {
    const raw = localStorage.getItem(UPDATE_BANNER_SNOOZE_KEY);
    if (!raw) return false;
    const until = Date.parse(raw);
    return Number.isFinite(until) && until > now;
  } catch {
    return false;
  }
}

export function snoozeUpdateBanner(days = 7): void {
  const until = new Date();
  until.setDate(until.getDate() + days);
  localStorage.setItem(UPDATE_BANNER_SNOOZE_KEY, until.toISOString());
}
