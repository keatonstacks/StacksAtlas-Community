import { Box, Button, Stack, Typography } from '@mui/material';
import type { Device } from '../../models/Device';
import { LeaseHistoryPanel } from '../LeaseHistoryPanel';

interface DeviceDrawerHistoryTabProps {
  device: Device;
  role?: string | null;
  isHub?: boolean;
  onResetMetrics: () => void;
}

export function DeviceDrawerHistoryTab({ device, role, isHub, onResetMetrics }: DeviceDrawerHistoryTabProps) {
  const isViewer = role?.toLowerCase() === 'viewer';

  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="overline" color="text.secondary" fontWeight={700} display="block" mb={1}>
          DHCP & address history
        </Typography>
        <LeaseHistoryPanel macAddress={device.macAddress} currentIp={device.ipAddress} />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: '130px 1fr', rowGap: 2 }}>
        <Typography variant="body2" color="text.secondary">Stability</Typography>
        <Typography
          variant="body2"
          fontWeight={700}
          color={
            (device.stabilityScore ?? 0) >= 90
              ? 'success.main'
              : (device.stabilityScore ?? 0) >= 60
                ? 'warning.main'
                : 'error.main'
          }
        >
          {device.stabilityScore ?? 0}/100
        </Typography>

        <Typography variant="body2" color="text.secondary">Uptime</Typography>
        <Typography variant="body2">{device.uptimePercent ?? 0}%</Typography>

        <Typography variant="body2" color="text.secondary">Avg latency</Typography>
        <Typography variant="body2">
          {device.averageLatencyMs ? `${Math.round(device.averageLatencyMs)} ms` : 'N/A'}
        </Typography>

        <Typography variant="body2" color="text.secondary">First discovered</Typography>
        <Typography variant="caption">
          {device.firstDiscoveredUtc
            ? new Date(device.firstDiscoveredUtc).toLocaleString()
            : device.firstSeen
              ? new Date(device.firstSeen).toLocaleString()
              : 'Unknown'}
        </Typography>

        <Typography variant="body2" color="text.secondary">Last seen</Typography>
        <Typography variant="caption">
          {device.lastSeen ? new Date(device.lastSeen).toLocaleString() : ' - '}
        </Typography>
      </Box>

      {!isViewer && (
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 1, pt: 1 }}>
          {isHub && device.nodeId && (
            <Typography variant="caption" color="text.secondary" textAlign="right">
              Resets stability counters on this hub and the owning node.
            </Typography>
          )}
          <Button variant="outlined" color="secondary" size="small" onClick={onResetMetrics} sx={{ fontWeight: 700 }}>
            Reset performance metrics
          </Button>
        </Box>
      )}
    </Stack>
  );
}
