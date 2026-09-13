import { describe, expect, it } from 'vitest';
import { getDeviceIconKind } from './deviceUtils';

describe('getDeviceIconKind', () => {
  it('maps Video Display to tv icon kind', () => {
    expect(getDeviceIconKind('Video Display')).toBe('tv');
  });

  it('maps Audio DSP to audio icon kind', () => {
    expect(getDeviceIconKind('Audio DSP')).toBe('audio');
  });

  it('maps Control Processor to control icon kind', () => {
    expect(getDeviceIconKind('Control Processor')).toBe('control');
  });

  it('falls back to tv for OLED in model when type unknown', () => {
    expect(getDeviceIconKind('Unknown', 'OLED55C4PUA', 'Living Room TV')).toBe('tv');
  });
});
