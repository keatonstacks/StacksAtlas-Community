import {
  Box,
  Chip,
  CircularProgress,
  IconButton,
  Stack,
  Tooltip,
  Typography,
  Button,
  alpha,
  useTheme,
} from '@mui/material';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import PowerSettingsNewIcon from '@mui/icons-material/PowerSettingsNew';
import RadarIcon from '@mui/icons-material/Radar';
import ScreenShareIcon from '@mui/icons-material/ScreenShare';
import TerminalIcon from '@mui/icons-material/Terminal';
import ShieldIcon from '@mui/icons-material/Shield';
import type { Device } from '../../models/Device';
import { deviceDisplayName, downloadRdpFile, normalizeSecurityGrade, securityGradeLabel } from './deviceDrawerUtils';
import { openExternalUrl } from '../../utils/openExternalUrl';

interface DeviceDrawerHeaderProps {
  device: Device;
  onClose: () => void;
  isPinging: boolean;
  pingResult: string | null;
  isWaking: boolean;
  wakeResult: string | null;
  isDeepScanning: boolean;
  scanProgress: number;
  deepScanStatus: string;
  hasDeepScanReady: boolean;
  deepScanBlockedReason: string;
  canAct: boolean;
  onPing: () => void;
  onWake: () => void;
  onDeepScan: () => void;
}

export function DeviceDrawerHeader({
  device,
  onClose,
  isPinging,
  pingResult,
  isWaking,
  wakeResult,
  isDeepScanning,
  scanProgress,
  deepScanStatus,
  hasDeepScanReady,
  deepScanBlockedReason,
  canAct,
  onPing,
  onWake,
  onDeepScan,
}: DeviceDrawerHeaderProps) {
  const theme = useTheme();
  const title = deviceDisplayName(device.name, device.hostname, device.ipAddress);
  const grade = normalizeSecurityGrade(device.securityGrade);
  const gradeColor =
    grade === 2 ? theme.palette.error.main : grade === 1 ? theme.palette.warning.main : theme.palette.success.main;
  const hasRdp = device.openPorts?.includes(3389);
  const hasSsh = device.openPorts?.includes(22);

  return (
    <Box
      sx={{
        px: 2,
        pt: 2,
        pb: 1.5,
        borderBottom: '1px solid',
        borderColor: 'divider',
        bgcolor: 'background.paper',
        flexShrink: 0,
      }}
    >
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={1} mb={1.5}>
        <Box sx={{ minWidth: 0, flex: 1 }}>
          <Typography variant="subtitle1" fontWeight={800} noWrap title={title}>
            {title}
          </Typography>
          <Stack direction="row" alignItems="center" spacing={0.75} flexWrap="wrap" mt={0.5}>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>
              {device.ipAddress}
            </Typography>
            <IconButton
              size="small"
              onClick={() => navigator.clipboard.writeText(device.ipAddress)}
              aria-label="Copy IP"
              sx={{ p: 0.25 }}
            >
              <ContentCopyIcon sx={{ fontSize: 14 }} />
            </IconButton>
            <Chip
              size="small"
              label={device.status === 'online' ? 'ONLINE' : 'OFFLINE'}
              sx={{
                height: 20,
                fontSize: '0.65rem',
                fontWeight: 800,
                bgcolor: alpha(device.status === 'online' ? theme.palette.success.main : theme.palette.error.main, 0.12),
                color: device.status === 'online' ? 'success.main' : 'error.main',
              }}
            />
            <Chip
              size="small"
              icon={<ShieldIcon sx={{ fontSize: '14px !important' }} />}
              label={securityGradeLabel(grade)}
              sx={{
                height: 20,
                fontSize: '0.65rem',
                fontWeight: 800,
                bgcolor: alpha(gradeColor, 0.12),
                color: gradeColor,
              }}
            />
          </Stack>
        </Box>
        <IconButton onClick={onClose} size="small" aria-label="Close drawer">
          <CloseIcon />
        </IconButton>
      </Stack>

      {canAct && (
        <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap>
          <Button
            size="small"
            variant="contained"
            disableElevation
            disabled={isPinging}
            startIcon={isPinging ? <CircularProgress size={14} color="inherit" /> : <RadarIcon />}
            onClick={onPing}
            sx={{
              fontWeight: 700,
              borderRadius: 2,
              flex: '1 1 auto',
              minWidth: 0,
              bgcolor: pingResult?.includes('REPLY') ? 'success.main' : pingResult ? 'error.main' : undefined,
            }}
          >
            {isPinging ? 'Ping…' : pingResult || 'Ping'}
          </Button>
          <Button
            size="small"
            variant="outlined"
            disabled={isWaking || !device.macAddress}
            startIcon={isWaking ? <CircularProgress size={14} color="inherit" /> : <PowerSettingsNewIcon />}
            onClick={onWake}
            sx={{ fontWeight: 700, borderRadius: 2, flex: '1 1 auto', minWidth: 0 }}
          >
            {isWaking ? 'Wake…' : wakeResult || 'Wake'}
          </Button>
          {hasSsh && (
            <Tooltip title="Open SSH">
              <Button
                size="small"
                variant="outlined"
                onClick={() => openExternalUrl(`ssh://${device.ipAddress}`)}
                sx={{ minWidth: 40, px: 1 }}
              >
                <TerminalIcon fontSize="small" />
              </Button>
            </Tooltip>
          )}
          {hasRdp && (
            <Tooltip title="Download RDP file">
              <Button
                size="small"
                variant="outlined"
                onClick={() => downloadRdpFile(device.ipAddress)}
                sx={{ minWidth: 40, px: 1 }}
              >
                <ScreenShareIcon fontSize="small" />
              </Button>
            </Tooltip>
          )}
          <Tooltip title={!hasDeepScanReady ? deepScanBlockedReason : isDeepScanning ? `Scanning (${scanProgress}%)` : 'Deep Scan'}>
            <span style={{ flex: '1 1 100%' }}>
              <Button
                size="small"
                variant="outlined"
                fullWidth
                disabled={isDeepScanning || !hasDeepScanReady}
                startIcon={isDeepScanning ? <CircularProgress size={14} color="inherit" /> : <RadarIcon />}
                onClick={onDeepScan}
                sx={{ fontWeight: 700, borderRadius: 2 }}
              >
                {isDeepScanning
                  ? `Scanning ${scanProgress}%`
                  : deepScanStatus === 'error'
                    ? 'Retry scan'
                    : 'Deep scan'}
              </Button>
            </span>
          </Tooltip>
        </Stack>
      )}
    </Box>
  );
}
