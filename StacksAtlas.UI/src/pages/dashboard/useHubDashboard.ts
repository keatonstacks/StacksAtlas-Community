import { useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { ApiService } from '../../services/apiService';
import { useAuth } from '../../context/AuthContext';
import { useConfirm } from '../../context/ConfirmContext';
import type { ManageSiteFormState } from '../../components/hub-dashboard/HubManageSiteDialog';
import type { EnrollMethod } from '../../components/hub-dashboard/HubEnrollDialog';
import type { FederatedNodeRow, HubAlertItem, HubFleetFilters, HubFleetStats, HubToastState } from '../../components/hub-dashboard/types';
import {
  filterFleetNodes,
  fleetHealthPercent,
  uniqueFilterValues,
} from '../../components/hub-dashboard/hubDashboardUtils';
import { summarizeFleetVersionDrift } from '../../utils/fleetVersionDrift';
import { getPreferredUpdateChannel } from '../../utils/preferredUpdateChannel';
import { latestStagedVersionForChannel } from '../../utils/updateDepotUi';

const emptyManageForm = (): ManageSiteFormState => ({
  name: '',
  client: '',
  building: '',
  room: '',
  syncUsers: false,
  syncUserRegistry: false,
  syncSsoSettings: false,
  syncAlertSettings: false,
  syncSiemSettings: false,
  delegateAlertDispatch: false,
});

export function useHubDashboard() {
  const navigate = useNavigate();
  const { isAdmin } = useAuth();
  const { confirm } = useConfirm();

  const [nodes, setNodes] = useState<FederatedNodeRow[]>([]);
  const [alerts, setAlerts] = useState<HubAlertItem[]>([]);
  const [hubVersion, setHubVersion] = useState<string | null>(null);
  const [stagedUpdateVersion, setStagedUpdateVersion] = useState<string | null>(null);
  const [stats, setStats] = useState<HubFleetStats>({
    totalNodes: 0,
    onlineNodes: 0,
    setupRequiredNodes: 0,
    totalDevices: 0,
    criticalAlerts: 0,
  });
  const [filters, setFilters] = useState<HubFleetFilters>({ search: '', client: '', building: '' });
  const [topologySiteId, setTopologySiteId] = useState('');
  const [toast, setToast] = useState<HubToastState>({ open: false, message: '', severity: 'success' });

  const [editNode, setEditNode] = useState<FederatedNodeRow | null>(null);
  const [manageForm, setManageForm] = useState<ManageSiteFormState>(emptyManageForm());
  const [saving, setSaving] = useState(false);

  const [enrollOpen, setEnrollOpen] = useState(false);
  const [enrollStep, setEnrollStep] = useState(0);
  const [enrollMethod, setEnrollMethod] = useState<EnrollMethod>('mtls');
  const [enrolling, setEnrolling] = useState(false);
  const [enrollUrl, setEnrollUrl] = useState('');
  const [enrollPassword, setEnrollPassword] = useState('');
  const [enrollName, setEnrollName] = useState('');
  const [enrollClient, setEnrollClient] = useState('');
  const [enrollBuilding, setEnrollBuilding] = useState('');
  const [enrollRoom, setEnrollRoom] = useState('');

  const loadData = async () => {
    try {
      const channel = getPreferredUpdateChannel();
      const [nodesData, alertsData, summaryData, versionInfo, depotStatus] = await Promise.all([
        ApiService.getFederationNodes(),
        ApiService.getAlertHistory(10),
        ApiService.getDashboardSummary(),
        ApiService.getSystemVersion().catch(() => null),
        ApiService.getUpdateDepotStatus().catch(() => null),
      ]);

      const nodeList = (nodesData || []) as FederatedNodeRow[];
      const alertList = (alertsData || []) as HubAlertItem[];

      setNodes(nodeList);
      setAlerts(alertList);
      setHubVersion(versionInfo?.version ?? null);
      setStagedUpdateVersion(latestStagedVersionForChannel(depotStatus, channel));
      setStats({
        totalNodes: nodeList.length,
        onlineNodes: nodeList.filter((n) => n.status?.toLowerCase() === 'online').length,
        setupRequiredNodes: nodeList.filter((n) => n.status?.toLowerCase() === 'setup_required').length,
        totalDevices: summaryData?.totalDevices || 0,
        criticalAlerts: alertList.filter((a) => new Date(a.triggeredAt) > new Date(Date.now() - 86400000)).length,
      });
    } catch (err) {
      console.error('Failed to load hub dashboard data:', err);
    }
  };

  useEffect(() => {
    loadData();
    const interval = setInterval(loadData, 10000);
    return () => clearInterval(interval);
  }, []);

  const clients = useMemo(() => uniqueFilterValues(nodes, 'client'), [nodes]);
  const buildings = useMemo(() => uniqueFilterValues(nodes, 'building'), [nodes]);
  const filteredNodes = useMemo(() => filterFleetNodes(nodes, filters), [nodes, filters]);
  const versionDrift = useMemo(() => summarizeFleetVersionDrift(nodes, hubVersion), [nodes, hubVersion]);
  const fleetHealth = fleetHealthPercent(stats.onlineNodes, stats.totalNodes);

  const selectableTopologyNodes = useMemo(
    () => filteredNodes.filter((n) => n.status?.toLowerCase() !== 'setup_required'),
    [filteredNodes],
  );

  useEffect(() => {
    if (topologySiteId && selectableTopologyNodes.some((n) => n.id === topologySiteId)) return;
    const preferred =
      selectableTopologyNodes.find((n) => n.status?.toLowerCase() === 'online')
      ?? selectableTopologyNodes[0];
    setTopologySiteId(preferred?.id ?? '');
  }, [selectableTopologyNodes, topologySiteId]);

  const openManage = (node: FederatedNodeRow) => {
    setEditNode(node);
    setManageForm({
      name: node.name || '',
      client: node.client || '',
      building: node.building || '',
      room: node.room || '',
      syncUsers: node.syncUsers ?? false,
      syncUserRegistry: node.syncUserRegistry ?? false,
      syncSsoSettings: node.syncSsoSettings ?? false,
      syncAlertSettings: node.syncAlertSettings ?? false,
      syncSiemSettings: node.syncSiemSettings ?? false,
      delegateAlertDispatch: node.delegateAlertDispatch ?? false,
    });
  };

  const handleUpdateNode = async () => {
    if (!editNode) return;
    setSaving(true);
    try {
      await ApiService.updateFederationNode(editNode.id, {
        name: manageForm.name,
        client: manageForm.client,
        building: manageForm.building,
        room: manageForm.room,
        syncUsers: manageForm.syncUsers,
        syncUserRegistry: manageForm.syncUserRegistry,
        syncSsoSettings: manageForm.syncSsoSettings,
        syncAlertSettings: manageForm.syncAlertSettings,
        syncSiemSettings: manageForm.syncSiemSettings,
        delegateAlertDispatch: manageForm.delegateAlertDispatch,
        overrideAlertSettings: editNode.overrideAlertSettings,
        overrideSiemSettings: editNode.overrideSiemSettings,
      });
      setToast({ open: true, message: 'Site metadata updated.', severity: 'success' });
      await loadData();
      setEditNode(null);
    } catch {
      setToast({ open: true, message: 'Failed to update site details.', severity: 'error' });
    } finally {
      setSaving(false);
    }
  };

  const handleForceSync = async (nodeId: string) => {
    try {
      await ApiService.sendNodeCommand(nodeId, {
        commandType: 'ForceSync',
        targetId: nodeId,
        parameters: {},
      });
      setToast({ open: true, message: 'Force sync sent.', severity: 'success' });
    } catch {
      setToast({ open: true, message: 'Failed to trigger sync.', severity: 'error' });
    }
  };

  const handleNotifyUpdate = async (nodeId: string) => {
    const channel = getPreferredUpdateChannel();
    try {
      const result = await ApiService.notifyNodeUpdate(nodeId, channel);
      setToast({
        open: true,
        message: result.message || `Update notify (${channel}) sent.`,
        severity: 'success',
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to notify update.';
      setToast({ open: true, message, severity: 'error' });
    }
  };

  const handleTriggerUpdate = async (nodeId: string, nodeName: string) => {
    const label = nodeName || nodeId;
    const channel = getPreferredUpdateChannel();
    const ok = window.confirm(
      `Offer ${channel} update to "${label}"?\n\nRequires a Hub-staged ${channel} package. Node admin must still confirm apply locally.`,
    );
    if (!ok) return;
    try {
      const result = await ApiService.triggerNodeUpdate(nodeId, channel);
      setToast({
        open: true,
        message: result.message || `Update trigger (${channel}) sent.`,
        severity: 'success',
      });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Failed to trigger update.';
      setToast({ open: true, message, severity: 'error' });
    }
  };

  const handleEnrollDirect = async () => {
    if (!enrollUrl || !enrollPassword || !enrollName) {
      setToast({ open: true, message: 'URL, password, and site name are required.', severity: 'error' });
      return;
    }
    setEnrolling(true);
    try {
      await ApiService.enrollNodeFromHub({
        nodeUrl: enrollUrl,
        nodeAdminPassword: enrollPassword,
        nodeName: enrollName,
        client: enrollClient,
        building: enrollBuilding,
        room: enrollRoom,
      });
      setToast({ open: true, message: 'Enrollment sent. Node will register shortly.', severity: 'success' });
      setEnrollOpen(false);
      setEnrollStep(0);
      setEnrollUrl('');
      setEnrollPassword('');
      setEnrollName('');
      setEnrollClient('');
      setEnrollBuilding('');
      setEnrollRoom('');
      await loadData();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      setToast({ open: true, message: `Enrollment failed: ${message}`, severity: 'error' });
    } finally {
      setEnrolling(false);
    }
  };

  const handleResetSite = async (nodeId: string, nodeName: string) => {
    const label = nodeName || nodeId;
    const typed = window.prompt(
      `Reset site "${label}"?\n\nWipes local inventory on the node. Hub enrollment is preserved.\nType the site name to confirm:`
    );
    if (typed !== label) {
      if (typed !== null) {
        setToast({ open: true, message: 'Confirmation did not match. Reset cancelled.', severity: 'error' });
      }
      return;
    }
    try {
      const result = await ApiService.resetFederationSite(nodeId);
      setToast({ open: true, message: result.message || `Site "${label}" reset initiated.`, severity: 'success' });
      await loadData();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      setToast({ open: true, message: `Reset failed: ${message}`, severity: 'error' });
    }
  };

  const handleDeleteNode = async (nodeId: string, nodeName: string) => {
    const ok = await confirm({
      title: 'Remove node',
      message: `Remove node "${nodeName || nodeId}" and all synchronized data from the Hub?`,
      confirmLabel: 'Remove',
      confirmColor: 'error',
    });
    if (!ok) return;
    try {
      await ApiService.deleteFederationNode(nodeId);
      setToast({ open: true, message: `Node "${nodeName}" removed.`, severity: 'success' });
      await loadData();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      setToast({ open: true, message: `Remove failed: ${message}`, severity: 'error' });
    }
  };

  const openEnroll = () => {
    setEnrollStep(0);
    setEnrollMethod('mtls');
    setEnrollOpen(true);
  };

  const openEnrollHint = () => {
    setEnrollStep(2);
    setEnrollMethod('mtls');
    setEnrollOpen(true);
  };

  const handleEnrollFieldChange = (field: string, value: string) => {
    const setters: Record<string, (v: string) => void> = {
      enrollUrl: setEnrollUrl,
      enrollPassword: setEnrollPassword,
      enrollName: setEnrollName,
      enrollClient: setEnrollClient,
      enrollBuilding: setEnrollBuilding,
      enrollRoom: setEnrollRoom,
    };
    setters[field]?.(value);
  };

  const focusTopologySite = (nodeId: string) => {
    setTopologySiteId(nodeId);
    window.requestAnimationFrame(() => {
      document.getElementById('hub-topology-panel')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    });
  };

  return {
    navigate,
    isAdmin,
    nodes,
    alerts,
    setAlerts,
    stats,
    filters,
    setFilters,
    toast,
    setToast,
    editNode,
    setEditNode,
    manageForm,
    setManageForm,
    saving,
    enrollOpen,
    setEnrollOpen,
    enrollStep,
    setEnrollStep,
    enrollMethod,
    setEnrollMethod,
    enrolling,
    enrollUrl,
    enrollPassword,
    enrollName,
    enrollClient,
    enrollBuilding,
    enrollRoom,
    clients,
    buildings,
    filteredNodes,
    hubVersion,
    stagedUpdateVersion,
    versionDrift,
    fleetHealth,
    topologySiteId,
    setTopologySiteId,
    focusTopologySite,
    loadData,
    openManage,
    handleUpdateNode,
    handleForceSync,
    handleNotifyUpdate,
    handleTriggerUpdate,
    handleEnrollDirect,
    handleResetSite,
    handleDeleteNode,
    openEnroll,
    openEnrollHint,
    handleEnrollFieldChange,
  };
}

export type HubDashboardController = ReturnType<typeof useHubDashboard>;
