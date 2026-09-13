import { describe, expect, it, vi } from 'vitest';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DEVICE_DRAWER_TABS, visibleDeviceDrawerTabs } from './types';
import { renderWithTheme } from '../../test/renderWithTheme';

// Lightweight tab shell test  -  full DeviceDrawer pulls API hooks; we verify tab config + nav labels.
describe('DeviceDrawer tabs', () => {
  it('defines six operator tabs in order', () => {
    expect(DEVICE_DRAWER_TABS.map((t) => t.id)).toEqual([
      'overview',
      'manage',
      'network',
      'security',
      'controls',
      'history',
    ]);
    expect(DEVICE_DRAWER_TABS.map((t) => t.label)).toEqual([
      'Overview',
      'Manage',
      'Network',
      'Security',
      'Controls',
      'History',
    ]);
  });

  it('hides Manage and Controls tabs in portable mode', () => {
    const portableTabs = visibleDeviceDrawerTabs(true).map((t) => t.id);
    expect(portableTabs).not.toContain('manage');
    expect(portableTabs).not.toContain('controls');
    const fullTabs = visibleDeviceDrawerTabs(false).map((t) => t.id);
    expect(fullTabs).toContain('manage');
    expect(fullTabs).toContain('controls');
  });

  it('switches active tab state in a minimal tab strip', async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();

    function TabStrip({ value }: { value: string }) {
      return (
        <div role="tablist">
          {visibleDeviceDrawerTabs(false).map((tab) => (
            <button
              key={tab.id}
              role="tab"
              aria-selected={value === tab.id}
              onClick={() => onChange(tab.id)}
            >
              {tab.label}
            </button>
          ))}
        </div>
      );
    }

    renderWithTheme(<TabStrip value="overview" />);
    await user.click(screen.getByRole('tab', { name: 'Network' }));
    expect(onChange).toHaveBeenCalledWith('network');
  });
});
