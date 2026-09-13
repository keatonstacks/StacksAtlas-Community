import { describe, expect, it } from 'vitest';
import {
  buildNodeHttpUrl,
  isMacOsDescription,
  resolveNodeHttpPort,
} from './appliancePorts';

describe('appliancePorts', () => {
  it('detects macOS from OS description', () => {
    expect(isMacOsDescription('Darwin 24.0.0')).toBe(true);
    expect(isMacOsDescription('Ubuntu 22.04')).toBe(false);
  });

  it('maps legacy mac HTTP port 5000 to 5050', () => {
    expect(resolveNodeHttpPort(5000, 'macOS 14.5')).toBe(5050);
    expect(resolveNodeHttpPort(undefined, 'Darwin')).toBe(5050);
    expect(resolveNodeHttpPort(5000, 'Linux')).toBe(5000);
  });

  it('builds node dashboard URL with resolved port', () => {
    expect(buildNodeHttpUrl('192.168.1.70', 5000, 'Darwin')).toBe('http://192.168.1.70:5050/');
    expect(buildNodeHttpUrl('192.168.1.239', 5000, 'Ubuntu')).toBe('http://192.168.1.239:5000/');
  });
});
