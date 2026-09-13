import React, { useEffect, useState } from 'react';
import {
  Box, Grid, Paper, Stack, Typography, TextField, Button, Chip, FormControl,
  alpha, useTheme, Select, MenuItem, Tooltip
} from '@mui/material';
import {
  SettingsEthernet as EthernetIcon,
  Wifi as WifiIcon,
  Save as SaveIcon,
  LocalOffer as TagIcon
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import type {
  NetworkInterfaceConfig, NetworkInterfaceInfo, NetworkRole
} from '../../services/apiService';

const ROLE_LABELS: Record<NetworkRole, string> = {
  Default: 'Default',
  HubCommunication: 'Hub Sync',
  AlertsAndSiem: 'Alerts / SIEM',
  DiscoveryFallback: 'Discovery Fallback',
};

const ROLE_COLORS: Record<NetworkRole, string> = {
  Default: '#607d8b',
  HubCommunication: '#1565c0',
  AlertsAndSiem: '#e65100',
  DiscoveryFallback: '#2e7d32',
};

const ALL_ROLES: NetworkRole[] = ['Default', 'HubCommunication', 'AlertsAndSiem', 'DiscoveryFallback'];

const readNodeField = (node: any, camel: string, pascal: string) => node?.[camel] ?? node?.[pascal];

const getNodeDisplayName = (node: any) => {
  if (!node) return 'Unknown Node';
  const rawName = (readNodeField(node, 'name', 'Name') ?? '').toString().trim();
  if (rawName) return rawName;
  const parts = [readNodeField(node, 'client', 'Client'), readNodeField(node, 'building', 'Building'), readNodeField(node, 'room', 'Room')].filter(Boolean);
  return parts.length ? parts.join(' · ') : readNodeField(node, 'id', 'Id') || 'Unnamed Node';
};

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

const normalizeInterfaceConfig = (raw: any): NetworkInterfaceConfig => ({
  interfaceId: (raw?.interfaceId ?? raw?.InterfaceId ?? '').toString(),
  roles: (raw?.roles ?? raw?.Roles ?? []).map((r: string) => r as NetworkRole),
  defaultVlanTag: raw?.defaultVlanTag ?? raw?.DefaultVlanTag ?? null,
});

const migrateRoleMappingsToConfigs = (mappings: { interfaceId: string; role: NetworkRole }[]): NetworkInterfaceConfig[] => {
  const byId = new Map<string, NetworkRole[]>();
  mappings.forEach(m => {
    if (!m.interfaceId) return;
    const list = byId.get(m.interfaceId) ?? [];
    if (!list.includes(m.role)) list.push(m.role);
    byId.set(m.interfaceId, list);
  });
  return Array.from(byId.entries()).map(([interfaceId, roles]) => ({ interfaceId, roles, defaultVlanTag: null }));
};

const parseInterfaceConfigsFromScanSettings = (json?: string | null): NetworkInterfaceConfig[] => {
  if (!json) return [];
  try {
    const parsed = JSON.parse(json);
    const net = parsed.network ?? parsed.Network ?? {};
    const configs = net.interfaceConfigs ?? net.InterfaceConfigs;
    if (Array.isArray(configs) && configs.length > 0)
      return configs.map(normalizeInterfaceConfig).filter(c => c.interfaceId);
    const mappings = net.interfaceRoleMappings ?? net.InterfaceRoleMappings ?? [];
    return migrateRoleMappingsToConfigs(mappings.map((m: any) => ({
      interfaceId: m.interfaceId ?? m.InterfaceId,
      role: (m.role ?? m.Role) as NetworkRole,
    })));
  } catch {
    return [];
  }
};

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

interface InterfacesTabProps {
  interfaceConfigs: NetworkInterfaceConfig[];
  setInterfaceConfigs: (configs: NetworkInterfaceConfig[]) => void;
  isDirty: boolean;
  setIsDirty: (dirty: boolean) => void;
  saving: boolean;
  onSave: () => Promise<void>;
  isHub: boolean;
  onNotify?: (message: string, severity?: 'success' | 'error') => void;
}

const InterfacesTab: React.FC<InterfacesTabProps> = ({
  interfaceConfigs,
  setInterfaceConfigs,
  isDirty,
  setIsDirty,
  saving,
  onSave,
  isHub,
  onNotify,
}) => {
  const theme = useTheme();
  const [interfaces, setInterfaces] = useState<NetworkInterfaceInfo[]>([]);
  const [nodes, setNodes] = useState<any[]>([]);
  const [selectedNodeId, setSelectedNodeId] = useState('all');
  const [localConfigs, setLocalConfigs] = useState<NetworkInterfaceConfig[]>([]);
  const [localDirty, setLocalDirty] = useState(false);
  const [localSaving, setLocalSaving] = useState(false);

  const cardBg = alpha(theme.palette.background.paper, 0.8);
  const cardBorder = `1px solid ${alpha(theme.palette.divider, 0.1)}`;

  const fieldSx = {
    '& .MuiOutlinedInput-root': {
      borderRadius: 1.5,
      bgcolor: alpha(theme.palette.background.paper, 0.5),
      fontSize: '0.72rem',
      minHeight: 40,
    },
    '& .MuiInputLabel-root': { fontSize: '0.65rem', fontWeight: 700 },
  };

  useEffect(() => {
    if (!isHub) {
      ApiService.getInterfaces('policy')
        .then(res => setInterfaces(res || []))
        .catch(err => console.error('Failed to load interfaces:', err));
    } else {
      ApiService.getFederationNodes()
        .then(res => setNodes(res || []))
        .catch(err => console.error('Failed to load nodes:', err));
    }
  }, [isHub]);

  useEffect(() => {
    if (!isHub) {
      setLocalConfigs(interfaceConfigs);
      setLocalDirty(false);
    }
  }, [interfaceConfigs, isHub]);

  useEffect(() => {
    if (isHub && selectedNodeId !== 'all') {
      const node = nodes.find(n => (n.id ?? n.Id) === selectedNodeId);
      setLocalConfigs(parseInterfaceConfigsFromScanSettings(node?.scanSettingsJson ?? node?.ScanSettingsJson));
      setInterfaces(parseInterfacesFromScanSettings(node?.scanSettingsJson ?? node?.ScanSettingsJson));
      setLocalDirty(false);
    } else if (isHub && selectedNodeId === 'all') {
      setLocalConfigs([]);
      setInterfaces([]);
      setLocalDirty(false);
    }
  }, [selectedNodeId, nodes, isHub]);

  const activeConfigs = isHub ? localConfigs : interfaceConfigs;
  const activeInterfaces = interfaces;
  const activeDirty = isHub ? localDirty : isDirty;
  const activeSaving = isHub ? localSaving : saving;

  const getConfig = (ifaceId: string) =>
    activeConfigs.find(c => c.interfaceId === ifaceId) ?? { interfaceId: ifaceId, roles: [], defaultVlanTag: null };

  const upsertConfig = (ifaceId: string, patch: Partial<NetworkInterfaceConfig>) => {
    const existing = getConfig(ifaceId);
    const updated = { ...existing, ...patch, interfaceId: ifaceId };
    const others = activeConfigs.filter(c => c.interfaceId !== ifaceId);
    const next = [...others, updated];
    if (isHub) {
      setLocalConfigs(next);
      setLocalDirty(true);
    } else {
      setInterfaceConfigs(next);
      setIsDirty(true);
    }
  };

  const toggleRole = (ifaceId: string, role: NetworkRole) => {
    const config = getConfig(ifaceId);
    const roles = config.roles.includes(role)
      ? config.roles.filter(r => r !== role)
      : [...config.roles, role];
    upsertConfig(ifaceId, { roles });
  };

  const handleHubSave = async () => {
    if (selectedNodeId === 'all') return;
    setLocalSaving(true);
    try {
      const node = nodes.find(n => (n.id ?? n.Id) === selectedNodeId);
      const json = node?.scanSettingsJson ?? node?.ScanSettingsJson ?? '{}';
      const parsed = JSON.parse(json);
      const net = parsed.network ?? parsed.Network ?? {};
      const combined = {
        ...parsed,
        network: { ...net, interfaceConfigs: localConfigs },
      };
      await ApiService.updateFederationNode(selectedNodeId, { scanSettingsJson: JSON.stringify(combined) });
      const refreshed = await ApiService.getFederationNodes();
      setNodes(refreshed || []);
      setLocalDirty(false);
      onNotify?.('Adapter policies saved on Hub. Changes will sync to the node immediately if online, or on next check-in.', 'success');
    } catch (err: any) {
      onNotify?.(`Failed to save adapter policies: ${err.message}`);
    } finally {
      setLocalSaving(false);
    }
  };

  const handleSaveClick = () => {
    if (isHub) handleHubSave();
    else onSave();
  };

  return (
    <Stack spacing={2}>
      <Paper
        variant="outlined"
        sx={{
          p: 2, px: 3, borderRadius: 4, bgcolor: cardBg, backdropFilter: 'blur(20px)', border: cardBorder,
          display: 'flex', justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap', gap: 2,
        }}
      >
        <Stack direction="row" alignItems="center" spacing={2}>
          <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.secondary.main, 0.12) }}>
            <EthernetIcon color="secondary" sx={{ fontSize: 20 }} />
          </Box>
          <Box>
            <Typography variant="subtitle2" fontWeight={900}>NETWORK ADAPTERS</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ opacity: 0.7 }}>
              Traffic roles + default VLAN label for fleet tracking (not 802.1Q tagging)
            </Typography>
          </Box>
        </Stack>
        <Stack direction="row" spacing={2} alignItems="center" flexWrap="wrap">
          {isHub && (
            <FormControl size="small" sx={{ minWidth: { xs: '100%', sm: 280 }, width: { xs: '100%', sm: 'auto' }, '& .MuiInputBase-root': { height: 40 } }}>
              <Select
                value={selectedNodeId}
                onChange={(e) => setSelectedNodeId(e.target.value)}
                displayEmpty
                sx={{ borderRadius: 2.5, fontSize: '0.72rem', fontWeight: 700 }}
              >
                <MenuItem value="all" disabled>Select a fleet node…</MenuItem>
                {nodes.map(node => (
                  <MenuItem key={node.id ?? node.Id} value={node.id ?? node.Id}>
                    {getNodeDisplayName(node)}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>
          )}
          <Button
            startIcon={<SaveIcon />}
            variant={activeDirty ? 'contained' : 'outlined'}
            onClick={handleSaveClick}
            disabled={activeSaving || !activeDirty || (isHub && selectedNodeId === 'all')}
            sx={{ fontWeight: 900, px: 3, borderRadius: 3, height: 40 }}
          >
            {activeSaving ? 'SAVING…' : 'SAVE ADAPTER POLICIES'}
          </Button>
        </Stack>
      </Paper>

      <Paper variant="outlined" sx={{ p: 2.5, px: 3, borderRadius: 3, bgcolor: alpha(theme.palette.primary.main, 0.04), border: `1px solid ${alpha(theme.palette.primary.main, 0.12)}` }}>
        <Typography variant="caption" sx={{ display: 'block', lineHeight: 1.55, opacity: 0.85 }}>
          <strong>Adapters (this tab)</strong> define outbound traffic roles and the default VLAN label stamped on discovered devices.
          <strong> Scanning (next tab)</strong> defines which subnets to sweep and which adapter sends probes  -  scope VLAN overrides adapter defaults when set.
        </Typography>
      </Paper>

      {isHub && selectedNodeId === 'all' && (
        <Paper variant="outlined" sx={{ p: 2, borderRadius: 3, bgcolor: alpha(theme.palette.warning.main, 0.05), border: `1px solid ${alpha(theme.palette.warning.main, 0.2)}` }}>
          <Typography variant="caption" sx={{ color: 'warning.main', fontWeight: 700 }}>
            Select a fleet node above to view and govern its physical adapters.
          </Typography>
        </Paper>
      )}

      {activeInterfaces.length === 0 && (!isHub || selectedNodeId !== 'all') && (
        <Paper variant="outlined" sx={{ p: 4, textAlign: 'center', borderRadius: 4, border: cardBorder }}>
          <Typography variant="body2" color="text.secondary">No network adapters detected.</Typography>
        </Paper>
      )}

      <Grid container spacing={2}>
        {activeInterfaces.map(iface => {
          const config = getConfig(iface.id);
          const isEthernet = iface.type?.toLowerCase().includes('ethernet');
          const primaryRole = config.roles[0];
          const borderColor = primaryRole ? ROLE_COLORS[primaryRole] : alpha(theme.palette.divider, 0.2);

          return (
            <Grid item xs={12} md={6} key={iface.id}>
              <Paper
                variant="outlined"
                sx={{
                  p: 2.5, borderRadius: 3, height: '100%',
                  border: `1.5px solid ${alpha(borderColor, config.roles.length ? 0.35 : 0.15)}`,
                  bgcolor: alpha(theme.palette.background.default, 0.35),
                }}
              >
                <Stack direction="row" spacing={1.5} alignItems="flex-start" mb={2}>
                  {isEthernet ? <EthernetIcon sx={{ color: borderColor, mt: 0.25 }} /> : <WifiIcon sx={{ color: borderColor, mt: 0.25 }} />}
                  <Box sx={{ flex: 1, minWidth: 0 }}>
                    <Typography variant="subtitle2" fontWeight={800} sx={{ fontFamily: 'monospace', fontSize: '0.8rem' }}>
                      {iface.name}
                    </Typography>
                    <Typography variant="caption" sx={{ opacity: 0.55, display: 'block' }}>
                      {iface.ipAddress} · {iface.description !== iface.name ? iface.description : iface.type}
                    </Typography>
                  </Box>
                </Stack>

                <Typography variant="overline" sx={{ fontSize: '0.58rem', fontWeight: 900, opacity: 0.55, display: 'block', mb: 1 }}>
                  Outbound traffic roles
                </Typography>
                <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap sx={{ mb: 2 }}>
                  {ALL_ROLES.map(role => {
                    const active = config.roles.includes(role);
                    return (
                      <Tooltip key={role} title={`Toggle ${ROLE_LABELS[role]} for outbound traffic`}>
                        <Chip
                          label={ROLE_LABELS[role]}
                          size="small"
                          onClick={() => toggleRole(iface.id, role)}
                          sx={{
                            height: 24,
                            fontSize: '0.62rem',
                            fontWeight: 800,
                            cursor: 'pointer',
                            bgcolor: active ? alpha(ROLE_COLORS[role], 0.15) : alpha(theme.palette.divider, 0.06),
                            color: active ? ROLE_COLORS[role] : alpha(theme.palette.text.primary, 0.4),
                            border: `1px solid ${active ? alpha(ROLE_COLORS[role], 0.4) : 'transparent'}`,
                          }}
                        />
                      </Tooltip>
                    );
                  })}
                </Stack>

                <TextField
                  fullWidth
                  size="small"
                  label="DEFAULT VLAN LABEL"
                  placeholder="20 or AV-VLAN"
                  value={config.defaultVlanTag ?? ''}
                  onChange={(e) => upsertConfig(iface.id, { defaultVlanTag: e.target.value || null })}
                  helperText="Applied to all devices discovered via this adapter unless a scope overrides it"
                  FormHelperTextProps={{ sx: { fontSize: '0.58rem', opacity: 0.55, mt: 0.75, mx: 0, lineHeight: 1.4 } }}
                  InputProps={{
                    startAdornment: <TagIcon sx={{ fontSize: 14, opacity: 0.35, mr: 1 }} />,
                  }}
                  inputProps={{ maxLength: 32, style: { fontFamily: 'monospace', fontSize: '0.72rem', fontWeight: 700 } }}
                  sx={fieldSx}
                />
              </Paper>
            </Grid>
          );
        })}
      </Grid>
    </Stack>
  );
};

export default InterfacesTab;
