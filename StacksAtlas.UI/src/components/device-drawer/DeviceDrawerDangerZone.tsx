import { Box, Button, Typography } from '@mui/material';
import DeleteIcon from '@mui/icons-material/Delete';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import type { Device } from '../../models/Device';

interface DeviceDrawerDangerZoneProps {
  device: Device;
  isRemoved: boolean;
  isArchive: boolean;
  isAdmin: boolean;
  role?: string | null;
  onDelete: (device: Device) => void;
  onRestore: (device: Device) => void;
  onHardDelete?: (device: Device) => void;
  onRestoreToFleet?: (device: Device) => void;
}

export function DeviceDrawerDangerZone({
  device,
  isRemoved,
  isArchive,
  isAdmin,
  role,
  onDelete,
  onRestore,
  onHardDelete,
  onRestoreToFleet,
}: DeviceDrawerDangerZoneProps) {
  const isViewer = role?.toLowerCase() === 'viewer';

  return (
    <Box sx={{ pt: 2, borderTop: '1px solid', borderColor: 'divider', textAlign: 'center' }}>
      <Typography
        variant="caption"
        sx={{
          color: isRemoved || isArchive ? 'success.main' : 'error.main',
          fontWeight: 700,
          letterSpacing: 1,
          display: 'block',
          mb: 2,
        }}
      >
        {isRemoved ? 'Fleet restore' : isArchive ? 'Recovery' : 'Danger zone'}
      </Typography>

      {isRemoved ? (
        isAdmin && onRestoreToFleet ? (
          <Button
            variant="contained"
            color="success"
            size="small"
            startIcon={<RestoreFromTrashIcon />}
            onClick={() => onRestoreToFleet(device)}
          >
            Restore to fleet
          </Button>
        ) : (
          <Typography variant="caption" color="text.secondary">Admin required to restore.</Typography>
        )
      ) : isArchive ? (
        <>
          <Button
            variant="contained"
            color="success"
            size="small"
            startIcon={<RestoreFromTrashIcon />}
            onClick={() => onRestore(device)}
          >
            Restore to inventory
          </Button>
          {onHardDelete && (
            <Button
              variant="text"
              color="error"
              size="small"
              startIcon={<DeleteIcon />}
              onClick={() => onHardDelete(device)}
              sx={{ mt: 1, display: 'block', mx: 'auto' }}
            >
              Remove from fleet
            </Button>
          )}
        </>
      ) : (
        <Button
          variant="outlined"
          color="error"
          size="small"
          startIcon={<DeleteIcon />}
          onClick={() => onDelete(device)}
          disabled={isViewer}
        >
          Remove device
        </Button>
      )}

      <Typography variant="caption" sx={{ mt: 1.5, opacity: 0.6, display: 'block' }}>
        {isRemoved
          ? 'Clears tombstone so discovery can add this device again.'
          : isArchive
            ? 'Moves device back to active registry.'
            : 'Hides device from registry  -  undo available immediately.'}
      </Typography>
    </Box>
  );
}
