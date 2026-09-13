import {
  Box, Chip, IconButton, Paper, Stack, Tooltip, Typography, alpha, useTheme,
} from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import ShieldIcon from '@mui/icons-material/Shield';
import BadgeIcon from '@mui/icons-material/Badge';
import VisibilityIcon from '@mui/icons-material/Visibility';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import VpnKeyIcon from '@mui/icons-material/VpnKey';
import type { User } from '../../../models/User';

function getRoleChipColor(role: string): 'error' | 'primary' | 'default' | 'secondary' {
  switch (role) {
    case 'Admin': return 'error';
    case 'Standard': return 'primary';
    case 'Viewer': return 'default';
    case 'AlertOnly': return 'secondary';
    default: return 'default';
  }
}

function getRoleIcon(role: string) {
  switch (role) {
    case 'Admin': return <ShieldIcon sx={{ fontSize: 16 }} />;
    case 'Standard': return <BadgeIcon sx={{ fontSize: 16 }} />;
    case 'Viewer': return <VisibilityIcon sx={{ fontSize: 16 }} />;
    case 'AlertOnly': return <NotificationsActiveIcon sx={{ fontSize: 16 }} />;
    default: return undefined;
  }
}

function providerLabel(
  u: User,
  isHub: boolean,
  isNode: boolean,
  currentNodeId: string | null,
): { label: string; color: 'primary' | 'success' | 'default'; dashed?: boolean } {
  if (isNode) {
    if (u.originNodeId) {
      const isFromThisNode = u.originNodeId === currentNodeId;
      return {
        label: isFromThisNode ? 'Local Node (Migrated)' : `Node: ${u.originNodeId}`,
        color: 'success',
        dashed: true,
      };
    }
    const prov = u.provider && u.provider !== 'Local' ? `Hub (${u.provider})` : 'Hub (Local)';
    return { label: prov, color: 'primary' };
  }
  if (isHub) {
    if (u.originNodeId) {
      return { label: `Node: ${u.originNodeId}`, color: 'success', dashed: true };
    }
    const prov = u.provider && u.provider !== 'Local' ? `Hub (${u.provider})` : 'Hub (Local)';
    return { label: prov, color: 'primary' };
  }
  return { label: u.provider || 'Local', color: 'default' };
}

interface MobileUserCardProps {
  user: User;
  isHub: boolean;
  isNode: boolean;
  currentNodeId: string | null;
  currentUsername?: string;
  onOpen: () => void;
  onReset: () => void;
  onDelete: () => void;
}

export function MobileUserCard({
  user,
  isHub,
  isNode,
  currentNodeId,
  currentUsername,
  onOpen,
  onReset,
  onDelete,
}: MobileUserCardProps) {
  const theme = useTheme();
  const prov = providerLabel(user, isHub, isNode, currentNodeId);
  const resetDisabled = isNode || (user.provider !== 'Local' && !!user.provider);
  const deleteDisabled = isNode || user.username === currentUsername;

  return (
    <Paper
      variant="outlined"
      onClick={onOpen}
      sx={{
        p: 1.5,
        borderRadius: 2,
        cursor: 'pointer',
        bgcolor: alpha(theme.palette.background.paper, 0.6),
        '&:active': { bgcolor: alpha(theme.palette.primary.main, 0.06) },
      }}
    >
      <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography variant="body2" fontWeight={800} noWrap>
            {user.username}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" noWrap>
            {user.email || 'No email'}
          </Typography>
          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap sx={{ mt: 1 }}>
            <Chip
              label={user.role}
              size="small"
              color={getRoleChipColor(user.role)}
              icon={getRoleIcon(user.role)}
              sx={{ borderRadius: 1.5, fontWeight: 600, maxWidth: '100%' }}
            />
            <Chip
              label={prov.label}
              size="small"
              variant="outlined"
              color={prov.color === 'default' ? undefined : prov.color}
              sx={{
                borderRadius: 1.5,
                fontWeight: 700,
                fontSize: '0.65rem',
                maxWidth: '100%',
                borderStyle: prov.dashed ? 'dashed' : 'solid',
              }}
            />
          </Stack>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>
            Last login: {user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleDateString() : 'Never'}
          </Typography>
        </Box>
        <Stack direction="row" spacing={0.25} onClick={(e) => e.stopPropagation()}>
          <Tooltip title={resetDisabled ? 'Managed by Hub / provider' : 'Reset password'}>
            <span>
              <IconButton
                size="small"
                color="warning"
                onClick={onReset}
                disabled={resetDisabled}
                aria-label="Reset password"
              >
                <VpnKeyIcon fontSize="small" />
              </IconButton>
            </span>
          </Tooltip>
          <Tooltip title={deleteDisabled ? 'Cannot delete' : 'Delete user'}>
            <span>
              <IconButton size="small" onClick={onDelete} disabled={deleteDisabled} aria-label="Delete user">
                <DeleteIcon fontSize="small" />
              </IconButton>
            </span>
          </Tooltip>
        </Stack>
      </Stack>
    </Paper>
  );
}
