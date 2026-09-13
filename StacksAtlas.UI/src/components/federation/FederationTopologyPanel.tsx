import {
  Box,
  Chip,
  CircularProgress,
  Grid,
  IconButton,
  Paper,
  Stack,
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
import {
  Devices as DevicesIcon,
  Refresh as RefreshIcon,
  Delete as DeleteIcon,
  RestartAlt as ResetSiteIcon,
  Terminal as TerminalIcon,
  Warning as AlertsIcon,
  OpenInNew as OpenDashboardIcon,
  OpenInNew as OpenSetupIcon,
  SystemUpdateAlt as SystemUpdateIcon,
  NotificationsActive as NotifyUpdateIcon,
} from '@mui/icons-material';
import React from 'react';
import { useNavigate } from 'react-router-dom';
import { getFederatedNodeSetupUrl, getFederatedNodeStatusPresentation, getFederatedNodeStatusColor, getFederatedNodeOpenUrl } from '../../utils/federatedNodeStatus';
import { resolveNodeHttpPort } from '../../utils/appliancePorts';
import { isPaidFleetSiteTier, licenseTierLabel, nodeLicenseTierLabel } from '../../utils/federationMode';
import { ApiService } from '../../services/apiService';
import {
  classifyNodeVsHub,
  fleetDriftKindLabel,
  fleetDriftSummaryLabel,
  isActionableHubUpdateOffer,
  summarizeFleetVersionDrift,
  type FleetVersionDriftKind,
} from '../../utils/fleetVersionDrift';
import { getPreferredUpdateChannel } from '../../utils/preferredUpdateChannel';
import { latestStagedVersionForChannel } from '../../utils/updateDepotUi';

interface FederationTopologyPanelProps {
  nodes: any[];
  loadingNodes: boolean;
  onRefresh: () => void;
  isAdmin: boolean;
  license?: {
    isActive?: boolean;
    tier?: unknown;
  } | null;
  onForceSync: (nodeId: string) => void;
  onNotifyUpdate?: (nodeId: string, nodeName: string) => void;
  onTriggerUpdate?: (nodeId: string, nodeName: string) => void;
  onResetSite: (nodeId: string, nodeName: string) => void;
  onDeleteNode: (nodeId: string, nodeName: string) => void;
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

export function FederationTopologyPanel({
  nodes,
  loadingNodes,
  onRefresh,
  isAdmin,
  license,
  onForceSync,
  onNotifyUpdate,
  onTriggerUpdate,
  onResetSite,
  onDeleteNode,
}: FederationTopologyPanelProps) {
  const theme = useTheme();
  const navigate = useNavigate();
  const hubTier = licenseTierLabel(license?.tier, !!license?.isActive);
  const unlicensedSites = nodes.filter((n) => !isPaidFleetSiteTier(n.licenseTier)).length;
  const operationalSites = nodes.filter((n) => n.status?.toLowerCase() !== 'setup_required').length;

  const [hubVersion, setHubVersion] = React.useState<string | null>(null);
  const [stagedUpdateVersion, setStagedUpdateVersion] = React.useState<string | null>(null);

  const loadVersionAndDepot = React.useCallback(async () => {
    const channel = getPreferredUpdateChannel();
    const [info, depot] = await Promise.all([
      ApiService.getSystemVersion().catch(() => null),
      ApiService.getUpdateDepotStatus().catch(() => null),
    ]);
    return {
      hubVersion: info?.version ?? null,
      stagedUpdateVersion: latestStagedVersionForChannel(depot, channel),
    };
  }, []);

  React.useEffect(() => {
    let cancelled = false;
    void loadVersionAndDepot().then((next) => {
      if (cancelled) return;
      setHubVersion(next.hubVersion);
      setStagedUpdateVersion(next.stagedUpdateVersion);
    });
    return () => {
      cancelled = true;
    };
  }, [loadVersionAndDepot]);

  const handleRefresh = () => {
    onRefresh();
    void loadVersionAndDepot().then((next) => {
      setHubVersion(next.hubVersion);
      setStagedUpdateVersion(next.stagedUpdateVersion);
    });
  };

  const driftSummary = React.useMemo(
    () => summarizeFleetVersionDrift(nodes, hubVersion),
    [nodes, hubVersion],
  );
  const driftByNodeId = React.useMemo(() => {
    const map = new Map<string, FleetVersionDriftKind>();
    for (const row of driftSummary.rows) {
      if (row.nodeId) map.set(row.nodeId, row.kind);
    }
    return map;
  }, [driftSummary.rows]);

  return (
    <Grid item xs={12}>
      <Paper
        variant="outlined"
        sx={{
          p: 4,
          borderRadius: 4,
          bgcolor: alpha(theme.palette.background.paper, 0.8),
          backdropFilter: 'blur(20px)',
          border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
          boxShadow: `0 12px 24px ${alpha('#000', 0.2)}`,
        }}
      >
        <Stack direction="row" justifyContent="space-between" alignItems="center" mb={3}>
          <Stack direction="row" alignItems="center" spacing={2}>
            <Box sx={{ p: 1.5, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1), border: `1px solid ${alpha(theme.palette.primary.main, 0.15)}` }}>
              <DevicesIcon color="primary" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" fontWeight={900} sx={{ letterSpacing: -0.5, lineHeight: 1.1 }}>
                REGISTERED APPLIANCES (TOPOLOGY SUMMARY)
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ opacity: 0.7 }}>
                MONITOR AND MANAGE EDGE NODES REGISTERED TO THIS HUB
              </Typography>
            </Box>
          </Stack>
          <IconButton onClick={handleRefresh} disabled={loadingNodes} size="small">
            <RefreshIcon />
          </IconButton>
        </Stack>

        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} mb={2} alignItems={{ xs: 'flex-start', sm: 'center' }} flexWrap="wrap" useFlexGap>
          <Chip
            label={`Hub: ${hubTier}`}
            size="small"
            color={license?.isActive ? 'primary' : 'default'}
            sx={{ fontWeight: 900, fontSize: '0.65rem' }}
          />
          {hubVersion && (
            <Chip
              label={`Hub v${hubVersion}`}
              size="small"
              variant="outlined"
              sx={{ fontWeight: 800, fontSize: '0.65rem' }}
            />
          )}
          {nodes.length > 0 && (
            <Chip
              label={fleetDriftSummaryLabel(driftSummary, hubVersion)}
              size="small"
              color={driftSummary.behind > 0 ? 'warning' : driftSummary.unknown > 0 ? 'default' : 'success'}
              sx={{ fontWeight: 800, fontSize: '0.65rem', maxWidth: '100%' }}
            />
          )}
          <Typography variant="caption" color="text.secondary" fontWeight={700}>
            {nodes.length} enrolled site{nodes.length === 1 ? '' : 's'} ({operationalSites} operational)
            {' · '}
            $40 Business license per appliance (Hub + each site)
          </Typography>
        </Stack>

        {unlicensedSites > 0 && (
          <Typography variant="caption" color="warning.main" fontWeight={800} sx={{ display: 'block', mb: 2 }}>
            {unlicensedSites} site(s) report Free or unlicensed. Activate Business on each remote appliance (Settings → License on that node).
          </Typography>
        )}

        {loadingNodes ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 8 }}>
            <CircularProgress size={32} />
          </Box>
        ) : nodes.length === 0 ? (
          <Box sx={{ py: 6, textAlign: 'center', color: 'text.secondary' }}>
            <Typography variant="body2" sx={{ fontStyle: 'italic', mb: 1 }}>
              No registered edge nodes yet.
            </Typography>
            <Typography variant="caption" sx={{ display: 'block', maxWidth: 480, mx: 'auto', lineHeight: 1.55 }}>
              Enroll a Node using an mTLS connection string or Direct Push in <strong>Fleet Federation</strong> below,
              or from the Hub Dashboard <strong>Enroll Remote Node</strong> action.
            </Typography>
          </Box>
        ) : (
          <TableContainer sx={{ overflow: 'hidden' }}>
            <Table size="small">
              <TableHead>
                <TableRow>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>SITE IDENTITY</TableCell>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>STATUS</TableCell>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>LICENSE</TableCell>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>LOCATION PROVENANCE</TableCell>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>SPECIFICATIONS</TableCell>
                  <TableCell sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>DEEP LINKS</TableCell>
                  <TableCell align="right" sx={{ fontWeight: 900, fontSize: '0.65rem', letterSpacing: 1, border: 0, opacity: 0.5 }}>ACTIONS</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {nodes.map((node) => {
                  const statusPresentation = getFederatedNodeStatusPresentation(node.status);
                  const statusColor = getFederatedNodeStatusColor(theme, statusPresentation.tone);
                  const setupUrl = node.status?.toLowerCase() === 'setup_required'
                    ? getFederatedNodeSetupUrl(node.ipAddress, node.httpsPort)
                    : null;
                  const openUrl = getFederatedNodeOpenUrl(node.ipAddress, node.httpPort, node.httpsPort, node.os);
                  const displayHttpPort = resolveNodeHttpPort(node.httpPort, node.os);
                  const formattedSize = node.databaseSize !== undefined && node.databaseSize > 0
                    ? `${(node.databaseSize / 1024 / 1024).toFixed(2)} MB`
                    : '0.00 MB';
                  const locationParts = [node.client, node.building, node.room].filter(Boolean);
                  const locationStr = locationParts.join(' / ') || '-';
                  const driftKind = driftByNodeId.get(node.id) ?? classifyNodeVsHub(node.version, hubVersion);
                  const canOfferUpdate = isActionableHubUpdateOffer(stagedUpdateVersion, node.version);

                  return (
                    <TableRow key={node.id} hover sx={{ '&:hover': { bgcolor: alpha(theme.palette.action.hover, 0.02) } }}>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}`, py: 1.5 }}>
                        <Typography variant="body2" fontWeight={800}>{node.name || 'Unnamed Node'}</Typography>
                        <Typography variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.6 }}>ID: {node.id}</Typography>
                      </TableCell>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        <Chip
                          label={statusPresentation.label}
                          size="small"
                          variant="outlined"
                          sx={{
                            fontWeight: 900,
                            fontSize: '0.55rem',
                            height: 18,
                            color: statusColor,
                            borderColor: alpha(statusColor, 0.3),
                            bgcolor: alpha(statusColor, 0.04),
                          }}
                        />
                      </TableCell>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        <Chip
                          label={nodeLicenseTierLabel(node.licenseTier, node.isPortable)}
                          size="small"
                          variant="outlined"
                          color={isPaidFleetSiteTier(node.licenseTier) ? 'success' : 'warning'}
                          sx={{ fontWeight: 800, fontSize: '0.55rem', height: 20 }}
                        />
                      </TableCell>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        <Typography variant="caption" fontWeight={700}>{locationStr}</Typography>
                      </TableCell>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        <Typography variant="caption" display="block" sx={{ fontFamily: 'monospace', fontWeight: 700 }}>
                          IP: {node.ipAddress || '-'} HTTP:{displayHttpPort} HTTPS:{node.httpsPort || 5001}
                        </Typography>
                        <Stack direction="row" spacing={0.75} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mt: 0.5 }}>
                          <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.65rem' }}>
                            Ver: {node.version || '-'} | OS: {node.os || '-'} | DB: {formattedSize}
                          </Typography>
                          <Tooltip title={hubVersion ? `Compared to Hub v${hubVersion} (last Node registration)` : 'Hub version unavailable'}>
                            <Chip
                              label={fleetDriftKindLabel(driftKind)}
                              size="small"
                              color={driftChipColor(driftKind)}
                              variant="outlined"
                              sx={{ fontWeight: 800, fontSize: '0.55rem', height: 18 }}
                            />
                          </Tooltip>
                        </Stack>
                      </TableCell>
                      <TableCell sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        <Stack direction="row" spacing={1}>
                          {openUrl && (
                            <Tooltip title="Open node dashboard">
                              <IconButton
                                size="small"
                                component="a"
                                href={openUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                sx={{ color: theme.palette.primary.main }}
                              >
                                <OpenDashboardIcon sx={{ fontSize: 16 }} />
                              </IconButton>
                            </Tooltip>
                          )}
                          {setupUrl && (
                            <Tooltip title="Open site setup on node">
                              <IconButton
                                size="small"
                                component="a"
                                href={setupUrl}
                                target="_blank"
                                rel="noopener noreferrer"
                                sx={{ color: theme.palette.warning.main }}
                              >
                                <OpenSetupIcon sx={{ fontSize: 16 }} />
                              </IconButton>
                            </Tooltip>
                          )}
                          <Tooltip title="View Devices">
                            <IconButton
                              size="small"
                              onClick={() => navigate(`/devices?nodeId=${encodeURIComponent(node.id)}`)}
                              sx={{ color: theme.palette.primary.main }}
                            >
                              <DevicesIcon sx={{ fontSize: 16 }} />
                            </IconButton>
                          </Tooltip>
                          <Tooltip title="View Logs">
                            <IconButton
                              size="small"
                              onClick={() => navigate(`/logs?nodeId=${encodeURIComponent(node.id)}`)}
                              sx={{ color: theme.palette.info.main }}
                            >
                              <TerminalIcon sx={{ fontSize: 16 }} />
                            </IconButton>
                          </Tooltip>
                          <Tooltip title="View Alerts">
                            <IconButton
                              size="small"
                              onClick={() => navigate(`/alerts?nodeId=${encodeURIComponent(node.id)}`)}
                              sx={{ color: theme.palette.warning.main }}
                            >
                              <AlertsIcon sx={{ fontSize: 16 }} />
                            </IconButton>
                          </Tooltip>
                        </Stack>
                      </TableCell>
                      <TableCell align="right" sx={{ borderBottom: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                        {isAdmin ? (
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            <Tooltip title="Force Sync Node">
                              <IconButton
                                size="small"
                                onClick={() => onForceSync(node.id)}
                                sx={{ color: theme.palette.success.main }}
                              >
                                <RefreshIcon sx={{ fontSize: 16 }} />
                              </IconButton>
                            </Tooltip>
                            {onNotifyUpdate && (
                              <Tooltip title={
                                canOfferUpdate
                                  ? 'Notify Node of Hub-staged update'
                                  : stagedUpdateVersion
                                    ? 'Node is already at or ahead of the staged depot package'
                                    : 'Stage a package on the Hub depot first'
                              }>
                                <span>
                                  <IconButton
                                    size="small"
                                    onClick={() => onNotifyUpdate(node.id, node.name)}
                                    disabled={!statusPresentation.isOperational || !canOfferUpdate}
                                    sx={{ color: theme.palette.info.main }}
                                  >
                                    <NotifyUpdateIcon sx={{ fontSize: 16 }} />
                                  </IconButton>
                                </span>
                              </Tooltip>
                            )}
                            {onTriggerUpdate && (
                              <Tooltip title={
                                canOfferUpdate
                                  ? 'Update now (Node admin still confirms apply)'
                                  : stagedUpdateVersion
                                    ? 'Node is already at or ahead of the staged depot package'
                                    : 'Stage a package on the Hub depot first'
                              }>
                                <span>
                                  <IconButton
                                    size="small"
                                    onClick={() => onTriggerUpdate(node.id, node.name)}
                                    disabled={!statusPresentation.isOperational || !canOfferUpdate}
                                    sx={{ color: theme.palette.secondary.main }}
                                  >
                                    <SystemUpdateIcon sx={{ fontSize: 16 }} />
                                  </IconButton>
                                </span>
                              </Tooltip>
                            )}
                            <Tooltip title="Reset Site (wipe local inventory; stays enrolled)">
                              <IconButton
                                size="small"
                                onClick={() => onResetSite(node.id, node.name)}
                                disabled={!statusPresentation.isOperational}
                                sx={{ color: theme.palette.warning.main }}
                              >
                                <ResetSiteIcon sx={{ fontSize: 16 }} />
                              </IconButton>
                            </Tooltip>
                            <Tooltip title="Delete Node">
                              <IconButton
                                size="small"
                                onClick={() => onDeleteNode(node.id, node.name)}
                                sx={{ color: theme.palette.error.main }}
                              >
                                <DeleteIcon sx={{ fontSize: 16 }} />
                              </IconButton>
                            </Tooltip>
                          </Stack>
                        ) : (
                          <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.6rem' }}>Admin only</Typography>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                })}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </Paper>
    </Grid>
  );
}
