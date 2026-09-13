import { useState } from 'react';
import {
  IconButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
} from '@mui/material';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import StorageIcon from '@mui/icons-material/Storage';
import LanIcon from '@mui/icons-material/Lan';
import SystemUpdateAltIcon from '@mui/icons-material/SystemUpdateAlt';

interface MobileHeaderOverflowMenuProps {
  isHub: boolean;
  isAdmin: boolean;
  onlineCount: number;
  totalDevices: number;
  federatedOnlineNodes: number;
  federatedNodeCount: number;
  applianceDbSize: number;
  fleetDbSize: number;
  showDualDb: boolean;
  applianceEngine: string;
  formatDbMb: (bytes: number) => string;
  onOpenDatabase: () => void;
  onOpenNetwork: () => void;
  onOpenUpdates: () => void;
  showUpdateAction: boolean;
}

export function MobileHeaderOverflowMenu({
  isHub,
  isAdmin,
  onlineCount,
  totalDevices,
  federatedOnlineNodes,
  federatedNodeCount,
  applianceDbSize,
  fleetDbSize,
  showDualDb,
  applianceEngine,
  formatDbMb,
  onOpenDatabase,
  onOpenNetwork,
  onOpenUpdates,
  showUpdateAction,
}: MobileHeaderOverflowMenuProps) {
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const open = Boolean(anchorEl);

  const close = () => setAnchorEl(null);

  const handleDatabase = () => {
    close();
    onOpenDatabase();
  };

  const handleNetwork = () => {
    close();
    onOpenNetwork();
  };

  const handleUpdates = () => {
    close();
    onOpenUpdates();
  };

  const inventoryLabel = isHub
    ? `${federatedOnlineNodes} / ${federatedNodeCount} sites online`
    : `${onlineCount} / ${totalDevices} devices online`;

  const dbLabel = showDualDb
    ? `Appliance ${formatDbMb(applianceDbSize)} MB + Fleet ${formatDbMb(fleetDbSize)} MB`
    : `${formatDbMb(applianceDbSize)} MB · ${applianceEngine}`;

  return (
    <>
      <IconButton
        onClick={(e) => setAnchorEl(e.currentTarget)}
        size="small"
        aria-label="More actions"
        sx={{ display: { xs: 'inline-flex', md: 'none' } }}
      >
        <MoreVertIcon fontSize="small" />
      </IconButton>
      <Menu
        anchorEl={anchorEl}
        open={open}
        onClose={close}
        anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
        transformOrigin={{ horizontal: 'right', vertical: 'top' }}
      >
        <MenuItem disabled sx={{ opacity: '1 !important' }}>
          <ListItemText
            primary={inventoryLabel}
            primaryTypographyProps={{ variant: 'body2', fontWeight: 700 }}
          />
        </MenuItem>
        <MenuItem disabled sx={{ opacity: '1 !important' }}>
          <ListItemText
            primary={dbLabel}
            secondary={isAdmin ? 'Tap Database to open settings' : 'View only'}
            primaryTypographyProps={{ variant: 'body2', fontWeight: 600, fontSize: '0.8rem' }}
          />
        </MenuItem>
        {isAdmin && (
          <MenuItem onClick={handleDatabase}>
            <ListItemIcon>
              <StorageIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary="Database settings" />
          </MenuItem>
        )}
        {!isHub && (
          <MenuItem onClick={handleNetwork}>
            <ListItemIcon>
              <LanIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary="Network interface" />
          </MenuItem>
        )}
        {showUpdateAction && (
          <MenuItem onClick={handleUpdates}>
            <ListItemIcon>
              <SystemUpdateAltIcon fontSize="small" />
            </ListItemIcon>
            <ListItemText primary="Software update" />
          </MenuItem>
        )}
      </Menu>
    </>
  );
}
