import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  Grid,
  Link,
  Paper,
  Stack,
  Switch,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import { Hub as OpenAvcIcon } from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import { OPENAVC_LINKS, STACKSATLAS_INTEGRATION_LINKS } from '../../config/integrationLinks';
import { isHubMode } from '../../utils/federationMode';

interface OpenAvcSettingsForm {
  enabled: boolean;
  baseUrl: string;
  username: string;
  password: string;
}

interface OpenAvcTabProps {
  fedSettings: { mode?: unknown };
  onNotify: (message: string, severity?: 'success' | 'error') => void;
}

const defaultForm: OpenAvcSettingsForm = {
  enabled: false,
  baseUrl: 'http://127.0.0.1:8080',
  username: '',
  password: '',
};

function OpenAvcResourceLinks() {
  return (
    <Alert severity="info" sx={{ borderRadius: 2 }}>
      <Typography variant="body2" fontWeight={700} gutterBottom>
        Resources
      </Typography>
      <Stack direction="row" flexWrap="wrap" useFlexGap columnGap={2} rowGap={0.5}>
        <Link
          href={STACKSATLAS_INTEGRATION_LINKS.DOCS_OPENAVC_CONTROL_BRIDGE}
          target="_blank"
          rel="noopener noreferrer"
          variant="body2"
          fontWeight={700}
        >
          StacksAtlas Control Bridge guide
        </Link>
        <Link
          href={OPENAVC_LINKS.DOCS}
          target="_blank"
          rel="noopener noreferrer"
          variant="body2"
          fontWeight={700}
        >
          OpenAVC documentation
        </Link>
        <Link
          href={OPENAVC_LINKS.RELEASES}
          target="_blank"
          rel="noopener noreferrer"
          variant="body2"
          fontWeight={700}
        >
          Download OpenAVC
        </Link>
      </Stack>
    </Alert>
  );
}

