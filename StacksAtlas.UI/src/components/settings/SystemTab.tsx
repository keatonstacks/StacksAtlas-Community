import React from 'react';
import { 
  Box, 
  Grid, 
  Paper, 
  Stack, 
  Typography, 
  TextField, 
  Button,
  Switch,
  FormControlLabel,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  IconButton as MuiIconButton,
  Alert,
  Chip,
  Divider,
  alpha,
  useTheme
} from '@mui/material';
import {
  Settings as SystemIcon,
  Security as SecurityIcon,
  Description as DocsIcon,
  Launch as OpenIcon,
  Delete as DeleteIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import SoftwareUpdatesBlock from './SoftwareUpdatesBlock';

interface SystemTabProps {
  fedSettings: any;
  handleSaveOverrideSettings?: (settings: any) => Promise<void>;
  systemConfig: any;
  setSystemConfig: (config: any) => void;
  systemDirty: boolean;
  setSystemDirty: (dirty: boolean) => void;
  savingSystem: boolean;
  handleSaveSystem: () => void;
  newKeyLabel: string;
  setNewKeyLabel: (label: string) => void;
  handleCreateNewApiKey: () => void;
  saving: boolean;
  apiKeys: any[];
  handleRevokeApiKey: (id: string) => void;
  systemRuntimeInfo?: {
    isPortable?: boolean;
    installMode?: string;
    dataDirectory?: string;
    promoteHint?: string | null;
  } | null;
  mode?: 'light' | 'dark';
  setMode?: (mode: 'light' | 'dark') => void;
  technicianMode?: boolean;
  setTechnicianMode?: (enabled: boolean) => void;
}

const SystemTab: React.FC<SystemTabProps> = ({
  fedSettings,
  handleSaveOverrideSettings,
  systemConfig,
  setSystemConfig,
  systemDirty,
  setSystemDirty,
  savingSystem,
  handleSaveSystem,
  newKeyLabel,
  setNewKeyLabel,
  handleCreateNewApiKey,
  saving,
  apiKeys,
  handleRevokeApiKey,
  systemRuntimeInfo,
  mode,
  setMode,
  technicianMode,
  setTechnicianMode,
}) => {
  const theme = useTheme();

  const isHubConnected = fedSettings?.mode === 0 && !!fedSettings?.hubUrl;
  const isSyncEnabled = fedSettings?.syncSiemSettings;
  const isOverridden = fedSettings?.overrideSiemSettings;
  const isGoverned = isHubConnected && isSyncEnabled && !isOverridden;

  const handleToggleOverride = async () => {
    if (handleSaveOverrideSettings) {
      const updated = {
        ...fedSettings,
        overrideSiemSettings: !isOverridden
      };
      await handleSaveOverrideSettings(updated);
    }
  };

  return (
    <Grid container spacing={3}>
      {systemRuntimeInfo?.isPortable && (
        <Grid item xs={12}>
          <Alert severity="info" sx={{ borderRadius: 3 }}>
            <Stack spacing={1}>
              <Stack direction="row" alignItems="center" spacing={1}>
                <Chip label="PORTABLE" size="small" color="info" sx={{ fontWeight: 800 }} />
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  No background service  -  data stays in your user folder.
                </Typography>
              </Stack>
              {systemRuntimeInfo.dataDirectory && (
                <Typography variant="caption" color="text.secondary" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
                  {systemRuntimeInfo.dataDirectory}
                </Typography>
              )}
              {systemRuntimeInfo.promoteHint && (
                <Typography variant="body2">{systemRuntimeInfo.promoteHint}</Typography>
              )}
            </Stack>
          </Alert>
        </Grid>
      )}
      {systemRuntimeInfo?.isPortable && setMode && setTechnicianMode && (
        <Grid item xs={12} md={5}>
          <Paper
            variant="outlined"
            sx={{
              p: 3,
              borderRadius: 4,
              bgcolor: alpha(theme.palette.background.paper, 0.8),
              border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            }}
          >
            <Typography variant="subtitle1" sx={{ fontWeight: 600, mb: 2 }}>
              DISPLAY & DIAGNOSTICS
            </Typography>
            <Stack direction="row" spacing={4}>
              <FormControlLabel
                control={
                  <Switch
                    checked={mode === 'dark'}
                    onChange={(e) => setMode(e.target.checked ? 'dark' : 'light')}
                    color="primary"
                  />
                }
                label={<Typography variant="caption" sx={{ fontWeight: 600 }}>DARK THEME</Typography>}
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={!!technicianMode}
                    onChange={(e) => setTechnicianMode(e.target.checked)}
                    color="secondary"
                  />
                }
                label={<Typography variant="caption" sx={{ fontWeight: 600 }}>DIAGNOSTICS MODE</Typography>}
              />
            </Stack>
          </Paper>
        </Grid>
      )}

      <Grid item xs={12}>
        <SoftwareUpdatesBlock />
      </Grid>

      <Grid item xs={12}>
        <Divider sx={{ borderColor: alpha(theme.palette.divider, 0.12) }} />
      </Grid>

      {/* NETWORKING & SIEM (LEFT) */}
      <Grid item xs={12} md={7}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: systemDirty ? `2px solid ${theme.palette.primary.main}` : `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          <Stack direction="row" justifyContent="space-between" alignItems="center" mb={4}>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
                <SystemIcon color="primary" sx={{ fontSize: 24 }} />
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>APPLIANCE NETWORKING</Typography>
                <Typography variant="caption" color="text.secondary">
                  {systemRuntimeInfo?.isPortable
                    ? 'CORE INFRASTRUCTURE PORTS'
                    : 'CORE INFRASTRUCTURE PORTS AND SIEM EXPORT'}
                </Typography>
              </Box>
            </Stack>
            <Button variant={systemDirty ? "contained" : "outlined"} onClick={handleSaveSystem} disabled={savingSystem || !systemDirty} sx={{ fontWeight: 600 }}>
              {savingSystem ? "APPLYING..." : "SAVE & RESTART"}
            </Button>
          </Stack>

          <Grid container spacing={4}>
            <Grid item xs={6}><TextField fullWidth label="HTTP PORT" type="number" value={systemConfig.httpPort} onChange={(e) => { setSystemConfig({ ...systemConfig, httpPort: parseInt(e.target.value) || 5000 }); setSystemDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} /></Grid>
            <Grid item xs={6}><TextField fullWidth label="HTTPS PORT" type="number" value={systemConfig.httpsPort} onChange={(e) => { setSystemConfig({ ...systemConfig, httpsPort: parseInt(e.target.value) || 5001 }); setSystemDirty(true); }} size="small" sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} /></Grid>
            
            {!systemRuntimeInfo?.isPortable && (
            <Grid item xs={12}>
              <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.primary.main, 0.05), border: '1px solid', borderColor: alpha(theme.palette.primary.main, 0.1) }}>
                <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
                  <Typography variant="overline" color="primary" sx={{ fontWeight: 900 }}>SYSLOG & SIEM EXPORT</Typography>
                  <Switch disabled={isGoverned} size="small" checked={systemConfig.syslogEnabled} onChange={(e) => { setSystemConfig({ ...systemConfig, syslogEnabled: e.target.checked }); setSystemDirty(true); }} />
                </Stack>
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
                          ? 'SIEM Settings are currently synced from the Central Hub and read-only.' 
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
                <Stack spacing={2}>
                  <TextField fullWidth label="SYSLOG HOST" value={systemConfig.syslogHost} disabled={isGoverned || !systemConfig.syslogEnabled} onChange={(e) => { setSystemConfig({ ...systemConfig, syslogHost: e.target.value }); setSystemDirty(true); }} size="small" />
                  <Stack direction="row" spacing={2}>
                    <TextField fullWidth label="PORT" type="number" disabled={isGoverned || !systemConfig.syslogEnabled} value={systemConfig.syslogPort} onChange={(e) => { setSystemConfig({ ...systemConfig, syslogPort: parseInt(e.target.value) || 514 }); setSystemDirty(true); }} size="small" />
                    <TextField fullWidth label="APP NAME" disabled={isGoverned || !systemConfig.syslogEnabled} value={systemConfig.syslogAppName} onChange={(e) => { setSystemConfig({ ...systemConfig, syslogAppName: e.target.value }); setSystemDirty(true); }} size="small" />
                  </Stack>
                </Stack>
              </Box>
            </Grid>
            )}

            <Grid item xs={12}>
              <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.secondary.main, 0.05), border: '1px solid', borderColor: alpha(theme.palette.secondary.main, 0.1) }}>
                <Typography variant="overline" color="secondary" sx={{ fontWeight: 900, display: 'block', mb: 1 }}>TLS SECURITY</Typography>
                <Typography variant="caption" color="text.secondary" display="block" mb={2}>Download the appliance root certificate for trusted browser communication.</Typography>
                <Button fullWidth variant="outlined" color="secondary" startIcon={<SecurityIcon />} onClick={() => ApiService.downloadCertificate()} sx={{ fontWeight: 600, borderStyle: 'dashed' }}>DOWNLOAD TLS CERTIFICATE</Button>
              </Box>
            </Grid>
          </Grid>
        </Paper>
      </Grid>

      {/* API & INTEGRATIONS (RIGHT)  -  production appliance only */}
      {!systemRuntimeInfo?.isPortable && (
      <Grid item xs={12} md={5}>
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
          <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.secondary.main, 0.1) }}>
              <DocsIcon color="secondary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>API INTEGRATIONS</Typography>
              <Typography variant="caption" color="text.secondary">MANAGE EXTERNAL SYSTEM ACCESS</Typography>
            </Box>
          </Stack>
          
          <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.background.default, 0.4), border: `1px solid ${alpha(theme.palette.divider, 0.08)}`, mb: 3 }}>
            <Typography variant="overline" color="primary" sx={{ fontWeight: 900, display: 'block', mb: 2 }}>GENERATE NEW TOKEN</Typography>
            <Stack spacing={2}>
              <TextField fullWidth size="small" label="TOKEN LABEL" value={newKeyLabel} onChange={(e) => setNewKeyLabel(e.target.value)} sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} />
              <Button fullWidth variant="contained" onClick={handleCreateNewApiKey} disabled={saving || !newKeyLabel.trim()} sx={{ fontWeight: 600 }}>GENERATE ACCESS TOKEN</Button>
            </Stack>
          </Box>

          <TableContainer component={Paper} variant="outlined" sx={{ bgcolor: 'transparent', mb: 3, borderRadius: 2, overflowX: 'auto', maxWidth: '100%' }}>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell><Typography variant="caption" sx={{ fontWeight: 900 }}>LABEL</Typography></TableCell>
                  <TableCell align="right"></TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {apiKeys.map((k: any) => (
                  <TableRow key={k.id}>
                    <TableCell><Typography variant="body2" sx={{ fontWeight: 700, fontFamily: 'monospace' }}>{k.label}</Typography></TableCell>
                    <TableCell align="right">
                      <MuiIconButton size="small" color="error" onClick={() => handleRevokeApiKey(k.id)}><DeleteIcon fontSize="small" /></MuiIconButton>
                    </TableCell>
                  </TableRow>
                ))}
                {apiKeys.length === 0 && (
                  <TableRow>
                    <TableCell colSpan={2} align="center"><Typography variant="caption" sx={{ opacity: 0.5 }}>NO ACTIVE TOKENS</Typography></TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          </TableContainer>

          <Button fullWidth variant="outlined" startIcon={<DocsIcon />} endIcon={<OpenIcon />} onClick={() => window.open('/api-docs', '_blank')} sx={{ fontWeight: 900, py: 1.2, borderRadius: 2 }}>OPEN API DOCUMENTATION</Button>
        </Paper>
      </Grid>
      )}
    </Grid>
  );
};

export default SystemTab;
