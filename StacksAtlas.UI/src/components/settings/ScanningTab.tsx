import React, { useEffect, useState } from 'react';
import {
  Box,
  Grid,
  Paper,
  Stack,
  Typography,
  TextField,
  Button,
  Switch,
  Slider,
  FormControlLabel,
  IconButton as MuiIconButton,
  Select,
  MenuItem,
  FormControl,
  InputLabel,
  Chip,
  Divider,
  Tooltip,
  alpha,
  useTheme
} from '@mui/material';
import {
  Terminal as TerminalIcon,
  Save as SaveIcon,
  Delete as DeleteIcon,
  Add as AddIcon,
  Router as RouterIcon,
  WarningAmber as WarningIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import type { NetworkScope, NetworkInterfaceConfig, NetworkInterfaceInfo, NetworkRole, NetworkRoutingDiagnostic } from '../../services/apiService';

/** Resolve federated node fields regardless of API casing. */
const readNodeField = (node: any, camel: string, pascal: string) => node?.[camel] ?? node?.[pascal];

const getNodeStatusOnline = (node: any) =>
  (readNodeField(node, 'status', 'Status') ?? '').toString().toUpperCase() === 'ONLINE';

const getNodeLocationHint = (node: any) => {
  const parts = [
    readNodeField(node, 'client', 'Client'),
    readNodeField(node, 'building', 'Building'),
    readNodeField(node, 'room', 'Room'),
  ].filter(Boolean);
  return parts.length ? parts.join(' · ') : null;
};

/** Prefer operator-friendly name from Hub; fall back to location then id. */
const getNodeDisplayName = (node: any) => {
  if (!node) return 'Unknown Node';
  const rawName = (readNodeField(node, 'name', 'Name') ?? '').toString().trim();
  if (rawName) return rawName;
  return getNodeLocationHint(node) || readNodeField(node, 'id', 'Id') || 'Unnamed Node';
};

const normalizeFederationNode = (node: any) => ({
  ...node,
  id: readNodeField(node, 'id', 'Id'),
  name: readNodeField(node, 'name', 'Name'),
  status: readNodeField(node, 'status', 'Status'),
  client: readNodeField(node, 'client', 'Client'),
  building: readNodeField(node, 'building', 'Building'),
  room: readNodeField(node, 'room', 'Room'),
  scanSettingsJson: readNodeField(node, 'scanSettingsJson', 'ScanSettingsJson'),
});

const normalizeInterface = (iface: any): NetworkInterfaceInfo => ({
  id: (iface?.id ?? iface?.Id ?? '').toString(),
  name: (iface?.name ?? iface?.Name ?? '').toString(),
  description: (iface?.description ?? iface?.Description ?? '').toString(),
  ipAddress: (iface?.ipAddress ?? iface?.IpAddress ?? '').toString(),
  subnetMask: (iface?.subnetMask ?? iface?.SubnetMask ?? '').toString(),
  gatewayAddress: iface?.gatewayAddress ?? iface?.GatewayAddress ?? undefined,
  type: (iface?.type ?? iface?.Type ?? '').toString(),
  speed: Number(iface?.speed ?? iface?.Speed ?? 0),
  status: (iface?.status ?? iface?.Status ?? '').toString(),
});

const parseInterfacesFromScanSettings = (json?: string | null): NetworkInterfaceInfo[] => {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    const raw = parsed.interfaces ?? parsed.Interfaces ?? [];
    return (Array.isArray(raw) ? raw : []).map(normalizeInterface).filter(i => i.id);
  } catch {
    return [];
  }
};

const normalizeInterfaceConfig = (raw: any): NetworkInterfaceConfig => ({
  interfaceId: (raw?.interfaceId ?? raw?.InterfaceId ?? '').toString(),
  roles: (raw?.roles ?? raw?.Roles ?? []).map((r: string) => r as NetworkRole),
  defaultVlanTag: raw?.defaultVlanTag ?? raw?.DefaultVlanTag ?? null,
});

const parseInterfaceConfigsFromNetwork = (net: any): NetworkInterfaceConfig[] => {
  const configs = net?.interfaceConfigs ?? net?.InterfaceConfigs;
  if (Array.isArray(configs) && configs.length > 0)
    return configs.map(normalizeInterfaceConfig).filter(c => c.interfaceId);
  const mappings = net?.interfaceRoleMappings ?? net?.InterfaceRoleMappings ?? [];
  const byId = new Map<string, NetworkRole[]>();
  (mappings as any[]).forEach(m => {
    const id = (m.interfaceId ?? m.InterfaceId ?? '').toString();
    const role = (m.role ?? m.Role ?? '').toString();
    if (!id || !role) return;
    const list = byId.get(id) ?? [];
    if (!list.includes(role as NetworkRole)) list.push(role as NetworkRole);
    byId.set(id, list);
  });
  return Array.from(byId.entries()).map(([interfaceId, roles]) => ({ interfaceId, roles, defaultVlanTag: null as string | null }));
};

const getAdapterVlanDefault = (interfaceId: string | null | undefined, configs: NetworkInterfaceConfig[]) => {
  if (!interfaceId) return null;
  return configs.find(c => c.interfaceId === interfaceId)?.defaultVlanTag ?? null;
};

