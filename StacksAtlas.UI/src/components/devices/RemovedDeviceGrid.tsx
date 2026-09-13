import {
  Box,
  Button,
  Checkbox,
  Chip,
  Paper,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import RestoreFromTrashIcon from '@mui/icons-material/RestoreFromTrash';
import RadarIcon from '@mui/icons-material/Radar';
import type { Device } from '../../models/Device';

function formatWhen(iso?: string | null): string {
  if (!iso) return ' - ';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return ' - ';
  }
}

export type RemovedDeviceGridProps = {
  devices: Device[];
  nodesMap: Record<string, string>;
  selectionEnabled: boolean;
  isAdmin: boolean;
  selectedIds: Set<string>;
  allPageSelected: boolean;
  pageIndeterminate: boolean;
  onToggleSelect: (id: string) => void;
  onToggleSelectPage: () => void;
  onRestoreToFleet: (device: Device) => void;
  onDeviceClick?: (device: Device) => void;
  busy?: boolean;
  isPortable?: boolean;
};

export function RemovedDeviceGrid({
  devices,
  nodesMap,
  selectionEnabled,
  isAdmin,
  selectedIds,
  allPageSelected,
  pageIndeterminate,
  onToggleSelect,
  onToggleSelectPage,
  onRestoreToFleet,
  onDeviceClick,
  busy = false,
  isPortable = false,
}: RemovedDeviceGridProps) {
  const theme = useTheme();

  if (devices.length === 0) {
    return (
      <Paper variant="outlined" sx={{ p: 6, textAlign: 'center', borderRadius: 3 }}>
        <Typography color="text.secondary" fontWeight={600}>
          No permanently removed devices. Use &quot;Remove from Fleet&quot; in Archive to suppress a device here.
        </Typography>
      </Paper>
    );
  }

  return (
    <TableContainer
      component={Paper}
      variant="outlined"
      sx={{ borderRadius: 3, bgcolor: alpha(theme.palette.error.main, 0.02) }}
    >
      <Table size="small" stickyHeader>
        <TableHead>
          <TableRow>
            {selectionEnabled && isAdmin && (
              <TableCell padding="checkbox">
                <Checkbox
                  indeterminate={pageIndeterminate}
                  checked={allPageSelected}
                  onChange={onToggleSelectPage}
                  size="small"
                />
              </TableCell>
            )}
            <TableCell sx={{ fontWeight: 700 }}>Name</TableCell>
            {!isPortable && <TableCell sx={{ fontWeight: 700 }}>Node / Site</TableCell>}
            <TableCell sx={{ fontWeight: 700 }}>IP</TableCell>
            <TableCell sx={{ fontWeight: 700 }}>MAC</TableCell>
            <TableCell sx={{ fontWeight: 700 }}>Removed</TableCell>
            <TableCell sx={{ fontWeight: 700 }}>Removed by</TableCell>
            <TableCell sx={{ fontWeight: 700 }}>Rediscovery</TableCell>
            <TableCell sx={{ fontWeight: 700 }}>Last scan hit</TableCell>
            {isAdmin && <TableCell sx={{ fontWeight: 700 }} align="right">Actions</TableCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {devices.map(device => {
            const hits = device.rediscoveryHitCount ?? 0;
            const nodeLabel = device.nodeId
              ? (nodesMap[device.nodeId] || device.nodeId)
              : 'Local';

            return (
              <TableRow
                key={device.id}
                hover
                sx={{ cursor: onDeviceClick ? 'pointer' : 'default' }}
                onClick={() => onDeviceClick?.(device)}
              >
                {selectionEnabled && isAdmin && (
                  <TableCell padding="checkbox" onClick={e => e.stopPropagation()}>
                    <Checkbox
                      checked={selectedIds.has(device.id)}
                      onChange={() => onToggleSelect(device.id)}
                      size="small"
                    />
                  </TableCell>
                )}
                <TableCell>
                  <Typography variant="body2" fontWeight={700}>
                    {device.name || device.ipAddress || 'Unknown'}
                  </Typography>
                </TableCell>
                {!isPortable && (
                <TableCell>
                  <Typography variant="caption" fontFamily="monospace">
                    {nodeLabel}
                  </Typography>
                </TableCell>
                )}
                <TableCell>
                  <Typography variant="caption" fontFamily="monospace">
                    {device.ipAddress || ' - '}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Typography variant="caption" fontFamily="monospace">
                    {device.macAddress || ' - '}
                  </Typography>
                </TableCell>
                <TableCell>
                  <Typography variant="caption">{formatWhen(device.removedUtc)}</Typography>
                </TableCell>
                <TableCell>
                  <Typography variant="caption">{device.removedBy || ' - '}</Typography>
                </TableCell>
                <TableCell onClick={e => e.stopPropagation()}>
                  <Tooltip
                    title={
                      hits > 0
                        ? 'Network scans detected this identity after removal (logged only  -  not re-added).'
                        : 'No rediscovery hits since removal.'
                    }
                  >
                    <Chip
                      icon={hits > 0 ? <RadarIcon /> : undefined}
                      label={hits}
                      size="small"
                      color={hits > 0 ? 'warning' : 'default'}
                      variant={hits > 0 ? 'filled' : 'outlined'}
                      sx={{ fontWeight: 800, minWidth: 48 }}
                    />
                  </Tooltip>
                </TableCell>
                <TableCell>
                  {hits > 0 ? (
                    <Box>
                      <Typography variant="caption" display="block">
                        {formatWhen(device.lastRediscoveryAttemptUtc)}
                      </Typography>
                      {device.lastRediscoveryIp && (
                        <Typography variant="caption" color="text.secondary" fontFamily="monospace">
                          {device.lastRediscoveryIp}
                        </Typography>
                      )}
                    </Box>
                  ) : (
                    <Typography variant="caption" color="text.secondary"> - </Typography>
                  )}
                </TableCell>
                {isAdmin && (
                  <TableCell align="right" onClick={e => e.stopPropagation()}>
                    <Button
                      size="small"
                      variant="contained"
                      color="success"
                      disabled={busy}
                      startIcon={<RestoreFromTrashIcon />}
                      onClick={() => onRestoreToFleet(device)}
                      sx={{ fontWeight: 800, borderRadius: 2 }}
                    >
                      Restore to Fleet
                    </Button>
                  </TableCell>
                )}
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
