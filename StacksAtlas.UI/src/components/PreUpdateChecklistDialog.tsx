import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Typography,
} from '@mui/material';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import type { PreUpdateChecklistItem } from '../utils/updatePreFlight';

interface PreUpdateChecklistDialogProps {
  open: boolean;
  targetVersion: string | null;
  items: PreUpdateChecklistItem[];
  applying?: boolean;
  onCancel: () => void;
  onConfirm: () => void;
}

export function PreUpdateChecklistDialog({
  open,
  targetVersion,
  items,
  applying = false,
  onCancel,
  onConfirm,
}: PreUpdateChecklistDialogProps) {
  return (
    <Dialog open={open} onClose={applying ? undefined : onCancel} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ fontWeight: 900 }}>
        Pre-update checklist
        {targetVersion ? (
          <Typography variant="caption" display="block" color="text.secondary" sx={{ mt: 0.5, fontWeight: 600 }}>
            Installing v{targetVersion}
          </Typography>
        ) : null}
      </DialogTitle>
      <DialogContent>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Confirm you are ready  -  apply will begin immediately after you continue.
        </Typography>
        <List dense disablePadding>
          {items.map((item) => (
            <ListItem key={item.id} disableGutters sx={{ alignItems: 'flex-start', py: 0.75 }}>
              <ListItemIcon sx={{ minWidth: 32, mt: 0.25 }}>
                <CheckCircleOutlineIcon fontSize="small" color="primary" />
              </ListItemIcon>
              <ListItemText
                primary={item.label}
                secondary={item.detail}
                primaryTypographyProps={{ variant: 'body2', fontWeight: 600 }}
                secondaryTypographyProps={{ variant: 'caption' }}
              />
            </ListItem>
          ))}
        </List>
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
        <Button onClick={onCancel} disabled={applying}>
          Cancel
        </Button>
        <Button variant="contained" color="secondary" onClick={onConfirm} disabled={applying}>
          {applying ? 'Starting…' : 'Continue & apply'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
