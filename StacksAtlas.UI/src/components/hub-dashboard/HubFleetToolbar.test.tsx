import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HubFleetToolbar } from './HubFleetToolbar';
import { renderWithTheme } from '../../test/renderWithTheme';

describe('HubFleetToolbar', () => {
  it('persists client filter when search changes', async () => {
    const user = userEvent.setup();
    const onFiltersChange = vi.fn();

    renderWithTheme(
      <HubFleetToolbar
        filters={{ search: '', client: 'Acme', building: '' }}
        clients={['Acme', 'Globex']}
        buildings={['HQ']}
        isAdmin
        onFiltersChange={onFiltersChange}
        onEnrollClick={() => {}}
      />,
    );

    const search = screen.getByPlaceholderText('Search sites…');
    await user.type(search, 'alpha');

    expect(onFiltersChange).toHaveBeenCalled();
    const lastCall = onFiltersChange.mock.calls.at(-1)?.[0];
    expect(lastCall).toMatchObject({ search: expect.stringContaining('a') });
  });

});
