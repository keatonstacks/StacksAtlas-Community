import { describe, expect, it } from 'vitest';
import {
  depotStatusSummaryLabel,
  formatBytes,
  latestStagedVersionForChannel,
  listDepotVersionRows,
} from './updateDepotUi';
import type { UpdateDepotStatus } from '../models/Updates';

describe('updateDepotUi', () => {
  it('formats byte sizes', () => {
    expect(formatBytes(0)).toBe('0 B');
    expect(formatBytes(512)).toBe('512 B');
    expect(formatBytes(2048)).toBe('2.0 KB');
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB');
  });

  it('summarizes empty depot', () => {
    expect(depotStatusSummaryLabel(null)).toBe('Depot empty (nothing staged yet)');
    expect(depotStatusSummaryLabel({ depotRoot: '/x', channels: [], queriedUtc: '' })).toBe(
      'Depot empty (nothing staged yet)',
    );
  });

  it('lists and summarizes staged versions', () => {
    const status: UpdateDepotStatus = {
      depotRoot: 'C:/ProgramData/StacksAtlas/updates/depot',
      queriedUtc: '2026-07-09T12:00:00Z',
      channels: [
        {
          channel: 'preview',
          versions: [
            {
              version: '1.9.2',
              path: 'C:/depot/preview/1.9.2',
              hasManifest: true,
              hasSignature: true,
              publishedUtc: '2026-07-09T10:00:00Z',
              artifacts: [
                { fileName: 'StacksAtlas.msi', sizeBytes: 1024 },
                { fileName: 'linux-docker.meta.json', sizeBytes: 64 },
              ],
              totalBytes: 1088,
            },
          ],
        },
      ],
    };

    const rows = listDepotVersionRows(status);
    expect(rows).toHaveLength(1);
    expect(rows[0]).toMatchObject({
      channel: 'preview',
      version: '1.9.2',
      artifactCount: 2,
      totalBytes: 1088,
      hasManifest: true,
      hasSignature: true,
    });
    expect(depotStatusSummaryLabel(status)).toContain('Staged preview v1.9.2');
    expect(depotStatusSummaryLabel(status)).toContain('2 artifacts');
  });

  it('picks latest staged version for a channel', () => {
    const status: UpdateDepotStatus = {
      depotRoot: '/depot',
      queriedUtc: '',
      channels: [
        {
          channel: 'stable',
          versions: [
            {
              version: '1.9.1',
              path: '',
              hasManifest: true,
              hasSignature: true,
              publishedUtc: null,
              artifacts: [],
              totalBytes: 0,
            },
            {
              version: '1.9.3',
              path: '',
              hasManifest: true,
              hasSignature: true,
              publishedUtc: null,
              artifacts: [],
              totalBytes: 0,
            },
            {
              version: '1.9.2',
              path: '',
              hasManifest: true,
              hasSignature: true,
              publishedUtc: null,
              artifacts: [],
              totalBytes: 0,
            },
            {
              version: '1.9.9',
              path: '',
              hasManifest: false,
              hasSignature: false,
              publishedUtc: null,
              artifacts: [],
              totalBytes: 0,
            },
          ],
        },
        {
          channel: 'preview',
          versions: [
            {
              version: '1.9.4',
              path: '',
              hasManifest: true,
              hasSignature: true,
              publishedUtc: null,
              artifacts: [],
              totalBytes: 0,
            },
          ],
        },
      ],
    };

    expect(latestStagedVersionForChannel(status, 'stable')).toBe('1.9.3');
    expect(latestStagedVersionForChannel(status, 'preview')).toBe('1.9.4');
    expect(latestStagedVersionForChannel(status, 'missing')).toBeNull();
    expect(latestStagedVersionForChannel(null, 'stable')).toBeNull();
  });
});
