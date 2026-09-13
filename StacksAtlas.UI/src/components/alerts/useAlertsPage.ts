import { useEffect, useMemo, useRef, useState } from 'react';
import { useConfirm } from '../../context/ConfirmContext';
import { useLocation } from 'react-router-dom';
import { ApiService } from '../../services/apiService';
import type { FilterState } from '../FilterScopeToolbar';
import {
  getIdString,
  groupSystemEvents,
  isAuditOnlyAlert,
  matchesScopeFilter,
  parseAlertsUrlParams,
} from './alertsUtils';
import type {
  AlertEvent,
  AlertsDevice,
  AlertsTabId,
  GroupedEvent,
  HistorySortKey,
  SystemEvent,
} from './types';

export function useAlertsPage() {
  const location = useLocation();
  const { confirm: requestConfirm } = useConfirm();
  const initialUrl = useMemo(() => parseAlertsUrlParams(location.search), []);

  const [activeTab, setActiveTab] = useState<AlertsTabId>(initialUrl.tab);
  const [events, setEvents] = useState<SystemEvent[]>([]);
  const [devices, setDevices] = useState<AlertsDevice[]>([]);
  const [appliedFilters, setAppliedFilters] = useState<FilterState>(initialUrl.filters);
  const [alertHistory, setAlertHistory] = useState<AlertEvent[]>([]);

  const [loading, setLoading] = useState(true);
  const [historyLoading, setHistoryLoading] = useState(false);

  const [severityFilter, setSeverityFilter] = useState<string>(initialUrl.severity);
  const [search, setSearch] = useState(initialUrl.search);
  const [selectedGroupKeys, setSelectedGroupKeys] = useState<string[]>([]);

  const [historySearch, setHistorySearch] = useState('');
  const [historyFilter, setHistoryFilter] = useState<string>('All');
  const [auditSearch, setAuditSearch] = useState('');
  const [auditFilter, setAuditFilter] = useState<string>('All');
  const [selectedHistoryIds, setSelectedHistoryIds] = useState<string[]>([]);
  const [selectedAuditIds, setSelectedAuditIds] = useState<string[]>([]);

  const [orderBy, setOrderBy] = useState<HistorySortKey>('triggeredAt');
  const [order, setOrder] = useState<'asc' | 'desc'>('desc');

  const [toastOpen, setToastOpen] = useState(false);
  const [toastMsg, setToastMsg] = useState('');

  const deletedIdsRef = useRef<Set<string>>(new Set());

  useEffect(() => {
    const parsed = parseAlertsUrlParams(location.search);
    if (parsed.search) setSearch(parsed.search);
    if (parsed.severity) setSeverityFilter(parsed.severity);
    setActiveTab(parsed.tab);
    setAppliedFilters(parsed.filters);
  }, [location.search]);

  const handleRequestSort = (property: HistorySortKey) => {
    const isAsc = orderBy === property && order === 'asc';
    setOrder(isAsc ? 'desc' : 'asc');
    setOrderBy(property);
  };

  const loadData = async () => {
    try {
      const [eventData, devData] = await Promise.all([
        ApiService.getRecentEvents(200, appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined),
        ApiService.getDevices(),
      ]);

      const cleanEvents = (eventData || []).filter((e: SystemEvent) => {
        const idStr = getIdString(e.id);
        return idStr && !deletedIdsRef.current.has(idStr);
      });

      setEvents(cleanEvents);
      setDevices(Array.isArray(devData) ? devData : devData?.devices || []);
    } catch (err) {
      console.error('Critical Sync Failure:', err);
    } finally {
      setLoading(false);
    }
  };

  const loadAlertHistory = async () => {
    try {
      setHistoryLoading(true);
      const data = await ApiService.getAlertHistory(
        100,
        appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined,
      );
      setAlertHistory(data || []);
    } catch (err) {
      console.error('Failed to load history:', err);
    } finally {
      setHistoryLoading(false);
    }
  };

  useEffect(() => {
    loadData();
    const interval = setInterval(loadData, 10000);
    return () => clearInterval(interval);
  }, [appliedFilters.nodeIds]);

  useEffect(() => {
    if (activeTab === 'history' || activeTab === 'audit') loadAlertHistory();
  }, [activeTab, appliedFilters.nodeIds]);

  const scopedEvents = useMemo(
    () => events.filter((evt) => matchesScopeFilter(evt, appliedFilters)),
    [events, appliedFilters],
  );

  const filteredGroups = useMemo(() => {
    return groupSystemEvents(scopedEvents, deletedIdsRef.current)
      .filter((evt) => {
        const matchesSeverity = severityFilter === 'All' || evt.severity === severityFilter;
        const searchTerm = search.toLowerCase();
        const device = devices.find((d) => d.ipAddress === evt.deviceIp);

        return (
          matchesSeverity &&
          (evt.message.toLowerCase().includes(searchTerm) ||
            evt.deviceIp?.includes(searchTerm) ||
            evt.type.toLowerCase().includes(searchTerm) ||
            (device && device.name.toLowerCase().includes(searchTerm)))
        );
      })
      .sort((a, b) => new Date(b.lastSeen).getTime() - new Date(a.lastSeen).getTime());
  }, [scopedEvents, severityFilter, search, devices]);

  const sortHistory = (items: AlertEvent[]) =>
    [...items].sort((a, b) => {
      if (orderBy === 'triggeredAt') {
        const timeA = new Date(a.triggeredAt || 0).getTime();
        const timeB = new Date(b.triggeredAt || 0).getTime();
        return order === 'asc' ? timeA - timeB : timeB - timeA;
      }

      if (orderBy === 'success') {
        const boolA = a.success ? 1 : 0;
        const boolB = b.success ? 1 : 0;
        return order === 'asc' ? boolA - boolB : boolB - boolA;
      }

      let valA = (a[orderBy] as string) || '';
      let valB = (b[orderBy] as string) || '';
      valA = valA.toString().toLowerCase();
      valB = valB.toString().toLowerCase();

      if (valA < valB) return order === 'asc' ? -1 : 1;
      if (valA > valB) return order === 'asc' ? 1 : -1;
      return 0;
    });

  const filterHistoryItems = (items: AlertEvent[], searchTerm: string, typeFilter: string) =>
    sortHistory(
      items.filter((item) => {
        if (!matchesScopeFilter(item, appliedFilters)) return false;
        const matchesFilter = typeFilter === 'All' || item.alertType === typeFilter;
        const term = searchTerm.toLowerCase();
        return (
          matchesFilter &&
          ((item.deviceName || '').toLowerCase().includes(term) ||
            (item.deviceIp || '').includes(term) ||
            (item.sentToEmails || []).some((e) => (e || '').toLowerCase().includes(term)) ||
            (item.alertType || '').toLowerCase().includes(term) ||
            (item.errorMessage || '').toLowerCase().includes(term))
        );
      }),
    );

  const dispatchedHistory = useMemo(
    () => alertHistory.filter((item) => !isAuditOnlyAlert(item)),
    [alertHistory],
  );

  const auditHistory = useMemo(
    () => alertHistory.filter((item) => isAuditOnlyAlert(item)),
    [alertHistory],
  );

  const filteredHistory = useMemo(
    () => filterHistoryItems(dispatchedHistory, historySearch, historyFilter),
    [dispatchedHistory, historyFilter, historySearch, orderBy, order, appliedFilters],
  );

  const filteredAudit = useMemo(
    () => filterHistoryItems(auditHistory, auditSearch, auditFilter),
    [auditHistory, auditFilter, auditSearch, orderBy, order, appliedFilters],
  );

  const showToast = (message: string) => {
    setToastMsg(message);
    setToastOpen(true);
  };

  const copyDetailedBriefs = (selectedGroups: GroupedEvent[]) => {
    const report = selectedGroups
      .map((group) => {
        const device = devices.find((d) => d.ipAddress === group.deviceIp);
        return `
[StacksAtlas INTELLIGENCE BRIEF]
-------------------------------
EVENT: ${group.type}
SEVERITY: ${group.severity.toUpperCase()}
DEVICE: ${device?.name || 'Unknown Node'} (${group.deviceIp || 'System Level'})
MESSAGE: ${group.message}
LAST SEEN: ${new Date(group.lastSeen).toLocaleString()}
TOTAL OCCURRENCES: ${group.count}
-------------------------------`.trim();
      })
      .join('\n\n');

    navigator.clipboard.writeText(report);
    showToast(
      selectedGroups.length > 1
        ? `Batch copied ${selectedGroups.length} briefs`
        : 'Intelligence brief copied',
    );
  };

  const copyNotificationBrief = (item: AlertEvent) => {
    const report = `
[StacksAtlas NOTIFICATION REPORT]
-------------------------------
TIMESTAMP: ${new Date(item.triggeredAt).toLocaleString()}
EVENT: ${item.alertType}
DEVICE: ${item.deviceName} (${item.deviceIp})
EMAILS: ${item.sentToEmails.length > 0 ? item.sentToEmails.join(', ') : 'None'}
WEBHOOKS: ${item.sentToWebhooks?.length > 0 ? item.sentToWebhooks.join(', ') : 'None'}
STATUS: ${item.success ? 'SUCCESS' : 'FAILED'}
${item.errorMessage ? `ERROR: ${item.errorMessage}` : ''}
-------------------------------`.trim();

    navigator.clipboard.writeText(report);
    showToast('Notification report copied');
  };

  const deleteGroups = async (keysToDismiss: string[]) => {
    const isBulk = keysToDismiss.length > 1;
    if (isBulk && !(await requestConfirm({
      message: `Confirm permanent dismissal of ${keysToDismiss.length} anomaly groups?`,
      confirmColor: 'warning',
    }))) return;

    const targetIdsToPurge: string[] = [];
    keysToDismiss.forEach((key) => {
      const foundGroup = filteredGroups.find((g) => g.groupKey === key);
      if (foundGroup) {
        foundGroup.allIds.forEach((idStr) => {
          if (idStr && idStr !== '[object Object]') targetIdsToPurge.push(idStr);
        });
      }
    });

    if (targetIdsToPurge.length === 0) return;

    targetIdsToPurge.forEach((id) => deletedIdsRef.current.add(id));
    setEvents((current) => current.filter((e) => !targetIdsToPurge.includes(getIdString(e.id))));
    setSelectedGroupKeys((prev) => prev.filter((k) => !keysToDismiss.includes(k)));

    try {
      let failCount = 0;
      for (const id of targetIdsToPurge) {
        try {
          await ApiService.deleteEvent(id);
        } catch {
          deletedIdsRef.current.delete(id);
          failCount++;
        }
      }
      if (failCount > 0) {
        showToast(`Cleared most, but ${failCount} failed.`);
        loadData();
      } else {
        showToast(isBulk ? `${keysToDismiss.length} Threads Dismissed` : 'Anomaly Dismissed');
      }
    } catch {
      showToast('Critical error during remote sync');
    }
  };

  const deleteAlerts = async (ids: string[]) => {
    const isBulk = ids.length > 1;
    if (isBulk && !(await requestConfirm({
      message: `Confirm permanent deletion of ${ids.length} notification logs?`,
      confirmColor: 'error',
    }))) return;

    try {
      let failCount = 0;
      for (const id of ids) {
        try {
          await ApiService.deleteAlert(id);
        } catch {
          failCount++;
        }
      }

      setAlertHistory((current) => current.filter((a) => !ids.includes(a.id)));
      setSelectedHistoryIds((prev) => prev.filter((id) => !ids.includes(id)));
      setSelectedAuditIds((prev) => prev.filter((id) => !ids.includes(id)));

      if (failCount > 0) {
        showToast(`Deleted most, but ${failCount} failed.`);
      } else {
        showToast(isBulk ? `${ids.length} Notifications Deleted` : 'Notification Log Deleted');
      }
    } catch {
      showToast('Error syncing deletion');
    }
  };

  const clearAllEvents = async () => {
    const ok = await requestConfirm({
      title: 'Wipe intelligence data',
      message: 'WIPE ALL INTELLIGENCE DATA?',
      confirmLabel: 'Wipe',
      confirmColor: 'error',
    });
    if (!ok) return;
    try {
      await ApiService.clearAllEvents(appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined);
      setEvents([]);
      setSelectedGroupKeys([]);
      deletedIdsRef.current.clear();
      showToast('Intelligence database flushed');
    } catch (err) {
      console.error(err);
    }
  };

  const clearHistory = async () => {
    const ok = await requestConfirm({
      title: 'Wipe notification history',
      message: 'WIPE ENTIRE NOTIFICATION HISTORY?',
      confirmLabel: 'Wipe',
      confirmColor: 'error',
    });
    if (!ok) return;
    try {
      await ApiService.clearAlertHistory(appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined);
      setAlertHistory([]);
      setSelectedHistoryIds([]);
      setSelectedAuditIds([]);
      showToast('Notification history flushed');
    } catch (err) {
      console.error(err);
    }
  };

  const scopeActive =
    appliedFilters.nodeIds.length > 0 ||
    Boolean(appliedFilters.client || appliedFilters.building || appliedFilters.room);

  return {
    activeTab,
    setActiveTab,
    devices,
    appliedFilters,
    setAppliedFilters,
    loading,
    historyLoading,
    severityFilter,
    setSeverityFilter,
    search,
    setSearch,
    selectedGroupKeys,
    setSelectedGroupKeys,
    historySearch,
    setHistorySearch,
    historyFilter,
    setHistoryFilter,
    auditSearch,
    setAuditSearch,
    auditFilter,
    setAuditFilter,
    selectedHistoryIds,
    setSelectedHistoryIds,
    selectedAuditIds,
    setSelectedAuditIds,
    orderBy,
    order,
    handleRequestSort,
    toastOpen,
    setToastOpen,
    toastMsg,
    filteredGroups,
    filteredHistory,
    filteredAudit,
    dispatchedHistory,
    auditHistory,
    events,
    alertHistory,
    scopeActive,
    copyDetailedBriefs,
    copyNotificationBrief,
    deleteGroups,
    deleteAlerts,
    clearAllEvents,
    clearHistory,
  };
}
