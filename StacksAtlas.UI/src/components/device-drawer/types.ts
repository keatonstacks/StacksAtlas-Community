import type { Device } from '../../models/Device';
import type { DeviceViewMode } from '../devices/types';

export interface DrawerUser {
  id: string;
  username: string;
  role: string;
}

export interface DeviceDrawerProps {
  open: boolean;
  onClose: () => void;
  device: Device | null;
  users: DrawerUser[];
  onDeviceUpdate: (device?: Device) => void;
  onDelete: (device: Device) => void;
  onRestore: (device: Device) => void;
  onHardDelete?: (device: Device) => void;
  onRestoreToFleet?: (device: Device) => void;
  viewMode?: DeviceViewMode;
  nodesMap?: Record<string, string>;
  onNotify?: (message: string) => void;
}

export type DeviceDrawerTab = 'overview' | 'manage' | 'network' | 'security' | 'controls' | 'history';

export const DEVICE_DRAWER_TABS: { id: DeviceDrawerTab; label: string; portableHidden?: boolean }[] = [
  { id: 'overview', label: 'Overview' },
  { id: 'manage', label: 'Manage', portableHidden: true },
  { id: 'network', label: 'Network' },
  { id: 'security', label: 'Security' },
  { id: 'controls', label: 'Controls', portableHidden: true },
  { id: 'history', label: 'History' },
];

export function visibleDeviceDrawerTabs(isPortable: boolean) {
  return DEVICE_DRAWER_TABS.filter((tab) => !isPortable || !tab.portableHidden);
}
