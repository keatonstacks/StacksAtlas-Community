import {
  Box,
  Button,
  Divider,
  FormControl,
  MenuItem,
  Paper,
  Select,
  Stack,
  Switch,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import WarningIcon from '@mui/icons-material/Warning';
import type { Device } from '../../models/Device';
import { ApiService } from '../../services/apiService';
import type { DrawerUser } from './types';

interface DeviceDrawerManageTabProps {
  device: Device;
  role?: string | null;
  isAdmin: boolean;
  userId?: string | null;
  user?: { username: string } | null;
  users: DrawerUser[];
  deviceAlertsEnabled: boolean;
  userNotificationsGloballyEnabled: boolean;
  isViewer: boolean;
  onDeviceUpdate: () => void;
  onToggleAlerts: () => void;
}

export function DeviceDrawerManageTab({
  device,
  role,
  isAdmin,
  userId,
  user,
  users,
  deviceAlertsEnabled,
  userNotificationsGloballyEnabled,
  isViewer,
  onDeviceUpdate,
  onToggleAlerts,
}: DeviceDrawerManageTabProps) {
  const theme = useTheme();

  return (
    <Stack spacing={2}>
      <Typography variant="overline" color="text.secondary" fontWeight={700}>
        Responsibility & alerting
      </Typography>

      {!userNotificationsGloballyEnabled && (
        <Paper sx={{ p: 1.5, bgcolor: alpha(theme.palette.warning.main, 0.1), borderRadius: 2 }}>
          <Stack direction="row" spacing={1} alignItems="center">
            <WarningIcon color="warning" sx={{ fontSize: 18 }} />
            <Typography variant="caption" fontWeight={700} color="warning.main">
              Global notifications disabled in your profile.
            </Typography>
          </Stack>
        </Paper>
      )}

      <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
        <Stack spacing={2}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Box>
              <Typography variant="body2" fontWeight={700}>Monitoring alerts</Typography>
              <Typography variant="caption" color="text.secondary">Notify on state changes</Typography>
            </Box>
            <Switch size="small" checked={deviceAlertsEnabled} onChange={onToggleAlerts} disabled={isViewer} />
          </Box>
          <Divider sx={{ borderStyle: 'dashed' }} />
          {device.managedByUserId ? (
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Typography variant="body1" fontWeight={900} color="primary.main">
                {device.managedByUsername}
              </Typography>
              {(isAdmin || userId === device.managedByUserId) && (
                <Button
                  color="error"
                  size="small"
                  disabled={isViewer}
                  onClick={async () => {
                    await ApiService.updateDeviceManager(device.id, null, null);
                    onDeviceUpdate();
                  }}
                >
                  Release
                </Button>
              )}
            </Stack>
          ) : (
            <Stack direction="row" spacing={1} flexWrap="wrap">
              {(role?.toLowerCase() === 'standard' || isAdmin) && userId && user && (
                <Button
                  size="small"
                  disabled={isViewer}
                  onClick={async () => {
                    await ApiService.updateDeviceManager(device.id, userId, user.username);
                    onDeviceUpdate();
                  }}
                >
                  Claim device
                </Button>
              )}
              {isAdmin && (
                <FormControl size="small" variant="standard" sx={{ minWidth: 120 }}>
                  <Select
                    displayEmpty
                    value=""
                    disabled={isViewer}
                    onChange={async (e) => {
                      const targetUser = users.find((u) => u.id === e.target.value);
                      if (targetUser) {
                        await ApiService.updateDeviceManager(device.id, targetUser.id, targetUser.username);
                        onDeviceUpdate();
                      }
                    }}
                  >
                    <MenuItem value="" disabled>Assign to…</MenuItem>
                    {users.map((u) => (
                      <MenuItem key={u.id} value={u.id}>{u.username}</MenuItem>
                    ))}
                  </Select>
                </FormControl>
              )}
            </Stack>
          )}
        </Stack>
      </Paper>
    </Stack>
  );
}
