import {
  Box,
  Tooltip,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import WarningIcon from '@mui/icons-material/Warning';
import LaunchIcon from '@mui/icons-material/Launch';
import ScreenShareIcon from '@mui/icons-material/ScreenShare';
import CloudIcon from '@mui/icons-material/Cloud';
import type { Device } from '../../models/Device';
import { getPortServiceName } from '../../utils/deviceUtils';
import { openExternalUrl } from '../../utils/openExternalUrl';
import { downloadRdpFile } from './deviceDrawerUtils';

interface OpenPortsGridProps {
  device: Device;
  isHub: boolean;
}

export function OpenPortsGrid({ device, isHub }: OpenPortsGridProps) {
  const theme = useTheme();

  if (!device.openPorts?.length) {
    return (
      <Typography variant="body2" color="text.disabled" sx={{ fontStyle: 'italic', bgcolor: 'action.hover', p: 2, borderRadius: 2, textAlign: 'center' }}>
        No open ports detected in last scan.
      </Typography>
    );
  }

  return (
    <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap' }}>
      {device.openPorts.map((port) => {
        const isRisky = [21, 23, 80].includes(port);
        const serviceName = getPortServiceName(port);
        const isWeb = [80, 443, 8080, 8443].includes(port);
        const isRdp = port === 3389;
        const isSsh = port === 22;
        const isTelnet = port === 23;
        const isFtp = port === 21;
        const isClickable = isWeb || isRdp || isSsh || isTelnet || isFtp;

        let tooltipTitle = '';
        if (isRdp) tooltipTitle = 'Download RDP connection file';
        else if (isSsh) tooltipTitle = 'Open SSH (ssh://)';
        else if (isRisky) {
          tooltipTitle = 'Security warning: unencrypted protocol';
          if (isTelnet) tooltipTitle += ' (telnet://)';
          else if (isFtp) tooltipTitle += ' (ftp://)';
          else if (isWeb) tooltipTitle += ' (open in browser)';
        } else {
          tooltipTitle = `Service: ${serviceName}`;
        }

        return (
          <Tooltip title={tooltipTitle} key={port} arrow>
            <Box
              role={isClickable ? 'button' : undefined}
              tabIndex={isClickable ? 0 : undefined}
              onKeyDown={(event) => {
                if (!isClickable || (event.key !== 'Enter' && event.key !== ' '))
                  return;
                event.preventDefault();
                (event.currentTarget as HTMLElement).click();
              }}
              onClick={(event) => {
                if (!isClickable)
                  return;
                event.preventDefault();
                event.stopPropagation();
                if (isWeb) {
                  const protocol = port === 443 || port === 8443 ? 'https' : 'http';
                  window.open(`${protocol}://${device.ipAddress}:${port}`, '_blank', 'noopener,noreferrer');
                } else if (isRdp) {
                  downloadRdpFile(device.ipAddress);
                } else if (isSsh) {
                  openExternalUrl(`ssh://${device.ipAddress}`);
                } else if (isTelnet) {
                  openExternalUrl(`telnet://${device.ipAddress}`);
                } else if (isFtp) {
                  window.open(`ftp://${device.ipAddress}`, '_blank', 'noopener,noreferrer');
                }
              }}
              sx={{
                border: '1px solid',
                borderColor: isRisky ? 'error.main' : isClickable ? 'primary.main' : 'divider',
                color: isRisky ? 'error.main' : isClickable ? 'primary.main' : 'text.primary',
                px: 1.5,
                py: 0.75,
                borderRadius: 5,
                fontSize: '0.75rem',
                fontWeight: 700,
                fontFamily: 'monospace',
                bgcolor: isRisky
                  ? alpha(theme.palette.error.main, 0.05)
                  : isClickable
                    ? alpha(theme.palette.primary.main, 0.05)
                    : 'transparent',
                display: 'flex',
                alignItems: 'center',
                gap: 0.5,
                cursor: isClickable ? 'pointer' : 'default',
                transition: 'all 0.2s',
                '&:hover': isClickable
                  ? {
                      bgcolor: isRisky
                        ? alpha(theme.palette.error.main, 0.15)
                        : alpha(theme.palette.primary.main, 0.15),
                      transform: 'translateY(-2px)',
                    }
                  : {},
              }}
            >
              <Typography variant="caption" sx={{ fontFamily: 'monospace', fontWeight: 800, fontSize: '0.85rem', lineHeight: 1 }}>
                {port}
              </Typography>
              {isRisky && <WarningIcon sx={{ fontSize: 12 }} />}
              {isClickable && !isRisky && (isRdp ? <ScreenShareIcon sx={{ fontSize: 12 }} /> : <LaunchIcon sx={{ fontSize: 12 }} />)}
              {isHub && device.nodeId && <CloudIcon sx={{ fontSize: 10, opacity: 0.7 }} />}
            </Box>
          </Tooltip>
        );
      })}
    </Box>
  );
}
