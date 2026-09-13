import {
  Box,
  Button,
  Chip,
  IconButton,
  List,
  ListItem,
  Paper,
  Stack,
  Tooltip,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import RouterIcon from '@mui/icons-material/Router';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import VisibilityIcon from '@mui/icons-material/Visibility';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import DeleteIcon from '@mui/icons-material/Delete';
import type { HubAlertItem } from './types';

interface HubAlertSnippetProps {
  alerts: HubAlertItem[];
  onViewAll: () => void;
  onCopy: (alert: HubAlertItem) => void;
  onViewDevice: (deviceId: string) => void;
  onOpenInAlerts: (alert: HubAlertItem) => void;
  onDismiss: (id: string) => void;
}

export function HubAlertSnippet({
  alerts,
  onViewAll,
  onCopy,
  onViewDevice,
  onOpenInAlerts,
  onDismiss,
}: HubAlertSnippetProps) {
  const theme = useTheme();

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 3,
        borderRadius: 4,
        bgcolor: alpha(theme.palette.background.paper, 0.8),
        backdropFilter: 'blur(20px)',
        border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
        boxShadow: `0 12px 24px ${alpha('#000', 0.2)}`,
      }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5} mb={2}>
        <Box sx={{ p: 0.8, borderRadius: 1.5, bgcolor: alpha(theme.palette.error.main, 0.1) }}>
          <NotificationsActiveIcon color="error" sx={{ fontSize: 20 }} />
        </Box>
        <Box>
          <Typography variant="subtitle1" fontWeight={900} sx={{ letterSpacing: -0.5, lineHeight: 1 }}>
            FLEET ALERTS
          </Typography>
          <Typography variant="caption" color="text.secondary">
            Recent anomalies across enrolled sites
          </Typography>
        </Box>
      </Stack>

      <List sx={{ maxHeight: 320, overflowY: 'auto', pr: 0.5 }}>
        {alerts.map((alert) => {
          const isDown = alert.alertType === 'DeviceDown';
          const isUp = alert.alertType === 'DeviceUp';
          const isNew = alert.alertType === 'NewDeviceDiscovered';
          const isDecoupled = alert.alertType === 'NodeDecoupled';
          const color =
            isDown || isDecoupled
              ? theme.palette.error.main
              : isUp
                ? theme.palette.success.main
                : isNew
                  ? theme.palette.info.main
                  : theme.palette.warning.main;

          return (
            <ListItem
              key={alert.id}
              disablePadding
              sx={{
                mb: 1.5,
                p: 1.5,
                borderRadius: 3,
                bgcolor: alpha(color, 0.04),
                borderLeft: `3px solid ${color}`,
                flexDirection: 'column',
                alignItems: 'flex-start',
              }}
            >
              <Stack direction="row" justifyContent="space-between" width="100%" mb={0.5}>
                <Chip
                  label={alert.alertType?.replace(/([A-Z])/g, ' $1').trim().toUpperCase() || 'ANOMALY'}
                  size="small"
                  sx={{ height: 18, fontSize: '9px', fontWeight: 900, bgcolor: alpha(color, 0.15), color }}
                />
                <Typography variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.6, fontSize: '0.65rem' }}>
                  {new Date(alert.triggeredAt).toLocaleTimeString()}
                </Typography>
              </Stack>
              <Typography variant="caption" fontWeight={800} sx={{ fontSize: '0.75rem' }}>
                {isDecoupled
                  ? `Node "${alert.deviceName}" decoupled`
                  : `${alert.deviceName || alert.deviceIp || 'Unknown'} ${
                      isDown ? 'offline' : isUp ? 'online' : isNew ? 'discovered' : 'updated'
                    }`}
              </Typography>
              <Stack direction="row" justifyContent="space-between" alignItems="center" width="100%" mt={0.75}>
                <Stack direction="row" alignItems="center" spacing={0.5} sx={{ opacity: 0.7 }}>
                  <RouterIcon sx={{ fontSize: 12 }} />
                  <Typography variant="caption" fontWeight={700} sx={{ fontSize: '0.65rem' }}>
                    {alert.nodeName || 'Remote node'}
                    {alert.room ? ` · ${alert.room}` : ''}
                  </Typography>
                </Stack>
                <Stack direction="row" spacing={0.25}>
                  <Tooltip title="Open in Alerts">
                    <IconButton size="small" onClick={() => onOpenInAlerts(alert)}>
                      <OpenInNewIcon sx={{ fontSize: 13 }} />
                    </IconButton>
                  </Tooltip>
                  <Tooltip title="Copy details">
                    <IconButton size="small" onClick={() => onCopy(alert)}>
                      <ContentCopyIcon sx={{ fontSize: 13 }} />
                    </IconButton>
                  </Tooltip>
                  {alert.deviceId && (
                    <Tooltip title="View device">
                      <IconButton size="small" color="primary" onClick={() => onViewDevice(alert.deviceId!)}>
                        <VisibilityIcon sx={{ fontSize: 13 }} />
                      </IconButton>
                    </Tooltip>
                  )}
                  <Tooltip title="Dismiss">
                    <IconButton size="small" color="error" onClick={() => onDismiss(alert.id)}>
                      <DeleteIcon sx={{ fontSize: 13 }} />
                    </IconButton>
                  </Tooltip>
                </Stack>
              </Stack>
            </ListItem>
          );
        })}
        {alerts.length === 0 && (
          <Box sx={{ py: 4, textAlign: 'center', opacity: 0.4 }}>
            <CheckCircleIcon sx={{ mb: 1 }} />
            <Typography variant="caption" fontWeight={700} display="block">
              All systems nominal
            </Typography>
          </Box>
        )}
      </List>

      <Button
        fullWidth
        variant="contained"
        color="error"
        onClick={onViewAll}
        sx={{ mt: 2, py: 1.25, fontWeight: 900, borderRadius: 3 }}
      >
        View all alerts
      </Button>
    </Paper>
  );
}
