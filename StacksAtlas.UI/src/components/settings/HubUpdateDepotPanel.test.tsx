import { describe, expect, it, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HubUpdateDepotPanel } from './HubUpdateDepotPanel';
import { renderWithTheme } from '../../test/renderWithTheme';
import { ApiService } from '../../services/apiService';

vi.mock('../../services/apiService', () => ({
  ApiService: {
    getUpdateDepotStatus: vi.fn(),
    stageUpdateDepot: vi.fn(),
  },
}));

describe('HubUpdateDepotPanel', () => {
  beforeEach(() => {
    vi.mocked(ApiService.getUpdateDepotStatus).mockReset();
    vi.mocked(ApiService.stageUpdateDepot).mockReset();
  });

  it('loads empty depot status and stages preview channel', async () => {
    const user = userEvent.setup();
    vi.mocked(ApiService.getUpdateDepotStatus)
      .mockResolvedValueOnce({
        depotRoot: 'C:/depot',
        channels: [],
        queriedUtc: '2026-07-09T12:00:00Z',
      })
      .mockResolvedValueOnce({
        depotRoot: 'C:/depot',
        queriedUtc: '2026-07-09T12:05:00Z',
        channels: [
          {
            channel: 'preview',
            versions: [
              {
                version: '1.9.2',
                path: 'C:/depot/preview/1.9.2',
                hasManifest: true,
                hasSignature: true,
                publishedUtc: null,
                artifacts: [{ fileName: 'StacksAtlas.msi', sizeBytes: 2048 }],
                totalBytes: 2048,
              },
            ],
          },
        ],
      });
    vi.mocked(ApiService.stageUpdateDepot).mockResolvedValue({
      success: true,
      channel: 'preview',
      version: '1.9.2',
      message: 'Staged 1 artifact(s) for preview v1.9.2.',
      path: 'C:/depot/preview/1.9.2',
      stagedArtifactKeys: ['win-x64-msi'],
      completedUtc: '2026-07-09T12:05:00Z',
    });

    renderWithTheme(<HubUpdateDepotPanel channel="preview" />);

    await waitFor(() => {
      expect(screen.getByText(/Depot empty/i)).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: /Stage preview to depot/i }));

    await waitFor(() => {
      expect(ApiService.stageUpdateDepot).toHaveBeenCalledWith('preview');
      expect(screen.getByText(/Staged 1 artifact/i)).toBeInTheDocument();
      expect(screen.getByText('1.9.2')).toBeInTheDocument();
    });
  });
});
