import { useState } from 'react';
import {
  Box,
  Button,
  Chip,
  Collapse,
  IconButton,
  Menu,
  MenuItem,
  Paper,
  Stack,
  Tooltip,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import KeyboardArrowDownIcon from '@mui/icons-material/KeyboardArrowDown';
import KeyboardArrowUpIcon from '@mui/icons-material/KeyboardArrowUp';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import RadarIcon from '@mui/icons-material/Radar';
import EditIcon from '@mui/icons-material/Edit';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import ErrorIcon from '@mui/icons-material/Error';
import InfoIcon from '@mui/icons-material/Info';
import OpenSetupIcon from '@mui/icons-material/OpenInNew';
import RestartAltIcon from '@mui/icons-material/RestartAlt';
import DeleteIcon from '@mui/icons-material/Delete';
import MapIcon from '@mui/icons-material/Map';
import SyncIcon from '@mui/icons-material/Sync';
import ShieldIcon from '@mui/icons-material/Shield';
import type { FederatedNodeRow } from './types';
import { formatHubIp, formatDatabaseSizeMb } from './hubDashboardUtils';
import {
  getFederatedNodeOpenUrl,
  getFederatedNodeSetupUrl,
  getFederatedNodeStatusColor,
  getFederatedNodeStatusPresentation,
} from '../../utils/federatedNodeStatus';
import {
  classifyNodeVsHub,
  fleetDriftKindLabel,
  isActionableHubUpdateOffer,
  type FleetVersionDriftKind,
} from '../../utils/fleetVersionDrift';
import { isPaidFleetSiteTier, nodeLicenseTierLabel } from '../../utils/federationMode';

interface HubNodeTableProps {
  nodes: FederatedNodeRow[];
  isAdmin: boolean;
  onEditNode: (node: FederatedNodeRow) => void;
  onScan: (nodeId: string) => void;
  onForceSync: (nodeId: string) => void;
  onNotifyUpdate?: (nodeId: string) => void;
  onTriggerUpdate?: (nodeId: string, nodeName: string) => void;
  onEnrollHint: () => void;
  onResetSite: (nodeId: string, nodeName: string) => void;
  onDeleteNode: (nodeId: string, nodeName: string) => void;
  onViewTopology?: (nodeId: string) => void;
  topologySiteId?: string;
  hubVersion?: string | null;
  /** Latest Hub-staged package version for the preferred channel (gates Notify / Update). */
  stagedUpdateVersion?: string | null;
}

function driftChipColor(kind: FleetVersionDriftKind): 'default' | 'success' | 'warning' | 'info' {
  switch (kind) {
    case 'behind':
      return 'warning';
    case 'ahead':
      return 'info';
    case 'current':
      return 'success';
    default:
      return 'default';
  }
}

export function HubNodeTable({
  nodes,
  isAdmin,
  onEditNode,
  onScan,
  onForceSync,
  onNotifyUpdate,
  onTriggerUpdate,
  onEnrollHint,
  onResetSite,
  onDeleteNode,
  onViewTopology,
  topologySiteId,
  hubVersion = null,
  stagedUpdateVersion = null,
}: HubNodeTableProps) {
  const theme = useTheme();
  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [menuAnchor, setMenuAnchor] = useState<{ el: HTMLElement; node: FederatedNodeRow } | null>(null);

  const toggleExpand = (id: string) => {
    setExpandedId((prev) => (prev === id ? null : id));
  };

  if (nodes.length === 0) {
    return (
      <Box sx={{ py: 6, textAlign: 'center', opacity: 0.5 }}>
        <Typography variant="body2" fontStyle="italic">
          No sites match the current filters.
        </Typography>
      </Box>
    );
  }

  return (
    <Stack spacing={1}>
      {nodes.map((node) => {
        const statusPresentation = getFederatedNodeStatusPresentation(node.status);
        const statusColor = getFederatedNodeStatusColor(theme, statusPresentation.tone);
        const setupUrl = node.status?.toLowerCase() === 'setup_required'
          ? getFederatedNodeSetupUrl(node.ipAddress, node.httpsPort)
          : null;
        const openUrl = getFederatedNodeOpenUrl(node.ipAddress, node.httpPort, node.httpsPort, node.os);
        const expanded = expandedId === node.id;
        const securityLabel =
          node.securityGrade === 2 ? 'CRITICAL' : node.securityGrade === 1 ? 'WARN' : 'HEALTHY';
        const securityColor =
          node.securityGrade === 2
            ? theme.palette.error.main
            : node.securityGrade === 1
              ? theme.palette.warning.main
              : theme.palette.success.main;
        const driftKind = classifyNodeVsHub(node.version, hubVersion);
        const canOfferUpdate = isActionableHubUpdateOffer(stagedUpdateVersion, node.version);

        return (
          <Paper
            key={node.id}
            variant="outlined"
            sx={{
              borderRadius: 3,
              overflow: 'hidden',
              border: `1px solid ${alpha(theme.palette.divider, 0.08)}`,
              bgcolor: alpha(theme.palette.background.paper, 0.4),
            }}
          >
            <Stack
              direction="row"
              alignItems="center"
              spacing={1}
              sx={{
                px: 2,
                py: 1.25,
                cursor: 'pointer',
                '&:hover': { bgcolor: alpha(theme.palette.action.hover, 0.04) },
              }}
              onClick={() => toggleExpand(node.id)}
            >
              <IconButton size="small" sx={{ p: 0.25 }} onClick={(e) => { e.stopPropagation(); toggleExpand(node.id); }}>
                {expanded ? <KeyboardArrowUpIcon fontSize="small" /> : <KeyboardArrowDownIcon fontSize="small" />}
              </IconButton>

              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Typography variant="body2" fontWeight={800} noWrap>
                  {node.name || 'Unnamed Node'}
                </Typography>
                <Typography variant="caption" color="primary" fontWeight={800} sx={{ fontSize: '0.65rem' }}>
                  {(node.client || 'INTERNAL').toUpperCase()}
                </Typography>
              </Box>

              <Chip
                label={statusPresentation.label}
                size="small"
                variant="outlined"
                icon={
                  statusPresentation.tone === 'success' ? (
                    <CheckCircleIcon style={{ fontSize: 10 }} />
                  ) : statusPresentation.tone === 'info' ? (
                    <InfoIcon style={{ fontSize: 10 }} />
                  ) : (
                    <ErrorIcon style={{ fontSize: 10 }} />
                  )
                }
                sx={{
                  fontWeight: 800,
                  fontSize: '0.6rem',
                  height: 22,
                  color: statusColor,
                  borderColor: alpha(statusColor, 0.35),
                  display: { xs: 'none', sm: 'flex' },
                }}
              />

              <Chip
                label={nodeLicenseTierLabel(node.licenseTier, node.isPortable)}
                size="small"
                variant="outlined"
                color={isPaidFleetSiteTier(node.licenseTier) ? 'success' : 'warning'}
                sx={{
                  fontWeight: 800,
                  fontSize: '0.55rem',
                  height: 22,
                  display: { xs: 'none', md: 'flex' },
                }}
              />

              <Typography
                variant="caption"
                sx={{ fontFamily: 'monospace', fontWeight: 700, opacity: 0.75, display: { xs: 'none', md: 'block' } }}
              >
                {formatHubIp(node.ipAddress)}
              </Typography>

              <Typography variant="caption" color="text.secondary" sx={{ display: { xs: 'none', lg: 'block' } }}>
                {node.building || '-'} · {node.room || 'Universal'}
              </Typography>

              <Stack direction="row" spacing={0.25} onClick={(e) => e.stopPropagation()}>
                {setupUrl && (
                  <Tooltip title="Open site setup">
                    <IconButton size="small" component="a" href={setupUrl} target="_blank" rel="noopener noreferrer" color="warning">
                      <OpenSetupIcon sx={{ fontSize: 16 }} />
                    </IconButton>
                  </Tooltip>
                )}
                {openUrl && (
                  <Tooltip title="Open node dashboard">
                    <IconButton size="small" component="a" href={openUrl} target="_blank" rel="noopener noreferrer" color="primary">
                      <OpenInNewIcon sx={{ fontSize: 16 }} />
                    </IconButton>
                  </Tooltip>
                )}
                {onViewTopology && statusPresentation.isOperational && (
                  <Tooltip title={topologySiteId === node.id ? 'Viewing on map' : 'View site topology'}>
                    <IconButton
                      size="small"
                      color={topologySiteId === node.id ? 'secondary' : 'default'}
                      onClick={() => onViewTopology(node.id)}
                    >
                      <MapIcon sx={{ fontSize: 16 }} />
                    </IconButton>
                  </Tooltip>
                )}
                {isAdmin && (
                  <>
                    <Tooltip title="Open site inventory (deep scan per device)">
                      <span>
                        <IconButton
                          size="small"
                          color="primary"
                          disabled={!statusPresentation.isOperational}
                          onClick={() => onScan(node.id)}
                        >
                          <RadarIcon sx={{ fontSize: 16 }} />
                        </IconButton>
                      </span>
                    </Tooltip>
                    <Tooltip title="Edit sync & site">
                      <IconButton size="small" onClick={() => onEditNode(node)}>
                        <EditIcon sx={{ fontSize: 16 }} />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="Enrollment string">
                      <IconButton size="small" onClick={onEnrollHint}>
                        <ContentCopyIcon sx={{ fontSize: 16 }} />
                      </IconButton>
                    </Tooltip>
                    <Tooltip title="More actions">
                      <IconButton
                        size="small"
                        onClick={(e) => setMenuAnchor({ el: e.currentTarget, node })}
                      >
                        <MoreVertIcon sx={{ fontSize: 16 }} />
                      </IconButton>
                    </Tooltip>
                  </>
                )}
              </Stack>
            </Stack>

            <Collapse in={expanded}>
              <Box sx={{ px: 2, pb: 2, pt: 0, borderTop: `1px solid ${alpha(theme.palette.divider, 0.06)}` }}>
                <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ mt: 1.5 }}>
                  <Box flex={1}>
                    <Typography variant="caption" color="text.secondary" fontWeight={800}>VERSION</Typography>
                    <Stack direction="row" spacing={0.75} alignItems="center" flexWrap="wrap" useFlexGap>
                      <Typography variant="body2" fontWeight={700}>{node.version || '-'}</Typography>
                      <Tooltip title={hubVersion ? `Compared to Hub v${hubVersion} (last Node registration)` : 'Hub version unavailable'}>
                        <Chip
                          label={fleetDriftKindLabel(driftKind)}
                          size="small"
                          color={driftChipColor(driftKind)}
                          variant="outlined"
                          sx={{ fontWeight: 800, fontSize: '0.55rem', height: 18 }}
                        />
                      </Tooltip>
                      <Chip
                        label={nodeLicenseTierLabel(node.licenseTier, node.isPortable)}
                        size="small"
                        variant="outlined"
                        color={isPaidFleetSiteTier(node.licenseTier) ? 'success' : 'warning'}
                        sx={{ fontWeight: 800, fontSize: '0.55rem', height: 18, display: { md: 'none' } }}
                      />
                    </Stack>
                  </Box>
                  <Box flex={1}>
                    <Typography variant="caption" color="text.secondary" fontWeight={800}>DATABASE</Typography>
                    <Typography variant="body2" fontWeight={700} fontFamily="monospace">
                      {formatDatabaseSizeMb(node.databaseSize)}
                    </Typography>
                    {node.databaseSize !== undefined && node.databaseSize > 100 * 1024 * 1024 && (
                      <Typography variant="caption" color="error.main" fontWeight={700}>
                        COMPACTION RECOMMENDED
                      </Typography>
                    )}
                  </Box>
                  <Box flex={1}>
                    <Typography variant="caption" color="text.secondary" fontWeight={800}>LAST SYNC</Typography>
                    <Typography variant="body2" fontWeight={700}>
                      {node.lastSeenUtc
                        ? new Date(node.lastSeenUtc).toLocaleString()
                        : 'Never'}
                    </Typography>
                  </Box>
                  <Box flex={1}>
                    <Typography variant="caption" color="text.secondary" fontWeight={800}>SECURITY</Typography>
                    <Stack direction="row" alignItems="center" spacing={0.5}>
                      <ShieldIcon sx={{ fontSize: 14, color: securityColor }} />
                      <Typography variant="body2" fontWeight={800} sx={{ color: securityColor }}>
                        {securityLabel}
                      </Typography>
                    </Stack>
                    {node.stabilityScore !== undefined && (
                      <Typography variant="caption" color="text.secondary">
                        {node.stabilityScore}% stability · {node.deviceCount ?? 0} devices
                      </Typography>
                    )}
                  </Box>
                </Stack>

                {(node.overrideAlertSettings || node.overrideSiemSettings) && (
                  <Stack direction="row" spacing={1} mt={1.5} flexWrap="wrap">
                    {node.overrideAlertSettings && (
                      <Chip label="Alerts overridden locally" size="small" color="warning" variant="outlined" />
                    )}
                    {node.overrideSiemSettings && (
                      <Chip label="SIEM overridden locally" size="small" color="warning" variant="outlined" />
                    )}
                  </Stack>
                )}

                {isAdmin && statusPresentation.isOperational && (
                  <Stack direction="row" spacing={1} mt={1.5} flexWrap="wrap">
                    {onViewTopology && (
                      <Button
                        size="small"
                        variant={topologySiteId === node.id ? 'contained' : 'outlined'}
                        startIcon={<MapIcon />}
                        onClick={() => onViewTopology(node.id)}
                        sx={{ fontWeight: 800, borderRadius: 2 }}
                      >
                        {topologySiteId === node.id ? 'On map' : 'View map'}
                      </Button>
                    )}
                    <Button
                      size="small"
                      variant="outlined"
                      startIcon={<SyncIcon />}
                      onClick={() => onForceSync(node.id)}
                      sx={{ fontWeight: 800, borderRadius: 2 }}
                    >
                      Force sync
                    </Button>
                    {onNotifyUpdate && canOfferUpdate && (
                      <Button
                        size="small"
                        variant="outlined"
                        onClick={() => onNotifyUpdate(node.id)}
                        sx={{ fontWeight: 800, borderRadius: 2 }}
                      >
                        Notify update
                      </Button>
                    )}
                    {onTriggerUpdate && canOfferUpdate && (
                      <Button
                        size="small"
                        variant="contained"
                        color="secondary"
                        onClick={() => onTriggerUpdate(node.id, node.name || node.id)}
                        sx={{ fontWeight: 800, borderRadius: 2 }}
                      >
                        Update now
                      </Button>
                    )}
                  </Stack>
                )}
              </Box>
            </Collapse>
          </Paper>
        );
      })}

      <Menu
        anchorEl={menuAnchor?.el}
        open={Boolean(menuAnchor)}
        onClose={() => setMenuAnchor(null)}
      >
        <MenuItem
          onClick={() => {
            if (menuAnchor) onResetSite(menuAnchor.node.id, menuAnchor.node.name || menuAnchor.node.id);
            setMenuAnchor(null);
          }}
        >
          <RestartAltIcon fontSize="small" sx={{ mr: 1 }} /> Reset site (wipe inventory)
        </MenuItem>
        <MenuItem
          sx={{ color: 'error.main' }}
          onClick={() => {
            if (menuAnchor) onDeleteNode(menuAnchor.node.id, menuAnchor.node.name || menuAnchor.node.id);
            setMenuAnchor(null);
          }}
        >
          <DeleteIcon fontSize="small" sx={{ mr: 1 }} /> Remove node from fleet
        </MenuItem>
      </Menu>
    </Stack>
  );
}
