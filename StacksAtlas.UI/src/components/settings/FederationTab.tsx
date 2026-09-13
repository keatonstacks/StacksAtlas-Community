import React from 'react';
import { isHubMode, normalizeExecutionMode } from '../../utils/federationMode';
import { 
  Box, 
  Grid, 
  Paper, 
  Stack, 
  Typography, 
  TextField, 
  Button,
  alpha,
  useTheme,
  Tabs,
  Tab,
  IconButton,
  CircularProgress,
  Switch,
  FormControlLabel,
  Alert
} from '@mui/material';
import {
  MoveUp as MigrationIcon,
  Save as SaveIcon,
  ContentCopy as CopyIcon,
  Security as SecurityIcon,
  Refresh as RefreshIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import type { FederationBacklogStatus, TailscaleReachabilityResult, TailscaleStatus, TailscaleTransportSummary } from '../../services/apiService';
import { EnrollmentStringPanel } from '../federation/EnrollmentStringPanel';
import { RemoteSiteChecklist } from '../federation/RemoteSiteChecklist';
import { TailscaleAclHelpPanel } from '../federation/TailscaleAclHelpPanel';
import { FederationTopologyPanel } from '../federation/FederationTopologyPanel';
import { enrollmentTransportForHost, parseEnrollmentHost } from '../../utils/enrollmentStringUtils';
import { useAuth } from '../../context/AuthContext';
import { useConfirm } from '../../context/ConfirmContext';
import { getPreferredUpdateChannel } from '../../utils/preferredUpdateChannel';

interface FederationTabProps {
  fedSettings: any;
  setFedSettings: (settings: any) => void;
  fedDirty: boolean;
  setFedDirty: (dirty: boolean) => void;
  savingFed: boolean;
  handleSaveFed: () => void;
  handleDecouple?: () => void;
  license: any;
  copyToClipboard: (text: string) => void;
  generatedFedToken: string | null;
  setGeneratedFedToken: (token: string | null) => void;
  onNotify?: (message: string, severity?: 'success' | 'error') => void;
}

const FederationTab: React.FC<FederationTabProps> = ({
  fedSettings,
  setFedSettings,
  fedDirty,
  setFedDirty,
  savingFed,
  handleSaveFed,
  handleDecouple,
  license,
  copyToClipboard,
  generatedFedToken,
  setGeneratedFedToken,
  onNotify
}) => {
  const theme = useTheme();
  const { isAdmin } = useAuth();
  const { confirm } = useConfirm();

  // Nodes Topology State
  const [nodes, setNodes] = React.useState<any[]>([]);
  const [loadingNodes, setLoadingNodes] = React.useState(false);
  const [backlogStatus, setBacklogStatus] = React.useState<FederationBacklogStatus | null>(null);

  const loadNodes = React.useCallback(async () => {
    setLoadingNodes(true);
    try {
      const data = await ApiService.getFederationNodes();
      setNodes(data || []);
    } catch (err) {
      console.error("Failed to load federation nodes:", err);
    } finally {
      setLoadingNodes(false);
    }
  }, []);

  React.useEffect(() => {
    const isFederatedNode = normalizeExecutionMode(fedSettings.mode) !== 1 && (!!fedSettings.hubUrl || !!fedSettings.useTailscaleForHubConnection);
    if (!isFederatedNode) {
      setBacklogStatus(null);
      return;
    }

    const loadBacklog = () => {
      ApiService.getFederationBacklogStatus()
        .then((res: FederationBacklogStatus) => setBacklogStatus(res))
        .catch(() => setBacklogStatus(null));
    };

    loadBacklog();
    const interval = window.setInterval(loadBacklog, 10000);
    return () => window.clearInterval(interval);
  }, [fedSettings.mode, fedSettings.hubUrl, fedSettings.useTailscaleForHubConnection]);

  React.useEffect(() => {
    if (isHubMode(fedSettings.mode)) {
      loadNodes();
    }
  }, [fedSettings.mode, loadNodes]);

  const handleForceSync = async (nodeId: string) => {
    try {
      await ApiService.sendNodeCommand(nodeId, {
        commandType: 'ForceSync',
        targetId: nodeId
      });
      onNotify?.(`Force Sync command successfully sent to Node "${nodeId}".`, 'success');
    } catch (err: any) {
      console.error("Failed to send force sync command:", err);
      onNotify?.(`Failed to send force sync command: ${err.message}`);
    }
  };

  const handleNotifyUpdate = async (nodeId: string, nodeName: string) => {
    const label = nodeName || nodeId;
    const channel = getPreferredUpdateChannel();
    try {
      const result = await ApiService.notifyNodeUpdate(nodeId, channel);
      onNotify?.(result.message || `Update notify (${channel}) sent to "${label}".`, 'success');
    } catch (err: any) {
      console.error('Failed to notify node update:', err);
      onNotify?.(`Failed to notify update: ${err.message}`);
    }
  };

  const handleTriggerUpdate = async (nodeId: string, nodeName: string) => {
    const label = nodeName || nodeId;
    const channel = getPreferredUpdateChannel();
    const ok = window.confirm(
      `Offer ${channel} update to "${label}"?\n\n` +
      `Requires a package staged on the Hub depot (${channel}). The Node admin must still confirm Download & Install locally.`,
    );
    if (!ok) return;
    try {
      const result = await ApiService.triggerNodeUpdate(nodeId, channel);
      onNotify?.(result.message || `Update trigger (${channel}) sent to "${label}".`, 'success');
    } catch (err: any) {
      console.error('Failed to trigger node update:', err);
      onNotify?.(`Failed to trigger update: ${err.message}`);
    }
  };

  const handleResetSite = async (nodeId: string, nodeName: string) => {
    const label = nodeName || nodeId;
    const typed = window.prompt(
      `Reset site "${label}"?\n\n` +
      `Wipes local inventory on this Node. Hub enrollment is preserved - the site will reconnect automatically.\n` +
      `This is NOT Decouple or Delete Node.\n\n` +
      `Type the site name to confirm:`
    );
    if (typed !== label) {
      if (typed !== null) onNotify?.('Confirmation did not match the site name. Reset cancelled.');
      return;
    }
    try {
      const result = await ApiService.resetFederationSite(nodeId);
      onNotify?.(result.message || `Site "${label}" reset initiated.`, 'success');
      await loadNodes();
    } catch (err: any) {
      console.error('Failed to reset site:', err);
      onNotify?.(`Failed to reset site: ${err.message}`);
    }
  };

  const handleDeleteNode = async (nodeId: string, nodeName: string) => {
    const ok = await confirm({
      title: 'Remove node',
      message: `Are you sure you want to permanently remove Node "${nodeName || nodeId}" and delete all its synchronized devices and logs from the Hub?`,
      confirmLabel: 'Remove',
      confirmColor: 'error',
    });
    if (!ok) return;
    try {
      await ApiService.deleteFederationNode(nodeId);
      onNotify?.(`Node "${nodeName || nodeId}" removed successfully.`, 'success');
      await loadNodes();
    } catch (err: any) {
      console.error("Failed to delete node:", err);
      onNotify?.(`Failed to remove node: ${err.message}`);
    }
  };

  const [nodeEnrollString, setNodeEnrollString] = React.useState("");
  const [friendlyName, setFriendlyName] = React.useState("");
  const [client, setClient] = React.useState("");
  const [building, setBuilding] = React.useState("");
  const [room, setRoom] = React.useState("");
  const [enrollTab, setEnrollTab] = React.useState<'mtls' | 'legacy'>('mtls');
  const [isEnrolling, setIsEnrolling] = React.useState(false);
  const [enrollError, setEnrollError] = React.useState<string | null>(null);
  const [enrollStringMismatch, setEnrollStringMismatch] = React.useState<string | null>(null);
  const [tailscaleStatus, setTailscaleStatus] = React.useState<TailscaleStatus | null>(null);
  const [transportSummary, setTransportSummary] = React.useState<TailscaleTransportSummary | null>(null);
  const [reachability, setReachability] = React.useState<TailscaleReachabilityResult | null>(null);
  const [loadingTailscale, setLoadingTailscale] = React.useState(false);
  const [tailscaleDirty, setTailscaleDirty] = React.useState(false);
  const [testingReachability, setTestingReachability] = React.useState(false);

  const markTailscaleDirty = () => setTailscaleDirty(true);

  const loadTailscaleDiagnostics = React.useCallback(async () => {
    setLoadingTailscale(true);
    try {
      const [status, summary] = await Promise.all([
        ApiService.getTailscaleStatus(),
        ApiService.getTailscaleTransportSummary()
      ]);
      setTailscaleStatus(status);
      setTransportSummary(summary);
    } catch (err) {
      console.error('Failed to load Tailscale diagnostics:', err);
    } finally {
      setLoadingTailscale(false);
    }
  }, []);

  React.useEffect(() => {
    loadTailscaleDiagnostics();
  }, [loadTailscaleDiagnostics]);

  const handleTestReachability = async () => {
    setTestingReachability(true);
    try {
      const result = await ApiService.testTailscaleReachability({
        useTailscaleForHubConnection: fedSettings.useTailscaleForHubConnection,
        hubTailscaleMagicDns: fedSettings.hubTailscaleMagicDns,
        hubTailscaleIpv4: fedSettings.hubTailscaleIpv4,
      });
      setReachability(result);
    } catch (err: any) {
      setReachability({ anyReachable: false, summary: err.message || 'Reachability test failed.', probes: [] });
    } finally {
      setTestingReachability(false);
    }
  };

  const applyLocalTailscaleToHubIdentity = () => {
    if (!tailscaleStatus?.magicDnsName && !tailscaleStatus?.tailnetIpv4) return;
    setFedSettings({
      ...fedSettings,
      hubTailscaleMagicDns: tailscaleStatus.magicDnsName || fedSettings.hubTailscaleMagicDns,
      hubTailscaleIpv4: tailscaleStatus.tailnetIpv4 || fedSettings.hubTailscaleIpv4
    });
    markTailscaleDirty();
  };

  const handleSaveTailscale = async () => {
    await handleSaveFed();
    setTailscaleDirty(false);
  };

  const tailscaleReachable =
    !!tailscaleStatus?.connected || !!tailscaleStatus?.detectedViaInterface;

  const hubTailscaleIdentityConfigured =
    !!(fedSettings.hubTailscaleMagicDns?.trim() || fedSettings.hubTailscaleIpv4?.trim());

  const handleEnrollStringChange = (value: string) => {
    setNodeEnrollString(value);
    setEnrollStringMismatch(null);
    const host = parseEnrollmentHost(value);
    if (!host) return;

    const stringTransport = enrollmentTransportForHost(host);
    const wantsTailscale = !!fedSettings.useTailscaleForHubConnection;

    if (stringTransport === 'tailscale' && !wantsTailscale) {
      setEnrollStringMismatch(
        'This string uses a Tailscale host (.ts.net or 100.x). Enable "Use Tailscale for Hub Connection" below and save before enrolling  -  or ask your Hub admin for a Direct Network string.'
      );
    } else if (stringTransport === 'lan' && wantsTailscale) {
      setEnrollStringMismatch(
        'This looks like a Direct Network enrollment string, but Tailscale transport is enabled. Use a Tailscale (Remote) string from the Hub, or turn off the Tailscale toggle if the Node reaches the Hub without Tailscale.'
      );
    }
  };

  const handleMtlsEnroll = async () => {
    if (!nodeEnrollString || !friendlyName) {
      setEnrollError("Hub Connection String and Friendly Site Name are required.");
      return;
    }
    setIsEnrolling(true);
    setEnrollError(null);
    try {
      const res = await ApiService.enrollNodeMtls({
        enrollmentString: nodeEnrollString,
        friendlyName,
        client,
        building,
        room
      });
      onNotify?.(res.message || "Appliance enrolled successfully. Restarting...", 'success');
    } catch (err: any) {
      console.error(err);
      let message = err.message || "Enrollment handshake failed. Double check your Hub connection details.";
      if (/already enrolled/i.test(message)) {
        message += " On the Hub: Federation → delete the old site row (or wait until it shows OFFLINE), then generate a fresh enrollment string.";
      }
      setEnrollError(message);
    } finally {
      setIsEnrolling(false);
    }
  };

  const hubMode = isHubMode(fedSettings.mode);

  return (
    <Grid container spacing={3}>
      {hubMode && (
        <FederationTopologyPanel
          nodes={nodes}
          loadingNodes={loadingNodes}
          onRefresh={loadNodes}
          isAdmin={!!isAdmin}
          license={license}
          onForceSync={handleForceSync}
          onNotifyUpdate={handleNotifyUpdate}
          onTriggerUpdate={handleTriggerUpdate}
          onResetSite={handleResetSite}
          onDeleteNode={handleDeleteNode}
        />
      )}

      <Grid item xs={12}>
        <Paper
          variant="outlined"
          sx={{
            p: 4,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: "blur(20px)",
            border: fedDirty ? `2px solid ${theme.palette.primary.main}` : `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: `0 12px 24px ${alpha("#000", 0.2)}`
          }}
        >
          <Stack direction="row" justifyContent="space-between" alignItems="center" mb={4}>
            <Stack direction="row" alignItems="center" spacing={2}>
              <Box sx={{ p: 1.5, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1), border: `1px solid ${alpha(theme.palette.primary.main, 0.15)}` }}>
                <MigrationIcon color="primary" sx={{ fontSize: 24 }} />
              </Box>
              <Box>
                <Typography variant="subtitle1" fontWeight={900} sx={{ letterSpacing: -0.5, lineHeight: 1.1 }}>FLEET FEDERATION</Typography>
                <Typography variant="caption" color="text.secondary" sx={{ opacity: 0.7 }}>CONNECT MULTIPLE APPLIANCES INTO A UNIFIED FLEET</Typography>
              </Box>
            </Stack>
            <Button
              startIcon={<SaveIcon />}
              variant={fedDirty ? "contained" : "outlined"}
              onClick={handleSaveFed}
              disabled={savingFed || !fedDirty}
              sx={{ 
                fontWeight: 900, 
                px: 3, 
                borderRadius: 3,
                height: 42,
                boxShadow: fedDirty ? `0 8px 24px ${alpha(theme.palette.primary.main, 0.3)}` : 'none'
              }}
            >
              {savingFed ? "SAVING..." : "SAVE & RESTART"}
            </Button>
          </Stack>

          <Grid container spacing={5}>
            <Grid item xs={12} md={4}>
              <Typography variant="overline" color="primary" fontWeight={900} sx={{ opacity: 0.6, display: 'block', mb: 1 }}>APPLIANCE ROLE</Typography>
              <Typography variant="caption" display="block" mb={2} sx={{ opacity: 0.8 }}>Select how this instance interacts with others.</Typography>
              <TextField
                fullWidth size="small"
                select
                value={normalizeExecutionMode(fedSettings.mode)}
                onChange={(e) => { setFedSettings({ ...fedSettings, mode: parseInt(e.target.value) }); setFedDirty(true); }}
                SelectProps={{ native: true }}
                sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4) } }}
              >
                <option value={0}>Appliance (Standard Node)</option>
                <option value={1} disabled={!license?.allowsHub}>Central Hub (Fleet Master)</option>
              </TextField>
              {!license?.allowsHub && (
                <Typography variant="caption" color="primary" sx={{ mt: 1, display: 'block', fontWeight: 900, fontSize: '0.6rem' }}>
                  🔒 PRO OR HIGHER LICENSE REQUIRED FOR HUB MODE
                </Typography>
              )}
            </Grid>

            <Grid item xs={12} md={8}>
              {normalizeExecutionMode(fedSettings.mode) === 0 ? (
                fedSettings.hubUrl ? (
                  <Stack spacing={2.5}>
                    <Box sx={{ 
                      p: 3, 
                      borderRadius: 4, 
                      bgcolor: alpha(theme.palette.success.main, 0.04), 
                      border: '1px solid', 
                      borderColor: alpha(theme.palette.success.main, 0.15),
                      boxShadow: `0 8px 24px ${alpha(theme.palette.success.main, 0.05)}`
                    }}>
                      <Typography variant="overline" color="success.main" fontWeight={900} display="block" mb={1} sx={{ letterSpacing: 1.5 }}>
                        ✅ APPLIANCE ENROLLED & CONNECTED
                      </Typography>
                      <Typography variant="caption" display="block" mb={3} sx={{ opacity: 0.8, lineHeight: 1.6 }}>
                        This appliance is actively linked to the Central Hub. Telemetry, event logs, and settings governance are synced securely.
                      </Typography>
                      <Grid container spacing={2}>
                        <Grid item xs={12} sm={6}>
                          <TextField
                            fullWidth size="small"
                            label="HUB DIRECTOR URL"
                            value={fedSettings.hubUrl}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4) } }}
                          />
                        </Grid>
                        <Grid item xs={12} sm={6}>
                          <TextField
                            fullWidth size="small"
                            label="SITE FRIENDLY NAME"
                            value={fedSettings.nodeDisplayName || ' - '}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontWeight: 700 } }}
                          />
                        </Grid>
                        <Grid item xs={12} sm={6}>
                          <TextField
                            fullWidth size="small"
                            label="NODE UNIQUE ID"
                            value={fedSettings.nodeId || 'N/A'}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4), fontFamily: 'monospace' } }}
                          />
                        </Grid>
                        <Grid item xs={12} sm={4}>
                          <TextField
                            fullWidth size="small"
                            label="CLIENT / ORG"
                            value={fedSettings.client || ' - '}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4) } }}
                          />
                        </Grid>
                        <Grid item xs={12} sm={4}>
                          <TextField
                            fullWidth size="small"
                            label="BUILDING"
                            value={fedSettings.building || ' - '}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4) } }}
                          />
                        </Grid>
                        <Grid item xs={12} sm={4}>
                          <TextField
                            fullWidth size="small"
                            label="ROOM / RACK"
                            value={fedSettings.room || ' - '}
                            InputProps={{ readOnly: true, sx: { borderRadius: 2, bgcolor: alpha(theme.palette.background.default, 0.4) } }}
                          />
                        </Grid>
                      </Grid>
                    </Box>

                    {backlogStatus && (backlogStatus.backlogSeverity === 'Warning' || !backlogStatus.hubConnected || backlogStatus.fullSyncRequired) && (
                      <Paper variant="outlined" sx={{ p: 2, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.warning.main, 0.06), border: `1px solid ${alpha(theme.palette.warning.main, 0.25)}` }}>
                        <Typography variant="subtitle2" fontWeight={900} sx={{ color: 'warning.main', mb: 0.5 }}>
                          Federation Backlog Status
                        </Typography>
                        <Typography variant="body2" sx={{ mb: 1 }}>{backlogStatus.summary}</Typography>
                        <Typography variant="caption" color="text.secondary" display="block">
                          Devices queued: {backlogStatus.pendingDeviceChanges.toLocaleString()} / {backlogStatus.maxPendingDeviceChanges.toLocaleString()}
                          {' · '}Alerts pending: {backlogStatus.pendingAlerts}
                          {' · '}Anomalies pending: {backlogStatus.pendingEvents}
                          {' · '}Logs pending: {backlogStatus.pendingLogs}
                          {backlogStatus.deepSleep ? ' · Deep sleep active' : ''}
                          {backlogStatus.fullSyncRequired ? ' · Full snapshot scheduled on reconnect' : ''}
                        </Typography>
                      </Paper>
                    )}

                    {handleDecouple && (
                      <Box 
                        sx={{ 
                          p: 3, 
                          borderRadius: 4, 
                          bgcolor: alpha(theme.palette.error.main, 0.04), 
                          border: `1px solid ${alpha(theme.palette.error.main, 0.15)}`,
                          backdropFilter: "blur(10px)",
                          boxShadow: `0 8px 24px ${alpha(theme.palette.error.main, 0.05)}`
                        }}
                      >
                        <Typography variant="subtitle2" sx={{ fontWeight: 900, color: theme.palette.error.main, mb: 1, letterSpacing: -0.2 }}>
                          🚨 EMERGENCY BREAK-GLASS DECOUPLING
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2, fontWeight: 500, lineHeight: 1.5 }}>
                          If the central Hub is permanently offline or unreachable, use this escape hatch to demote this Node back to Standalone mode. This will clear the Hub URL and automatically promote all synchronized users to local accounts so you are not locked out.
                        </Typography>
                        <Button
                          variant="contained"
                          color="error"
                          onClick={handleDecouple}
                          disabled={savingFed}
                          sx={{ 
                            fontWeight: 900, 
                            px: 3, 
                            borderRadius: 2,
                            textTransform: 'none',
                            boxShadow: `0 4px 14px ${alpha(theme.palette.error.main, 0.25)}`
                          }}
                        >
                          {savingFed ? "DECOUPLING..." : "Break Glass & Decouple Node"}
                        </Button>
                      </Box>
                    )}
                  </Stack>
                ) : (
                  <Stack spacing={2.5}>
                    <Tabs
                      value={enrollTab}
                      onChange={(_, val) => setEnrollTab(val)}
                      textColor="primary"
                      indicatorColor="primary"
                      variant="fullWidth"
                      sx={{ 
                        borderBottom: 1, 
                        borderColor: alpha(theme.palette.divider, 0.1),
                        '& .MuiTab-root': { fontWeight: 900 }
                      }}
                    >
                      <Tab label="Sovereign mTLS" value="mtls" />
                      <Tab label="Manual Outbound" value="legacy" />
                    </Tabs>

                    {enrollTab === 'mtls' ? (
                      <Stack spacing={2.5}>
                        <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.5 }}>
                          Enroll securely using mTLS. Generate an enrollment connection string on the Central Hub
                          ({fedSettings.useTailscaleForHubConnection ? 'Tailscale (Remote)' : 'Direct Network'}),
                          paste it below, and specify the Node&apos;s friendly site metadata.
                        </Typography>

                        {fedSettings.useTailscaleForHubConnection && (
                          <Alert severity="info" sx={{ py: 0.5 }}>
                            Tailscale transport is enabled  -  paste the <strong>Tailscale (Remote)</strong> string from your Hub.
                            Ensure this Node is connected to Tailscale and Hub MagicDNS / tailnet IP is filled in below before enrolling.
                          </Alert>
                        )}

                        {enrollError && (
                          <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.error.main, 0.1), border: `1px solid ${alpha(theme.palette.error.main, 0.2)}` }}>
                            <Typography variant="caption" color="error.main" fontWeight={800}>{enrollError}</Typography>
                          </Box>
                        )}

                        {enrollStringMismatch && (
                          <Alert severity="warning" sx={{ py: 0.5 }}>
                            {enrollStringMismatch}
                          </Alert>
                        )}

                        <TextField
                          fullWidth size="small"
                          label="HUB CONNECTION STRING"
                          placeholder={fedSettings.useTailscaleForHubConnection
                            ? 'sa-enroll://hub.example.ts.net:5002?token=...'
                            : 'sa-enroll://192.168.x.x:5002?token=...'}
                          value={nodeEnrollString}
                          onChange={(e) => handleEnrollStringChange(e.target.value)}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />

                        <TextField
                          fullWidth size="small"
                          label="FRIENDLY SITE NAME"
                          placeholder="e.g. Chicago Branch"
                          value={friendlyName}
                          onChange={(e) => setFriendlyName(e.target.value)}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />

                        <TextField
                          fullWidth size="small"
                          label="CLIENT / ORGANIZATION"
                          placeholder="e.g. Acme Corp"
                          value={client}
                          onChange={(e) => setClient(e.target.value)}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />

                        <Grid container spacing={2}>
                          <Grid item xs={6}>
                            <TextField
                              fullWidth size="small"
                              label="BUILDING"
                              placeholder="e.g. Building B"
                              value={building}
                              onChange={(e) => setBuilding(e.target.value)}
                              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                            />
                          </Grid>
                          <Grid item xs={6}>
                            <TextField
                              fullWidth size="small"
                              label="ROOM / RACK"
                              placeholder="e.g. Server Room"
                              value={room}
                              onChange={(e) => setRoom(e.target.value)}
                              sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                            />
                          </Grid>
                        </Grid>

                        <Button
                          variant="contained"
                          color="primary"
                          startIcon={<SecurityIcon />}
                          onClick={handleMtlsEnroll}
                          disabled={isEnrolling}
                          sx={{ 
                            fontWeight: 900, 
                            py: 1.5, 
                            borderRadius: 2,
                            boxShadow: `0 8px 24px ${alpha(theme.palette.primary.main, 0.3)}`
                          }}
                        >
                          {isEnrolling ? "ENROLLING APPLIANCE..." : "ENROLL APPLIANCE VIA mTLS"}
                        </Button>
                      </Stack>
                    ) : (
                      <Stack spacing={2.5}>
                        <Typography variant="caption" color="text.secondary" sx={{ lineHeight: 1.5 }}>
                          Legacy static configuration. Provide the upstream Hub Director URL and static federation token manually.
                        </Typography>
                        <TextField
                          fullWidth size="small"
                          label="HUB DIRECTOR URL"
                          placeholder="https://hub.stacksatlas.local:5001"
                          value={fedSettings.hubUrl || ''}
                          onChange={(e) => { setFedSettings({ ...fedSettings, hubUrl: e.target.value }); setFedDirty(true); }}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />
                        <TextField
                          fullWidth size="small"
                          type="password"
                          label="FEDERATION TOKEN"
                          value={fedSettings.federationToken || ''}
                          onChange={(e) => { setFedSettings({ ...fedSettings, federationToken: e.target.value }); setFedDirty(true); }}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />
                      </Stack>
                    )}
                  </Stack>
                )
              ) : (
                <Box sx={{ 
                  p: 4, 
                  borderRadius: 4, 
                  bgcolor: alpha(theme.palette.primary.main, 0.04), 
                  border: '1px solid', 
                  borderColor: alpha(theme.palette.primary.main, 0.1),
                  boxShadow: `inset 0 2px 10px ${alpha("#000", 0.1)}`
                }}>
                  <Typography variant="overline" color="primary" fontWeight={900} display="block" mb={1} sx={{ letterSpacing: 1.5, opacity: 0.8 }}>HUB CONTROLLER ACTIVE</Typography>
                  <Typography variant="caption" display="block" mb={3} sx={{ opacity: 0.8, lineHeight: 1.6, maxWidth: 600 }}>
                    Generate a fleet enrollment token below. Managed nodes must provide this token to securely register with your Hub controller. 
                    Tokens follow a <strong>show-once</strong> security model for maximum isolation.
                  </Typography>
                  <Stack direction="row" spacing={2} alignItems="center">
                    <TextField
                      fullWidth size="small"
                      value={fedSettings.federationToken ? `${fedSettings.federationToken.substring(0, 4)}${'•'.repeat(16)}` : 'NO TOKEN CONFIGURED'}
                      InputProps={{ 
                        readOnly: true,
                        sx: { 
                          fontFamily: 'monospace', 
                          fontWeight: 800, 
                          fontSize: '0.9rem',
                          letterSpacing: 2,
                          bgcolor: alpha(theme.palette.background.default, 0.5),
                          borderRadius: 2
                        }
                      }}
                    />
                    <Button
                      variant="outlined"
                      startIcon={<CopyIcon />}
                      onClick={() => {
                        if (fedSettings.federationToken) {
                          copyToClipboard(fedSettings.federationToken);
                        }
                      }}
                      disabled={!fedSettings.federationToken}
                      sx={{ fontWeight: 900, height: 40, px: 3, borderRadius: 2 }}
                    >
                      COPY
                    </Button>
                    {isAdmin && (
                    <Button
                      variant="contained"
                      color="warning"
                      onClick={() => {
                        const newToken = crypto.randomUUID().replace(/-/g, '').substring(0, 24);
                        setFedSettings({ ...fedSettings, federationToken: newToken });
                        setFedDirty(true);
                        setGeneratedFedToken(newToken);
                      }}
                      sx={{ fontWeight: 900, height: 40, px: 3, borderRadius: 2 }}
                    >
                      REGEN
                    </Button>
                    )}
                  </Stack>
                  {generatedFedToken && (
                    <Box sx={{ mt: 3, p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.warning.main, 0.1), border: `1px solid ${alpha(theme.palette.warning.main, 0.2)}` }}>
                      <Typography variant="caption" color="warning.main" fontWeight={900} sx={{ display: 'block', mb: 0.5 }}>NEW FEDERATION TOKEN GENERATED:</Typography>
                      <Typography sx={{ fontFamily: 'monospace', fontWeight: 900, fontSize: '1rem', letterSpacing: 1 }}>{generatedFedToken}</Typography>
                      <Typography variant="caption" sx={{ mt: 1, display: 'block', opacity: 0.7 }}>Save this now. It will be masked on the next reload.</Typography>
                    </Box>
                  )}
                  <Typography variant="caption" color="text.secondary" sx={{ mt: 2.5, opacity: 0.6, fontStyle: 'italic', display: 'flex', alignItems: 'center', gap: 1 }}>
                    <SecurityIcon sx={{ fontSize: 14 }} /> ⚠️ Regenerating will invalidate the current enrollment secret. Existing nodes will require manual re-enrollment.
                  </Typography>

                  {isAdmin ? (
                  <Box sx={{ mt: 3, pt: 3, borderTop: `1px solid ${alpha(theme.palette.divider, 0.1)}` }}>
                    <Typography variant="overline" color="secondary" fontWeight={900} display="block" mb={1}>
                      Sovereign mTLS Enrollment
                    </Typography>
                    <Typography variant="caption" color="text.secondary" display="block" mb={2} sx={{ lineHeight: 1.5 }}>
                      Generate a one-time connection string for Edge Nodes. Choose Direct Network for routable Hub reachability, or Tailscale (Remote) for tailnet-only sites.
                    </Typography>
                    <EnrollmentStringPanel
                      compact
                      tailscaleConfiguredHint={hubTailscaleIdentityConfigured}
                      onCopied={(text) => copyToClipboard(text)}
                      onError={(message) => onNotify?.(message)}
                    />
                  </Box>
                  ) : (
                    <Typography variant="caption" color="text.secondary" sx={{ mt: 3, display: 'block', fontStyle: 'italic' }}>
                      Enrollment string generation and fleet management require Administrator access.
                    </Typography>
                  )}
                </Box>
              )}
            </Grid>
          </Grid>
        </Paper>
      </Grid>

      <Grid item xs={12}>
        <Paper
          variant="outlined"
          sx={{
            p: 4,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: 'blur(20px)',
            border: tailscaleDirty
              ? `2px solid ${theme.palette.primary.main}`
              : `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            boxShadow: tailscaleDirty ? `0 8px 24px ${alpha(theme.palette.primary.main, 0.15)}` : undefined,
          }}
        >
          <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
            <Box>
              <Typography variant="subtitle1" fontWeight={900}>Remote Federation via Tailscale</Typography>
              <Typography variant="caption" color="text.secondary">
                Federation control plane over tailscale0  -  discovery stays on physical NICs.
              </Typography>
            </Box>
            <Stack direction="row" spacing={1} alignItems="center">
              <Button
                startIcon={<SaveIcon />}
                variant={tailscaleDirty ? 'contained' : 'outlined'}
                size="small"
                onClick={handleSaveTailscale}
                disabled={savingFed || !tailscaleDirty}
                sx={{ fontWeight: 900, borderRadius: 2 }}
              >
                {savingFed ? 'SAVING...' : 'SAVE & RESTART'}
              </Button>
              <IconButton onClick={loadTailscaleDiagnostics} disabled={loadingTailscale} size="small">
                <RefreshIcon />
              </IconButton>
            </Stack>
          </Stack>

          {loadingTailscale && !tailscaleStatus ? (
            <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}><CircularProgress size={28} /></Box>
          ) : (
            <Stack spacing={2.5}>
              {!hubMode && <RemoteSiteChecklist />}
              {hubMode && (
                <TailscaleAclHelpPanel onCopied={(text) => copyToClipboard(text)} />
              )}
              {tailscaleStatus?.keyExpired && (
                <Alert severity="error">Tailscale node key is expired. Re-authenticate this appliance in your Tailscale admin console.</Alert>
              )}
              {tailscaleStatus?.keyExpiringSoon && !tailscaleStatus.keyExpired && (
                <Alert severity="warning">Tailscale node key expires soon ({tailscaleStatus.keyExpiryUtc ? new Date(tailscaleStatus.keyExpiryUtc).toLocaleString() : 'unknown'}).</Alert>
              )}
              {!tailscaleReachable && (
                <Alert severity="warning">
                  <Typography variant="body2" fontWeight={700} gutterBottom>Tailscale not detected on this host</Typography>
                  <Typography variant="body2" component="div">
                    Install Tailscale and ensure the daemon is running (e.g. <code>sudo systemctl enable --now tailscaled</code>, then <code>sudo tailscale up</code>).
                    Docker nodes need <code>network_mode: host</code> so <strong>tailscale0</strong> is visible inside the container,
                    or mount <code>/var/run/tailscale/tailscaled.sock</code> and set <code>STACKSATLAS_TAILSCALE_SOCKET</code> for CLI diagnostics.
                  </Typography>
                </Alert>
              )}
              {tailscaleStatus?.cliInstalled && !tailscaleStatus?.connected && !tailscaleStatus?.detectedViaInterface && (
                <Alert severity="warning">
                  Tailscale CLI is present but this node is not connected. Run <code>sudo tailscale up</code> on the host and refresh.
                </Alert>
              )}
              {tailscaleStatus?.detectedViaInterface && !tailscaleStatus?.cliInstalled && (
                <Alert severity="info">
                  Tailscale detected via host <strong>tailscale0</strong> (Docker host-network). MagicDNS and key-expiry diagnostics require the CLI on the host or a socket mount.
                </Alert>
              )}
              {tailscaleStatus?.cliInstalled && tailscaleReachable && tailscaleStatus?.magicDnsName && (
                <Alert severity="success">Tailscale connected  -  MagicDNS: {tailscaleStatus.magicDnsName}</Alert>
              )}

              <Grid container spacing={2}>
                <Grid item xs={12} md={4}>
                  <Typography variant="overline" color="text.secondary">Local Tailscale</Typography>
                  <Typography variant="body2" fontWeight={700}>
                    {tailscaleStatus?.connected ? 'Connected' : tailscaleStatus?.cliInstalled ? 'Installed (offline)' : 'Not detected'}
                  </Typography>
                  <Typography variant="caption" display="block" sx={{ fontFamily: 'monospace' }}>
                    {tailscaleStatus?.magicDnsName || ' - '} · {tailscaleStatus?.tailnetIpv4 || ' - '}
                  </Typography>
                </Grid>
                <Grid item xs={12} md={8}>
                  <Typography variant="overline" color="text.secondary">Transport Summary</Typography>
                  <Typography variant="body2">
                    Discovery: {transportSummary?.discoveryInterface || ' - '} | Federation: {transportSummary?.federationInterface || ' - '}
                  </Typography>
                </Grid>
              </Grid>

              {hubMode ? (
                <Grid container spacing={2}>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth size="small"
                      label="HUB MAGICDNS HOSTNAME"
                      placeholder="hub.example.ts.net"
                      value={fedSettings.hubTailscaleMagicDns || ''}
                      onChange={(e) => { setFedSettings({ ...fedSettings, hubTailscaleMagicDns: e.target.value }); markTailscaleDirty(); }}
                      sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                    />
                  </Grid>
                  <Grid item xs={12} md={6}>
                    <TextField
                      fullWidth size="small"
                      label="HUB TAILNET IPv4"
                      placeholder="100.x.x.x"
                      value={fedSettings.hubTailscaleIpv4 || ''}
                      onChange={(e) => { setFedSettings({ ...fedSettings, hubTailscaleIpv4: e.target.value }); markTailscaleDirty(); }}
                      sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                    />
                  </Grid>
                  <Grid item xs={12}>
                    <Button variant="outlined" size="small" onClick={applyLocalTailscaleToHubIdentity} disabled={!tailscaleStatus?.magicDnsName && !tailscaleStatus?.tailnetIpv4}>
                      Use This Host's Tailscale Identity
                    </Button>
                  </Grid>
                </Grid>
              ) : (
                <Stack spacing={2}>
                  <FormControlLabel
                    control={
                      <Switch
                        checked={!!fedSettings.useTailscaleForHubConnection}
                        onChange={(e) => { setFedSettings({ ...fedSettings, useTailscaleForHubConnection: e.target.checked }); markTailscaleDirty(); }}
                      />
                    }
                    label="Use Tailscale for Hub Connection"
                  />
                  {fedSettings.useTailscaleForHubConnection && (
                    <Grid container spacing={2}>
                      <Grid item xs={12}>
                        <Typography variant="caption" color="text.secondary">
                          Enter the Hub&apos;s tailnet identity (from Hub Settings → Federation). Required for overlay transport when LAN Hub URL is unreachable.
                        </Typography>
                      </Grid>
                      <Grid item xs={12} md={6}>
                        <TextField
                          fullWidth size="small"
                          label="HUB MAGICDNS HOSTNAME"
                          placeholder="kstacks-desktop.tail77671f.ts.net"
                          value={fedSettings.hubTailscaleMagicDns || ''}
                          onChange={(e) => { setFedSettings({ ...fedSettings, hubTailscaleMagicDns: e.target.value }); markTailscaleDirty(); }}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />
                      </Grid>
                      <Grid item xs={12} md={6}>
                        <TextField
                          fullWidth size="small"
                          label="HUB TAILNET IPv4"
                          placeholder="100.74.74.30"
                          value={fedSettings.hubTailscaleIpv4 || ''}
                          onChange={(e) => { setFedSettings({ ...fedSettings, hubTailscaleIpv4: e.target.value }); markTailscaleDirty(); }}
                          sx={{ '& .MuiOutlinedInput-root': { borderRadius: 2 } }}
                        />
                      </Grid>
                    </Grid>
                  )}
                </Stack>
              )}

              <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap">
                <Button
                  variant="contained"
                  size="small"
                  onClick={handleTestReachability}
                  disabled={testingReachability}
                  startIcon={testingReachability ? <CircularProgress size={16} color="inherit" /> : undefined}
                >
                  Test Hub Reachability via Tailscale
                </Button>
                {reachability && (
                  <Typography variant="caption" color={reachability.anyReachable ? 'success.main' : 'warning.main'}>
                    {reachability.summary}
                  </Typography>
                )}
              </Stack>
              <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
                Probes use the values above (saved settings not required). Save &amp; Restart still applies transport changes to federation sync.
              </Typography>
            </Stack>
          )}
        </Paper>
      </Grid>
    </Grid>
  );
};

export default FederationTab;
