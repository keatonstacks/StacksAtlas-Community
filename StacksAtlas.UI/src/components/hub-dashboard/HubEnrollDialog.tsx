import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Stack,
  Step,
  StepLabel,
  Stepper,
  Tab,
  Tabs,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import { EnrollmentStringPanel } from '../federation/EnrollmentStringPanel';
import { RemoteSiteChecklist } from '../federation/RemoteSiteChecklist';

export type EnrollMethod = 'mtls' | 'direct';

interface HubEnrollDialogProps {
  open: boolean;
  step: number;
  method: EnrollMethod;
  enrolling: boolean;
  enrollUrl: string;
  enrollPassword: string;
  enrollName: string;
  enrollClient: string;
  enrollBuilding: string;
  enrollRoom: string;
  onClose: () => void;
  onMethodChange: (method: EnrollMethod) => void;
  onStepChange: (step: number) => void;
  onFieldChange: (field: string, value: string) => void;
  onEnrollDirect: () => void;
  onCopied: () => void;
  onError: (message: string) => void;
}

const STEPS = ['Site identity', 'Transport', 'Connect'];

export function HubEnrollDialog({
  open,
  step,
  method,
  enrolling,
  enrollUrl,
  enrollPassword,
  enrollName,
  enrollClient,
  enrollBuilding,
  enrollRoom,
  onClose,
  onMethodChange,
  onStepChange,
  onFieldChange,
  onEnrollDirect,
  onCopied,
  onError,
}: HubEnrollDialogProps) {
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
        Enroll <span style={{ color: theme.palette.secondary.main }}>remote node</span>
      </DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} alternativeLabel sx={{ mb: 3, mt: 1 }}>
          {STEPS.map((label) => (
            <Step key={label}>
              <StepLabel>{label}</StepLabel>
            </Step>
          ))}
        </Stepper>

        {step === 0 && (
          <Stack spacing={2}>
            <Typography variant="caption" color="text.secondary">
              Site identity appears on the Hub fleet dashboard and in fleet-scoped filters.
            </Typography>
            <TextField
              label="Friendly site name"
              required
              fullWidth
              value={enrollName}
              onChange={(e) => onFieldChange('enrollName', e.target.value)}
              size="small"
            />
            <TextField
              label="Client / organization"
              fullWidth
              value={enrollClient}
              onChange={(e) => onFieldChange('enrollClient', e.target.value)}
              size="small"
            />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                label="Building"
                fullWidth
                value={enrollBuilding}
                onChange={(e) => onFieldChange('enrollBuilding', e.target.value)}
                size="small"
              />
              <TextField
                label="Room / rack"
                fullWidth
                value={enrollRoom}
                onChange={(e) => onFieldChange('enrollRoom', e.target.value)}
                size="small"
              />
            </Stack>
          </Stack>
        )}

        {step === 1 && (
          <Stack spacing={2}>
            <RemoteSiteChecklist defaultExpanded />
            <Typography variant="caption" color="text.secondary">
              Complete Tailscale ACL and reachability steps before generating an enrollment string or using direct push.
            </Typography>
          </Stack>
        )}

        {step === 2 && (
          <Stack spacing={2}>
            <Tabs
              value={method}
              onChange={(_, val) => onMethodChange(val)}
              variant="fullWidth"
              sx={{ borderBottom: 1, borderColor: 'divider' }}
            >
              <Tab label="Sovereign mTLS" value="mtls" sx={{ fontWeight: 800 }} />
              <Tab label="Direct push" value="direct" sx={{ fontWeight: 800 }} />
            </Tabs>

            {method === 'mtls' ? (
              <EnrollmentStringPanel
                active={open}
                compact
                showRemoteSiteLink
                onCopied={onCopied}
                onError={onError}
              />
            ) : (
              <Stack spacing={2}>
                <Typography variant="caption" color="text.secondary">
                  Push federation credentials to a standalone appliance. Linux/Docker: <code>http://&lt;ip&gt;:5000</code> · macOS: <code>http://&lt;ip&gt;:5050</code> · Windows: <code>https://&lt;ip&gt;:5001</code>
                </Typography>
                <TextField
                  label="Appliance API URL"
                  fullWidth
                  placeholder="http://192.168.1.239:5000 or http://192.168.1.70:5050"
                  value={enrollUrl}
                  onChange={(e) => onFieldChange('enrollUrl', e.target.value)}
                  size="small"
                />
                <TextField
                  label="Admin password"
                  type="password"
                  fullWidth
                  value={enrollPassword}
                  onChange={(e) => onFieldChange('enrollPassword', e.target.value)}
                  size="small"
                />
              </Stack>
            )}
          </Stack>
        )}
      </DialogContent>
      <DialogActions sx={{ p: 3, flexWrap: 'wrap', gap: 1 }}>
        <Button onClick={onClose} color="inherit" sx={{ fontWeight: 800 }}>
          {step === 2 && method === 'mtls' ? 'Close' : 'Cancel'}
        </Button>
        {step > 0 && (
          <Button onClick={() => onStepChange(step - 1)} sx={{ fontWeight: 800 }}>
            Back
          </Button>
        )}
        {step < 2 && (
          <Button
            variant="contained"
            onClick={() => onStepChange(step + 1)}
            disabled={step === 0 && !enrollName.trim()}
            sx={{ fontWeight: 900, borderRadius: 2 }}
          >
            Next
          </Button>
        )}
        {step === 2 && method === 'direct' && (
          <Button
            variant="contained"
            color="secondary"
            disabled={enrolling || !enrollUrl || !enrollPassword || !enrollName}
            onClick={onEnrollDirect}
            sx={{ fontWeight: 900, borderRadius: 2 }}
          >
            {enrolling ? 'Enrolling…' : 'Enroll node'}
          </Button>
        )}
      </DialogActions>
    </Dialog>
  );
}
