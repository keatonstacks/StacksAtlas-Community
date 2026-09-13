import { Avatar, Badge, useTheme } from '@mui/material';
import { getDeviceIcon } from '../../utils/deviceUtils';
import type { Device } from '../../models/Device';

interface DeviceStatusAvatarProps {
  device: Pick<Device, 'type' | 'model' | 'name' | 'status'>;
  size?: number;
}

export function DeviceStatusAvatar({ device, size = 40 }: DeviceStatusAvatarProps) {
  const theme = useTheme();
  const isOnline = device.status?.toLowerCase() === 'online';

  return (
    <Badge
      overlap="circular"
      variant="dot"
      sx={{
        '& .MuiBadge-badge': {
          bgcolor: isOnline ? 'success.main' : 'error.main',
          width: 10,
          height: 10,
          borderRadius: '50%',
          border: `2px solid ${theme.palette.background.paper}`,
          ...(isOnline && {
            '&::after': {
              position: 'absolute',
              top: 0,
              left: 0,
              width: '100%',
              height: '100%',
              borderRadius: '50%',
              animation: 'deviceStatusRipple 1.2s infinite ease-in-out',
              border: '1px solid currentColor',
              content: '""',
            },
          }),
        },
        '@keyframes deviceStatusRipple': {
          '0%': { transform: 'scale(.8)', opacity: 1 },
          '100%': { transform: 'scale(2.4)', opacity: 0 },
        },
      }}
    >
      <Avatar
        sx={{
          width: size,
          height: size,
          bgcolor: 'background.default',
          border: '1px solid',
          borderColor: 'divider',
          color: 'primary.main',
        }}
      >
        {getDeviceIcon(device.type, device.model, device.name)}
      </Avatar>
    </Badge>
  );
}
