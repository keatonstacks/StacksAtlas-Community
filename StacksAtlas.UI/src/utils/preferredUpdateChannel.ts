import type { UpdateChannel } from '../models/Updates';
import { normalizeUpdateChannel } from './updateCheckUi';

/** Shared Hub preference: Infrastructure preview toggle drives fleet Notify / Update now. */
export const PREFERRED_UPDATE_CHANNEL_KEY = 'stacksatlas.preferredUpdateChannel.v1';

export function getPreferredUpdateChannel(): UpdateChannel {
  try {
    return normalizeUpdateChannel(localStorage.getItem(PREFERRED_UPDATE_CHANNEL_KEY));
  } catch {
    return 'stable';
  }
}

export function setPreferredUpdateChannel(channel: UpdateChannel): void {
  try {
    localStorage.setItem(PREFERRED_UPDATE_CHANNEL_KEY, normalizeUpdateChannel(channel));
  } catch {
    // Ignore quota / private mode failures
  }
}
