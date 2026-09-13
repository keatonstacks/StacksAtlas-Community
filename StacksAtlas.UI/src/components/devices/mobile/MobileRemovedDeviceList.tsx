import { Paper, Stack, Typography } from '@mui/material';
import type { Device } from '../../../models/Device';
import { MobileRemovedDeviceCard } from './MobileRemovedDeviceCard';

interface MobileRemovedDeviceListProps {
  devices: Device[];
  nodesMap: Record<string, string>;
  isPortable?: boolean;
  selectionEnabled?: boolean;
  isAdmin?: boolean;
  selectedIds: Set<string>;
  busy?: boolean;
  onToggleSelect: (id: string) => void;
  onDeviceClick: (device: Device) => void;
  onRestoreToFleet: (device: Device) => void;
}

export function MobileRemovedDeviceList({
  devices,
  nodesMap,
  isPortable = false,
  selectionEnabled = false,
  isAdmin = false,
  selectedIds,
  busy = false,
  onToggleSelect,
  onDeviceClick,
  onRestoreToFleet,
}: MobileRemovedDeviceListProps) {
  if (devices.length === 0) {
    return (
      <Paper variant="outlined" sx={{ p: 4, textAlign: 'center', borderRadius: 3 }}>
        <Typography color="text.secondary" fontWeight={600} variant="body2">
          No permanently removed devices. Use &quot;Remove from Fleet&quot; in Archive to suppress a device here.
        </Typography>
      </Paper>
    );
  }

  return (
    <Stack spacing={1.25}>
      {devices.map((device) => {
        const nodeLabel = device.nodeId
          ? (nodesMap[device.nodeId] || device.nodeId)
          : 'Local';

        return (
          <MobileRemovedDeviceCard
            key={device.id}
            device={device}
            nodeLabel={nodeLabel}
            isPortable={isPortable}
            selectionEnabled={selectionEnabled}
            isAdmin={isAdmin}
            selected={selectedIds.has(device.id)}
            busy={busy}
            onToggleSelect={onToggleSelect}
            onOpen={() => onDeviceClick(device)}
            onRestoreToFleet={() => onRestoreToFleet(device)}
          />
        );
      })}
    </Stack>
  );
}
