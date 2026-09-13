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
  FormControlLabel,
  Switch,
  alpha,
  useTheme
} from '@mui/material';
import {
  Settings as SystemIcon,
  Save as SaveIcon
} from '@mui/icons-material';

interface IdentityTabProps {
  isHub: boolean;
  fedSettings: any;
  setFedSettings: (settings: any) => void;
  fedDirty: boolean;
  setFedDirty: (dirty: boolean) => void;
  handleSaveFed: () => void;
  savingFed: boolean;
  mode: string;
  setMode: (mode: "light" | "dark") => void;
  technicianMode: boolean;
  setTechnicianMode: (mode: boolean) => void;
}

const IdentityTab: React.FC<IdentityTabProps> = ({
  isHub,
  fedSettings,
  setFedSettings,
  fedDirty,
  setFedDirty,
  handleSaveFed,
  savingFed,
  mode,
  setMode,
  technicianMode,
  setTechnicianMode
}) => {
  const theme = useTheme();

  return (
    <Grid container spacing={3} justifyContent="center">
      {/* APPLIANCE IDENTITY & CONTEXT */}
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
          <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
              <SystemIcon color="primary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>{isHub ? 'HUB IDENTITY' : 'INSTANCE IDENTITY'}</Typography>
              <Typography variant="caption" color="text.secondary">{isHub ? 'CONFIGURE HUB NAME AND FLEET CONTEXT' : 'CONFIGURE SITE CONTEXT AND GLOBAL IDENTIFIERS'}</Typography>
            </Box>
          </Stack>
          
          <Divider sx={{ mb: 4, opacity: 0.1 }} />

          <Grid container spacing={4}>
            <Grid item xs={12}>
              <Typography variant="overline" color="primary" sx={{ fontWeight: 600, display: 'block', mb: 1.5, opacity: 0.6 }}>
                NETWORK IDENTIFIER
              </Typography>
              <TextField
                fullWidth size="small"
                label={isHub ? "HUB NAME" : "FRIENDLY SITE NAME"}
                placeholder={isHub ? "Central Fleet Manager" : "e.g. New York Data Center"}
                value={fedSettings.nodeId === 'node-unnamed' ? '' : fedSettings.nodeId}
                disabled={!isHub && !!fedSettings.hubUrl}
                onChange={(e) => { setFedSettings({ ...fedSettings, nodeId: e.target.value }); setFedDirty(true); }}
                helperText={(!isHub && !!fedSettings.hubUrl) ? "Site name is locked when federated to a Central Hub." : (isHub ? "Global identifier for this Hub controller." : "Visible in federation dashboards and alerts.")}
                sx={{ 
                  '& .MuiOutlinedInput-root': { 
                    borderRadius: 2, 
                    bgcolor: alpha(theme.palette.background.default, 0.4),
                    fontSize: '0.85rem',
                    fontWeight: 600
                  } 
                }}
              />
            </Grid>

            {!isHub && (
            <Grid item xs={12}>
              <Typography variant="overline" color="primary" sx={{ fontWeight: 600, display: 'block', mb: 1.5, opacity: 0.6 }}>
                GEOGRAPHIC HIERARCHY
              </Typography>
              <Grid container spacing={2.5}>
                <Grid item xs={12}>
                  <TextField
                    fullWidth size="small"
                    label="CLIENT / ORGANIZATION"
                    placeholder="e.g. Acme Corp"
                    value={fedSettings.client}
                    onChange={(e) => { setFedSettings({ ...fedSettings, client: e.target.value }); setFedDirty(true); }}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                  />
                </Grid>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth size="small"
                    label="BUILDING / SITE"
                    placeholder="e.g. Building 4"
                    value={fedSettings.building}
                    onChange={(e) => { setFedSettings({ ...fedSettings, building: e.target.value }); setFedDirty(true); }}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                  />
                </Grid>
                <Grid item xs={12} sm={6}>
                  <TextField
                    fullWidth size="small"
                    label="ROOM / RACK"
                    placeholder="e.g. Server Room A"
                    value={fedSettings.room}
                    onChange={(e) => { setFedSettings({ ...fedSettings, room: e.target.value }); setFedDirty(true); }}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                  />
                </Grid>
              </Grid>
            </Grid>
            )}

            <Grid item xs={12} sx={{ mt: 1 }}>
              <Typography variant="overline" color="secondary" sx={{ fontWeight: 600, display: 'block', mb: 1.5, opacity: 0.6 }}>
                USER PREFERENCES
              </Typography>
              <Stack direction="row" spacing={6}>
                <FormControlLabel
                  control={<Switch checked={mode === "dark"} onChange={(e) => setMode(e.target.checked ? "dark" : "light")} color="primary" />}
                  label={<Typography variant="caption" sx={{ fontWeight: 600, letterSpacing: 0.5 }}>DARK THEME</Typography>}
                />
                <FormControlLabel
                  control={<Switch checked={technicianMode} onChange={(e) => setTechnicianMode(e.target.checked)} color="secondary" />}
                  label={<Typography variant="caption" sx={{ fontWeight: 600, letterSpacing: 0.5 }}>DIAGNOSTICS MODE</Typography>}
                />
              </Stack>
            </Grid>

            <Grid item xs={12}>
              <Button
                fullWidth
                variant={fedDirty ? "contained" : "outlined"}
                startIcon={<SaveIcon />}
                onClick={handleSaveFed}
                disabled={savingFed || !fedDirty}
                sx={{ 
                  fontWeight: 600, 
                  py: 1.5, 
                  borderRadius: 3,
                  boxShadow: fedDirty ? `0 8px 16px ${alpha(theme.palette.primary.main, 0.2)}` : 'none'
                }}
              >
                {savingFed ? "SAVING..." : (fedDirty ? "COMMIT IDENTITY CHANGES" : "IDENTIFIED")}
              </Button>
            </Grid>
          </Grid>
        </Paper>
      </Grid>
    </Grid>
  );
};

export default IdentityTab;
