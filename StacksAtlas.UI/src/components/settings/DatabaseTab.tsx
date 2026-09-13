import { useState, useEffect } from 'react';
import {
  Box,
  Grid,
  Paper,
  Stack,
  Typography,
  TextField,
  Button,
  FormControl,
  Select,
  MenuItem,
  InputLabel,
  Checkbox,
  FormControlLabel,
  CircularProgress,
  Alert,
  alpha,
  useTheme
} from '@mui/material';
import {
  Storage as StorageIcon,
  FlashOn as TestIcon,
  FolderOpen as FolderIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import { useGlobalStats } from '../../context/GlobalStatsContext';
import { useConfirm } from '../../context/ConfirmContext';
import { mergeStorageMetrics } from '../../utils/databaseStorage';

type DatabaseStatus = {
  provider: string;
  connectionString: string;
  databaseSize: number;
  isHub?: boolean;
  applianceEngine?: string;
  fleetEngine?: string | null;
  fleetProvider?: string;
  canConfigureFleetEngine?: boolean;
  applianceDatabaseSize?: number;
  fleetDatabaseSize?: number;
  hasFleetDatabase?: boolean;
  applianceFileName?: string;
  fleetFileName?: string;
};

export default function DatabaseTab() {
  const theme = useTheme();
  const { confirm } = useConfirm();
  const {
    applianceDbSize,
    fleetDbSize,
    hasFleetDatabase: globalHasFleet,
    applianceEngine: globalApplianceEngine,
    refresh: refreshGlobalStats,
  } = useGlobalStats();

  const [loading, setLoading] = useState(true);
  const [status, setStatus] = useState<DatabaseStatus | null>(null);
  const [maintenance, setMaintenance] = useState<any>(null);
  const [storage, setStorage] = useState(() => mergeStorageMetrics(null, null));

  const [provider, setProvider] = useState('Sqlite');
  const [host, setHost] = useState('localhost');
  const [port, setPort] = useState('5432');
  const [databaseName, setDatabaseName] = useState('stacksatlas_hub');
  const [username, setUsername] = useState('postgres');
  const [password, setPassword] = useState('');
  const [migrateData, setMigrateData] = useState(true);

  const [testing, setTesting] = useState(false);
  const [applying, setApplying] = useState(false);
  const [testResult, setTestResult] = useState<{ success: boolean; message: string } | null>(null);
  const [applyResult, setApplyResult] = useState<{ success: boolean; message: string } | null>(null);

  useEffect(() => {
    async function loadDbStatus() {
      try {
        const [data, health] = await Promise.all([
          ApiService.getDatabaseStatus(),
          ApiService.getDatabaseHealth().catch(() => null),
        ]);
        setStatus(data);
        setMaintenance(health);
        setStorage(mergeStorageMetrics(data, health));
        void refreshGlobalStats();
        const fleetProv = data.fleetProvider || data.provider || 'Sqlite';
        setProvider(data.canConfigureFleetEngine ? fleetProv : 'Sqlite');

        if (data.canConfigureFleetEngine && fleetProv !== 'Sqlite' && data.connectionString) {
          try {
            const parts = data.connectionString.split(';');
            parts.forEach((p: string) => {
              const [key, val] = p.split('=');
              if (!key || !val) return;
              const k = key.trim().toLowerCase();
              const v = val.trim();
              if (k === 'host' || k === 'server') setHost(v);
              else if (k === 'port') setPort(v);
              else if (k === 'database' || k === 'initial catalog') setDatabaseName(v);
              else if (k === 'username' || k === 'user id' || k === 'user') setUsername(v);
            });
          } catch { /* ignore parse errors */ }
        }
      } catch (err) {
        console.error('Failed to load database status:', err);
      } finally {
        setLoading(false);
      }
    }
    loadDbStatus();
  }, []);

  const buildConnectionString = () => {
    if (provider === 'Sqlite') return '';
    if (provider === 'PostgreSQL' || provider === 'Postgres') {
      return `Host=${host};Port=${port};Database=${databaseName};Username=${username};Password=${password};Timeout=15;CommandTimeout=30;`;
    }
    return `Server=${host},${port};Database=${databaseName};User Id=${username};Password=${password};TrustServerCertificate=True;Timeout=15;`;
  };

  const handleTestConnection = async () => {
    setTesting(true);
    setTestResult(null);
    try {
      const connStr = buildConnectionString();
      const result = await ApiService.testDatabaseConnection({ provider, connectionString: connStr });
      setTestResult({ success: true, message: result.message || 'Successfully connected!' });
    } catch (err: any) {
      setTestResult({ success: false, message: err.message || 'Failed to establish database connection.' });
    } finally {
      setTesting(false);
    }
  };

  const handleApplySettings = async () => {
    const ok = await confirm({
      title: 'Change fleet database',
      message:
        'CRITICAL OPERATION: You are changing the Hub fleet database engine.\n\nDuring migration, node communications will be securely buffered in memory.\nDo you wish to proceed?',
      confirmColor: 'error',
    });
    if (!ok) return;

    setApplying(true);
    setApplyResult(null);
    try {
      const connStr = buildConnectionString();
      const result = await ApiService.applyDatabaseSettings({ provider, connectionString: connStr, migrateData });
      setApplyResult({ success: true, message: result.message || 'Fleet database infrastructure upgraded!' });
    } catch (err: any) {
      setApplyResult({ success: false, message: err.message || 'Migration failed. Ingestion safely rolled back to previous state.' });
    } finally {
      setApplying(false);
    }
  };

  if (loading) {
    return (
      <Box display="flex" justifyContent="center" py={8}>
        <CircularProgress />
      </Box>
    );
  }

  const formatMb = (bytes: number) => (bytes / 1024 / 1024).toFixed(2);
  const applianceBytes = applianceDbSize > 0 ? applianceDbSize : storage.applianceBytes;
  const fleetBytes = fleetDbSize > 0 ? fleetDbSize : storage.fleetBytes;
  const applianceMb = formatMb(applianceBytes);
  const fleetMb = formatMb(fleetBytes);
  const showDualStore = (globalHasFleet || storage.hasFleet) && (storage.isHub || globalHasFleet);
  const applianceEngine = globalApplianceEngine || storage.applianceEngine;
  const fleetEngine = storage.fleetEngine;
  const showFleetMigration = storage.canConfigureFleetEngine;
  const usesExternalFleetEngine = showFleetMigration && provider !== 'Sqlite';

  return (
    <Grid container spacing={3}>
      <Grid item xs={12} md={showFleetMigration ? 5 : 12}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: 'blur(20px)',
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha('#000', 0.1)}`
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={4}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
              <StorageIcon color="primary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>DATABASE STATUS</Typography>
              <Typography variant="caption" color="text.secondary">
                {showDualStore ? 'APPLIANCE + FLEET STORES' : 'LOCAL APPLIANCE STORE'}
              </Typography>
            </Box>
          </Stack>

          <Stack spacing={3}>
            {!showDualStore && (
              <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.background.default, 0.4), border: `1px solid ${alpha(theme.palette.divider, 0.08)}` }}>
                <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 800, display: 'block', mb: 0.5 }}>APPLIANCE ENGINE</Typography>
                <Typography variant="h5" sx={{ fontWeight: 900, color: 'primary.main', fontFamily: 'monospace' }}>
                  {applianceEngine.toUpperCase()}
                </Typography>
              </Box>
            )}

            {showDualStore ? (
              <Stack spacing={2}>
                <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.success.main, 0.05), border: `1px solid ${alpha(theme.palette.success.main, 0.1)}` }}>
                  <Stack direction="row" alignItems="center" spacing={1} mb={1}>
                    <FolderIcon color="success" fontSize="small" />
                    <Typography variant="caption" color="success.main" sx={{ fontWeight: 900 }}>APPLIANCE · {applianceEngine.toUpperCase()}</Typography>
                  </Stack>
                  <Typography variant="caption" color="text.secondary" display="block" sx={{ fontFamily: 'monospace', mb: 0.5 }}>
                    {status?.applianceFileName ?? 'StacksAtlas.db'}
                  </Typography>
                  <Typography variant="h6" sx={{ fontWeight: 800, fontFamily: 'monospace' }}>{applianceMb} MB</Typography>
                  <Typography variant="caption" color="text.secondary">Hub users, settings, and local appliance state.</Typography>
                </Box>
                <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.primary.main, 0.05), border: `1px solid ${alpha(theme.palette.primary.main, 0.1)}` }}>
                  <Stack direction="row" alignItems="center" spacing={1} mb={1}>
                    <StorageIcon color="primary" fontSize="small" />
                    <Typography variant="caption" color="primary.main" sx={{ fontWeight: 900 }}>FLEET · {fleetEngine.toUpperCase()}</Typography>
                  </Stack>
                  <Typography variant="caption" color="text.secondary" display="block" sx={{ fontFamily: 'monospace', mb: 0.5 }}>
                    {usesExternalFleetEngine ? 'External relational store' : (status?.fleetFileName ?? 'StacksAtlas.Hub.db')}
                  </Typography>
                  <Typography variant="h6" sx={{ fontWeight: 800, fontFamily: 'monospace' }}>{fleetMb} MB</Typography>
                  <Typography variant="caption" color="text.secondary">Enrolled nodes, fleet devices, federated logs and alerts.</Typography>
                </Box>
              </Stack>
            ) : (
              <Box sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.success.main, 0.05), border: `1px solid ${alpha(theme.palette.success.main, 0.1)}` }}>
                <Stack direction="row" alignItems="center" spacing={1} mb={1}>
                  <FolderIcon color="success" fontSize="small" />
                  <Typography variant="caption" color="success.main" sx={{ fontWeight: 900 }}>LOCAL APPLIANCE DATABASE</Typography>
                </Stack>
                <Typography variant="caption" color="text.secondary" display="block" sx={{ fontFamily: 'monospace', mb: 0.5 }}>
                  {status?.applianceFileName ?? 'StacksAtlas.db'}
                </Typography>
                <Typography variant="h6" sx={{ fontWeight: 800, fontFamily: 'monospace' }}>{applianceMb} MB</Typography>
                <Typography variant="caption" color="text.secondary">
                  Encrypted {applianceEngine} store  -  devices, alerts, logs, and local settings for this site.
                </Typography>
              </Box>
            )}

            {maintenance?.nextCleanupUtc && (
              <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic' }}>
                Next scheduled maintenance: {new Date(maintenance.nextCleanupUtc).toLocaleString()}
              </Typography>
            )}
          </Stack>
        </Paper>
      </Grid>

      {showFleetMigration && (
        <Grid item xs={12} md={7}>
          <Paper
            variant="outlined"
            sx={{
              p: 3,
              borderRadius: 4,
              bgcolor: alpha(theme.palette.background.paper, 0.8),
              backdropFilter: 'blur(20px)',
              border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
              boxShadow: `0 8px 16px ${alpha('#000', 0.1)}`
            }}
          >
            <Stack direction="row" alignItems="center" spacing={1.5} mb={4}>
              <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.secondary.main, 0.1) }}>
                <TestIcon color="secondary" sx={{ fontSize: 24 }} />
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>FLEET ENGINE ASSISTANT</Typography>
                <Typography variant="caption" color="text.secondary">MIGRATE HUB FLEET STORE (NOT APPLIANCE LITEDB)</Typography>
              </Box>
            </Stack>

            <Stack spacing={3}>
              <FormControl fullWidth size="small">
                <InputLabel id="provider-select-label">FLEET DATABASE PROVIDER</InputLabel>
                <Select
                  labelId="provider-select-label"
                  value={provider}
                  label="FLEET DATABASE PROVIDER"
                  onChange={(e) => {
                    setProvider(e.target.value);
                    setPort(e.target.value === 'PostgreSQL' ? '5432' : e.target.value === 'SQLServer' ? '1433' : '');
                    setTestResult(null);
                    setApplyResult(null);
                  }}
                  sx={{ fontWeight: 600 }}
                >
                  <MenuItem value="Sqlite" sx={{ fontWeight: 600 }}>SQLite file (StacksAtlas.Hub.db)</MenuItem>
                  <MenuItem value="PostgreSQL" sx={{ fontWeight: 600 }}>PostgreSQL (Enterprise)</MenuItem>
                  <MenuItem value="SQLServer" sx={{ fontWeight: 600 }}>Microsoft SQL Server (Enterprise)</MenuItem>
                </Select>
              </FormControl>

              {provider !== 'Sqlite' && (
                <Grid container spacing={2}>
                  <Grid item xs={8}>
                    <TextField fullWidth size="small" label="HOST / ENDPOINT" value={host} onChange={(e) => setHost(e.target.value)} />
                  </Grid>
                  <Grid item xs={4}>
                    <TextField fullWidth size="small" label="PORT" type="number" value={port} onChange={(e) => setPort(e.target.value)} />
                  </Grid>
                  <Grid item xs={12}>
                    <TextField fullWidth size="small" label="DATABASE NAME" value={databaseName} onChange={(e) => setDatabaseName(e.target.value)} />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField fullWidth size="small" label="USERNAME" value={username} onChange={(e) => setUsername(e.target.value)} />
                  </Grid>
                  <Grid item xs={6}>
                    <TextField fullWidth size="small" label="PASSWORD" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
                  </Grid>
                  <Grid item xs={12}>
                    <FormControlLabel
                      control={<Checkbox checked={migrateData} onChange={(e) => setMigrateData(e.target.checked)} color="primary" />}
                      label={<Typography variant="body2" sx={{ fontWeight: 600 }}>Migrate existing fleet SQLite data into the new engine</Typography>}
                    />
                  </Grid>
                </Grid>
              )}

              {testResult && (
                <Alert severity={testResult.success ? 'success' : 'error'} sx={{ borderRadius: 2, fontWeight: 600 }}>
                  {testResult.message}
                </Alert>
              )}

              {applyResult && (
                <Alert severity={applyResult.success ? 'success' : 'error'} sx={{ borderRadius: 2, fontWeight: 600 }}>
                  {applyResult.message}
                </Alert>
              )}

              {provider !== 'Sqlite' && (
                <Stack direction="row" spacing={2} pt={2}>
                  <Button fullWidth variant="outlined" onClick={handleTestConnection} disabled={testing || applying} startIcon={testing ? <CircularProgress size={16} /> : <TestIcon />}>
                    {testing ? 'TESTING...' : 'TEST CONNECTION'}
                  </Button>
                  <Button fullWidth variant="contained" onClick={handleApplySettings} disabled={applying || testing} startIcon={applying ? <CircularProgress size={16} /> : <StorageIcon />}>
                    {applying ? 'MIGRATING...' : 'APPLY & MIGRATE'}
                  </Button>
                </Stack>
              )}
            </Stack>
          </Paper>
        </Grid>
      )}
    </Grid>
  );
}
