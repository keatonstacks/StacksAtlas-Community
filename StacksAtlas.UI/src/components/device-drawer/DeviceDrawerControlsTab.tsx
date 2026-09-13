import { Alert, Stack } from '@mui/material';
import type { Device } from '../../models/Device';
import { usePortableMode } from '../../hooks/usePortableMode';
import { DeviceDrawerOpenAvcPanel } from './DeviceDrawerOpenAvcPanel';

interface DeviceDrawerControlsTabProps {
  device: Device;
  isHub: boolean;
  isAdmin: boolean;
  onDeviceUpdate?: () => void;
}

export function DeviceDrawerControlsTab({ device, isHub, isAdmin, onDeviceUpdate }: DeviceDrawerControlsTabProps) {
  const { isPortable } = usePortableMode();

  if (isPortable) {
    return (
      <Alert severity="info" sx={{ borderRadius: 2 }}>
        OpenAVC control requires a permanent install (not portable mode).
      </Alert>
    );
  }

  return (
    <Stack spacing={3}>
      <DeviceDrawerOpenAvcPanel device={device} isHub={isHub} isAdmin={isAdmin} onDeviceUpdate={onDeviceUpdate} />
    </Stack>
  );
}
