import {
  Box,
  Chip,
  LinearProgress,
  Paper,
  Stack,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import RadarIcon from '@mui/icons-material/Radar';
import type { Device } from '../../models/Device';
import { EditableField } from '../EditableField';
import { DeviceTypeSelect } from './DeviceTypeSelect';
import { DeviceAssetFields } from './DeviceAssetFields';
import { DeviceAttachmentFields } from './DeviceAttachmentFields';
import { DeviceDrawerDangerZone } from './DeviceDrawerDangerZone';

interface DeviceDrawerOverviewTabProps {
  device: Device;
  isHub: boolean;
  nodesMap?: Record<string, string>;
  isRemoved: boolean;
  isArchive: boolean;
  role?: string | null;
  isAdmin: boolean;
  onDeviceUpdate: (device?: Device) => void;
  onNotify?: (message: string) => void;
  onDelete: (device: Device) => void;
  onRestore: (device: Device) => void;
  onHardDelete?: (device: Device) => void;
  onRestoreToFleet?: (device: Device) => void;
}

export function DeviceDrawerOverviewTab({
  device,
  isHub,
  nodesMap,
  isRemoved,
  isArchive,
  role,
  isAdmin,
  onDeviceUpdate,
  onNotify,
  onDelete,
  onRestore,
  onHardDelete,
  onRestoreToFleet,
}: DeviceDrawerOverviewTabProps) {
  const theme = useTheme();
  const isViewer = role?.toLowerCase() === 'viewer';

  return (
    <Stack spacing={3}>
      {isRemoved && (
        <Paper
          variant="outlined"
          sx={{
            p: 2,
            borderRadius: 3,
            bgcolor: alpha(theme.palette.error.main, 0.06),
            borderColor: alpha(theme.palette.error.main, 0.35),
          }}
        >
          <Typography variant="overline" color="error.main" fontWeight={800} display="block" mb={1}>
            Removed from fleet
          </Typography>
          <Typography variant="caption" display="block">
            <strong>Removed:</strong> {device.removedUtc ? new Date(device.removedUtc).toLocaleString() : ' - '}
          </Typography>
          <Typography variant="caption" display="block">
            <strong>By:</strong> {device.removedBy || ' - '}
          </Typography>
          <Chip
            icon={(device.rediscoveryHitCount ?? 0) > 0 ? <RadarIcon /> : undefined}
            label={`Rediscovery hits: ${device.rediscoveryHitCount ?? 0}`}
            size="small"
            color={(device.rediscoveryHitCount ?? 0) > 0 ? 'warning' : 'default'}
            sx={{ fontWeight: 800, mt: 1 }}
          />
        </Paper>
      )}

      <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.background.default, 0.3) }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: '110px 1fr', rowGap: 2, alignItems: 'center' }}>
          <Typography variant="body2" color="text.secondary">Name</Typography>
          <EditableField device={device} fieldName="name" onUpdated={onDeviceUpdate} disabled={isViewer} />

          <Typography variant="body2" color="text.secondary">Hostname</Typography>
          <Box>
            <EditableField
              device={device}
              fieldName="hostname"
              onUpdated={onDeviceUpdate}
              disabled={isViewer}
              monospace
              placeholder="Set when reverse DNS is unavailable"
            />
            {device.isHostnameManuallySet && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
                Protected from discovery overwrites
              </Typography>
            )}
          </Box>

          <Typography variant="body2" color="text.secondary">Location</Typography>
          <EditableField device={device} fieldName="location" onUpdated={onDeviceUpdate} disabled={isViewer} />

          <Typography variant="body2" color="text.secondary">Model</Typography>
          <EditableField device={device} fieldName="model" onUpdated={onDeviceUpdate} disabled={isViewer} />

          <Typography variant="body2" color="text.secondary">Type</Typography>
          <DeviceTypeSelect device={device} onUpdated={onDeviceUpdate} disabled={isViewer} />

          <Typography variant="body2" color="text.secondary">Confidence</Typography>
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
            <LinearProgress
              variant="determinate"
              value={Math.min(100, device.confidenceScore)}
              sx={{ width: 80, height: 6, borderRadius: 3 }}
            />
            <Typography variant="caption" fontWeight={700}>{device.confidenceScore}%</Typography>
          </Box>

          <Typography variant="body2" color="text.secondary">OS</Typography>
          <Typography variant="body2">{device.operatingSystem || 'Unknown'}</Typography>

          <Typography variant="body2" color="text.secondary">MAC</Typography>
          <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.75rem' }}>{device.macAddress}</Typography>

          {(device.discoveryInterfaceName || device.discoveryInterfaceId) && (
            <>
              <Typography variant="body2" color="text.secondary">Discovered via</Typography>
              <Typography variant="body2" sx={{ fontFamily: 'monospace', fontSize: '0.75rem' }}>
                {device.discoveryInterfaceName || device.discoveryInterfaceId}
              </Typography>
            </>
          )}

          {device.discoveryVlanTag && (
            <>
              <Typography variant="body2" color="text.secondary">VLAN</Typography>
              <Typography variant="body2" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>
                {device.discoveryVlanTag}
              </Typography>
            </>
          )}

          <Typography variant="body2" color="text.secondary">Vendor</Typography>
          <EditableField device={device} fieldName="vendor" onUpdated={onDeviceUpdate} disabled={isViewer} />

          {isHub && (
            <>
              <Typography variant="body2" color="text.secondary">Site</Typography>
              <Chip
                label={device.nodeId ? (nodesMap?.[device.nodeId] || device.nodeId).toUpperCase() : 'LOCAL'}
                size="small"
                sx={{ width: 'fit-content', fontWeight: 900, fontSize: '0.65rem' }}
              />
            </>
          )}
        </Box>
      </Paper>

      <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.background.default, 0.3) }}>
        <Typography variant="overline" color="text.secondary" fontWeight={700} display="block" sx={{ mb: 2 }}>
          Network link
        </Typography>
        <DeviceAttachmentFields
          device={device}
          onUpdated={(updated) => onDeviceUpdate(updated)}
          onError={(msg) => onNotify?.(msg)}
          disabled={isViewer}
        />
      </Paper>

      <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.background.default, 0.3) }}>
        <Typography variant="overline" color="text.secondary" fontWeight={700} display="block" sx={{ mb: 2 }}>
          Asset
        </Typography>
        <DeviceAssetFields
          device={device}
          onUpdated={(updated) => onDeviceUpdate(updated)}
          onError={(msg) => onNotify?.(msg)}
          disabled={isViewer}
        />
      </Paper>

      <DeviceDrawerDangerZone
        device={device}
        isRemoved={isRemoved}
        isArchive={isArchive}
        isAdmin={isAdmin}
        role={role}
        onDelete={onDelete}
        onRestore={onRestore}
        onHardDelete={onHardDelete}
        onRestoreToFleet={onRestoreToFleet}
      />
    </Stack>
  );
}
