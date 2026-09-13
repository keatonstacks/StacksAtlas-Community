import React from 'react';
import { 
  Box, 
  Grid, 
  Paper, 
  Stack, 
  Typography, 
  TextField, 
  Button,
  alpha,
  useTheme
} from '@mui/material';
import {
  Gavel as GovernanceIcon,
  Refresh as ResetIcon,
  CloudDownload as DownloadIcon,
  MoveUp as MigrationIcon,
  ViewColumn as ColumnIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import { useConfirm } from '../../context/ConfirmContext';
import { SnapshotManager } from '../SnapshotManager';
import { Chip } from '@mui/material';

interface SecurityTabProps {
  saving: boolean;
  setSaving: (saving: boolean) => void;
  newKey: string;
  setNewKey: (key: string) => void;
  handleRotateKey: () => void;
  masterPass: string;
  setMasterPass: (pass: string) => void;
  handleExportPortable: () => void;
  importFile: File | null;
  setImportFile: (file: File | null) => void;
  importPass: string;
  setImportPass: (pass: string) => void;
  visibility: any;
  toggleColumn: (col: any) => void;
  formatLabel: (col: string) => string;
  onNotify?: (message: string, severity?: 'success' | 'error') => void;
}

const SecurityTab: React.FC<SecurityTabProps> = ({
  saving,
  setSaving,
  newKey,
  setNewKey,
  handleRotateKey,
  masterPass,
  setMasterPass,
  handleExportPortable,
  importFile,
  setImportFile,
  importPass,
  setImportPass,
  visibility,
  toggleColumn,
  formatLabel,
  onNotify,
}) => {
  const theme = useTheme();
  const { confirm } = useConfirm();

  const handleImportPortable = async () => {
    if (!importFile || !importPass) return;
    const ok = await confirm({
      title: 'Import portable database',
      message: 'Overwrite local data?',
      confirmColor: 'warning',
    });
    if (!ok) return;
    setSaving(true);
    try {
      await ApiService.importPortableDatabase(importFile, importPass);
      window.location.reload();
    } catch (err: unknown) {
      onNotify?.(err instanceof Error ? err.message : 'Import failed');
    } finally {
      setSaving(false);
    }
  };

  return (
    <Grid container spacing={3}>
      {/* SNAPSHOTS & RECOVERY (TOP ROW) */}
      <Grid item xs={12} lg={8}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            height: '100%',
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          <SnapshotManager />
        </Paper>
      </Grid>

      {/* REGISTRY SCHEMA GOVERNANCE (TOP RIGHT) */}
      <Grid item xs={12} lg={4}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            height: '100%',
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={2}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.secondary.main, 0.1) }}>
              <ColumnIcon color="secondary" sx={{ fontSize: 20 }} />
            </Box>
            <Box>
              <Typography variant="subtitle2" fontWeight={900}>REGISTRY SCHEMA</Typography>
              <Typography variant="caption" color="text.secondary">VISIBLE DATA PROJECTIONS</Typography>
            </Box>
          </Stack>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 3, opacity: 0.7 }}>
            Define which data vectors are visible across the global registry.
          </Typography>
          
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
            {(Object.keys(visibility) as Array<keyof typeof visibility>).map((col) => (
              <Chip
                key={col.toString()}
                label={formatLabel(col as string)}
                onClick={() => toggleColumn(col)}
                variant={visibility[col] ? "filled" : "outlined"}
                color={visibility[col] ? "primary" : "default"}
                size="small"
                sx={{ 
                  fontWeight: 900, 
                  fontSize: '0.6rem',
                  height: 22,
                  borderRadius: 1.5,
                  '&.MuiChip-filled': { boxShadow: `0 4px 8px ${alpha(theme.palette.primary.main, 0.2)}` }
                }}
              />
            ))}
          </Box>
        </Paper>
      </Grid>

      {/* ENCRYPTION & MIGRATION (BOTTOM ROW) */}
      <Grid item xs={12} md={6}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            height: '100%',
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
              <GovernanceIcon color="primary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>MASTER KEY GOVERNANCE</Typography>
              <Typography variant="caption" color="text.secondary">RE-ENCRYPT THE ENTIRE DATA REPOSITORY</Typography>
            </Box>
          </Stack>
          
          <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.warning.main, 0.05), border: '1px solid', borderColor: alpha(theme.palette.warning.main, 0.1), mb: 3 }}>
            <Typography variant="caption" color="warning.main" sx={{ fontWeight: 600, display: 'block', mb: 1 }}>⚠️ HIGH-RISK OPERATION</Typography>
            <Typography variant="caption" sx={{ opacity: 0.8 }}>
              Changing the master key re-encrypts the database at rest. Ensure you have a physical backup of the new key.
            </Typography>
          </Box>

          <Stack spacing={2}>
            <TextField fullWidth label="NEW MASTER SECRET" type="password" value={newKey} onChange={(e) => setNewKey(e.target.value)} size="small" sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} />
            <Button variant="contained" fullWidth startIcon={<ResetIcon />} onClick={handleRotateKey} disabled={saving || !newKey} sx={{ fontWeight: 600 }}>ROTATE ENCRYPTION KEY</Button>
          </Stack>
        </Paper>
      </Grid>

      <Grid item xs={12} md={6}>
        <Paper 
          variant="outlined" 
          sx={{ 
            p: 3, 
            height: '100%',
            borderRadius: 4, 
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.secondary.main, 0.1) }}>
              <MigrationIcon color="secondary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>DATABASE MIGRATION</Typography>
              <Typography variant="caption" color="text.secondary">EXPORT OR IMPORT PORTABLE INSTANCES</Typography>
            </Box>
          </Stack>

          <Grid container spacing={2}>
            <Grid item xs={12}>
              <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), border: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                <Typography variant="overline" sx={{ fontWeight: 600, display: 'block', mb: 1 }}>EXPORT PORTABLE DB</Typography>
                <Stack spacing={2}>
                  <TextField fullWidth size="small" type="password" label="MIGRATION PASSWORD" value={masterPass} onChange={(e) => setMasterPass(e.target.value)} sx={{ '& .MuiOutlinedInput-root': { fontWeight: 600 } }} />
                  <Button variant="outlined" fullWidth startIcon={<DownloadIcon />} onClick={handleExportPortable} disabled={saving || !masterPass} sx={{ fontWeight: 600, borderStyle: 'dashed', borderWidth: 2 }}>GENERATE ENCRYPTED EXPORT</Button>
                </Stack>
              </Box>
            </Grid>
            <Grid item xs={12}>
              <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), border: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                <Typography variant="overline" sx={{ fontWeight: 600, display: 'block', mb: 1 }}>IMPORT PORTABLE DB</Typography>
                <Stack direction="row" spacing={1}>
                   <Button variant="outlined" component="label" color="secondary" size="small" sx={{ fontWeight: 600, flex: 1, borderStyle: 'dashed' }}>
                    {importFile?.name ? importFile.name.substring(0, 15) + '...' : "SELECT ARCHIVE"}
                    <input type="file" hidden accept=".db" onChange={(e) => setImportFile(e.target.files?.[0] || null)} />
                  </Button>
                  <TextField size="small" type="password" label="PASS" value={importPass} onChange={(e) => setImportPass(e.target.value)} sx={{ width: 100 }} />
                  <Button variant="contained" color="secondary" size="small" startIcon={<MigrationIcon />} onClick={handleImportPortable} disabled={saving || !importFile || !importPass} sx={{ fontWeight: 600 }}>IMPORT</Button>
                </Stack>
              </Box>
            </Grid>
          </Grid>
        </Paper>
      </Grid>
    </Grid>
  );
};

export default SecurityTab;