const parseScanSettingsJson = (json?: string | null) => {
  const empty = {
    subnets: [] as NetworkScope[],
    stability: {
      offlineStrikeThreshold: 3,
      enableInstantRecovery: true,
      maxParallelPings: 256,
      pingTimeoutMs: 200,
      pingRetries: 0,
      enableNmapDeepScan: false,
      maxConcurrentDeepScans: 2,
    },
    refreshInterval: 60,
    interfaceConfigs: [] as NetworkInterfaceConfig[],
    interfaces: [] as NetworkInterfaceInfo[],
    scanEngine: null as { nmapAvailable?: boolean; pcapAvailable?: boolean; pcapLabel?: string } | null,
  };
  if (!json) return empty;
  try {
    const parsed = JSON.parse(json);
    const net = parsed.Network || parsed.network || {};
    const poll = parsed.Polling || parsed.polling || {};
    const engine = parsed.scanEngine ?? parsed.ScanEngine ?? null;
    const rawSubs = net.Subnets || net.subnets || [];
    const normalizedSubs = rawSubs.map((s: any) =>
      typeof s === 'string'
        ? { cidr: s, interfaceId: null, vlanTag: null, enableArp: true, enablePing: true, enableMdns: true, enableUpnp: true }
        : {
            ...s,
            interfaceId: s.interfaceId ?? s.InterfaceId ?? null,
            vlanTag: s.vlanTag ?? s.VlanTag ?? null,
          }
    );
    return {
      subnets: normalizedSubs,
      stability: {
        offlineStrikeThreshold: net.OfflineStrikeThreshold ?? net.offlineStrikeThreshold ?? 3,
        enableInstantRecovery: net.EnableInstantRecovery ?? net.enableInstantRecovery ?? true,
        maxParallelPings: net.MaxParallelPings ?? net.maxParallelPings ?? 256,
        pingTimeoutMs: net.PingTimeoutMs ?? net.pingTimeoutMs ?? 200,
        pingRetries: net.PingRetries ?? net.pingRetries ?? 0,
        enableNmapDeepScan: net.EnableNmapDeepScan ?? net.enableNmapDeepScan ?? false,
        maxConcurrentDeepScans: net.MaxConcurrentDeepScans ?? net.maxConcurrentDeepScans ?? 2,
      },
      refreshInterval: poll.RefreshIntervalSeconds ?? poll.refreshIntervalSeconds ?? 60,
      interfaceConfigs: parseInterfaceConfigsFromNetwork(net),
      interfaces: parseInterfacesFromScanSettings(json),
      scanEngine: engine ? {
        nmapAvailable: engine.nmapAvailable ?? engine.NmapAvailable,
        pcapAvailable: engine.pcapAvailable ?? engine.PcapAvailable,
        pcapLabel: engine.pcapLabel ?? engine.PcapLabel ?? 'LibPcap',
      } : null,
    };
  } catch {
    return empty;
  }
};

interface ScanningTabProps {
  subnets: NetworkScope[];
  interfaceConfigs: NetworkInterfaceConfig[];
  handleUpdateSubnet: (index: number, value: NetworkScope) => void;
  handleDeleteSubnet: (index: number) => void;
  handleAddSubnet: () => void;
  isDirty: boolean;
  stabilityDirty: boolean;
  handleApplyNetworkChanges: () => void;
  saving: boolean;
  savingStability: boolean;
  setSavingStability: (saving: boolean) => void;
  stabilitySettings: any;
  setStabilitySettings: (settings: any) => void;
  setStabilityDirty: (dirty: boolean) => void;
  refreshInterval: number;
  setRefreshInterval: (interval: number) => void;
  saveRefreshInterval: (interval: number) => void;
  isHub: boolean;
  systemStatus: any;
  onNotify?: (message: string, severity?: 'success' | 'error') => void;
}

