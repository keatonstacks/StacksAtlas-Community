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
  FormControlLabel,
  IconButton as MuiIconButton,
  alpha,
  useTheme
} from '@mui/material';
import {
  Security as SecurityIcon,
  Save as SaveIcon,
  Delete as DeleteIcon,
  Add as AddIcon,
  Shield as ShieldIcon
} from '@mui/icons-material';

interface AuthTabProps {
  authSettings: any;
  setAuthSettings: (settings: any) => void;
  authDirty: boolean;
  setAuthDirty: (dirty: boolean) => void;
  savingAuth: boolean;
  handleSaveAuth: () => void;
  license: any;
  handleTestOidc: () => void;
  handleTestLdap: () => void;
  handleUpdateRoleMapping: (index: number, field: string, value: any) => void;
  handleDeleteRoleMapping: (index: number) => void;
  handleAddRoleMapping: () => void;
  isNode?: boolean;
}

const AuthTab: React.FC<AuthTabProps> = ({
  authSettings,
  setAuthSettings,
  authDirty,
  setAuthDirty,
  savingAuth,
  handleSaveAuth,
  license,
  handleTestOidc,
  handleTestLdap,
  handleUpdateRoleMapping,
  handleDeleteRoleMapping,
  handleAddRoleMapping,
  isNode = false
}) => {
  const theme = useTheme();

  return (
    <Grid container spacing={3}>
      <Grid item xs={12}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: authDirty ? `2px solid ${theme.palette.primary.main}` : `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          {isNode && (
            <Box 
              sx={{ 
                p: 2.5, 
                mb: 4, 
                borderRadius: 3, 
                bgcolor: alpha(theme.palette.primary.main, 0.05),
                border: `1px solid ${alpha(theme.palette.primary.main, 0.25)}`,
                backdropFilter: "blur(10px)",
                display: 'flex', 
                alignItems: 'center', 
                gap: 2,
                boxShadow: `0 4px 20px ${alpha(theme.palette.primary.main, 0.05)}`
              }}
            >
              <ShieldIcon color="primary" sx={{ fontSize: 28 }} />
              <Box>
                <Typography variant="subtitle2" sx={{ fontWeight: 700, letterSpacing: -0.2 }}>
                  GLOBAL SSO & IAM GOVERNANCE ACTIVE
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25, fontWeight: 500 }}>
                  SSO policies, identity providers (OIDC/LDAP), and role configurations are governed globally by your StacksAtlas Hub. Local controls are locked.
                </Typography>
              </Box>
            </Box>
          )}

          <Stack direction="row" justifyContent="space-between" alignItems="center" mb={4}>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
                <SecurityIcon color="primary" sx={{ fontSize: 24 }} />
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>IDENTITY & ACCESS (SSO)</Typography>
                <Typography variant="caption" color="text.secondary">CONFIGURE ENTERPRISE IDENTITY INTEGRATION (OIDC / LDAP)</Typography>
              </Box>
            </Stack>
            <Button
              variant={authDirty ? "contained" : "outlined"}
              startIcon={isNode ? <ShieldIcon /> : <SaveIcon />}
              onClick={handleSaveAuth}
              disabled={savingAuth || !authDirty || isNode}
              sx={{ 
                fontWeight: 600, 
                px: 3, 
                py: 1, 
                borderRadius: 2
              }}
            >
              {savingAuth ? "SAVING..." : (isNode ? "GOVERNED BY HUB" : "APPLY AUTH SETTINGS")}
            </Button>
          </Stack>

          <Grid container spacing={4}>
            <Grid item xs={12} md={4}>
              <Typography variant="overline" color="primary" sx={{ fontWeight: 600 }}>GLOBAL CONTROLS</Typography>
              <Divider sx={{ mb: 2, mt: 0.5 }} />
              <Stack spacing={2.5}>
                <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.05), border: '1px solid', borderColor: alpha(theme.palette.primary.main, 0.1), opacity: isNode ? 0.7 : 1 }}>
                  <FormControlLabel
                    control={<Switch checked={authSettings.ssoEnabled} disabled={isNode || !license?.ssoEnabled} onChange={(e) => { setAuthSettings({ ...authSettings, ssoEnabled: e.target.checked }); setAuthDirty(true); }} />}
                    label={<Typography variant="body2" sx={{ fontWeight: 600 }}>ENABLE SSO LOGIN {!license?.ssoEnabled && "🔒"}</Typography>}
                  />
                  {!license?.ssoEnabled && (
                    <Typography variant="caption" color="primary" sx={{ display: 'block', mt: 0.5, fontWeight: 600, fontSize: '0.65rem' }}>
                      ENTERPRISE LICENSE REQUIRED
                    </Typography>
                  )}
                </Box>
                <TextField 
                  fullWidth size="small" select label="PRIMARY PROVIDER" 
                  value={authSettings.provider} 
                  disabled={isNode}
                  onChange={(e) => { setAuthSettings({ ...authSettings, provider: e.target.value }); setAuthDirty(true); }}
                  SelectProps={{ native: true }}
                  sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                >
                  <option value="OIDC">OIDC (Azure AD, Okta, etc.)</option>
                  <option value="LDAP">LDAP / Active Directory</option>
                </TextField>
                <TextField 
                  fullWidth size="small" select label="DEFAULT NEW USER ROLE" 
                  value={authSettings.defaultRole} 
                  disabled={isNode}
                  onChange={(e) => { setAuthSettings({ ...authSettings, defaultRole: e.target.value }); setAuthDirty(true); }}
                  SelectProps={{ native: true }}
                  sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }}
                >
                  <option value="Viewer">Viewer (Read-Only)</option>
                  <option value="Standard">Standard (Operator)</option>
                  <option value="Admin">Admin (Full Control)</option>
                </TextField>
              </Stack>
            </Grid>

            <Grid item xs={12} md={8}>
              {authSettings.provider === 'OIDC' ? (
                <Box>
                  <Typography variant="overline" color="primary" sx={{ fontWeight: 600, opacity: 0.6 }}>OIDC CONFIGURATION</Typography>
                  <Divider sx={{ mb: 3, mt: 0.5, opacity: 0.1 }} />
                  <Grid container spacing={3}>
                    <Grid item xs={12}><TextField fullWidth size="small" label="AUTHORITY URL" value={authSettings.oidc.authority} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, oidc: { ...authSettings.oidc, authority: e.target.value } }); setAuthDirty(true); }} sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} /></Grid>
                    <Grid item xs={12} sm={6}><TextField fullWidth size="small" label="CLIENT ID" value={authSettings.oidc.clientId} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, oidc: { ...authSettings.oidc, clientId: e.target.value } }); setAuthDirty(true); }} sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} /></Grid>
                    <Grid item xs={12} sm={6}><TextField fullWidth size="small" label="CLIENT SECRET" type="password" value={authSettings.oidc.clientSecret} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, oidc: { ...authSettings.oidc, clientSecret: e.target.value } }); setAuthDirty(true); }} sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} /></Grid>
                    <Grid item xs={12}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}><TextField fullWidth size="small" label="SCOPES" value={authSettings.oidc.scope} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, oidc: { ...authSettings.oidc, scope: e.target.value } }); setAuthDirty(true); }} sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 600 } }} /><Button variant="outlined" onClick={handleTestOidc} disabled={isNode} sx={{ fontWeight: 600, minWidth: { xs: '100%', sm: 140 }, borderRadius: 2, flexShrink: 0 }}>VERIFY OIDC</Button></Stack></Grid>
                    <Grid item xs={12}>
                      <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.info.main, 0.05), border: '1px dashed', borderColor: alpha(theme.palette.info.main, 0.3) }}>
                        <Typography variant="caption" color="info.main" sx={{ display: 'block', mb: 1, letterSpacing: 0.5, fontWeight: 600 }}>OIDC CALLBACK URL</Typography>
                        <Typography variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.8, fontSize: '0.75rem', fontWeight: 600 }}>{window.location.origin}/signin-oidc</Typography>
                      </Box>
                    </Grid>
                  </Grid>
                </Box>
              ) : (
                <Box>
                  <Typography variant="overline" color="primary" sx={{ fontWeight: 600 }}>LDAP / ACTIVE DIRECTORY</Typography>
                  <Divider sx={{ mb: 2, mt: 0.5 }} />
                  <Grid container spacing={2}>
                    <Grid item xs={12} sm={9}><TextField fullWidth size="small" label="LDAP SERVER" value={authSettings.ldap.server} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, server: e.target.value } }); setAuthDirty(true); }} /></Grid>
                    <Grid item xs={12} sm={3}><TextField fullWidth size="small" label="PORT" type="number" value={authSettings.ldap.port} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, port: parseInt(e.target.value) } }); setAuthDirty(true); }} /></Grid>
                    <Grid item xs={12}><TextField fullWidth size="small" label="BASE DN" value={authSettings.ldap.baseDn} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, baseDn: e.target.value } }); setAuthDirty(true); }} /></Grid>
                    <Grid item xs={12}>
                      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'center' }}>
                        <TextField fullWidth size="small" label="USER FILTER" value={authSettings.ldap.userFilter} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, userFilter: e.target.value } }); setAuthDirty(true); }} />
                        <FormControlLabel
                          control={<Switch size="small" checked={authSettings.ldap.useSsl} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, useSsl: e.target.checked } }); setAuthDirty(true); }} />}
                          label={<Typography variant="caption" sx={{ fontWeight: 600 }}>SSL</Typography>}
                        />
                      </Stack>
                    </Grid>
                    <Grid item xs={12}>
                      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                        <TextField fullWidth size="small" label="GROUP ATTRIBUTE" value={authSettings.ldap.groupAttribute} disabled={isNode} onChange={(e) => { setAuthSettings({ ...authSettings, ldap: { ...authSettings.ldap, groupAttribute: e.target.value } }); setAuthDirty(true); }} />
                        <Button variant="outlined" onClick={handleTestLdap} disabled={isNode} sx={{ fontWeight: 600, minWidth: 140 }}>VERIFY LDAP</Button>
                      </Stack>
                    </Grid>
                  </Grid>
                </Box>
              )}
            </Grid>

            <Grid item xs={12}>
              <Typography variant="overline" color="primary" sx={{ fontWeight: 600 }}>ROLE MAPPINGS</Typography>
              <Divider sx={{ mb: 2, mt: 0.5 }} />
              <Stack spacing={2}>
                {authSettings.roleMappings.map((mapping: any, index: number) => (
                  <Stack key={index} direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'center' }}>
                    <TextField placeholder="External Group" size="small" fullWidth value={mapping.externalGroup} disabled={isNode} onChange={(e) => handleUpdateRoleMapping(index, 'externalGroup', e.target.value)} sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} />
                    <TextField size="small" select label="ROLE" value={mapping.stacksAtlasRole} disabled={isNode} onChange={(e) => handleUpdateRoleMapping(index, 'stacksAtlasRole', e.target.value)} SelectProps={{ native: true }} sx={{ minWidth: 150, '& .MuiOutlinedInput-root': { fontWeight: 600 } }}>
                      <option value="Viewer">Viewer</option>
                      <option value="Standard">Standard</option>
                      <option value="Admin">Admin</option>
                    </TextField>
                    <MuiIconButton color="error" size="small" disabled={isNode} onClick={() => handleDeleteRoleMapping(index)}><DeleteIcon fontSize="small" /></MuiIconButton>
                  </Stack>
                ))}
                <Button startIcon={<AddIcon />} size="small" disabled={isNode} onClick={handleAddRoleMapping} sx={{ alignSelf: 'flex-start', fontWeight: 600 }}>ADD MAPPING</Button>
              </Stack>
            </Grid>
          </Grid>
        </Paper>
      </Grid>
    </Grid>
  );
};

export default AuthTab;
