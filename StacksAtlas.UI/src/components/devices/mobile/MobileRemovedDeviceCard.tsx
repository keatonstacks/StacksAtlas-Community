import {
  Box, Button, Checkbox, Chip, Paper, Stack, Tooltip, Typography, alpha, useTheme,
} from '@mui/material';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import RadarIcon from '@mui/icons-material/Radar';
import type { Device } from '../../../models/Device';

function formatWhen(iso?: string | null): string {
  if (!iso) return ' - ';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return ' - ';
  }
}

interface MobileRemovedDeviceCardProps {
  device: Device;
  nodeLabel: string;
  isPortable?: boolean;
  selectionEnabled?: boolean;
  selected?: boolean;
  isAdmin?: boolean;
  busy?: boolean;
  onToggleSelect?: (id: string) => void;
  onOpen: () => void;
  onRestoreToFleet: () => void;
}

export function MobileRemovedDeviceCard({
  device,
  nodeLabel,
  isPortable = false,
  selectionEnabled = false,
  selected = false,
  isAdmin = false,
  busy = false,
  onToggleSelect,
  onOpen,
  onRestoreToFleet,
}: MobileRemovedDeviceCardProps) {
  const theme = useTheme();
  const hits = device.rediscoveryHitCount ?? 0;
  const title = device.name || device.ipAddress || 'Unknown';

  return (
    <Paper
      variant="outlined"
      onClick={onOpen}
      sx={{
        p: 1.5,
        borderRadius: 2,
        cursor: 'pointer',
        bgcolor: alpha(theme.palette.error.main, 0.03),
        borderColor: alpha(theme.palette.error.main, 0.15),
        '&:active': { bgcolor: alpha(theme.palette.error.main, 0.06) },
      }}
    >
      <Stack direction="row" spacing={1.25} alignItems="flex-start">
        {selectionEnabled && isAdmin && (
          <Checkbox
            size="small"
            checked={selected}
            onClick={(e) => e.stopPropagation()}
            onChange={() => onToggleSelect?.(device.id)}
            sx={{ p: 0.5, mt: 0.25 }}
          />
        )}
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" fontWeight={800} noWrap>
            {title}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" noWrap fontFamily="monospace">
            {device.ipAddress || ' - '} · {device.macAddress || ' - '}
          </Typography>
          {!isPortable && (
            <Typography variant="caption" color="text.secondary" display="block" noWrap sx={{ mt: 0.25 }}>
              {nodeLabel}
            </Typography>
          )}
          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap sx={{ mt: 1 }}>
            <Chip
              label={`Removed ${formatWhen(device.removedUtc)}`}
              size="small"
              variant="outlined"
              sx={{ borderRadius: 1.5, fontSize: '0.65rem', fontWeight: 700, maxWidth: '100%' }}
            />
            {device.removedBy && (
              <Chip
                label={`By ${device.removedBy}`}
                size="small"
                sx={{ borderRadius: 1.5, fontSize: '0.65rem', fontWeight: 700, maxWidth: '100%' }}
              />
            )}
            <Tooltip
              title={
                hits > 0
                  ? 'Network scans detected this identity after removal (logged only  -  not re-added).'
                  : 'No rediscovery hits since removal.'
              }
            >
              <Chip
                icon={hits > 0 ? <RadarIcon /> : undefined}
                label={`Rediscovery: ${hits}`}
                size="small"
                color={hits > 0 ? 'warning' : 'default'}
                variant={hits > 0 ? 'filled' : 'outlined'}
                sx={{ borderRadius: 1.5, fontWeight: 800, fontSize: '0.65rem' }}
              />
            </Tooltip>
          </Stack>
          {hits > 0 && device.lastRediscoveryAttemptUtc && (
            <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.75 }}>
              Last hit: {formatWhen(device.lastRediscoveryAttemptUtc)}
              {device.lastRediscoveryIp ? ` · ${device.lastRediscoveryIp}` : ''}
            </Typography>
          )}
        </Box>
      </Stack>
      {isAdmin && (
        <Button
          size="small"
          variant="contained"
          color="success"
          fullWidth
          disabled={busy}
          startIcon={<RestoreFromTrashIcon />}
          onClick={(e) => {
            e.stopPropagation();
            onRestoreToFleet();
          }}
          sx={{ mt: 1.5, fontWeight: 800, borderRadius: 2 }}
        >
          Restore to Fleet
        </Button>
      )}
    </Paper>
  );
}
