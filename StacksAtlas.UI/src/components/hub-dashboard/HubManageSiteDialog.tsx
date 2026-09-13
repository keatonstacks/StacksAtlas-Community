import {
  Box,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  Stack,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import type { FederatedNodeRow } from './types';

export interface ManageSiteFormState {
  name: string;
  client: string;
  building: string;
  room: string;
  syncUsers: boolean;
  syncUserRegistry: boolean;
  syncSsoSettings: boolean;
  syncAlertSettings: boolean;
  syncSiemSettings: boolean;
  delegateAlertDispatch: boolean;
}

interface HubManageSiteDialogProps {
  open: boolean;
  node: FederatedNodeRow | null;
  form: ManageSiteFormState;
  saving: boolean;
  onClose: () => void;
  onSave: () => void;
  onChange: (patch: Partial<ManageSiteFormState>) => void;
}

export function HubManageSiteDialog({
  open,
  node,
  form,
  saving,
  onClose,
  onSave,
  onChange,
}: HubManageSiteDialogProps) {
  const theme = useTheme();
  const isMobile = useIsMobileLayout();

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="sm"
      fullWidth
      fullScreen={isMobile}
      PaperProps={{
        sx: { borderRadius: 4, bgcolor: alpha(theme.palette.background.paper, 0.95), backdropFilter: 'blur(20px)' },
      }}
    >
      <DialogTitle sx={{ fontWeight: 900 }}>
        Manage <span style={{ color: theme.palette.primary.main }}>site</span>
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField
            label="Friendly site name"
            fullWidth
            value={form.name}
            onChange={(e) => onChange({ name: e.target.value })}
            size="small"
          />
          <TextField
            label="Client / organization"
            fullWidth
            value={form.client}
            onChange={(e) => onChange({ client: e.target.value })}
            size="small"
          />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField
              label="Building"
              fullWidth
              value={form.building}
              onChange={(e) => onChange({ building: e.target.value })}
              size="small"
            />
            <TextField
              label="Room / rack"
              fullWidth
              value={form.room}
              onChange={(e) => onChange({ room: e.target.value })}
              size="small"
            />
          </Stack>

          <FormControlLabel
            control={
              <Checkbox
                checked={form.syncUserRegistry}
                onChange={(e) => onChange({ syncUserRegistry: e.target.checked })}
              />
            }
            label={
              <Box>
                <Typography variant="body2" fontWeight={800}>Sync core user registry</Typography>
                <Typography variant="caption" color="text.secondary" display="block">
                  Pushes accounts and role mappings to the node; locks local user CRUD.
                </Typography>
              </Box>
            }
          />
          {form.syncUserRegistry && (
            <Box
              sx={{
                p: 1.5,
                borderRadius: 2,
                bgcolor: alpha(theme.palette.success.main, 0.08),
                border: `1px solid ${alpha(theme.palette.success.main, 0.2)}`,
                display: 'flex',
                gap: 1.5,
              }}
            >
              <CheckCircleIcon color="success" sx={{ fontSize: 20 }} />
              <Typography variant="caption" color="text.secondary">
                Sovereign user migration will run on save  -  local node accounts (except admin) merge into the Hub registry.
              </Typography>
            </Box>
          )}

          <FormControlLabel
            control={
              <Checkbox
                checked={form.syncSsoSettings}
                onChange={(e) => onChange({ syncSsoSettings: e.target.checked })}
              />
            }
            label={
              <Box>
                <Typography variant="body2" fontWeight={800}>Sync global SSO settings</Typography>
                <Typography variant="caption" color="text.secondary" display="block">
                  OIDC/LDAP config from Hub; locks local SSO edits on the node.
                </Typography>
              </Box>
            }
          />

          <FormControlLabel
            control={
              <Checkbox
                checked={form.syncAlertSettings}
                onChange={(e) => onChange({ syncAlertSettings: e.target.checked })}
              />
            }
            label={
              <Box>
                <Typography variant="body2" fontWeight={800}>Sync outbound notifications</Typography>
                <Typography variant="caption" color="text.secondary" display="block">
                  SMTP and webhook settings from Hub.
                </Typography>
                {node?.overrideAlertSettings && (
                  <Typography variant="caption" color="warning.main" fontWeight={700} display="block">
                    This node has overridden alert settings locally.
                  </Typography>
                )}
              </Box>
            }
          />

          <FormControlLabel
            control={
              <Checkbox
                checked={form.delegateAlertDispatch}
                onChange={(e) => onChange({ delegateAlertDispatch: e.target.checked })}
              />
            }
            label={
              <Box>
                <Typography variant="body2" fontWeight={800}>Delegate alert dispatch to Hub</Typography>
                <Typography variant="caption" color="text.secondary" display="block">
                  Node routes email/webhooks through the Hub gateway.
                </Typography>
              </Box>
            }
            sx={{ ml: 1 }}
          />

          <FormControlLabel
            control={
              <Checkbox
                checked={form.syncSiemSettings}
                onChange={(e) => onChange({ syncSiemSettings: e.target.checked })}
              />
            }
            label={
              <Box>
                <Typography variant="body2" fontWeight={800}>Sync syslog & SIEM settings</Typography>
                <Typography variant="caption" color="text.secondary" display="block">
                  SIEM streaming config from Hub.
                </Typography>
                {node?.overrideSiemSettings && (
                  <Typography variant="caption" color="warning.main" fontWeight={700} display="block">
                    This node has overridden SIEM settings locally.
                  </Typography>
                )}
              </Box>
            }
          />
        </Stack>
      </DialogContent>
      <DialogActions sx={{ p: 3 }}>
        <Button onClick={onClose} color="inherit" sx={{ fontWeight: 800 }}>Cancel</Button>
        <Button onClick={onSave} variant="contained" disabled={saving} sx={{ fontWeight: 900, borderRadius: 2 }}>
          {saving ? 'Saving…' : 'Save changes'}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
