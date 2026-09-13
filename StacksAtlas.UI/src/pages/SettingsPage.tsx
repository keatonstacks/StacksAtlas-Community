import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { 
  Dialog, DialogTitle, DialogContent, DialogContentText, DialogActions, 
  IconButton as MuiIconButton, 
  TextField, Button, Typography, Box, 
  Stack, Tabs, Tab, CircularProgress, 
  alpha, useTheme, Snackbar, Alert
} from '@mui/material';
import { 
  ContentCopy as CopyIcon
} from '@mui/icons-material';
import { ApiService } from "../services/apiService";
import type { NetworkScope, NetworkInterfaceConfig } from "../services/apiService";
import { isHubMode, normalizeExecutionMode, licenseTierLabel } from '../utils/federationMode';
import { useColumnVisibility } from "../context/ColumnVisibilityContext";
import { PageHeader } from "../components/pageheader";
import { PageShell } from "../components/mobile/PageShell";
import { useAuth } from "../context/AuthContext";
import { useConfirm } from "../context/ConfirmContext";
import { useLicense } from "../hooks/useLicense";
import { useIsMobileLayout } from "../hooks/useIsMobileLayout";

import { APP_VERSION } from "../constants/version";

import IdentityTab from '../components/settings/IdentityTab';
import LicensingTab from '../components/settings/LicensingTab';
import FederationTab from '../components/settings/FederationTab';
import AlertsTab from '../components/settings/AlertsTab';
import ScanningTab from '../components/settings/ScanningTab';
import InterfacesTab from '../components/settings/InterfacesTab';
import AuthTab from '../components/settings/AuthTab';
import SecurityTab from '../components/settings/SecurityTab';
import SystemTab from '../components/settings/SystemTab';
import DatabaseTab from '../components/settings/DatabaseTab';
import PortableEvaluationTab from '../components/settings/PortableEvaluationTab';
import OpenAvcTab from '../components/settings/OpenAvcTab';

type SettingsTabKey =
  | 'identity'
  | 'licensing'
  | 'portable'
  | 'federation'
  | 'alerts'
  | 'interfaces'
  | 'scanning'
  | 'auth'
  | 'governance'
  | 'infrastructure'
  | 'openavc'
  | 'database';

type SettingsTabDef = {
  key: SettingsTabKey;
  label: string;
  adminOnly: boolean;
  /** Shown in production appliance mode */
  appliance: boolean;
  /** Shown in portable mode */
  portable: boolean;
};

const SETTINGS_TAB_DEFS: SettingsTabDef[] = [
  { key: 'identity', label: 'IDENTITY', adminOnly: false, appliance: true, portable: false },
  { key: 'licensing', label: 'LICENSING', adminOnly: true, appliance: true, portable: false },
  { key: 'portable', label: 'PORTABLE', adminOnly: true, appliance: false, portable: true },
  { key: 'federation', label: 'FEDERATION', adminOnly: true, appliance: true, portable: false },
  { key: 'alerts', label: 'ALERTS', adminOnly: true, appliance: true, portable: false },
  { key: 'interfaces', label: 'INTERFACES', adminOnly: true, appliance: true, portable: true },
  { key: 'scanning', label: 'SCANNING', adminOnly: true, appliance: true, portable: true },
  { key: 'auth', label: 'AUTH (SSO)', adminOnly: true, appliance: true, portable: false },
  { key: 'governance', label: 'DATA GOVERNANCE', adminOnly: true, appliance: true, portable: false },
  { key: 'infrastructure', label: 'INFRASTRUCTURE', adminOnly: true, appliance: true, portable: true },
  { key: 'openavc', label: 'OPENAVC', adminOnly: true, appliance: true, portable: false },
  { key: 'database', label: 'DATABASE INFRA', adminOnly: true, appliance: true, portable: false },
];

const SETTINGS_TAB_ALIASES: Record<string, SettingsTabKey> = {
  identity: 'identity',
  licensing: 'licensing',
  portable: 'portable',
  evaluation: 'portable',
  federation: 'federation',
  alerts: 'alerts',
  interfaces: 'interfaces',
  scanning: 'scanning',
  auth: 'auth',
  governance: 'governance',
  infrastructure: 'infrastructure',
  openavc: 'openavc',
  integrations: 'openavc',
  system: 'infrastructure',
  updates: 'infrastructure',
  database: 'database',
};

function resolveVisibleSettingsTabs(isPortable: boolean, isAdmin: boolean): SettingsTabDef[] {
  return SETTINGS_TAB_DEFS.filter((tab) => {
    if (isPortable ? !tab.portable : !tab.appliance) return false;
    if (tab.adminOnly && !isAdmin) return false;
    return true;
  });
}

