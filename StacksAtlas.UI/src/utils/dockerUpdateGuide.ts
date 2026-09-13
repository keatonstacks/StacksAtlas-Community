import type { UpdateCheckResult } from '../models/Updates';

export interface DockerUpdateGuide {
  imageRef: string;
  pullCommand: string;
  recreateCommand: string;
  digestNote: string | null;
}

/**
 * Host-side Docker upgrade steps from update-check metadata (image + optional digest).
 * Compose directory is operator-local (homelab default: ~/docker-main).
 */
export function buildDockerUpdateGuide(result: UpdateCheckResult): DockerUpdateGuide | null {
  if ((result.applyMode ?? '').trim().toLowerCase() !== 'guided') return null;

  const image = (result.image ?? '').trim();
  const digest = (result.digest ?? '').trim();
  if (!image && !digest) return null;

  const imageRef = image || 'ghcr.io/keatonstacks/stacksatlas:latest';
  const digestNote = digest
    ? `Expected digest: ${digest.startsWith('sha256:') ? digest : `sha256:${digest}`}`
    : null;

  return {
    imageRef,
    pullCommand: `docker pull ${imageRef}`,
    recreateCommand:
      'docker tag ' +
      imageRef +
      ' stacksatlas:latest\n' +
      'cd ~/docker-main   # or your compose directory\n' +
      '# Deep Scan (image: stacksatlas:deep-scan + build:): MUST rebuild or you stay on the old version\n' +
      'docker compose build --no-cache stacksatlas\n' +
      'docker compose up -d --force-recreate stacksatlas\n' +
      '# Plain image only (no build: block): skip build, just:\n' +
      '# docker compose up -d --force-recreate stacksatlas',
    digestNote,
  };
}
