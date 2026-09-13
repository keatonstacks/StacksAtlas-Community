import React, { useState } from 'react';
import {
  Dialog, DialogTitle, DialogContent, DialogActions,
  Button, Typography, CircularProgress,
} from '@mui/material';
import { ApiService } from '../services/apiService';

interface EndEvaluationDialogProps {
  open: boolean;
  onClose: () => void;
  dataDirectory?: string | null;
  onComplete: () => void;
}

const EndEvaluationDialog: React.FC<EndEvaluationDialogProps> = ({
  open,
  onClose,
  dataDirectory,
  onComplete,
}) => {
  const [busy, setBusy] = useState(false);

  const handleConfirm = async () => {
    setBusy(true);
    try {
      await ApiService.closePortable();
    } catch {
      // API may shut down before the browser finishes reading the response  -  removal still runs.
    } finally {
      localStorage.clear();
      sessionStorage.clear();
      ApiService.setToken(null);
      setBusy(false);
      onComplete();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={busy ? undefined : onClose}
      maxWidth="xs"
      fullWidth
      PaperProps={{ sx: { m: 2, maxWidth: 360 } }}
    >
      <DialogTitle sx={{ fontWeight: 700, fontSize: '1rem', pb: 0.5 }}>
        Close & remove portable data?
      </DialogTitle>
      <DialogContent sx={{ pt: 1 }}>
        <Typography variant="body2" color="text.secondary" sx={{ fontSize: '0.8125rem', lineHeight: 1.45 }}>
          Stops StacksAtlas and deletes all portable data on this machine. This cannot be undone.
        </Typography>
        {dataDirectory && (
          <Typography
            variant="caption"
            color="text.secondary"
            sx={{
              fontFamily: 'monospace',
              wordBreak: 'break-all',
              display: 'block',
              mt: 1.5,
              fontSize: '0.7rem',
              lineHeight: 1.35,
            }}
          >
            {dataDirectory}
          </Typography>
        )}
      </DialogContent>
      <DialogActions sx={{ px: 2, pb: 1.5, pt: 0 }}>
        <Button onClick={onClose} disabled={busy} size="small">
          Cancel
        </Button>
        <Button
          color="error"
          variant="contained"
          size="small"
          onClick={handleConfirm}
          disabled={busy}
          startIcon={busy ? <CircularProgress size={14} color="inherit" /> : undefined}
        >
          {busy ? 'Removing…' : 'Remove & close'}
        </Button>
      </DialogActions>
    </Dialog>
  );
};

export default EndEvaluationDialog;