function normalizeTabKey(raw: string | null | undefined, isPortable: boolean): SettingsTabKey | null {
  if (!raw) return null;
  const key = SETTINGS_TAB_ALIASES[raw.toLowerCase()];
  if (!key) return null;
  if (isPortable && key === 'licensing') return 'portable';
  if (!isPortable && key === 'portable') return 'licensing';
  return key;
}

function migrateRoleMappings(mappings: { interfaceId: string; role: string }[]): NetworkInterfaceConfig[] {
  const byId = new Map<string, string[]>();
  mappings.forEach(m => {
    if (!m.interfaceId) return;
    const list = byId.get(m.interfaceId) ?? [];
    if (!list.includes(m.role)) list.push(m.role);
    byId.set(m.interfaceId, list);
  });
  return Array.from(byId.entries()).map(([interfaceId, roles]) => ({
    interfaceId,
    roles: roles as NetworkInterfaceConfig['roles'],
    defaultVlanTag: null,
  }));
}

export default function SettingsPage({
  mode,
  setMode
}: {
  mode: "light" | "dark";
  setMode: (m: "light" | "dark") => void;
}) {
  const theme = useTheme();
  const isMobile = useIsMobileLayout();
  const { visibility, setVisibility } = useColumnVisibility();
  const { isAdmin } = useAuth();
  const { confirm } = useConfirm();
  const { status: license, loading: licenseLoading, activate: activateLicense, refresh: refreshLicense } = useLicense();
  const [keyInput, setKeyInput] = useState("");
  const [toast, setToast] = useState<{ open: boolean; message: string; severity: 'success' | 'error' }>({
    open: false,
    message: '',
    severity: 'error',
  });
  const showToast = (message: string, severity: 'success' | 'error' = 'error') =>
    setToast({ open: true, message, severity });

  // State Management
  const [refreshInterval, setRefreshInterval] = useState<number>(10);
  const [technicianMode, setTechnicianMode] = useState(false);
  const [subnets, setSubnets] = useState<NetworkScope[]>([]);
  const [interfaceConfigs, setInterfaceConfigs] = useState<NetworkInterfaceConfig[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [isDirty, setIsDirty] = useState(false); // Tracks if changes are unapplied

  // Email Settings State
  const [emailSettings, setEmailSettings] = useState<any>({
    smtpHost: 'smtp.gmail.com',
    smtpPort: 587,
    useSsl: true,
    username: '',
    password: '',
    fromAddress: '',
    fromName: 'StacksAtlas Alerts',
    enabled: false
  });
  const [emailDirty, setEmailDirty] = useState(false);
  const [testEmail, setTestEmail] = useState("");
  const [sendingTest, setSendingTest] = useState(false);



  // Stability / Hysteresis Settings State
  const [stabilitySettings, setStabilitySettings] = useState<{
    offlineStrikeThreshold: number;
    enableInstantRecovery: boolean;
    maxParallelPings: number;
    pingTimeoutMs: number;
    pingRetries: number;
    enableNmapDeepScan: boolean;
    maxConcurrentDeepScans: number;
  }>({
    offlineStrikeThreshold: 3,
    enableInstantRecovery: true,
    maxParallelPings: 256,
    pingTimeoutMs: 200,
    pingRetries: 0,
    enableNmapDeepScan: false,
    maxConcurrentDeepScans: 2
  });

  const [stabilityDirty, setStabilityDirty] = useState(false);
  const [savingStability, setSavingStability] = useState(false);

  // System Settings State
  const [systemConfig, setSystemConfig] = useState({
    httpPort: 5000,
    httpsPort: 5001,
    syslogEnabled: false,
    syslogHost: '',
    syslogPort: 514,
    syslogAppName: 'StacksAtlas'
  });
  const [systemDirty, setSystemDirty] = useState(false);
  const [savingSystem, setSavingSystem] = useState(false);

  // Auth Settings State
  const [systemStatus, setSystemStatus] = useState<any>(null);
  const [systemRuntimeInfo, setSystemRuntimeInfo] = useState<any>(null);
  const [authSettings, setAuthSettings] = useState<any>({
    ssoEnabled: false,
    provider: 'OIDC',
    defaultRole: 'Viewer',
    oidc: { authority: '', clientId: '', clientSecret: '', scope: 'openid profile email' },
    ldap: { server: '', port: 389, useSsl: false, baseDn: '', userFilter: '(sAMAccountName={0})', groupAttribute: 'memberOf' },
    roleMappings: []
  });
  const [authDirty, setAuthDirty] = useState(false);
  const [savingAuth, setSavingAuth] = useState(false);


  // Federation Settings State
  const [fedSettings, setFedSettings] = useState<any>({
    mode: 0, // Standalone
    hubUrl: '',
    nodeId: 'node-unnamed',
    nodeDisplayName: '',
    federationToken: '',
    client: '',
    building: '',
    room: '',
    overrideAlertSettings: false,
    overrideSiemSettings: false,
    useTailscaleForHubConnection: false,
    hubTailscaleMagicDns: '',
    hubTailscaleIpv4: ''
  });
  const [savingFed, setSavingFed] = useState(false);
  const [fedDirty, setFedDirty] = useState(false);
  const [generatedFedToken, setGeneratedFedToken] = useState<string | null>(null);

  // Migration & Key State
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importPass, setImportPass] = useState("");
  const [generatedKeyData, setGeneratedKeyData] = useState<any>(null);
  const [copySuccess, setCopySuccess] = useState(false);


  const [activeTabKey, setActiveTabKey] = useState<SettingsTabKey>(() => {
    const saved = sessionStorage.getItem('stacksatlas.settings.activeTabKey');
    if (saved && saved in SETTINGS_TAB_ALIASES) return saved as SettingsTabKey;
    const legacy = sessionStorage.getItem('stacksatlas.settings.activeTab');
    const legacyMap: Record<string, SettingsTabKey> = {
      '0': 'identity',
      '1': 'licensing',
      '2': 'federation',
      '3': 'alerts',
      '4': 'interfaces',
      '5': 'scanning',
      '6': 'auth',
      '7': 'governance',
      '8': 'infrastructure',
      '9': 'database',
    };
    return legacyMap[legacy ?? ''] ?? 'identity';
  });
  const [searchParams] = useSearchParams();
  const isPortable = !!systemRuntimeInfo?.isPortable;
  const visibleTabs = useMemo(
    () => resolveVisibleSettingsTabs(isPortable, isAdmin),
    [isPortable, isAdmin]
  );
  const isHub = isHubMode(fedSettings.mode);
  const isNode = normalizeExecutionMode(fedSettings.mode) === 0 && !!fedSettings.hubUrl && (fedSettings.syncSsoSettings || fedSettings.syncUsers);

  useEffect(() => {
    if (loading) return;
    if (!visibleTabs.some((tab) => tab.key === activeTabKey)) {
      const fallback = visibleTabs[0]?.key ?? (isPortable ? 'portable' : 'identity');
      setActiveTabKey(fallback);
      sessionStorage.setItem('stacksatlas.settings.activeTabKey', fallback);
    }
  }, [loading, visibleTabs, activeTabKey, isPortable]);

  useEffect(() => {
    const tabKey = normalizeTabKey(searchParams.get('tab'), isPortable);
    if (!tabKey) return;
    if (!visibleTabs.some((tab) => tab.key === tabKey)) return;
    setActiveTabKey(tabKey);
    sessionStorage.setItem('stacksatlas.settings.activeTabKey', tabKey);
  }, [searchParams, isPortable, visibleTabs]);

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    setCopySuccess(true);
    setTimeout(() => setCopySuccess(false), 2000);
  };

  // 1. Initial Data Load
  useEffect(() => {
    async function loadSettings() {
      try {
        const promises = [
          ApiService.getPollingSettings(),
          ApiService.getNetworkSettings(),
          ApiService.getSystemConfig()
        ];

        // Only load email settings for admins
        // Only load sensitive settings for admins
        if (isAdmin) {
          promises.push(ApiService.getEmailSettings());
          promises.push(ApiService.getAuthSettings());
          promises.push(ApiService.getFederationSettings());
          promises.push(ApiService.getSystemStatus());
          promises.push(ApiService.getSystemRuntimeInfo());
        }


        const results = await Promise.all(promises);
        const [pollData, netData, systemData, emailData] = results;

        setRefreshInterval(pollData.refreshIntervalSeconds || 10);
        // Subnets may be returned as NetworkScope[] from new API, or legacy string[] - normalize either
        const rawSubnets = netData?.subnets || netData || [];
        if (rawSubnets.length > 0 && typeof rawSubnets[0] === 'string') {
          setSubnets(rawSubnets.map((cidr: string) => ({ cidr, interfaceId: null, enableArp: true, enablePing: true, enableMdns: true, enableUpnp: true })));
        } else {
          setSubnets(rawSubnets);
        }
        setInterfaceConfigs(
          (() => {
            const rawConfigs = netData?.interfaceConfigs ?? netData?.InterfaceConfigs;
            if (Array.isArray(rawConfigs) && rawConfigs.length > 0) return rawConfigs;
            return migrateRoleMappings(netData?.interfaceRoleMappings ?? netData?.InterfaceRoleMappings ?? []);
          })()
        );

        if (systemData) {
          setSystemConfig({
            httpPort: systemData.httpPort || 5000,
            httpsPort: systemData.httpsPort || 5001,
            syslogEnabled: systemData.syslogEnabled || false,
            syslogHost: systemData.syslogHost || '',
            syslogPort: systemData.syslogPort || 514,
            syslogAppName: systemData.syslogAppName || 'StacksAtlas'
          });
        }

        // Load stability settings from network settings response
        if (netData) {
          setStabilitySettings({
            offlineStrikeThreshold: netData.offlineStrikeThreshold ?? 3,
            enableInstantRecovery: netData.enableInstantRecovery ?? true,
            maxParallelPings: netData.maxParallelPings ?? 256,
            pingTimeoutMs: netData.pingTimeoutMs ?? 200,
            pingRetries: netData.pingRetries ?? 0,
            enableNmapDeepScan: netData.enableNmapDeepScan ?? false,
            maxConcurrentDeepScans: netData.maxConcurrentDeepScans ?? 2
          });

        }


        if (emailData && isAdmin) {
          setEmailSettings(emailData);
        }

        if (isAdmin) {
          if (results[4]) setAuthSettings(results[4]);
          if (results[5]) setFedSettings({
            ...results[5],
            mode: normalizeExecutionMode(results[5].mode ?? results[5].Mode),
          });
          if (results[6]) setSystemStatus(results[6]);
          if (results[7]) setSystemRuntimeInfo(results[7]);
        }


      } catch (err) {
        console.error("Critical: Failed to sync with StacksAtlas Engine:", err);
      } finally {
        setLoading(false);
      }
    }
    loadSettings();
  }, []);

  // 2. Persistent Save for Polling (Immediate)
  const saveRefreshInterval = async (value: number) => {
    try {
      await ApiService.updatePollingSettings({
        refreshIntervalSeconds: value
      });
    } catch (err) { console.error("Polling update failed:", err); }
  };

  // 3. Subnet Management Logic (Draft Mode)
  const handleAddSubnet = () => {
    setSubnets([...subnets, { cidr: '', interfaceId: null, enableArp: true, enablePing: true, enableMdns: true, enableUpnp: true }]);
    setIsDirty(true);
  };

  const handleUpdateSubnet = (index: number, value: NetworkScope) => {
    const newSubnets = [...subnets];
    newSubnets[index] = value;
    setSubnets(newSubnets);
    setIsDirty(true);
  };

  const handleDeleteSubnet = (index: number) => {
    setSubnets(subnets.filter((_, i) => i !== index));
    setIsDirty(true);
  };

  // 4. Commit Changes to Backend
  const handleApplyNetworkChanges = async () => {
    setSaving(true);
    try {
      const saved = await ApiService.updateNetworkSettings({ subnets, interfaceConfigs, ...stabilitySettings });
      const savedConfigs = saved?.interfaceConfigs ?? saved?.InterfaceConfigs;
      if (Array.isArray(savedConfigs))
        setInterfaceConfigs(savedConfigs);
      setIsDirty(false);
    } catch (err: any) {
      console.error("Failed to commit network changes:", err);
      showToast(`Failed to save network settings: ${err.message ?? err}`);
    } finally {
      setSaving(false);
    }
  };

  const handleSaveSystem = async () => {
    const ok = await confirm({
      title: 'Change ports',
      message:
        'CRITICAL: Changing ports requires an engine restart. The application will disconnect and you must manually navigate to the new port in your browser. Proceed?',
      confirmColor: 'warning',
    });
    if (!ok) return;

    setSavingSystem(true);
    try {
      await ApiService.updateSystemPorts(systemConfig);
      setSystemDirty(false);

      // Trigger restart
      await ApiService.restartSystem();
      showToast("System is restarting. Wait ~10 seconds, then open the appliance on the new port.", 'success');
    } catch (err: any) {
      console.error("System update failed:", err);
      showToast(`Error saving system settings: ${err.message}`);
    } finally {
      setSavingSystem(false);
    }
  };


  const toggleColumn = (col: string) => {
    setVisibility({ ...visibility, [col]: !visibility[col as keyof typeof visibility] });
  };

  // Email Settings Handlers
  const handleSendTestEmail = async () => {
    if (!testEmail) return;
    setSendingTest(true);
    try {
      await ApiService.sendTestEmail(testEmail);
      showToast("Test email sent! Check your inbox.", 'success');
    } catch (err: any) {
      showToast(`Failed to send test email: ${err.message}`);
    } finally {
      setSendingTest(false);
    }
  };

  const handleSaveEmail = async () => {

    setSaving(true);
    try {
      await ApiService.updateEmailSettings(emailSettings);
      setEmailDirty(false);
    } catch (err: any) {
      console.error("Email update failed:", err);
    } finally {
      setSaving(false);
    }
  };


  const handleSaveAuth = async () => {
    setSavingAuth(true);
    try {
      await ApiService.updateAuthSettings(authSettings);
      setAuthDirty(false);
    } catch (err: any) {
      console.error("Auth update failed:", err);
    } finally {
      setSavingAuth(false);
    }
  };


  const handleTestLdap = async () => {
    setSavingAuth(true);
    try {
      await ApiService.testLdap(authSettings.ldap);
      showToast("LDAP connection successful.", 'success');
    } catch (err: any) {
      showToast(`LDAP error: ${err.message}`);
    } finally {
      setSavingAuth(false);
    }
  };

  const handleTestOidc = async () => {
    setSavingAuth(true);
    try {
      await ApiService.testOidc(authSettings.oidc);
      showToast("OIDC configuration valid.", 'success');
    } catch (err: any) {
      showToast(`OIDC error: ${err.message}`);
    } finally {
      setSavingAuth(false);
    }
  };


  const handleSaveFed = async () => {
    const ok = await confirm({
      title: 'Save federation settings',
      message:
        'Changing Federation settings requires a system restart to reconfigure SignalR and Background Workers. Proceed?',
      confirmColor: 'warning',
    });
    if (!ok) return;

    setSavingFed(true);
    try {
      await ApiService.updateFederationSettings(fedSettings);
      setFedDirty(false);

      await ApiService.restartSystem();
    } catch (err: any) {
      showToast(`Error saving federation settings: ${err.message}`);
    } finally {
      setSavingFed(false);
    }
  };

  const handleSaveOverrideSettings = async (updatedSettings: any) => {
    try {
      await ApiService.updateFederationSettings(updatedSettings);
      setFedSettings(updatedSettings);
      setFedDirty(false);
    } catch (err: any) {
      showToast(`Error saving local overrides: ${err.message}`);
    }
  };

  const handleDecoupleNode = async () => {
    const ok = await confirm({
      title: 'Decouple from Hub',
      message:
        'CRITICAL WARNING: This will immediately demote this Node back to Standalone mode, promote all federated user accounts to standard local accounts, and restart the engine. Are you absolutely sure?',
      confirmLabel: 'Decouple',
      confirmColor: 'error',
    });
    if (!ok) return;

    setSavingFed(true);
    try {
      await ApiService.decoupleNode();
      setFedDirty(false);
      
      await ApiService.restartSystem();
      showToast("Appliance decoupled successfully. Restarting engine...", 'success');
    } catch (err: any) {
      showToast(`Decoupling failed: ${err.message}`);
    } finally {
      setSavingFed(false);
    }
  };





  const handleAddRoleMapping = () => {
    setAuthSettings({
      ...authSettings,
      roleMappings: [...authSettings.roleMappings, { externalGroup: '', stacksAtlasRole: 'Viewer' }]
    });
    setAuthDirty(true);
  };

  const handleUpdateRoleMapping = (index: number, field: string, value: string) => {
    const newMappings = [...authSettings.roleMappings];
    newMappings[index] = { ...newMappings[index], [field]: value };
    setAuthSettings({ ...authSettings, roleMappings: newMappings });
    setAuthDirty(true);
  };

  const handleDeleteRoleMapping = (index: number) => {
    setAuthSettings({
      ...authSettings,
      roleMappings: authSettings.roleMappings.filter((_: any, i: number) => i !== index)
    });
    setAuthDirty(true);
  };

  // --- SECURITY TOOLS ---

  const [newKey, setNewKey] = useState('');
  const [masterPass, setMasterPass] = useState('');
  const [apiKeys, setApiKeys] = useState<any[]>([]);
  const [newKeyLabel, setNewKeyLabel] = useState("");


  useEffect(() => {
    if (isAdmin) {
      ApiService.getApiKeys().then(setApiKeys).catch(console.error);
    }
  }, [isAdmin]);

  const handleRotateKey = async () => {

    if (!newKey) return;
    const ok = await confirm({
      title: 'Rotate encryption key',
      message:
        'CRITICAL: Rotating the encryption key requires rebuilding the database. If the process is interrupted, data loss may occur. StacksAtlas will automatically take a backup first. Proceed?',
      confirmColor: 'error',
    });
    if (!ok) return;

    setSaving(true);
    try {
      await ApiService.rotateEncryptionKey(newKey);
      showToast('Key rotation complete. Changes are now active.', 'success');
      setNewKey('');
    } catch (err: any) {
      showToast(`Error: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };


  const handleExportPortable = async () => {
    if (!masterPass) return;
    setSaving(true);
    try {
      await ApiService.exportPortableDatabase(masterPass);
    } catch (err: any) {
      showToast(`Export failed: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };




  const handleCreateNewApiKey = async () => {
    if (!newKeyLabel.trim()) return;
    setSaving(true);
    try {
      const res = await ApiService.createApiKey(newKeyLabel);
      setGeneratedKeyData(res);
      setNewKeyLabel("");
      const keys = await ApiService.getApiKeys();
      setApiKeys(keys);
    } catch (err: any) {
      showToast(`Key generation failed: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };


  const handleRevokeApiKey = async (id: string) => {
    const ok = await confirm({
      title: 'Revoke API key',
      message: 'Are you sure you want to revoke this key? Any integrations using it will immediately lose access.',
      confirmLabel: 'Revoke',
      confirmColor: 'error',
    });
    if (!ok) return;
    setSaving(true);
    try {
      await ApiService.revokeApiKey(id);
      const keys = await ApiService.getApiKeys();
      setApiKeys(keys);
    } catch (err: any) {
      showToast(`Key revocation failed: ${err.message}`);
    } finally {
      setSaving(false);
    }
  };







  const formatLabel = (str: string) => str.replace(/([A-Z])/g, ' $1').replace(/^./, s => s.toUpperCase());

  if (loading) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '80vh' }}>
        <CircularProgress color="primary" />
      </Box>
    );
  }

  return (
    <>
    <PageShell isMobile={isMobile}>

      <PageHeader
        title="SYSTEM CONFIGURATION"
        subtitle="APPLIANCE GOVERNANCE & CORE CONTROLS"
        stats={[
          { label: "ENGINE VERSION", value: `v${APP_VERSION}` },
          {
            label: isPortable ? "MODE" : "LICENSING",
            value: isPortable
              ? "PORTABLE"
              : licenseTierLabel(license?.tier, !!license?.isActive).replace(' (FREE)', ''),
            color: isPortable || license?.isActive ? "success.main" : "text.secondary",
          },
          { label: "STATUS", value: "ONLINE", color: "success.main" }
        ]}
      />

      <Box sx={{ 
        mb: 4, 
        p: 0.75, 
        borderRadius: 4, 
        bgcolor: alpha(theme.palette.background.paper, 0.4),
        backdropFilter: "blur(12px)",
        border: `1px solid ${alpha(theme.palette.divider, 0.08)}`,
        width: { xs: '100%', md: 'fit-content' },
        maxWidth: '100%',
        boxShadow: `0 4px 20px ${alpha("#000", 0.15)}`
      }}>
        <Tabs 
          value={activeTabKey}
          onChange={(_, key) => {
            const next = key as SettingsTabKey;
            setActiveTabKey(next);
            sessionStorage.setItem('stacksatlas.settings.activeTabKey', next);
          }} 
          variant="scrollable"
          scrollButtons="auto"
          allowScrollButtonsMobile
          sx={{
            minHeight: 38,
            maxWidth: '100%',
            '& .MuiTabs-scroller': { overflow: 'auto !important' },
            '& .MuiTabs-indicator': {
              height: '100%',
              borderRadius: 3,
              bgcolor: alpha(theme.palette.primary.main, 0.12),
              zIndex: 0,
              border: `1px solid ${alpha(theme.palette.primary.main, 0.2)}`
            },
            '& .MuiTab-root': { 
              fontWeight: 600, 
              minWidth: { xs: 96, md: 140 }, 
              fontSize: '0.65rem',
              letterSpacing: 1.5,
              zIndex: 1,
              minHeight: 38,
              borderRadius: 3,
              transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
              color: alpha(theme.palette.text.primary, 0.4),
              mx: 0.25,
              px: { xs: 1.25, md: 2 },
              '&.Mui-selected': { 
                color: 'primary.main',
                textShadow: `0 0 10px ${alpha(theme.palette.primary.main, 0.3)}`
              },
              '&:hover': {
                color: 'text.primary',
                bgcolor: alpha(theme.palette.action.hover, 0.05)
              }
            }
          }}
        >
          {visibleTabs.map((tab) => (
            <Tab key={tab.key} value={tab.key} label={tab.label} />
          ))}
        </Tabs>
      </Box>

      {activeTabKey === 'identity' && (
        <IdentityTab 
          isHub={isHub}
          fedSettings={fedSettings}
          setFedSettings={setFedSettings}
          fedDirty={fedDirty}
          setFedDirty={setFedDirty}
          handleSaveFed={handleSaveFed}
          savingFed={savingFed}
          mode={mode}
          setMode={setMode}
          technicianMode={technicianMode}
          setTechnicianMode={setTechnicianMode}
        />
      )}

      {activeTabKey === 'licensing' && isAdmin && (
        <LicensingTab 
          license={license}
          licenseLoading={licenseLoading}
          refreshLicense={refreshLicense}
          keyInput={keyInput}
          setKeyInput={setKeyInput}
          activateLicense={activateLicense}
          onNotify={showToast}
        />
      )}

      {activeTabKey === 'portable' && isAdmin && (
        <PortableEvaluationTab
          promoteHint={systemRuntimeInfo?.promoteHint}
          dataDirectory={systemRuntimeInfo?.dataDirectory}
        />
      )}

      {activeTabKey === 'federation' && isAdmin && (
        <FederationTab 
          fedSettings={fedSettings}
          setFedSettings={setFedSettings}
          fedDirty={fedDirty}
          setFedDirty={setFedDirty}
          savingFed={savingFed}
          handleSaveFed={handleSaveFed}
          handleDecouple={handleDecoupleNode}
          license={license}
          copyToClipboard={copyToClipboard}
          generatedFedToken={generatedFedToken}
          setGeneratedFedToken={setGeneratedFedToken}
          onNotify={showToast}
        />
      )}

      {activeTabKey === 'alerts' && isAdmin && (
        <AlertsTab 
          fedSettings={fedSettings}
          handleSaveOverrideSettings={handleSaveOverrideSettings}
          emailSettings={emailSettings}
          setEmailSettings={setEmailSettings}
          emailDirty={emailDirty}
          setEmailDirty={setEmailDirty}
          handleSaveEmail={handleSaveEmail}
          saving={saving}
          testEmail={testEmail}
          setTestEmail={setTestEmail}
          handleSendTestEmail={handleSendTestEmail}
          sendingTest={sendingTest}
        />
      )}

      {activeTabKey === 'interfaces' && isAdmin && (
        <InterfacesTab
          isHub={isHub}
          interfaceConfigs={interfaceConfigs}
          setInterfaceConfigs={(configs) => { setInterfaceConfigs(configs); setIsDirty(true); }}
          isDirty={isDirty}
          setIsDirty={setIsDirty}
          saving={saving}
          onSave={handleApplyNetworkChanges}
          onNotify={showToast}
        />
      )}

      {activeTabKey === 'scanning' && isAdmin && (
        <ScanningTab 
          isHub={isHub}
          subnets={subnets}
          interfaceConfigs={interfaceConfigs}
          handleUpdateSubnet={handleUpdateSubnet}
          handleDeleteSubnet={handleDeleteSubnet}
          handleAddSubnet={handleAddSubnet}
          isDirty={isDirty}
          handleApplyNetworkChanges={handleApplyNetworkChanges}
          saving={saving}
          savingStability={savingStability}
          setSavingStability={setSavingStability}
          refreshInterval={refreshInterval}
          setRefreshInterval={setRefreshInterval}
          saveRefreshInterval={saveRefreshInterval}
          stabilitySettings={stabilitySettings}
          setStabilitySettings={setStabilitySettings}
          stabilityDirty={stabilityDirty}
          setStabilityDirty={setStabilityDirty}
          systemStatus={systemStatus}
          onNotify={showToast}
        />
      )}

      {activeTabKey === 'auth' && isAdmin && (
        <AuthTab 
          authSettings={authSettings}
          setAuthSettings={setAuthSettings}
          authDirty={authDirty}
          setAuthDirty={setAuthDirty}
          savingAuth={savingAuth}
          handleSaveAuth={handleSaveAuth}
          handleUpdateRoleMapping={handleUpdateRoleMapping}
          handleDeleteRoleMapping={handleDeleteRoleMapping}
          handleAddRoleMapping={handleAddRoleMapping}
          handleTestLdap={handleTestLdap}
          handleTestOidc={handleTestOidc}
          license={license}
          isNode={isNode}
        />
      )}

      {activeTabKey === 'governance' && isAdmin && (
        <SecurityTab 
          saving={saving}
          setSaving={setSaving}
          newKey={newKey}
          setNewKey={setNewKey}
          handleRotateKey={handleRotateKey}
          masterPass={masterPass}
          setMasterPass={setMasterPass}
          handleExportPortable={handleExportPortable}
          importFile={importFile}
          setImportFile={setImportFile}
          importPass={importPass}
          setImportPass={setImportPass}
          visibility={visibility}
          toggleColumn={toggleColumn}
          formatLabel={formatLabel}
          onNotify={showToast}
        />
      )}

      {activeTabKey === 'infrastructure' && isAdmin && (
        <SystemTab 
          fedSettings={fedSettings}
          handleSaveOverrideSettings={handleSaveOverrideSettings}
          systemConfig={systemConfig}
          setSystemConfig={setSystemConfig}
          systemDirty={systemDirty}
          setSystemDirty={setSystemDirty}
          savingSystem={savingSystem}
          handleSaveSystem={handleSaveSystem}
          newKeyLabel={newKeyLabel}
          setNewKeyLabel={setNewKeyLabel}
          handleCreateNewApiKey={handleCreateNewApiKey}
          saving={saving}
          apiKeys={apiKeys}
          handleRevokeApiKey={handleRevokeApiKey}
          systemRuntimeInfo={systemRuntimeInfo}
          mode={mode}
          setMode={setMode}
          technicianMode={technicianMode}
          setTechnicianMode={setTechnicianMode}
        />
      )}

      {activeTabKey === 'openavc' && isAdmin && (
        <OpenAvcTab fedSettings={fedSettings} onNotify={showToast} />
      )}

      {activeTabKey === 'database' && isAdmin && (
        <DatabaseTab />
      )}

      {/* Critical Actions Area */}
      <Box mt={8} pb={4} display="flex" flexDirection="column" alignItems="center" gap={1}>
        <Typography variant="caption" sx={{ opacity: 0.4, fontWeight: 700, fontFamily: 'monospace' }}>
          StacksAtlas Engine v{APP_VERSION} Build 942
        </Typography>
      </Box>

    </PageShell>

    {/* API KEY MODAL */}
    <Dialog open={!!generatedKeyData} onClose={() => setGeneratedKeyData(null)} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ fontWeight: 700, color: 'success.main' }}>API KEY CREATED</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          Please copy this key now. For your security, StacksAtlas only stores a secure hash and you will <strong>never be able to see it again</strong>.
        </DialogContentText>
        <Stack spacing={1}>
          <TextField
            fullWidth
            size="small"
            value={generatedKeyData?.token || ''}
            InputProps={{
              readOnly: true,
              endAdornment: (
                <MuiIconButton onClick={() => copyToClipboard(generatedKeyData?.token || '')} size="small">
                  <CopyIcon fontSize="small" color={copySuccess ? "success" : "inherit"} />
                </MuiIconButton>
              ),
            }}
            sx={{ bgcolor: alpha(theme.palette.success.main, 0.05), '& .MuiOutlinedInput-root': { fontFamily: 'monospace', fontSize: '0.8rem', fontWeight: 600 } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setGeneratedKeyData(null)} sx={{ fontWeight: 600 }}>I COPIED IT, CLOSE</Button>
      </DialogActions>
    </Dialog>

    {/* FEDERATION TOKEN MODAL */}
    <Dialog open={!!generatedFedToken} onClose={() => setGeneratedFedToken(null)} maxWidth="sm" fullWidth>
      <DialogTitle sx={{ fontWeight: 700, color: 'primary.main' }}>FEDERATION TOKEN CREATED</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          Please copy this token now. You will need it to enroll standard appliances into your fleet. <strong>This is the only time it will be shown in plain text.</strong>
        </DialogContentText>
        <Stack spacing={1}>
          <TextField
            fullWidth
            size="small"
            value={generatedFedToken || ''}
            InputProps={{
              readOnly: true,
              endAdornment: (
                <MuiIconButton onClick={() => copyToClipboard(generatedFedToken || '')} size="small">
                  <CopyIcon fontSize="small" color={copySuccess ? "success" : "inherit"} />
                </MuiIconButton>
              ),
            }}
            sx={{ bgcolor: alpha(theme.palette.primary.main, 0.05), '& .MuiOutlinedInput-root': { fontFamily: 'monospace', fontSize: '0.8rem', fontWeight: 600 } }}
          />
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setGeneratedFedToken(null)} sx={{ fontWeight: 600 }}>I COPIED IT, CLOSE</Button>
      </DialogActions>
    </Dialog>

    <Snackbar open={toast.open} autoHideDuration={6000} onClose={() => setToast((t) => ({ ...t, open: false }))}>
      <Alert severity={toast.severity} onClose={() => setToast((t) => ({ ...t, open: false }))} sx={{ width: '100%' }}>
        {toast.message}
      </Alert>
    </Snackbar>
  </>
  );
}

