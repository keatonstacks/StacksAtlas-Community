import React from 'react';
import { 
  Box, 
  Grid, 
  Paper, 
  Stack, 
  Typography, 
  Divider, 
  TextField, 
  Button,
  Switch,
  alpha,
  useTheme
} from '@mui/material';
import { WebhookSettings } from '../WebhookSettings';

interface AlertsTabProps {
  fedSettings: any;
  handleSaveOverrideSettings?: (settings: any) => Promise<void>;
  emailSettings: any;
  setEmailSettings: (settings: any) => void;
  emailDirty: boolean;
  setEmailDirty: (dirty: boolean) => void;
  handleSaveEmail: () => void;
  saving: boolean;
  testEmail: string;
  setTestEmail: (email: string) => void;
  handleSendTestEmail: () => void;
  sendingTest: boolean;
}

const AlertsTab: React.FC<AlertsTabProps> = ({
  fedSettings,
  handleSaveOverrideSettings,
  emailSettings,
  setEmailSettings,
  emailDirty,
  setEmailDirty,
  handleSaveEmail,
  saving,
  testEmail,
  setTestEmail,
  handleSendTestEmail,
  sendingTest
}) => {
  const theme = useTheme();

  const isHubConnected = fedSettings?.mode === 0 && !!fedSettings?.hubUrl;
  const isSyncEnabled = fedSettings?.syncAlertSettings;
  const isOverridden = fedSettings?.overrideAlertSettings;
  const isGoverned = isHubConnected && isSyncEnabled && !isOverridden;

  const handleToggleOverride = async () => {
    if (handleSaveOverrideSettings) {
      const updated = {
        ...fedSettings,
        overrideAlertSettings: !isOverridden
      };
      await handleSaveOverrideSettings(updated);
    }
  };

  return (
    <Grid container spacing={3}>
      <Grid item xs={12} md={6}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={2}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.error.main, 0.1) }}>
              <Typography sx={{ color: 'error.main', fontWeight: 600, fontSize: 16 }}>📧</Typography>
            </Box>
            <Box sx={{ flexGrow: 1 }}>
              <Typography variant="subtitle2" sx={{ fontWeight: 600 }}>EMAIL ALERTS</Typography>
              <Typography variant="caption" color="text.secondary" display="block">CORE SMTP NOTIFICATION ENGINE</Typography>
            </Box>
            <Switch disabled={isGoverned} checked={emailSettings.enabled} onChange={(e) => { setEmailSettings({ ...emailSettings, enabled: e.target.checked }); setEmailDirty(true); }} />
          </Stack>
          <Divider sx={{ mb: 3, opacity: 0.1 }} />
          
          {isHubConnected && isSyncEnabled && (
            <Box 
              sx={{ 
                mb: 3, 
                p: 2, 
                borderRadius: 3, 
                bgcolor: isGoverned ? alpha(theme.palette.info.main, 0.05) : alpha(theme.palette.warning.main, 0.05), 
                border: `1px solid ${isGoverned ? alpha(theme.palette.info.main, 0.2) : alpha(theme.palette.warning.main, 0.2)}`,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: 2
              }}
            >
              <Stack spacing={0.5}>
                <Typography variant="subtitle2" sx={{ fontWeight: 700, color: isGoverned ? 'info.main' : 'warning.main', display: 'flex', alignItems: 'center', gap: 1 }}>
                  {isGoverned ? '🔒 Governed by Hub (Active)' : '⚠️ Overridden Locally'}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {isGoverned 
                    ? 'SMTP and Webhooks are currently synced from the Central Hub and read-only.' 
                    : 'Central Hub synchronization is paused; local configurations are active.'
                  }
                </Typography>
              </Stack>
              <Button 
                variant="outlined" 
                size="small" 
                color={isGoverned ? 'info' : 'warning'}
                onClick={handleToggleOverride}
                sx={{ fontWeight: 600, textTransform: 'none', borderRadius: 2 }}
              >
                {isGoverned ? 'Override' : 'Revert'}
              </Button>
            </Box>
          )}

          <Grid container spacing={3}>
            <Grid item xs={12}>
              <TextField disabled={isGoverned} fullWidth label="SMTP HOST" value={emailSettings.smtpHost} onChange={(e) => { setEmailSettings({ ...emailSettings, smtpHost: e.target.value }); setEmailDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} />
            </Grid>
            <Grid item xs={8}>
              <TextField disabled={isGoverned} fullWidth label="USERNAME" value={emailSettings.username} onChange={(e) => { setEmailSettings({ ...emailSettings, username: e.target.value }); setEmailDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} />
            </Grid>
            <Grid item xs={4}>
              <TextField disabled={isGoverned} fullWidth label="PORT" type="number" value={emailSettings.smtpPort} onChange={(e) => { setEmailSettings({ ...emailSettings, smtpPort: parseInt(e.target.value) }); setEmailDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} />
            </Grid>
            <Grid item xs={12}>
              <TextField disabled={isGoverned} fullWidth label="APP PASSWORD" type="password" value={emailSettings.password} onChange={(e) => { setEmailSettings({ ...emailSettings, password: e.target.value }); setEmailDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} />
            </Grid>
            <Grid item xs={12}>
              <Button 
                variant="contained" 
                fullWidth 
                onClick={handleSaveEmail} 
                disabled={isGoverned || !emailDirty || saving} 
                sx={{ fontWeight: 600, py: 1.5, borderRadius: 3, boxShadow: (!isGoverned && emailDirty) ? `0 8px 16px ${alpha(theme.palette.primary.main, 0.2)}` : 'none' }}
              >
                {saving ? "SAVING..." : "COMMIT SMTP CONFIG"}
              </Button>
            </Grid>
            
            <Grid item xs={12} sx={{ mt: 1 }}>
              <Typography variant="overline" color="primary" sx={{ fontWeight: 600, display: 'block', mb: 2, opacity: 0.6 }}>TEST CONNECTION</Typography>
              <Stack direction="row" spacing={1.5}>
                <TextField 
                  fullWidth 
                  size="small" 
                  placeholder="test@example.com" 
                  value={testEmail} 
                  onChange={(e) => setTestEmail(e.target.value)} 
                  sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                />
                <Button 
                  variant="outlined" 
                  onClick={handleSendTestEmail} 
                  disabled={!testEmail || sendingTest}
                  sx={{ fontWeight: 600, minWidth: 120, borderRadius: 2 }}
                >
                  {sendingTest ? "SENDING..." : "SEND TEST"}
                </Button>
              </Stack>
            </Grid>
          </Grid>
        </Paper>
      </Grid>
      <Grid item xs={12} md={6}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          <WebhookSettings isGoverned={isGoverned} />
        </Paper>
      </Grid>
    </Grid>
  );
};

export default AlertsTab;
