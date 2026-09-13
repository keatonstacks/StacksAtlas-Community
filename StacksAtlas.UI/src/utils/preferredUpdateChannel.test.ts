import { beforeEach, describe, expect, it } from 'vitest';
import {
  PREFERRED_UPDATE_CHANNEL_KEY,
  getPreferredUpdateChannel,
  setPreferredUpdateChannel,
} from './preferredUpdateChannel';

describe('preferredUpdateChannel', () => {
  beforeEach(() => {
    localStorage.removeItem(PREFERRED_UPDATE_CHANNEL_KEY);
  });

  it('defaults to stable when unset', () => {
    expect(getPreferredUpdateChannel()).toBe('stable');
  });

  it('persists preview for Notify/Update now', () => {
    setPreferredUpdateChannel('preview');
    expect(getPreferredUpdateChannel()).toBe('preview');
    expect(localStorage.getItem(PREFERRED_UPDATE_CHANNEL_KEY)).toBe('preview');
  });

  it('normalizes unknown values to stable', () => {
    localStorage.setItem(PREFERRED_UPDATE_CHANNEL_KEY, 'beta');
    expect(getPreferredUpdateChannel()).toBe('stable');
  });
});
