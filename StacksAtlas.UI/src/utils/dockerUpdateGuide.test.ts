import { describe, expect, it } from 'vitest';
import { buildDockerUpdateGuide } from './dockerUpdateGuide';
import type { UpdateCheckResult } from '../models/Updates';

function base(overrides: Partial<UpdateCheckResult> = {}): UpdateCheckResult {
  return {
    status: 'updateAvailable',
    updateAvailable: true,
    currentVersion: '1.9.1',
    channel: 'preview',
    artifactKey: 'linux-docker',
    availableVersion: '1.9.2',
    downloadUrl: null,
    sha256: null,
    image: 'ghcr.io/keatonstacks/stacksatlas:1.9.2',
    digest: 'sha256:abc123',
    criticality: 'recommended',
    releaseNotesUrl: null,
    publishedUtc: null,
    message: 'Update available',
    applySupported: false,
    applyMode: 'guided',
    ...overrides,
  };
}

describe('buildDockerUpdateGuide', () => {
  it('returns null when not guided', () => {
    expect(buildDockerUpdateGuide(base({ applyMode: 'manual' }))).toBeNull();
  });

  it('builds pull and recreate commands from image + digest', () => {
    const guide = buildDockerUpdateGuide(base());
    expect(guide).not.toBeNull();
    expect(guide!.pullCommand).toBe('docker pull ghcr.io/keatonstacks/stacksatlas:1.9.2');
    expect(guide!.recreateCommand).toContain('docker tag ghcr.io/keatonstacks/stacksatlas:1.9.2 stacksatlas:latest');
    expect(guide!.recreateCommand).toContain('docker compose build --no-cache stacksatlas');
    expect(guide!.recreateCommand).toContain('docker compose up -d --force-recreate stacksatlas');
    expect(guide!.recreateCommand).toContain('Deep Scan');
    expect(guide!.digestNote).toContain('sha256:abc123');
  });

  it('returns null without image or digest', () => {
    expect(buildDockerUpdateGuide(base({ image: null, digest: null }))).toBeNull();
  });
});