const OpenAvcTab: React.FC<OpenAvcTabProps> = ({ fedSettings, onNotify }) => {
  const theme = useTheme();
  const hubMode = isHubMode(fedSettings?.mode);
  const [form, setForm] = useState<OpenAvcSettingsForm>(defaultForm);
  const [passwordConfigured, setPasswordConfigured] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [testing, setTesting] = useState(false);
  const [healthStatus, setHealthStatus] = useState<string | null>(null);
  const [healthMessage, setHealthMessage] = useState<string | null>(null);
  const [candidates, setCandidates] = useState<
    Array<{ deviceId: string; ipAddress: string; hostname?: string; baseUrl: string; version?: string; source: string }>
  >([]);

  const loadCandidates = useCallback(async () => {
    if (hubMode) return;
    try {
      const found = await ApiService.getOpenAvcNetworkCandidates();
      setCandidates(found ?? []);
    } catch {
      setCandidates([]);
    }
  }, [hubMode]);

  const loadHealth = useCallback(async () => {
    if (hubMode) return;
    try {
      const health = await ApiService.getOpenAvcHealth();
      setHealthStatus(health.status);
      setHealthMessage(health.message ?? null);
    } catch {
      setHealthStatus(null);
      setHealthMessage(null);
    }
  }, [hubMode]);

  const loadSettings = useCallback(async () => {
    if (hubMode) {
      setLoading(false);
      return;
    }
    try {
      const data = await ApiService.getOpenAvcSettings();
      setForm({
        enabled: !!data.enabled,
        baseUrl: data.baseUrl || defaultForm.baseUrl,
        username: data.username || '',
        password: '',
      });
      setPasswordConfigured(!!data.password);
      setDirty(false);
    } catch {
      onNotify('Failed to load OpenAVC settings.', 'error');
    } finally {
      setLoading(false);
    }
  }, [hubMode, onNotify]);

  useEffect(() => {
    void loadSettings();
    void loadHealth();
    void loadCandidates();
  }, [loadSettings, loadHealth, loadCandidates]);

  const handleSave = async () => {
    if (form.enabled && !passwordConfigured && !form.password.trim()) {
      onNotify('Enter your OpenAVC password before enabling the integration.', 'error');
      return;
    }
    setSaving(true);
    try {
      await ApiService.updateOpenAvcSettings({
        ...form,
        password: form.password.trim() || (passwordConfigured ? '********' : ''),
      });
      setDirty(false);
      onNotify('OpenAVC settings saved.', 'success');
      await loadSettings();
      await loadHealth();
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : 'Failed to save OpenAVC settings.';
      onNotify(message, 'error');
    } finally {
      setSaving(false);
    }
  };

  const handleTest = async () => {
    if (dirty) {
      onNotify('Save settings before testing the connection.', 'error');
      return;
    }
    setTesting(true);
    try {
      const result = await ApiService.testOpenAvcConnection();
      onNotify(
        result.version
          ? `Connected to OpenAVC ${result.version}.`
          : (result.message || 'Connected to OpenAVC.'),
        'success',
      );
      await loadHealth();
    } catch (e: unknown) {
      const message = e instanceof Error ? e.message : 'OpenAVC connection test failed.';
      onNotify(message, 'error');
    } finally {
      setTesting(false);
    }
  };

  if (hubMode) {
    return (
      <Stack spacing={2}>
        <Alert severity="info" sx={{ borderRadius: 2 }}>
          OpenAVC runs on each site Node. Configure it on the node that hosts OpenAVC, not on the Hub.
        </Alert>
        <OpenAvcResourceLinks />
      </Stack>
    );
  }

  return (
    <Stack spacing={3}>
      <Paper
        variant="outlined"
        sx={{
          p: { xs: 2, sm: 3 },
          borderRadius: 3,
          bgcolor: alpha(theme.palette.info.main, 0.03),
          borderColor: alpha(theme.palette.info.main, 0.2),
        }}
      >
        <Stack direction="row" spacing={1.5} alignItems="center" mb={2} flexWrap="wrap" useFlexGap>
          <OpenAvcIcon color="info" />
          <Box flex={1}>
            <Typography variant="subtitle1" fontWeight={800}>OpenAVC Control Bridge</Typography>
            <Typography variant="caption" color="text.secondary">
              Link inventory devices to OpenAVC on the site node. Run macros and driver commands from the Controls tab.{' '}
              <Link
                href={STACKSATLAS_INTEGRATION_LINKS.DOCS_OPENAVC_CONTROL_BRIDGE}
                target="_blank"
                rel="noopener noreferrer"
                variant="caption"
                fontWeight={700}
              >
                Integration guide
              </Link>
            </Typography>
          </Box>
          {form.enabled && healthStatus && healthStatus !== 'disabled' && (
            <Chip
              size="small"
              label={
                healthStatus === 'online'
                  ? 'OpenAVC online'
                  : healthStatus === 'auth_failed'
                    ? 'Login failed'
                    : 'OpenAVC offline'
              }
              color={
                healthStatus === 'online'
                  ? 'success'
                  : healthStatus === 'auth_failed'
                    ? 'error'
                    : 'default'
              }
              variant={healthStatus === 'online' ? 'filled' : 'outlined'}
              sx={{ fontWeight: 800 }}
            />
          )}
        </Stack>

        {form.enabled && healthStatus === 'offline' && (
          <Alert severity="warning" sx={{ mb: 2, borderRadius: 2 }}>
            {healthMessage || 'Cannot reach OpenAVC. Check the service and base URL in Settings.'}
          </Alert>
        )}

        {form.enabled && healthStatus === 'auth_failed' && (
          <Alert severity="error" sx={{ mb: 2, borderRadius: 2 }}>
            {healthMessage || 'OpenAVC rejected the saved credentials. Update username and password below.'}
          </Alert>
        )}

        {loading ? (
          <Typography variant="body2" color="text.secondary">Loading…</Typography>
        ) : (
          <Grid container spacing={2}>
            <Grid item xs={12}>
              <FormControlLabel
                control={
                  <Switch
                    checked={form.enabled}
                    onChange={(e) => {
                      setForm((f) => ({ ...f, enabled: e.target.checked }));
                      setDirty(true);
                    }}
                  />
                }
                label="Enable OpenAVC integration"
              />
            </Grid>
            <Grid item xs={12} md={8}>
              <TextField
                fullWidth
                size="small"
                label="OpenAVC base URL"
                placeholder="http://127.0.0.1:8080"
                value={form.baseUrl}
                onChange={(e) => {
                  setForm((f) => ({ ...f, baseUrl: e.target.value }));
                  setDirty(true);
                }}
              />
            </Grid>
            <Grid item xs={12} md={4}>
              <TextField
                fullWidth
                size="small"
                label="Username"
                value={form.username}
                onChange={(e) => {
                  setForm((f) => ({ ...f, username: e.target.value }));
                  setDirty(true);
                }}
              />
            </Grid>
            <Grid item xs={12} md={6}>
              <TextField
                fullWidth
                size="small"
                type="password"
                label="Password"
                placeholder={passwordConfigured ? 'Enter new password to change' : 'OpenAVC login password'}
                value={form.password}
                onChange={(e) => {
                  setForm((f) => ({ ...f, password: e.target.value }));
                  setDirty(true);
                }}
                helperText={
                  passwordConfigured && !form.password
                    ? 'Password saved. Leave blank to keep current.'
                    : 'Same credentials you use to log into OpenAVC Programmer'
                }
              />
            </Grid>
            <Grid item xs={12}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
                <Button
                  variant="contained"
                  disabled={!dirty || saving}
                  onClick={() => void handleSave()}
                  sx={{ fontWeight: 700 }}
                >
                  {saving ? 'Saving…' : 'Save'}
                </Button>
                <Button
                  variant="outlined"
                  disabled={testing || !form.enabled}
                  onClick={() => void handleTest()}
                  sx={{ fontWeight: 700 }}
                >
                  {testing ? 'Testing…' : 'Test login'}
                </Button>
              </Stack>
              <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 1 }}>
                Verifies your OpenAVC username and password (not just that the service is up).
              </Typography>
            </Grid>
          </Grid>
        )}
      </Paper>

      {!hubMode && candidates.length > 0 && (
        <Alert severity="info" sx={{ borderRadius: 2 }}>
          <Typography variant="body2" fontWeight={700} gutterBottom>
            OpenAVC hosts found on your network
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
            Found during scan (ports 8080/8443). Pick the OpenAVC host for this site if it is not on this machine.
          </Typography>
          <Stack spacing={0.75}>
            {candidates.map((candidate) => (
              <Stack
                key={candidate.baseUrl}
                direction="row"
                spacing={1}
                alignItems="center"
                flexWrap="wrap"
                useFlexGap
              >
                <Chip
                  size="small"
                  label={candidate.version ? `OpenAVC ${candidate.version}` : 'OpenAVC'}
                  color="info"
                  variant="outlined"
                  sx={{ fontWeight: 700 }}
                />
                <Typography variant="caption" sx={{ fontFamily: 'monospace' }}>
                  {candidate.baseUrl}
                </Typography>
                {candidate.hostname && (
                  <Typography variant="caption" color="text.secondary">
                    ({candidate.hostname})
                  </Typography>
                )}
                <Button
                  size="small"
                  variant="text"
                  sx={{ fontWeight: 700, minWidth: 0, px: 0.5 }}
                  onClick={() => {
                    setForm((f) => ({ ...f, baseUrl: candidate.baseUrl, enabled: true }));
                    setDirty(true);
                  }}
                >
                  Use this URL
                </Button>
              </Stack>
            ))}
          </Stack>
        </Alert>
      )}

      <Alert severity="info" sx={{ borderRadius: 2 }}>
        API tokens and <strong>/api-docs</strong> are under <strong>Settings → Infrastructure</strong> for custom scripts and automation.
        Link devices from the inventory <strong>Controls</strong> tab using the OpenAVC device id from Programmer.
      </Alert>

      <OpenAvcResourceLinks />
    </Stack>
  );
};

export default OpenAvcTab;
