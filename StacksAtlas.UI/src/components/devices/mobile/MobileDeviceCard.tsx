import {
  Box, Checkbox, Chip, LinearProgress, Stack, Typography, alpha, useTheme,
} from '@mui/material';
import ShieldIcon from '@mui/icons-material/Shield';
import { DeviceStatusAvatar } from '../DeviceStatusAvatar';
import { isDanteDevice, DanteBadge } from '../../../utils/deviceUtils';
import type { Device } from '../../../models/Device';

interface MobileDeviceCardProps {
  device: Device;
  isPtpMaster?: boolean;
  nodeLabel?: string;
  openAvcDriver?: string;
  openAvcIntegrationStatus?: string;
  selectionEnabled?: boolean;
  selected?: boolean;
  onToggleSelect?: (id: string) => void;
  onOpen: () => void;
}

const gradeLabels = ['GREEN', 'YELLOW', 'RED'] as const;
const gradeColors = ['success.main', 'warning.main', 'error.main'] as const;

export function MobileDeviceCard({
  device,
  isPtpMaster = false,
  nodeLabel,
  openAvcDriver,
  openAvcIntegrationStatus,
  selectionEnabled = false,
  selected = false,
  onToggleSelect,
  onOpen,
}: MobileDeviceCardProps) {
  const theme = useTheme();
  const stability = device.stabilityScore ?? 0;
  const statusColor = stability > 80 ? theme.palette.success.main : stability > 50 ? theme.palette.warning.main : theme.palette.error.main;
  const grade = device.securityGrade ?? 0;

  const openAvcChipColor =
    openAvcIntegrationStatus === 'auth_failed'
      ? 'error'
      : openAvcIntegrationStatus === 'offline' || openAvcIntegrationStatus === 'disabled'
        ? 'default'
        : 'info';

  return (
    <Box
      onClick={onOpen}
      sx={{
        p: 1.5,
        borderRadius: 2,
        border: '1px solid',
        borderColor: isPtpMaster ? alpha(theme.palette.info.main, 0.35) : 'divider',
        bgcolor: isPtpMaster ? alpha(theme.palette.info.main, 0.06) : alpha(theme.palette.background.paper, 0.6),
        cursor: 'pointer',
      }}
    >
      <Stack direction="row" spacing={1.5} alignItems="flex-start">
        {selectionEnabled && (
          <Checkbox
            size="small"
            checked={selected}
            onClick={(e) => e.stopPropagation()}
            onChange={() => onToggleSelect?.(device.id)}
            sx={{ p: 0.5, mt: 0.25 }}
          />
        )}
        <DeviceStatusAvatar device={device} size={40} />
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" fontWeight={800} noWrap>
            {device.name || device.ipAddress}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" noWrap>
            {device.vendor || 'Unknown'} · {device.ipAddress}
          </Typography>
          {nodeLabel && (
            <Typography variant="caption" color="text.secondary" display="block" noWrap sx={{ mt: 0.25 }}>
              {nodeLabel}
            </Typography>
          )}
          {openAvcDriver && (
            <Chip
              size="small"
              label={openAvcDriver}
              color={openAvcChipColor}
              variant="outlined"
              sx={{
                mt: 0.5,
                height: 18,
                fontSize: '0.6rem',
                fontWeight: 800,
                opacity: openAvcChipColor === 'default' ? 0.65 : 1,
              }}
            />
          )}
          <Stack direction="row" alignItems="center" spacing={1} sx={{ mt: 0.75 }} flexWrap="wrap" useFlexGap>
            <LinearProgress
              variant="determinate"
              value={stability}
              sx={{
                width: 48,
                height: 4,
                borderRadius: 2,
                bgcolor: alpha(theme.palette.divider, 0.1),
                '& .MuiLinearProgress-bar': { bgcolor: statusColor },
              }}
            />
            <Typography variant="caption" fontWeight={900} sx={{ color: statusColor }}>
              {stability}%
            </Typography>
            <Chip
              icon={<ShieldIcon sx={{ fontSize: '14px !important' }} />}
              label={gradeLabels[grade] ?? 'GREEN'}
              size="small"
              sx={{
                height: 20,
                fontSize: '0.6rem',
                fontWeight: 800,
                color: gradeColors[grade],
              }}
              variant="outlined"
            />
            {isDanteDevice(device) && <DanteBadge />}
          </Stack>
        </Box>
      </Stack>
    </Box>
  );
}
