import { Box, Stack, Typography } from '@mui/material';
import type { Device, ServiceDetail } from '../../models/Device';
import { TraceroutePanel } from '../TraceroutePanel';
import { OpenPortsGrid } from './OpenPortsGrid';
import { DeepScanPanel } from './DeepScanPanel';

interface DeviceDrawerNetworkTabProps {
  device: Device;
  isHub: boolean;
  serviceDetails: ServiceDetail[];
  isDeepScanning: boolean;
  scanProgress: number;
  isIntelligenceOpen: boolean;
  onToggleIntelligence: () => void;
}

export function DeviceDrawerNetworkTab({
  device,
  isHub,
  serviceDetails,
  isDeepScanning,
  scanProgress,
  isIntelligenceOpen,
  onToggleIntelligence,
}: DeviceDrawerNetworkTabProps) {
  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="overline" color="text.secondary" fontWeight={700} display="block" mb={1}>
          Open ports
        </Typography>
        <OpenPortsGrid device={device} isHub={isHub} />
        <DeepScanPanel
          serviceDetails={serviceDetails}
          isDeepScanning={isDeepScanning}
          scanProgress={scanProgress}
          isOpen={isIntelligenceOpen}
          isHub={isHub}
          onToggle={onToggleIntelligence}
        />
      </Box>

      <Box>
        <Typography variant="overline" color="text.secondary" fontWeight={700} display="block" mb={1}>
          Path diagnostics
        </Typography>
        <TraceroutePanel
          targetIp={device.ipAddress}
          deviceId={device.id}
          hubRemoteDevice={isHub && !!device.nodeId}
        />
      </Box>
    </Stack>
  );
}
