import { describe, expect, it } from 'vitest';
import { screen } from '@testing-library/react';
import { HubKpiStrip } from './HubKpiStrip';
import { renderWithTheme } from '../../test/renderWithTheme';

describe('HubKpiStrip', () => {
  it('renders fleet KPI labels and values', () => {
    renderWithTheme(
      <HubKpiStrip
        stats={{
          totalNodes: 4,
          onlineNodes: 3,
          setupRequiredNodes: 1,
          totalDevices: 120,
          criticalAlerts: 2,
        }}
      />,
    );

    expect(screen.getByText('MANAGED NODES')).toBeInTheDocument();
    expect(screen.getByText('4')).toBeInTheDocument();
    expect(screen.getByText('FLEET DEVICES')).toBeInTheDocument();
    expect(screen.getByText('120')).toBeInTheDocument();
    expect(screen.getByText('SETUP REQUIRED')).toBeInTheDocument();
    expect(screen.getByText('FLEET SYNC')).toBeInTheDocument();
  });
});
