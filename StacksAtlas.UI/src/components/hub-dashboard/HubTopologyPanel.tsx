import { lazy, Suspense, useMemo } from 'react';
import {
  Box,
  Chip,
  FormControl,
  MenuItem,
  Paper,
  Select,
  Typography,
  alpha,
  useTheme,
  type SxProps,
  type Theme,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';
import type { FederatedNodeRow } from './types';
import {
  getFederatedNodeStatusColor,
  getFederatedNodeStatusPresentation,
} from '../../utils/federatedNodeStatus';

const TopologyGraph = lazy(() =>
  import('../network/TopologyGraph').then((m) => ({ default: m.TopologyGraph })),
);

interface HubTopologyPanelProps {
  nodes: FederatedNodeRow[];
  siteId: string;
  onSiteIdChange: (siteId: string) => void;
  sx?: SxProps<Theme>;
}

export function HubTopologyPanel({ nodes, siteId, onSiteIdChange, sx }: HubTopologyPanelProps) {
  const theme = useTheme();
  const navigate = useNavigate();

  const selectableNodes = useMemo(
    () => nodes.filter((n) => n.status?.toLowerCase() !== 'setup_required'),
    [nodes],
  );

  const selectedNode = useMemo(
    () => selectableNodes.find((n) => n.id === siteId) ?? null,
    [selectableNodes, siteId],
  );

  const statusPresentation = selectedNode
    ? getFederatedNodeStatusPresentation(selectedNode.status)
    : null;
  const statusColor = statusPresentation
    ? getFederatedNodeStatusColor(theme, statusPresentation.tone)
    : theme.palette.text.secondary;

  if (selectableNodes.length === 0) {
    return (
      <Paper
        variant="outlined"
        sx={{
          p: 3,
          borderRadius: 3,
          textAlign: 'center',
          bgcolor: alpha(theme.palette.background.paper, 0.8),
          ...sx,
        }}
      >
        <Typography variant="caption" color="text.secondary">
          Enroll a site to view per-site topology maps.
        </Typography>
      </Paper>
    );
  }

  return (
    <Paper
      id="hub-topology-panel"
      variant="outlined"
      sx={{
        p: 0,
        borderRadius: 3,
        height: { xs: 440, md: 560 },
        overflow: 'hidden',
        bgcolor: 'background.paper',
        display: 'flex',
        flexDirection: 'column',
        border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
        ...sx,
      }}
    >
      <Box
        sx={{
          px: 2,
          pt: 1.25,
          pb: 0.75,
          flexShrink: 0,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          gap: 1.5,
          flexWrap: 'wrap',
        }}
      >
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Typography variant="overline" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5 }}>
            SITE TOPOLOGY
          </Typography>
          {selectedNode && statusPresentation && (
            <Chip
              label={statusPresentation.label}
              size="small"
              variant="outlined"
              sx={{
                fontWeight: 800,
                fontSize: '0.6rem',
                height: 22,
                color: statusColor,
                borderColor: alpha(statusColor, 0.35),
              }}
            />
          )}
          {selectedNode?.deviceCount !== undefined && (
            <Typography variant="caption" color="text.secondary" fontWeight={700}>
              {selectedNode.deviceCount} devices
            </Typography>
          )}
        </Box>
        <FormControl size="small" sx={{ minWidth: { xs: 160, sm: 200 } }}>
          <Select
            value={siteId}
            onChange={(e) => onSiteIdChange(e.target.value)}
            displayEmpty
            sx={{ height: 32, fontSize: '0.8rem', fontWeight: 600 }}
          >
            {selectableNodes.map((node) => (
              <MenuItem key={node.id} value={node.id}>
                {node.name?.trim() || node.id}
              </MenuItem>
            ))}
          </Select>
        </FormControl>
      </Box>

      <Box sx={{ flex: 1, minHeight: 0, width: '100%' }}>
        {siteId ? (
          <Suspense
            fallback={
              <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
                <Typography variant="caption" color="text.secondary">
                  Loading topology...
                </Typography>
              </Box>
            }
          >
            <TopologyGraph
              nodeId={siteId}
              onNodeClick={(deviceId) => {
                if (deviceId === 'root_gateway' || deviceId === 'host_machine') return;
                const params = new URLSearchParams({
                  nodeId: siteId,
                  id: deviceId,
                });
                navigate(`/devices?${params.toString()}`);
              }}
            />
          </Suspense>
        ) : (
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
            <Typography variant="caption" color="text.secondary">
              Select a site to load its topology.
            </Typography>
          </Box>
        )}
      </Box>
    </Paper>
  );
}
