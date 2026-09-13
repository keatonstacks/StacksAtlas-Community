import {
  Box, IconButton, LinearProgress, Stack, Tooltip, Typography, alpha, useTheme,
} from '@mui/material';

import ContentCopyIcon from '@mui/icons-material/ContentCopy';

import DeleteIcon from '@mui/icons-material/Delete';

import OpenInNewIcon from '@mui/icons-material/OpenInNew';

import VisibilityIcon from '@mui/icons-material/Visibility';

import { DeviceStatusAvatar } from '../../devices/DeviceStatusAvatar';

import type { Device } from '../../../models/Device';



interface MobileNodeRegistryCardProps {

  device: Device;

  isPtpMaster: boolean;

  isAdmin: boolean;

  onOpen: () => void;

  onViewDevices: () => void;

  onCopyIp: () => void;

  onIgnore: () => void;

}



export function MobileNodeRegistryCard({

  device,

  isPtpMaster,

  isAdmin,

  onOpen,

  onViewDevices,

  onCopyIp,

  onIgnore,

}: MobileNodeRegistryCardProps) {

  const theme = useTheme();

  const score = device.stabilityScore ?? 0;

  const statusColor = score > 80 ? theme.palette.success.main : score > 50 ? theme.palette.warning.main : theme.palette.error.main;



  return (

    <Box

      sx={{

        p: 1.5,

        borderRadius: 2,

        border: '1px solid',

        borderColor: isPtpMaster ? alpha(theme.palette.info.main, 0.35) : 'divider',

        bgcolor: isPtpMaster ? alpha(theme.palette.info.main, 0.06) : alpha(theme.palette.background.paper, 0.6),

      }}

    >

      <Stack direction="row" spacing={1.5} alignItems="flex-start" onClick={onOpen} sx={{ cursor: 'pointer' }}>

        <DeviceStatusAvatar device={device} size={40} />

        <Box sx={{ flex: 1, minWidth: 0 }}>

          <Typography variant="body2" fontWeight={800} noWrap>

            {device.name || 'UNIDENTIFIED NODE'}

          </Typography>

          <Typography variant="caption" color="text.secondary" display="block" noWrap>

            {device.vendor || 'GENERIC'} · {device.ipAddress}

          </Typography>

          <Stack direction="row" alignItems="center" spacing={1} sx={{ mt: 0.75 }}>

            <LinearProgress

              variant="determinate"

              value={score}

              sx={{

                width: 48,

                height: 4,

                borderRadius: 2,

                bgcolor: alpha(theme.palette.divider, 0.1),

                '& .MuiLinearProgress-bar': { bgcolor: statusColor },

              }}

            />

            <Typography variant="caption" fontWeight={900} sx={{ color: statusColor }}>

              {score}%

            </Typography>

          </Stack>

        </Box>

      </Stack>

      <Stack direction="row" justifyContent="flex-end" spacing={0.5} sx={{ mt: 1 }}>

        <Tooltip title="Open device drawer">

          <IconButton size="small" onClick={onOpen} aria-label="Open device">

            <VisibilityIcon fontSize="small" />

          </IconButton>

        </Tooltip>

        <Tooltip title="Open in Devices">

          <IconButton size="small" onClick={onViewDevices} aria-label="Open in devices">

            <OpenInNewIcon fontSize="small" color="primary" />

          </IconButton>

        </Tooltip>

        <Tooltip title="Copy IP">

          <IconButton size="small" onClick={onCopyIp} aria-label="Copy IP">

            <ContentCopyIcon fontSize="small" />

          </IconButton>

        </Tooltip>

        {isAdmin && (

          <Tooltip title="Ignore node">

            <IconButton size="small" color="error" onClick={onIgnore} aria-label="Ignore node">

              <DeleteIcon fontSize="small" />

            </IconButton>

          </Tooltip>

        )}

      </Stack>

    </Box>

  );

}