const ScanningTab: React.FC<ScanningTabProps> = ({
  subnets,
  interfaceConfigs,
  handleUpdateSubnet,
  handleDeleteSubnet,
  handleAddSubnet,
  isDirty,
  stabilityDirty,
  handleApplyNetworkChanges,
  saving,
  savingStability,
  setSavingStability,
  stabilitySettings,
  setStabilitySettings,
  setStabilityDirty,
  refreshInterval,
  setRefreshInterval,
  saveRefreshInterval,
  isHub,
  systemStatus,
  onNotify,
}) => {
  const theme = useTheme();
  const [interfaces, setInterfaces] = useState<NetworkInterfaceInfo[]>([]);
  const [routingDiagnostics, setRoutingDiagnostics] = useState<NetworkRoutingDiagnostic | null>(null);
  const [showRoutingDetails, setShowRoutingDetails] = useState(false);

  // --- Hub-mode States ---
  const [nodes, setNodes] = useState<any[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState<string>("all");
  const [localSubnets, setLocalSubnets] = useState<NetworkScope[]>([]);
  const [localStabilitySettings, setLocalStabilitySettings] = useState<any>({
    offlineStrikeThreshold: 3,
    enableInstantRecovery: true,
    maxParallelPings: 256,
    pingTimeoutMs: 200,
    pingRetries: 0,
    enableNmapDeepScan: false,
    maxConcurrentDeepScans: 2
  });
  const [localRefreshInterval, setLocalRefreshInterval] = useState<number>(60);
  const [localInterfaceConfigs, setLocalInterfaceConfigs] = useState<NetworkInterfaceConfig[]>([]);
  const [nodeInterfaces, setNodeInterfaces] = useState<NetworkInterfaceInfo[]>([]);
  const [nodeScanEngine, setNodeScanEngine] = useState<{ nmapAvailable?: boolean; pcapAvailable?: boolean; pcapLabel?: string } | null>(null);
  const [localDirty, setLocalDirty] = useState<boolean>(false);
  const [localSaving, setLocalSaving] = useState<boolean>(false);

  // Load network interfaces (standalone) and federation nodes (hub)
  useEffect(() => {
    if (!isHub) {
      ApiService.getInterfaces()
        .then((res: NetworkInterfaceInfo[]) => setInterfaces(res || []))
        .catch(err => console.error("Failed to load interfaces:", err));

      ApiService.getNetworkRoutingDiagnostics()
        .then((res: NetworkRoutingDiagnostic) => setRoutingDiagnostics(res))
        .catch(err => console.error("Failed to load routing diagnostics:", err));
    }

    if (isHub) {
      ApiService.getFederationNodes()
        .then(res => setNodes((res || []).map(normalizeFederationNode)))
        .catch(err => console.error("Failed to load federated nodes:", err));
    }
  }, [isHub]);

  // Refresh selected node scan settings while Hub Scanning tab is open
  useEffect(() => {
    if (!isHub || selectedNodeId === 'all') return;

    const refreshNodes = () => {
      ApiService.getFederationNodes()
        .then(res => setNodes((res || []).map(normalizeFederationNode)))
        .catch(err => console.error("Failed to refresh federated nodes:", err));
    };

    refreshNodes();
    const interval = setInterval(refreshNodes, 10000);
    return () => clearInterval(interval);
  }, [isHub, selectedNodeId]);

  // Handle selected Node's config initialization in Hub mode
  useEffect(() => {
    if (isHub) {
      if (selectedNodeId === "all") {
        setLocalSubnets([]);
        setLocalStabilitySettings({
          offlineStrikeThreshold: 3, enableInstantRecovery: true,
          maxParallelPings: 256, pingTimeoutMs: 200, pingRetries: 0,
          enableNmapDeepScan: false, maxConcurrentDeepScans: 2
        });
        setLocalRefreshInterval(60);
        setLocalInterfaceConfigs([]);
        setNodeInterfaces([]);
        setNodeScanEngine(null);
        setLocalDirty(false);
      } else {
        const node = nodes.find(n => n.id === selectedNodeId);
        const parsed = parseScanSettingsJson(node?.scanSettingsJson);
        setLocalSubnets(parsed.subnets);
        setLocalStabilitySettings(parsed.stability);
        setLocalRefreshInterval(parsed.refreshInterval);
        setLocalInterfaceConfigs(parsed.interfaceConfigs);
        setNodeInterfaces(parsed.interfaces);
        setNodeScanEngine(parsed.scanEngine);
        setLocalDirty(false);
      }
    }
  }, [selectedNodeId, nodes, isHub]);

  // --- Dynamic Binding Helpers ---
  const selectedNode = nodes.find(n => n.id === selectedNodeId);
  const activeSubnets = isHub ? localSubnets : subnets;
  const activeStability = isHub ? localStabilitySettings : stabilitySettings;
  const activeRefreshInterval = isHub ? localRefreshInterval : refreshInterval;
  const activeInterfaceConfigs = isHub ? localInterfaceConfigs : interfaceConfigs;
  const activeInterfaces = isHub ? nodeInterfaces : interfaces;
  const activeDirty = isHub ? localDirty : (isDirty || stabilityDirty);
  const activeSaving = isHub ? localSaving : (saving || savingStability);

  const handleUpdateSubnetLocal = (index: number, val: NetworkScope) => {
    if (isHub) {
      const newSubs = [...localSubnets];
      newSubs[index] = val;
      setLocalSubnets(newSubs);
      setLocalDirty(true);
    } else {
      handleUpdateSubnet(index, val);
    }
  };

  const handleDeleteSubnetLocal = (index: number) => {
    if (isHub) {
      setLocalSubnets(localSubnets.filter((_, i) => i !== index));
      setLocalDirty(true);
    } else {
      handleDeleteSubnet(index);
    }
  };

  const handleAddSubnetLocal = () => {
    if (isHub) {
      setLocalSubnets([...localSubnets, { cidr: '', interfaceId: null, vlanTag: null, enableArp: true, enablePing: true, enableMdns: true, enableUpnp: true }]);
      setLocalDirty(true);
    } else {
      handleAddSubnet();
    }
  };

  const handleStabilityChange = (field: string, val: any) => {
    if (isHub) {
      setLocalStabilitySettings({ ...localStabilitySettings, [field]: val });
      setLocalDirty(true);
    } else {
      setStabilitySettings({ ...stabilitySettings, [field]: val });
      setStabilityDirty(true);
    }
  };

  const handleIntervalChange = (val: number) => {
    if (isHub) {
      setLocalRefreshInterval(val);
      setLocalDirty(true);
    } else {
      setRefreshInterval(val);
      setStabilityDirty(true);
    }
  };

  const handleCommitChanges = async () => {
    if (isHub) {
      setLocalSaving(true);
      try {
        const existingParsed = selectedNodeId !== 'all'
          ? parseScanSettingsJson(selectedNode?.scanSettingsJson)
          : { interfaces: [] as NetworkInterfaceInfo[], scanEngine: null, interfaceConfigs: [] as NetworkInterfaceConfig[] };

        const combined = {
          network: {
            subnets: localSubnets,
            interfaceConfigs: existingParsed.interfaceConfigs,
            pingTimeoutMs: localStabilitySettings.pingTimeoutMs,
            pingRetries: localStabilitySettings.pingRetries,
            maxParallelPings: localStabilitySettings.maxParallelPings,
            offlineStrikeThreshold: localStabilitySettings.offlineStrikeThreshold,
            enableInstantRecovery: localStabilitySettings.enableInstantRecovery,
            enableNmapDeepScan: localStabilitySettings.enableNmapDeepScan,
            maxConcurrentDeepScans: localStabilitySettings.maxConcurrentDeepScans
          },
          polling: { refreshIntervalSeconds: localRefreshInterval, intervalSeconds: localRefreshInterval },
          interfaces: existingParsed.interfaces,
          scanEngine: existingParsed.scanEngine,
        };
        await ApiService.updateFederationNode(selectedNodeId, { scanSettingsJson: JSON.stringify(combined) });
        onNotify?.(selectedNodeId === "all"
          ? "Scanning configurations broadcasted for all active nodes."
          : `Scanning configurations saved for Node: ${getNodeDisplayName(selectedNode)}.`, 'success');
        const refreshedNodes = await ApiService.getFederationNodes();
        setNodes((refreshedNodes || []).map(normalizeFederationNode));
        setLocalDirty(false);
      } catch (err: any) {
        onNotify?.(`Configuration sync failed: ${err.message}`);
      } finally {
        setLocalSaving(false);
      }
    } else {
      await handleApplyNetworkChanges();
      if (stabilityDirty) {
        setSavingStability(true);
        try {
          await saveRefreshInterval(refreshInterval);
          setStabilityDirty(false);
        } finally { setSavingStability(false); }
      }
    }
  };

  const cardBg = alpha(theme.palette.background.paper, 0.8);
  const cardBorder = `1px solid ${alpha(theme.palette.divider, 0.1)}`;

  const localMissingDeps: string[] = systemStatus?.missingDependencies ?? [];
  const localNmapReady = !localMissingDeps.some((d: string) => d.toLowerCase().includes('nmap'));
  const localPcapReady = !localMissingDeps.some((d: string) => d.toLowerCase().includes('pcap'));
  const localPcapLabel = localMissingDeps.some((d: string) => d.toLowerCase().includes('npcap')) ? 'NPCAP DRIVER' : 'LIBPCAP';

  const hubScanEngineReady = isHub && selectedNodeId !== 'all' && nodeScanEngine;
  const showScanEnginePanel = (!isHub && systemStatus) || !!hubScanEngineReady;

  const scopeControlSx = {
    '& .MuiOutlinedInput-root': {
      borderRadius: 1.5,
      bgcolor: alpha(theme.palette.background.paper, 0.5),
      fontSize: '0.72rem',
      minHeight: 40,
    },
    '& .MuiInputLabel-root': { fontSize: '0.65rem', fontWeight: 700 },
    '& .MuiSelect-select': { py: 1 },
  };

  return (
    <Stack spacing={2}>
      {/* GLOBAL GOVERNANCE HEADER */}
      <Paper
        variant="outlined"
        sx={{
          p: 2, px: 3, borderRadius: 4,
          bgcolor: cardBg, backdropFilter: "blur(20px)",
          border: activeDirty ? `2px solid ${theme.palette.primary.main}` : cardBorder,
          boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`,
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          flexWrap: 'wrap', gap: 2, minHeight: 72
        }}
      >
        <Stack direction="row" alignItems="center" spacing={2} sx={{ flex: '1 1 240px', minWidth: 0 }}>
          <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1), display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
            <TerminalIcon color="primary" sx={{ fontSize: 20 }} />
          </Box>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="subtitle2" fontWeight={900} sx={{ lineHeight: 1.2, letterSpacing: 0.2 }}>SCANNING & DISCOVERY</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ opacity: 0.7, display: 'block', mt: 0.25, lineHeight: 1.2 }}>
              {isHub ? "FEDERATED FLEET ENGINE GOVERNANCE" : "MULTI-NIC DISCOVERY ENGINE"}
            </Typography>
          </Box>
        </Stack>

        <Stack direction="row" alignItems="center" spacing={2} sx={{ flex: '0 0 auto', flexWrap: 'wrap', gap: 1.5 }}>
          {isHub && (
            <FormControl
              size="small"
              sx={{
                minWidth: { xs: '100%', sm: 300 },
                width: { xs: '100%', sm: 'auto' },
                '& .MuiInputBase-root': { height: 40 },
                '& .MuiInputLabel-root': { fontWeight: 700, fontSize: '0.65rem', letterSpacing: 1 },
              }}
            >
              <InputLabel id="node-selector-label">SELECT TARGET NODE</InputLabel>
              <Select
                labelId="node-selector-label"
                id="node-selector"
                value={selectedNodeId}
                onChange={(e) => setSelectedNodeId(e.target.value)}
                label="SELECT TARGET NODE"
                renderValue={(value) => {
                  if (value === 'all') {
                    return (
                      <Typography component="span" sx={{ fontWeight: 700, fontSize: '0.72rem', color: 'primary.main' }}>
                        Apply to All Active Fleet Nodes
                      </Typography>
                    );
                  }
                  const node = nodes.find(n => n.id === value);
                  if (!node) return value;
                  const online = getNodeStatusOnline(node);
                  return (
                    <Stack direction="row" alignItems="center" spacing={1} sx={{ overflow: 'hidden', width: '100%' }}>
                      <Typography component="span" noWrap sx={{ fontWeight: 700, fontSize: '0.72rem', flex: 1, minWidth: 0 }}>
                        {getNodeDisplayName(node)}
                      </Typography>
                      <Typography component="span" sx={{ fontWeight: 700, fontSize: '0.62rem', opacity: 0.75, flexShrink: 0 }}>
                        {online ? '🟢 ONLINE' : '🔴 OFFLINE'}
                      </Typography>
                    </Stack>
                  );
                }}
                sx={{
                  borderRadius: 2.5,
                  bgcolor: alpha(theme.palette.background.default, 0.4),
                  '& .MuiSelect-select': { display: 'flex', alignItems: 'center', py: 0 },
                }}
              >
                <MenuItem value="all" sx={{ fontWeight: 700, fontSize: '0.72rem', color: 'primary.main' }}>
                  Apply to All Active Fleet Nodes
                </MenuItem>
                {nodes.map(node => {
                  const online = getNodeStatusOnline(node);
                  const locationHint = getNodeLocationHint(node);
                  return (
                    <MenuItem key={node.id} value={node.id} sx={{ py: 1 }}>
                      <Stack spacing={0.25} sx={{ minWidth: 0 }}>
                        <Stack direction="row" alignItems="center" spacing={1}>
                          <Typography variant="body2" fontWeight={800} sx={{ fontSize: '0.72rem' }}>
                            {getNodeDisplayName(node)}
                          </Typography>
                          <Typography component="span" sx={{ fontSize: '0.62rem', fontWeight: 700, opacity: 0.75 }}>
                            {online ? '🟢 ONLINE' : '🔴 OFFLINE'}
                          </Typography>
                        </Stack>
                        {locationHint && (
                          <Typography variant="caption" sx={{ opacity: 0.55, fontSize: '0.58rem' }}>
                            {locationHint}
                          </Typography>
                        )}
                        <Typography variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.45, fontSize: '0.58rem' }}>
                          ID: {node.id}
                        </Typography>
                      </Stack>
                    </MenuItem>
                  );
                })}
              </Select>
            </FormControl>
          )}

          <Button
            startIcon={<SaveIcon />}
            variant={activeDirty ? "contained" : "outlined"}
            onClick={handleCommitChanges}
            disabled={activeSaving || !activeDirty}
            sx={{ fontWeight: 900, px: 4, borderRadius: 3, height: 40, flexShrink: 0, boxShadow: activeDirty ? `0 8px 24px ${alpha(theme.palette.primary.main, 0.3)}` : 'none' }}
          >
            {activeSaving ? "SYNCING..." : "COMMIT ENGINE CHANGES"}
          </Button>
        </Stack>
      </Paper>

      {/* Hub warnings */}
      {isHub && selectedNodeId !== "all" && selectedNode && selectedNode.status?.toUpperCase() !== 'ONLINE' && (
        <Paper variant="outlined" sx={{ p: 1.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.error.main, 0.05), border: `1px solid ${alpha(theme.palette.error.main, 0.2)}` }}>
          <Typography variant="caption" sx={{ color: 'error.main', fontWeight: 700 }}>
            ⚠️ NODE IS CURRENTLY OFFLINE. Settings will be synchronized automatically when the Node checks in.
          </Typography>
        </Paper>
      )}
      {isHub && selectedNodeId === "all" && (
        <Paper variant="outlined" sx={{ p: 1.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.primary.main, 0.05), border: `1px solid ${alpha(theme.palette.primary.main, 0.2)}` }}>
          <Typography variant="caption" sx={{ color: 'primary.main', fontWeight: 700 }}>
            ✨ GLOBAL BROADCAST MODE: Changes will overwrite scanning configuration for all fleet nodes.
          </Typography>
        </Paper>
      )}

      {!isHub && routingDiagnostics?.requiresRemediation && (
        <Paper
          variant="outlined"
          sx={{
            p: 2, px: 3, borderRadius: 3,
            bgcolor: alpha(theme.palette.warning.main, 0.06),
            border: `1px solid ${alpha(theme.palette.warning.main, 0.25)}`
          }}
        >
          <Stack spacing={1.5}>
            <Stack direction="row" spacing={1.5} alignItems="flex-start">
              <WarningIcon sx={{ color: 'warning.main', mt: 0.2 }} />
              <Box sx={{ flex: 1 }}>
                <Typography variant="subtitle2" fontWeight={900} sx={{ color: 'warning.main' }}>
                  Multi-NIC Linux Routing Check
                </Typography>
                <Typography variant="body2" sx={{ mt: 0.5 }}>
                  {routingDiagnostics.summary}
                </Typography>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
                  {routingDiagnostics.detail}
                </Typography>
              </Box>
              <Button size="small" variant="text" onClick={() => setShowRoutingDetails(v => !v)} sx={{ fontWeight: 800 }}>
                {showRoutingDetails ? 'Hide Fix' : 'Show Fix'}
              </Button>
            </Stack>

            {showRoutingDetails && (
              <Box sx={{ pl: 4.5 }}>
                <Typography variant="caption" fontWeight={800} sx={{ display: 'block', mb: 0.75 }}>
                  Recommended host commands
                </Typography>
                {routingDiagnostics.sysctlCommands.map((cmd) => (
                  <Typography
                    key={cmd}
                    variant="caption"
                    component="div"
                    sx={{ fontFamily: 'monospace', bgcolor: alpha(theme.palette.background.default, 0.6), p: 1, borderRadius: 1, mb: 0.75 }}
                  >
                    {cmd}
                  </Typography>
                ))}
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>
                  Persist with <code>scripts/linux-host-init.sh</code> on bare-metal hosts, or set <code>STACKSATLAS_APPLY_RP_FILTER=1</code> on privileged Docker hosts.
                </Typography>
              </Box>
            )}
          </Stack>
        </Paper>
      )}

      {isHub && selectedNodeId !== 'all' && activeInterfaces.length === 0 && (
        <Paper variant="outlined" sx={{ p: 1.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.warning.main, 0.05), border: `1px solid ${alpha(theme.palette.warning.main, 0.2)}` }}>
          <Typography variant="caption" sx={{ color: 'warning.main', fontWeight: 700 }}>
            Node network adapters will appear after the next Hub check-in (within ~10s while this page is open). Select a scope NIC once the node reports its interfaces.
          </Typography>
        </Paper>
      )}

      {isHub && selectedNodeId !== 'all' && activeSubnets.length === 0 && (
        <Paper variant="outlined" sx={{ p: 1.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.info.main, 0.05), border: `1px solid ${alpha(theme.palette.info.main, 0.2)}` }}>
          <Typography variant="caption" sx={{ color: 'info.main', fontWeight: 700 }}>
            No scopes stored on the Hub for this node yet. Configure scopes here to push policy to the node, or save scanning settings on the node  -  they will sync up on the next check-in when the Hub has no policy for this site.
          </Typography>
        </Paper>
      )}

      <Paper variant="outlined" sx={{ p: 1.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.info.main, 0.04), border: `1px solid ${alpha(theme.palette.info.main, 0.15)}` }}>
        <Typography variant="caption" sx={{ fontWeight: 700, lineHeight: 1.5, display: 'block' }}>
          <strong>Scopes</strong> define <em>what</em> to scan (CIDR, bound NIC, engine toggles).{' '}
          <strong>Adapter policies</strong>  -  traffic roles and default VLAN labels  -  live on the <strong>Interfaces</strong> tab.
          VLAN fields here are optional scope overrides for fleet reporting, not 802.1Q tagging.
        </Typography>
      </Paper>

      <Grid container spacing={2}>
        {/* LEFT COLUMN: NETWORK SCOPES */}
        <Grid item xs={12} lg={6}>
          <Paper
            variant="outlined"
            sx={{
              p: 3, height: '100%', borderRadius: 4,
              bgcolor: cardBg, backdropFilter: "blur(20px)",
              border: cardBorder, boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`,
              display: 'flex', flexDirection: 'column', minHeight: 350
            }}
          >
            <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2.5}>
              <Box>
                <Typography variant="overline" color="primary" fontWeight={900} sx={{ opacity: 0.75, lineHeight: 1 }}>NETWORK SCOPES</Typography>
                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', opacity: 0.55 }}>
                  Each scope = one CIDR + NIC + optional VLAN label for fleet reporting
                </Typography>
              </Box>
              <Button
                startIcon={<AddIcon />}
                onClick={handleAddSubnetLocal}
                size="small"
                variant="outlined"
                sx={{ fontWeight: 700, borderRadius: 2, height: 28, px: 2 }}
              >
                ADD SCOPE
              </Button>
            </Stack>

            <Box sx={{
              flexGrow: 1, overflowY: 'auto', pr: 0.5, maxHeight: 420,
              '&::-webkit-scrollbar': { width: '4px' },
              '&::-webkit-scrollbar-thumb': { bgcolor: alpha(theme.palette.primary.main, 0.2), borderRadius: 2 }
            }}>
              <Stack spacing={1.5}>
                {activeSubnets.map((scope, index) => {
                  const boundIface = activeInterfaces.find(i => i.id === scope.interfaceId);
                  const adapterVlan = getAdapterVlanDefault(scope.interfaceId, activeInterfaceConfigs);
                  const vlanHelper = scope.vlanTag
                    ? 'Scope override  -  takes precedence over adapter default'
                    : adapterVlan
                      ? `Inherits "${adapterVlan}" from bound adapter (Interfaces tab)`
                      : 'Optional override  -  or set a default on the Interfaces tab';
                  return (
                    <Box
                      key={index}
                      sx={{
                        borderRadius: 2.5,
                        border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
                        bgcolor: alpha(theme.palette.background.default, 0.35),
                        overflow: 'hidden',
                        transition: 'border-color 0.2s',
                        '&:hover': { borderColor: alpha(theme.palette.primary.main, 0.25) }
                      }}
                    >
                      <Box sx={{ p: 2 }}>
                        <Stack spacing={1.75}>
                          <Stack direction="row" alignItems="flex-start" spacing={1}>
                            <RouterIcon sx={{ fontSize: 15, opacity: 0.4, flexShrink: 0, mt: 1.75 }} />
                            <TextField
                              fullWidth
                              size="small"
                              label="CIDR"
                              placeholder="192.168.1.0/24"
                              value={scope.cidr}
                              onChange={(e) => handleUpdateSubnetLocal(index, { ...scope, cidr: e.target.value })}
                              inputProps={{ style: { fontFamily: 'monospace', fontSize: '0.72rem', fontWeight: 700 } }}
                              sx={scopeControlSx}
                            />
                            <Tooltip title="Remove scope">
                              <MuiIconButton
                                color="error" size="small"
                                onClick={() => handleDeleteSubnetLocal(index)}
                                sx={{ bgcolor: alpha(theme.palette.error.main, 0.06), borderRadius: 1.5, flexShrink: 0, mt: 0.75 }}
                              >
                                <DeleteIcon sx={{ fontSize: 15 }} />
                              </MuiIconButton>
                            </Tooltip>
                          </Stack>

                          <FormControl fullWidth size="small" sx={scopeControlSx}>
                            <InputLabel>BIND TO NIC</InputLabel>
                            <Select
                              label="BIND TO NIC"
                              value={scope.interfaceId ?? ''}
                              onChange={(e) => handleUpdateSubnetLocal(index, { ...scope, interfaceId: e.target.value || null })}
                            >
                              <MenuItem value="" sx={{ fontSize: '0.72rem' }}>
                                <em style={{ opacity: 0.5 }}>Auto-Detect (Discovery Fallback NIC)</em>
                              </MenuItem>
                              {scope.interfaceId && !activeInterfaces.some(i => i.id === scope.interfaceId) && (
                                <MenuItem value={scope.interfaceId} sx={{ fontSize: '0.72rem', fontStyle: 'italic', opacity: 0.7 }}>
                                  {scope.interfaceId} (saved  -  awaiting node sync)
                                </MenuItem>
                              )}
                              {activeInterfaces.map(iface => (
                                <MenuItem key={iface.id} value={iface.id} sx={{ fontSize: '0.72rem', fontWeight: 700 }}>
                                  <Stack direction="row" alignItems="center" spacing={1}>
                                    <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
                                    <span>{iface.name}</span>
                                    <Typography component="span" variant="caption" sx={{ opacity: 0.5, fontFamily: 'monospace' }}>
                                      {iface.ipAddress}
                                    </Typography>
                                  </Stack>
                                </MenuItem>
                              ))}
                            </Select>
                          </FormControl>
                          {boundIface && (
                            <Typography variant="caption" sx={{ opacity: 0.45, fontSize: '0.58rem', display: 'block', lineHeight: 1.35, mt: -0.5 }}>
                              ↳ {boundIface.description && boundIface.description !== boundIface.name ? boundIface.description : boundIface.type} · {boundIface.ipAddress}
                            </Typography>
                          )}

                          <TextField
                            fullWidth
                            size="small"
                            label="VLAN OVERRIDE (OPTIONAL)"
                            placeholder={adapterVlan ? `Inherits: ${adapterVlan}` : '20 or AV-VLAN'}
                            value={scope.vlanTag ?? ''}
                            onChange={(e) => handleUpdateSubnetLocal(index, { ...scope, vlanTag: e.target.value || null })}
                            helperText={vlanHelper}
                            FormHelperTextProps={{ sx: { fontSize: '0.58rem', opacity: 0.55, mt: 0.75, mx: 0 } }}
                            inputProps={{ maxLength: 32, style: { fontFamily: 'monospace', fontSize: '0.72rem', fontWeight: 700 } }}
                            sx={scopeControlSx}
                          />

                          <Divider sx={{ opacity: 0.2, my: 0.25 }} />
                          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap>
                          {([
                            { key: 'enablePing', label: 'PING', color: '#0288d1' },
                            { key: 'enableArp', label: 'ARP', color: '#00897b' },
                            { key: 'enableMdns', label: 'mDNS', color: '#7b1fa2' },
                            { key: 'enableUpnp', label: 'UPnP', color: '#e65100' },
                          ] as { key: keyof NetworkScope; label: string; color: string }[]).map(({ key, label, color }) => {
                            const enabled = scope[key] as boolean;
                            return (
                              <Tooltip key={key} title={`Toggle ${label} discovery`}>
                                <Chip
                                  label={label}
                                  size="small"
                                  onClick={() => handleUpdateSubnetLocal(index, { ...scope, [key]: !enabled })}
                                  sx={{
                                    height: 20,
                                    fontSize: '0.58rem',
                                    fontWeight: 900,
                                    letterSpacing: 0.5,
                                    cursor: 'pointer',
                                    bgcolor: enabled ? alpha(color, 0.15) : alpha(theme.palette.divider, 0.08),
                                    color: enabled ? color : alpha(theme.palette.text.primary, 0.35),
                                    border: `1px solid ${enabled ? alpha(color, 0.35) : 'transparent'}`,
                                    transition: 'all 0.18s ease',
                                    '&:hover': { bgcolor: alpha(color, 0.25), transform: 'scale(1.05)' },
                                  }}
                                />
                              </Tooltip>
                            );
                          })}
                          </Stack>
                        </Stack>
                      </Box>
                    </Box>
                  );
                })}

                {activeSubnets.length === 0 && (
                  <Box sx={{ py: 6, textAlign: 'center', border: '1px dashed', borderColor: alpha(theme.palette.divider, 0.15), borderRadius: 3 }}>
                    <RouterIcon sx={{ fontSize: 32, opacity: 0.2, mb: 1 }} />
                    <Typography variant="caption" color="text.secondary" display="block">NO DISCOVERY SCOPES DEFINED</Typography>
                    <Typography variant="caption" sx={{ opacity: 0.4, fontSize: '0.6rem' }}>Click "ADD SCOPE" to define a network range</Typography>
                  </Box>
                )}
              </Stack>
            </Box>
          </Paper>
        </Grid>

        {/* RIGHT COLUMN: ENGINE PERFORMANCE & STATUS */}
        <Grid item xs={12} lg={6}>
          <Stack spacing={2} height="100%">
            <Paper
              variant="outlined"
              sx={{
                p: 3, borderRadius: 4,
                bgcolor: cardBg, backdropFilter: "blur(20px)",
                border: cardBorder, boxShadow: `0 8px 16px ${alpha("#000", 0.1)}`
              }}
            >
              <Typography variant="overline" color="primary" fontWeight={900} sx={{ opacity: 0.7, display: 'block', mb: 3 }}>ENGINE PERFORMANCE</Typography>

              <Grid container spacing={3}>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" sx={{ fontWeight: 900, opacity: 0.6, display: 'block', mb: 1 }}>
                    POLLING: {activeRefreshInterval >= 60 ? `${activeRefreshInterval / 60}m` : `${activeRefreshInterval}s`}
                  </Typography>
                  <Slider
                    size="small"
                    value={[5, 10, 30, 60, 300, 900, 1800, 3600].indexOf(activeRefreshInterval)}
                    min={0} max={7} step={1}
                    onChange={(_, v) => handleIntervalChange([5, 10, 30, 60, 300, 900, 1800, 3600][v as number])}
                  />
                </Grid>
                <Grid item xs={12} sm={6}>
                  <Typography variant="caption" sx={{ fontWeight: 900, opacity: 0.6, display: 'block', mb: 1 }}>
                    PARALLEL PINGS: {activeStability.maxParallelPings}
                  </Typography>
                  <Slider
                    size="small"
                    value={activeStability.maxParallelPings}
                    min={64} max={1024} step={32}
                    onChange={(_, v) => handleStabilityChange("maxParallelPings", v as number)}
                  />
                </Grid>

                <Grid item xs={4}>
                  <TextField fullWidth label="TIMEOUT (ms)" type="number" size="small"
                    value={activeStability.pingTimeoutMs}
                    onChange={(e) => handleStabilityChange("pingTimeoutMs", parseInt(e.target.value) || 0)}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 1.5, fontSize: '0.75rem', fontWeight: 800 } }}
                  />
                </Grid>
                <Grid item xs={4}>
                  <TextField fullWidth label="RETRIES" type="number" size="small"
                    value={activeStability.pingRetries}
                    onChange={(e) => handleStabilityChange("pingRetries", parseInt(e.target.value) || 0)}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 1.5, fontSize: '0.75rem', fontWeight: 800 } }}
                  />
                </Grid>
                <Grid item xs={4}>
                  <TextField fullWidth label="STRIKES" type="number" size="small"
                    value={activeStability.offlineStrikeThreshold}
                    onChange={(e) => handleStabilityChange("offlineStrikeThreshold", parseInt(e.target.value) || 0)}
                    sx={{ '& .MuiOutlinedInput-root': { borderRadius: 1.5, fontSize: '0.75rem', fontWeight: 800 } }}
                  />
                </Grid>

                <Grid item xs={12}>
                  <Stack direction="row" spacing={3}>
                    <FormControlLabel
                      control={<Switch size="small" checked={activeStability.enableInstantRecovery} onChange={(e) => handleStabilityChange("enableInstantRecovery", e.target.checked)} />}
                      label={<Typography variant="caption" sx={{ fontWeight: 700 }}>INSTANT RECOVERY</Typography>}
                    />
                    <FormControlLabel
                      control={<Switch size="small" checked={activeStability.enableNmapDeepScan} onChange={(e) => handleStabilityChange("enableNmapDeepScan", e.target.checked)} />}
                      label={<Typography variant="caption" sx={{ fontWeight: 700 }}>DEEP SCANNING</Typography>}
                    />
                  </Stack>
                </Grid>
              </Grid>
            </Paper>

            {showScanEnginePanel && (
              <Paper variant="outlined" sx={{ p: 2, borderRadius: 4, bgcolor: alpha(theme.palette.background.paper, 0.4), border: `1px solid ${alpha(theme.palette.divider, 0.05)}` }}>
                <Typography variant="caption" fontWeight={900} sx={{ opacity: 0.55, display: 'block', mb: 1.5, letterSpacing: 0.5 }}>
                  {isHub ? `DEEP SCAN ENGINE · ${getNodeDisplayName(selectedNode).toUpperCase()}` : 'DEEP SCAN ENGINE · LOCAL NODE'}
                </Typography>
                <Stack direction="row" spacing={3} justifyContent="space-around" flexWrap="wrap" useFlexGap>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Box sx={{
                      width: 8, height: 8, borderRadius: '50%',
                      bgcolor: (hubScanEngineReady ? nodeScanEngine?.nmapAvailable : localNmapReady) ? 'success.main' : 'error.main',
                      boxShadow: (hubScanEngineReady ? nodeScanEngine?.nmapAvailable : localNmapReady) ? `0 0 8px ${theme.palette.success.main}` : 'none'
                    }} />
                    <Typography variant="caption" fontWeight={900} sx={{ opacity: 0.8 }}>NMAP ENGINE</Typography>
                  </Stack>
                  <Stack direction="row" spacing={1} alignItems="center">
                    <Box sx={{
                      width: 8, height: 8, borderRadius: '50%',
                      bgcolor: (hubScanEngineReady ? nodeScanEngine?.pcapAvailable : localPcapReady) ? 'success.main' : 'error.main',
                      boxShadow: (hubScanEngineReady ? nodeScanEngine?.pcapAvailable : localPcapReady) ? `0 0 8px ${theme.palette.success.main}` : 'none'
                    }} />
                    <Typography variant="caption" fontWeight={900} sx={{ opacity: 0.8 }}>
                      {(hubScanEngineReady ? (nodeScanEngine?.pcapLabel ?? 'LIBPCAP') : localPcapLabel).toUpperCase()}
                    </Typography>
                  </Stack>
                </Stack>
              </Paper>
            )}
          </Stack>
        </Grid>
      </Grid>
    </Stack>
  );
};

export default ScanningTab;
