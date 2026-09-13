import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import Inventory2OutlinedIcon from '@mui/icons-material/Inventory2Outlined';
import { ApiService } from '../../services/apiService';
import type { UpdateChannel, UpdateDepotStageResult, UpdateDepotStatus } from '../../models/Updates';
import { depotStatusSummaryLabel, formatBytes, listDepotVersionRows } from '../../utils/updateDepotUi';

interface HubUpdateDepotPanelProps {
  channel: UpdateChannel;
}

export function HubUpdateDepotPanel({ channel }: HubUpdateDepotPanelProps) {
  const theme = useTheme();
  const [status, setStatus] = useState<UpdateDepotStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [staging, setStaging] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [stageResult, setStageResult] = useState<UpdateDepotStageResult | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await ApiService.getUpdateDepotStatus();
      setStatus(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load depot status.');
      setStatus(null);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  // Clear prior stage result when the Infrastructure preview toggle changes the channel.
  useEffect(() => {
    setStageResult(null);
    setError(null);
  }, [channel]);

  const handleStage = async () => {
    setStaging(true);
    setError(null);
    setStageResult(null);
    try {
      const result = await ApiService.stageUpdateDepot(channel);
      setStageResult(result);
      await refresh();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to stage update into the Hub depot.');
    } finally {
      setStaging(false);
    }
  };

  const rows = listDepotVersionRows(status);

  return (
    <Box
      sx={{
        p: 2,
        borderRadius: 2,
        border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
        bgcolor: alpha(theme.palette.background.default, 0.35),
      }}
    >
      <Stack spacing={1.5}>
        <Stack direction="row" spacing={1.25} alignItems="center">
          <Inventory2OutlinedIcon color="primary" fontSize="small" />
          <Box sx={{ minWidth: 0, flex: 1 }}>
            <Typography variant="subtitle2" fontWeight={800}>
              Hub update depot
            </Typography>
            <Typography variant="caption" color="text.secondary" display="block">
              Download once to this Hub. Enrolled Nodes check and download from the depot (air-gap path when the Hub is the only internet door).
            </Typography>
          </Box>
        </Stack>

        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ xs: 'stretch', sm: 'center' }}>
          <Chip
            size="small"
            label={loading ? 'Loading depot…' : depotStatusSummaryLabel(status)}
            color={rows.length > 0 ? 'success' : 'default'}
            sx={{ fontWeight: 800, fontSize: '0.65rem', maxWidth: '100%' }}
          />
          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            <Button
              size="small"
              variant="outlined"
              disabled={loading || staging}
              onClick={() => void refresh()}
              sx={{ fontWeight: 700 }}
            >
              Refresh
            </Button>
            <Button
              size="small"
              variant="contained"
              color="secondary"
              disabled={loading || staging}
              onClick={() => void handleStage()}
              startIcon={staging ? <CircularProgress size={14} color="inherit" /> : undefined}
              sx={{ fontWeight: 700 }}
            >
              {staging ? `Staging ${channel}…` : `Stage ${channel} to depot`}
            </Button>
          </Stack>
        </Stack>

        {error && (
          <Alert severity="error" sx={{ borderRadius: 2 }}>
            {error}
          </Alert>
        )}

        {stageResult?.success && (
          <Alert severity={stageResult.alreadyStaged ? 'warning' : 'success'} sx={{ borderRadius: 2 }}>
            {stageResult.message}
            {stageResult.stagedArtifactKeys?.length
              ? ` Keys: ${stageResult.stagedArtifactKeys.join(', ')}.`
              : ''}
          </Alert>
        )}

        {rows.length > 0 && (
          <Table size="small">
            <TableHead>
              <TableRow>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.65rem' }}>CHANNEL</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.65rem' }}>VERSION</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.65rem' }}>ARTIFACTS</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.65rem' }}>SIZE</TableCell>
                <TableCell sx={{ fontWeight: 800, fontSize: '0.65rem' }}>TRUST</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {rows.map((row) => (
                <TableRow key={`${row.channel}-${row.version}`}>
                  <TableCell>
                    <Typography variant="caption" fontWeight={700}>{row.channel}</Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="caption" fontFamily="monospace" fontWeight={700}>
                      {row.version}
                    </Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="caption">{row.artifactCount}</Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="caption">{formatBytes(row.totalBytes)}</Typography>
                  </TableCell>
                  <TableCell>
                    <Typography variant="caption" color="text.secondary">
                      {row.hasManifest && row.hasSignature ? 'manifest + sig' : 'incomplete'}
                    </Typography>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </Stack>
    </Box>
  );
}
