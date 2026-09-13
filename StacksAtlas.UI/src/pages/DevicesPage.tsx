import { useEffect, useState, useCallback, useMemo, useRef, startTransition } from "react";
import {
  Box,
  Typography,
  Button,
  Snackbar,
  TablePagination,
} from "@mui/material";
import { useSearchParams } from "react-router-dom";

import { useConfirm } from '../context/ConfirmContext';
import { useAuth } from '../context/AuthContext';
import { useGlobalStats } from "../context/GlobalStatsContext";
import { useColumnVisibility } from "../context/ColumnVisibilityContext";
import { ApiService } from "../services/apiService";
import type { OpenAvcLinkSummary } from "../services/apiService";
import type { Device } from "../models/Device";

import { PageHeader } from "../components/pageheader";
import { DeviceDrawer } from "../components/DeviceDrawer";

// Sub-components & Types
import {
  type ColumnKey,
  columnLabels,
  DEFAULT_COLUMN_ORDER,
  FEDERATION_COLUMN_KEYS,
  filterColumnsForPortable,
  type DeviceViewMode,
} from "../components/devices/types";
import { usePortableMode } from "../hooks/usePortableMode";
import { useIsMobileLayout } from "../hooks/useIsMobileLayout";
import { DeviceToolbar } from "../components/devices/DeviceToolbar";
import { DeviceGrid } from "../components/devices/DeviceGrid";
import { MobileDeviceList } from "../components/devices/mobile/MobileDeviceList";
import { MobileRemovedDeviceList } from "../components/devices/mobile/MobileRemovedDeviceList";
import { PageShell } from "../components/mobile/PageShell";
import { RemovedDeviceGrid } from "../components/devices/RemovedDeviceGrid";
import { DeviceBulkActionBar } from "../components/devices/DeviceBulkActionBar";
import type { DeviceGroupBy, DeviceGroupExpansion } from "../components/devices/deviceGrouping";
import { deviceGroupByStorageKey, normalizeDeviceGroupBy } from "../components/devices/deviceGrouping";
import { exportDevicesToCsv, exportRemovedDevicesToCsv, resolveInventoryExportColumns } from "../components/devices/deviceBulkExport";
import { downloadAssetCsv, downloadAssetCsvTemplate } from "../utils/deviceAssetCsv";
import {
  mapAttachmentParentRows,
  resolveAttachmentParentScope,
  type AttachmentParentOption,
} from "../utils/attachmentParentOptions";
export default function DevicesPage() {
  const { isAdmin, role } = useAuth();
  const canSelect = role?.toLowerCase() !== 'viewer';
  const { isHub } = useGlobalStats();
  const { isPortable } = usePortableMode();
  const isMobile = useIsMobileLayout();
  const { confirm } = useConfirm();

  const groupByStorageKey = deviceGroupByStorageKey(isHub);

  // --- GROUP BY PERSISTED STATE ---
  const [groupBy, setGroupBy] = useState<DeviceGroupBy>(() => {
    const saved = localStorage.getItem(deviceGroupByStorageKey(false));
    return normalizeDeviceGroupBy(saved, false);
  });
  const [groupExpansion, setGroupExpansion] = useState<DeviceGroupExpansion | null>(null);

  useEffect(() => {
    const saved = localStorage.getItem(groupByStorageKey);
    setGroupBy(normalizeDeviceGroupBy(saved, isHub));
  }, [groupByStorageKey, isHub]);

  useEffect(() => {
    localStorage.setItem(groupByStorageKey, groupBy);
  }, [groupBy, groupByStorageKey]);

  // --- REFRESH INTERVAL ---
  const [refreshInterval, setRefreshInterval] = useState(10);

  // --- COLUMN ORDER PERSISTED STATE ---
  const [columnOrder, setColumnOrder] = useState<ColumnKey[]>(() => {
    const saved = localStorage.getItem("device_column_order");
    if (saved) {
      try {
        const parsed = JSON.parse(saved);
        const validKeys = Object.keys(columnLabels) as ColumnKey[];
        const filtered = parsed.filter((k: string) => validKeys.includes(k as ColumnKey));
        const missing = validKeys.filter(k => !filtered.includes(k));
        return [...filtered, ...missing];
      } catch (e) {
        console.warn("Failed to parse column order", e);
      }
    }
    return DEFAULT_COLUMN_ORDER;
  });

  useEffect(() => {
    localStorage.setItem("device_column_order", JSON.stringify(columnOrder));
  }, [columnOrder]);

  // --- DYNAMIC COLUMNS VISIBILITY (from shared context) ---
  const { visibility } = useColumnVisibility();

  const effectiveColumnOrder = useMemo(
    () => (isPortable ? filterColumnsForPortable(columnOrder) : columnOrder),
    [columnOrder, isPortable]
  );

  const effectiveVisibility = useMemo(() => {
    if (!isPortable) return visibility;
    const next = { ...visibility };
    for (const key of FEDERATION_COLUMN_KEYS) {
      next[key] = false;
    }
    return next;
  }, [visibility, isPortable]);

  // Set widths
  const [columnWidths, setColumnWidths] = useState<Record<ColumnKey, number>>({
    name: 220,
    ipAddress: 140,
    hostname: 180,
    macAddress: 150,
    openPorts: 140,
    vendor: 150,
    status: 96,
    type: 140,
    model: 160,
    location: 150,
    lastSeen: 160,
    uptimePercent: 120,
    stabilityScore: 120,
    managedBy: 160,
    nodeId: 140,
    client: 150,
    building: 150,
    room: 150,
    discoveryVlanTag: 100,
    security: 76,
    serialNumber: 140,
    firmwareVersion: 140,
    assetTag: 120,
    warrantyExpiresUtc: 120,
    attachmentKind: 100,
    attachmentPort: 120,
    attachmentParent: 160,
  });

  // --- DATA STATES ---
  const [devices, setDevices] = useState<Device[]>([]);
  const [search, setSearch] = useState("");
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [viewMode, setViewMode] = useState<DeviceViewMode>("active");
  const [selectedDevice, setSelectedDevice] = useState<Device | null>(null);
  
  // Undo/Toast notification states
  const [showUndo, setShowUndo] = useState(false);
  const [snackbarMessage, setSnackbarMessage] = useState("Device removed from registry");
  const [lastDeletedId, setLastDeletedId] = useState<string | null>(null);

  const showToast = (message: string) => {
    setLastDeletedId(null);
    setSnackbarMessage(message);
    setShowUndo(true);
  };

  // Bulk selection
  const [selectedIds, setSelectedIds] = useState<Set<string>>(new Set());
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkProgress, setBulkProgress] = useState<{ current: number; total: number } | null>(null);

  // Filter States
  const [filterStatus, setFilterStatus] = useState<"All" | "online" | "offline" | "new">("All");
  const [filterVlanTags, setFilterVlanTags] = useState<string[]>([]);
  const [availableVlanTags, setAvailableVlanTags] = useState<string[]>([]);
  const [filterAttachmentKind, setFilterAttachmentKind] = useState("All");
  const [filterAttachmentPort, setFilterAttachmentPort] = useState("");
  const [filterAttachmentParentId, setFilterAttachmentParentId] = useState("");
  const [attachmentParentOptions, setAttachmentParentOptions] = useState<AttachmentParentOption[]>([]);
  const attachmentParentsFetchedRef = useRef(false);
  const attachmentParentsScopeKeyRef = useRef<string | undefined>(undefined);

  const parentNameById = useMemo(() => {
    const map: Record<string, string> = {};
    for (const d of devices) {
      map[d.id] = d.name?.trim() || d.hostname?.trim() || d.ipAddress;
    }
    for (const opt of attachmentParentOptions) {
      if (!map[opt.id]) map[opt.id] = opt.label;
    }
    return map;
  }, [devices, attachmentParentOptions]);

  useEffect(() => {
    if (!isPortable) return;
    if (groupBy !== "none") setGroupBy("none");
    if (filterVlanTags.length > 0) setFilterVlanTags([]);
  }, [isPortable, groupBy, filterVlanTags.length]);

  // Pagination State
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(50);
  const [totalCount, setTotalCount] = useState(0);
  const isGroupedView = !isPortable && groupBy !== "none" && viewMode !== "removed";

  const expandAllGroups = useCallback(() => {
    setGroupExpansion({ action: "expand", id: Date.now() });
  }, []);

  const collapseAllGroups = useCallback(() => {
    setGroupExpansion({ action: "collapse", id: Date.now() });
  }, []);

  // Auto-refresh states
  const [autoRefreshPaused, setAutoRefreshPaused] = useState(false);
  const [lastUpdated, setLastUpdated] = useState<Date | null>(null);

  // Sorting
  const [sortConfig, setSortConfig] = useState<{
    key: string;
    direction: "asc" | "desc";
  } | null>(null);

  const [users, setUsers] = useState<any[]>([]);
  const [ptpStatus, setPtpStatus] = useState<any>(null);
  const [nodesMap, setNodesMap] = useState<Record<string, string>>({});
  const [openAvcLinksMap, setOpenAvcLinksMap] = useState<Record<string, OpenAvcLinkSummary>>({});
  const [openAvcIntegrationStatus, setOpenAvcIntegrationStatus] = useState<string | null>(null);

  // --- INITIAL DATA FETCH ---
  useEffect(() => {
    if (isPortable || !isHub) return;
    async function loadNodes() {
      try {
        const data = await ApiService.getFederationNodes();
        if (Array.isArray(data)) {
          const map: Record<string, string> = {};
          data.forEach((node: { id?: string; name?: string }) => {
            if (node.id) {
              map[node.id] = node.name?.trim() || node.id;
            }
          });
          setNodesMap(map);
        }
      } catch {
        // Hub federation mapping optional in standalone mode
      }
    }
    loadNodes();
  }, [isPortable, isHub]);
  useEffect(() => {
    async function loadPolling() {
      try {
        const data = await ApiService.getPollingSettings();
        setRefreshInterval(data.refreshIntervalSeconds);
      } catch (err: any) {
        console.error("Failed to load polling settings:", err);
      }
    }
    loadPolling();
  }, []);

  useEffect(() => {
    if (isAdmin) {
      ApiService.getUsers().then(setUsers).catch(console.error);
    }
  }, [isAdmin]);

  useEffect(() => {
    ApiService.getPtpStatus().then(status => {
      if (status) setPtpStatus(status);
    }).catch(() => { });
  }, []);


  // --- QUERY ROUTING FOR ALERTS ---
  const [searchParams, setSearchParams] = useSearchParams();
  const filterNodeId = searchParams.get("nodeId") || "";

  const setFilterNodeId = useCallback((nodeId: string) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      if (nodeId) next.set("nodeId", nodeId);
      else next.delete("nodeId");
      return next;
    }, { replace: true });
    setPage(0);
  }, [setSearchParams]);

  const nodeFilterOptions = useMemo(
    () => Object.entries(nodesMap)
      .map(([id, label]) => ({ id, label }))
      .sort((a, b) => a.label.localeCompare(b.label)),
    [nodesMap],
  );

  const scopedSiteLabel = filterNodeId
    ? (nodesMap[filterNodeId] || filterNodeId)
    : null;

  const bulkAttachmentScope = useMemo(
    () => resolveAttachmentParentScope({
      filterNodeId,
      isHub,
      selectedDevices: devices.filter((d) => selectedIds.has(d.id)),
    }),
    [filterNodeId, isHub, devices, selectedIds],
  );

  const attachmentParentScopeKey = bulkAttachmentScope.nodeId
    ?? (bulkAttachmentScope.mixedSites ? "__mixed__" : "__fleet__");

  useEffect(() => {
    attachmentParentsFetchedRef.current = false;
    attachmentParentsScopeKeyRef.current = undefined;
    setAttachmentParentOptions([]);
  }, [attachmentParentScopeKey]);

  useEffect(() => {
    const targetId = searchParams.get("id");
    if (targetId && devices.length > 0) {
      const found = devices.find(d => d.id === targetId);
      if (found) {
        setSelectedDevice(found);
        setDrawerOpen(true);
      }
    }
  }, [searchParams, devices]);

  useEffect(() => {
    if (!selectedDevice || !drawerOpen || devices.length === 0) return;
    // While the drawer is open, avoid merging paginated list rows into the selected device  - 
    // list refresh can lag behind PATCH and would reset in-flight edits in child fields.
    const updated = devices.find((d) => d.id === selectedDevice.id);
    if (!updated) return;
    const listLastSeen = updated.lastSeen ?? '';
    const selectedLastSeen = selectedDevice.lastSeen ?? '';
    if (listLastSeen !== selectedLastSeen || updated.status !== selectedDevice.status) {
      setSelectedDevice((prev) => (prev ? { ...prev, ...updated } : prev));
    }
  }, [devices, selectedDevice, drawerOpen]);

  useEffect(() => {
    async function loadVlanTags() {
      try {
        const nodeIdParam = searchParams.get("nodeId") || undefined;
        const tags = await ApiService.getDiscoveryVlanTags(nodeIdParam);
        if (Array.isArray(tags)) setAvailableVlanTags(tags);
      } catch {
        setAvailableVlanTags([]);
      }
    }
    loadVlanTags();
  }, [searchParams]);

  const loadOpenAvcLinks = useCallback(() => {
    if (isPortable) return;
    ApiService.getOpenAvcLinks()
      .then((links) => {
        const map: Record<string, OpenAvcLinkSummary> = {};
        for (const link of links ?? []) {
          const raw = link as OpenAvcLinkSummary & { StacksAtlasDeviceId?: string };
          const id = String(raw.stacksAtlasDeviceId ?? raw.StacksAtlasDeviceId ?? '').toLowerCase();
          if (!id) continue;
          map[id] = {
            stacksAtlasDeviceId: id,
            openAvcDeviceId: raw.openAvcDeviceId ?? (raw as { OpenAvcDeviceId?: string }).OpenAvcDeviceId ?? '',
            driverName: raw.driverName ?? (raw as { DriverName?: string }).DriverName,
            driverId: raw.driverId ?? (raw as { DriverId?: string }).DriverId,
          };
        }
        setOpenAvcLinksMap(map);
      })
      .catch(() => {
        setOpenAvcLinksMap({});
      });
  }, [isPortable]);

  const refreshOpenAvcHealth = useCallback(() => {
    if (isPortable) return;
    ApiService.getOpenAvcHealth()
      .then((health) => setOpenAvcIntegrationStatus(health.status))
      .catch(() => setOpenAvcIntegrationStatus(null));
  }, [isPortable]);

  // --- PRIMARY FETCH ENGINE ---
  const fetchDevices = useCallback(() => {
    if (autoRefreshPaused) return;

    if (viewMode === "removed") {
      if (!isAdmin) return;
      ApiService.getRemovedDevices()
        .then((data: any) => {
          const list = Array.isArray(data) ? data : (data?.devices || []);
          startTransition(() => {
            setDevices(list);
            setLastUpdated(new Date());
          });
        })
        .catch((err) => {
          console.error("Error fetching removed devices:", err);
        });
      return;
    }

    const nodeIdParam = searchParams.get("nodeId");
    const groupedFetch = groupBy !== "none";

    ApiService.getDevices({
      skip: groupedFetch ? 0 : page * pageSize,
      take: groupedFetch ? 5000 : pageSize,
      search: search,
      status: filterStatus,
      showArchived: viewMode === "archive",
      nodeId: nodeIdParam || undefined,
      vlanTags: filterVlanTags.length > 0 ? filterVlanTags : undefined,
      attachmentKind: filterAttachmentKind,
      attachmentPort: filterAttachmentPort,
      attachmentParentId: filterAttachmentParentId || undefined,
    })
      .then((data: any) => {
        if (data && data.devices) {
          startTransition(() => {
            setDevices(data.devices || []);
            setTotalCount(data.totalCount || 0);
            setLastUpdated(new Date());
          });
        } else {
          startTransition(() => {
            setDevices(data || []);
            setTotalCount(Array.isArray(data) ? data.length : 0);
            setLastUpdated(new Date());
          });
        }
      })
      .catch((err) => {
        console.error("Error fetching devices:", err);
      });
  }, [viewMode, isAdmin, autoRefreshPaused, page, pageSize, groupBy, search, filterStatus, filterVlanTags, filterAttachmentKind, filterAttachmentPort, filterAttachmentParentId, searchParams]);

  const handleDrawerDeviceUpdate = useCallback((updated?: Device) => {
    if (updated?.id) {
      setSelectedDevice(updated);
      setDevices((prev) => prev.map((d) => (d.id === updated.id ? { ...d, ...updated } : d)));
      return;
    }
    if (selectedDevice?.id && drawerOpen) {
      void ApiService.getDevice(selectedDevice.id)
        .then((fresh: Device) => {
          setSelectedDevice(fresh);
          setDevices((prev) => prev.map((d) => (d.id === fresh.id ? { ...d, ...fresh } : d)));
        })
        .catch(() => { /* list refresh below */ });
    }
    fetchDevices();
  }, [drawerOpen, fetchDevices, selectedDevice?.id]);

  useEffect(() => {
    fetchDevices();
    loadOpenAvcLinks();
    refreshOpenAvcHealth();
    const interval = setInterval(() => {
      fetchDevices();
      loadOpenAvcLinks();
    }, refreshInterval * 1000);
    return () => clearInterval(interval);
  }, [fetchDevices, loadOpenAvcLinks, refreshOpenAvcHealth, refreshInterval]);

  useEffect(() => {
    if (isPortable) return;
    const healthInterval = setInterval(refreshOpenAvcHealth, 5 * 60 * 1000);
    return () => clearInterval(healthInterval);
  }, [isPortable, refreshOpenAvcHealth]);

  // --- CLIENT-SIDE SORTING (active / archive) ---
  const filtered = useMemo(() => {
    if (viewMode === "removed") return [];

    let list = [...devices];

    if (sortConfig) {
      const { key, direction } = sortConfig;

      list = list.sort((a, b) => {
        if (key === "lastSeen") {
          const timeA = new Date(a.lastSeen).getTime();
          const timeB = new Date(b.lastSeen).getTime();
          return direction === "asc" ? timeA - timeB : timeB - timeA;
        }

        if (key === "ipAddress") {
          const ipA = (a.ipAddress || "").split('.').map(Number);
          const ipB = (b.ipAddress || "").split('.').map(Number);

          for (let i = 0; i < 4; i++) {
            const valA = ipA[i] || 0;
            const valB = ipB[i] || 0;
            if (valA < valB) return direction === "asc" ? -1 : 1;
            if (valA > valB) return direction === "asc" ? 1 : -1;
          }
          return 0;
        }

        if (key === "discoveryVlanTag") {
          const numA = Number(a.discoveryVlanTag);
          const numB = Number(b.discoveryVlanTag);
          const bothNumeric = !Number.isNaN(numA) && !Number.isNaN(numB) && /^\d+$/.test(a.discoveryVlanTag || "") && /^\d+$/.test(b.discoveryVlanTag || "");
          if (bothNumeric) {
            return direction === "asc" ? numA - numB : numB - numA;
          }
        }

        if (key === "attachmentParent") {
          const nameA = (a.attachmentParentDeviceId
            ? parentNameById[a.attachmentParentDeviceId] || a.attachmentParentMac || a.attachmentParentDeviceId
            : "").toLowerCase();
          const nameB = (b.attachmentParentDeviceId
            ? parentNameById[b.attachmentParentDeviceId] || b.attachmentParentMac || b.attachmentParentDeviceId
            : "").toLowerCase();
          return direction === "asc" ? nameA.localeCompare(nameB) : nameB.localeCompare(nameA);
        }

        const valA = String((a as unknown as Record<string, unknown>)[key] ?? "").toLowerCase();
        const valB = String((b as unknown as Record<string, unknown>)[key] ?? "").toLowerCase();
        return direction === "asc"
          ? valA.localeCompare(valB)
          : valB.localeCompare(valA);
      });
    }

    return list;
  }, [devices, sortConfig, viewMode, parentNameById]);

  const removedFiltered = useMemo(() => {
    if (viewMode !== "removed") return [];
    let list = [...devices];
    const nodeIdParam = searchParams.get("nodeId");
    if (nodeIdParam) {
      list = list.filter(d => d.nodeId === nodeIdParam);
    }
    if (search.trim()) {
      const s = search.toLowerCase();
      list = list.filter(d =>
        (d.name || "").toLowerCase().includes(s) ||
        (d.ipAddress || "").includes(s) ||
        (d.macAddress || "").toLowerCase().includes(s) ||
        (d.hostname || "").toLowerCase().includes(s) ||
        (d.removedBy || "").toLowerCase().includes(s) ||
        (nodesMap[d.nodeId] || d.nodeId || "").toLowerCase().includes(s)
      );
    }
    list.sort((a, b) => {
      const ta = new Date(a.removedUtc || 0).getTime();
      const tb = new Date(b.removedUtc || 0).getTime();
      return tb - ta;
    });
    return list;
  }, [devices, search, viewMode, nodesMap, searchParams]);

  const removedPaged = useMemo(() => {
    const start = page * pageSize;
    return removedFiltered.slice(start, start + pageSize);
  }, [removedFiltered, page, pageSize]);

  useEffect(() => {
    if (viewMode === "removed") {
      setTotalCount(removedFiltered.length);
    }
  }, [viewMode, removedFiltered.length]);

  const selectableOnPage = useMemo(
    () => viewMode === "removed" ? removedPaged : filtered,
    [viewMode, removedPaged, filtered]
  );
  const selectedOnPageCount = selectableOnPage.filter(d => selectedIds.has(d.id)).length;
  const allPageSelected = selectableOnPage.length > 0 && selectedOnPageCount === selectableOnPage.length;
  const pageIndeterminate = selectedOnPageCount > 0 && !allPageSelected;

  useEffect(() => {
    setSelectedIds(new Set());
  }, [page, pageSize, viewMode, search, filterStatus, filterVlanTags, filterAttachmentKind, filterAttachmentPort, filterAttachmentParentId, searchParams]);

  useEffect(() => {
    setPage(0);
  }, [viewMode]);

  useEffect(() => {
    setPage(0);
  }, [groupBy]);

  useEffect(() => {
    if (viewMode === "removed" && !isAdmin) {
      setViewMode("active");
    }
  }, [viewMode, isAdmin]);

  const toggleSelect = useCallback((id: string) => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);

  const toggleSelectPage = useCallback(() => {
    setSelectedIds(prev => {
      const next = new Set(prev);
      if (allPageSelected) {
        selectableOnPage.forEach(d => next.delete(d.id));
      } else {
        selectableOnPage.forEach(d => next.add(d.id));
      }
      return next;
    });
  }, [allPageSelected, selectableOnPage]);

  const buildDeviceQueryParams = useCallback(() => {
    const nodeIdParam = searchParams.get("nodeId");
    return {
      search,
      status: filterStatus,
      showArchived: viewMode === "archive",
      nodeId: nodeIdParam || undefined,
      vlanTags: filterVlanTags.length > 0 ? filterVlanTags : undefined,
      attachmentKind: filterAttachmentKind,
      attachmentPort: filterAttachmentPort,
      attachmentParentId: filterAttachmentParentId || undefined,
    };
  }, [search, filterStatus, viewMode, filterVlanTags, filterAttachmentKind, filterAttachmentPort, filterAttachmentParentId, searchParams]);

  const loadAttachmentParents = useCallback(async () => {
    const { nodeId: nodeIdParam } = bulkAttachmentScope;
    const scopeKey = attachmentParentScopeKey;

    if (
      attachmentParentsFetchedRef.current
      && attachmentParentsScopeKeyRef.current === scopeKey
    ) {
      return;
    }

    attachmentParentsFetchedRef.current = true;
    attachmentParentsScopeKeyRef.current = scopeKey;

    try {
      const rows = await ApiService.getAttachmentParents({ nodeId: nodeIdParam });
      const includeSitePrefix = isHub && !nodeIdParam;
      const options = mapAttachmentParentRows(rows, nodesMap, includeSitePrefix);
      setAttachmentParentOptions(options);
    } catch (err) {
      attachmentParentsFetchedRef.current = false;
      console.error("Failed to load attachment parents:", err);
    }
  }, [attachmentParentScopeKey, bulkAttachmentScope, isHub, nodesMap]);

  const resolveDeviceForAttachment = useCallback(async (id: string): Promise<Device> => {
    const cached = devices.find((d) => d.id === id);
    if (cached) return cached;
    return (await ApiService.getDevice(id)) as Device;
  }, [devices]);

  const selectAllMatching = useCallback(async () => {
    if (viewMode === "removed") {
      setSelectedIds(new Set(removedFiltered.map(d => d.id)));
      return;
    }
    try {
      const data = await ApiService.getDevices({
        ...buildDeviceQueryParams(),
        skip: 0,
        take: totalCount || 10000,
      });
      const list: Device[] = data?.devices || data || [];
      const ids = list.map(d => d.id);
      setSelectedIds(new Set(ids));
    } catch (err) {
      console.error("Failed to select all matching devices:", err);
    }
  }, [viewMode, removedFiltered, buildDeviceQueryParams, totalCount]);

  const runBulkSequential = useCallback(async (
    ids: string[],
    action: (id: string) => Promise<unknown>,
    confirmMsg?: string,
    clearSelectionAfter = false,
  ) => {
    if (ids.length === 0) return;
    if (confirmMsg && !(await confirm({ message: confirmMsg, confirmColor: 'warning' }))) return;

    setBulkBusy(true);
    let ok = 0;
    let fail = 0;
    let lastError = '';
    for (let i = 0; i < ids.length; i++) {
      setBulkProgress({ current: i + 1, total: ids.length });
      try {
        await action(ids[i]);
        ok++;
      } catch (err) {
        console.error(err);
        fail++;
        lastError = err instanceof Error ? err.message : 'Request failed';
      }
    }
    setBulkBusy(false);
    setBulkProgress(null);
    if (clearSelectionAfter) {
      setSelectedIds(new Set());
    }
    setShowUndo(false);
    const failDetail = fail > 0 && lastError ? ` (${lastError})` : '';
    setSnackbarMessage(`Bulk action: ${ok} succeeded${fail ? `, ${fail} failed${failDetail}` : ''}`);
    setShowUndo(true);
    fetchDevices();
  }, [confirm, fetchDevices]);

  const handleBulkArchive = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.deleteDevice(id, false),
      `Archive ${ids.length} device${ids.length === 1 ? '' : 's'}? They can be restored from the Archive view.`,
      true,
    );
  };

  const handleBulkRestore = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.restoreDevice(id),
      `Restore ${ids.length} device${ids.length === 1 ? '' : 's'} to active inventory?`,
      true,
    );
  };

  const handleBulkPermanentDelete = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.deleteDevice(id, true),
      `Remove ${ids.length} device${ids.length === 1 ? '' : 's'} from fleet? They will be blocked from rediscovery until restored.`,
      true,
    );
  };

  const handleBulkRestoreToFleet = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.restoreToFleet(id),
      `Restore ${ids.length} device${ids.length === 1 ? '' : 's'} to the active fleet?`,
      true,
    );
  };

  const handleBulkEnableAlerts = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(ids, (id) => ApiService.updateDeviceAlerts(id, true));
  };

  const handleBulkDisableAlerts = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(ids, (id) => ApiService.updateDeviceAlerts(id, false));
  };

  const handleBulkClearWarranty = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceAsset(id, { warrantyExpiresUtc: null }),
      `Clear warranty on ${ids.length} device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkClearAssetTag = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceAsset(id, { assetTag: '' }),
      `Clear asset tag on ${ids.length} device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkClearManualAssetFields = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.clearManualDeviceAssetFields(id),
      `Clear manually entered serial and firmware on ${ids.length} device${ids.length === 1 ? '' : 's'}? OpenAVC values are kept.`,
    );
  };

  const handleBulkPing = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.runDevicePing(id),
      `Ping ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkDeepScan = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.triggerDeepScan(id),
      `Queue deep scan on ${ids.length} device${ids.length === 1 ? '' : 's'}? Scans run on the enrolled node (may take several minutes each).`,
    );
  };

  const handleBulkSetType = (type: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceType(id, type),
      `Set device type to "${type}" on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkSetAttachmentKind = (kind: string) => {
    const ids = Array.from(selectedIds);
    const label = kind === "WiFi" ? "Wi-Fi" : kind;
    runBulkSequential(
      ids,
      async (id) => {
        const d = await resolveDeviceForAttachment(id);
        return ApiService.updateDeviceAttachment(id, {
          kind,
          port: d.attachmentPort ?? "",
          parentDeviceId: d.attachmentParentDeviceId ?? null,
        });
      },
      `Set network mode to "${label}" on ${ids.length} selected device${ids.length === 1 ? "" : "s"}?`,
    );
  };

  const handleBulkSetAttachmentPort = (port: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      async (id) => {
        const d = await resolveDeviceForAttachment(id);
        return ApiService.updateDeviceAttachment(id, {
          kind: d.attachmentKind || "Unknown",
          port,
          parentDeviceId: d.attachmentParentDeviceId ?? null,
        });
      },
      `Set SSID to "${port}" on ${ids.length} selected device${ids.length === 1 ? "" : "s"}?`,
    );
  };

  const handleBulkSetAttachmentParent = (parentDeviceId: string) => {
    if (bulkAttachmentScope.mixedSites) {
      showToast("Select devices from a single site before setting uplink.");
      return;
    }
    const ids = Array.from(selectedIds);
    const parentLabel = parentNameById[parentDeviceId] || parentDeviceId;
    runBulkSequential(
      ids,
      async (id) => {
        const d = await resolveDeviceForAttachment(id);
        return ApiService.updateDeviceAttachment(id, {
          kind: d.attachmentKind || "Unknown",
          port: d.attachmentPort ?? "",
          parentDeviceId,
        });
      },
      `Set uplink to "${parentLabel}" on ${ids.length} selected device${ids.length === 1 ? "" : "s"}?`,
    );
  };

  const handleBulkClearAttachmentParent = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      async (id) => {
        const d = await resolveDeviceForAttachment(id);
        return ApiService.updateDeviceAttachment(id, {
          kind: d.attachmentKind || "Unknown",
          port: d.attachmentPort ?? "",
          parentDeviceId: null,
        });
      },
      `Clear uplink on ${ids.length} selected device${ids.length === 1 ? "" : "s"}?`,
    );
  };

  const handleBulkClearAttachmentPort = () => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      async (id) => {
        const d = await resolveDeviceForAttachment(id);
        return ApiService.updateDeviceAttachment(id, {
          kind: d.attachmentKind || "Unknown",
          port: "",
          parentDeviceId: d.attachmentParentDeviceId ?? null,
        });
      },
      `Clear SSID on ${ids.length} selected device${ids.length === 1 ? "" : "s"}?`,
    );
  };

  const handleBulkSetLocation = (location: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceLocation(id, location),
      `Set location to "${location}" on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkSetVendor = (vendor: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceVendor(id, vendor),
      `Set vendor to "${vendor}" on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkSetModel = (model: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceModel(id, model),
      `Set model to "${model}" on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkSetFirmware = (firmware: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceAsset(id, { firmwareVersion: firmware }),
      `Set firmware to "${firmware}" on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleBulkSetWarranty = (warrantyUtc: string) => {
    const ids = Array.from(selectedIds);
    runBulkSequential(
      ids,
      (id) => ApiService.updateDeviceAsset(id, { warrantyExpiresUtc: warrantyUtc }),
      `Set warranty date on ${ids.length} selected device${ids.length === 1 ? '' : 's'}?`,
    );
  };

  const handleExportSelected = async () => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0) return;

    if (viewMode === "removed") {
      const selected = removedFiltered.filter(d => selectedIds.has(d.id));
      exportRemovedDevicesToCsv(selected, 'removed_devices', { portable: isPortable, nodesMap });
      return;
    }

    const onPage = filtered.filter(d => selectedIds.has(d.id));
    if (onPage.length === ids.length) {
      exportDevicesToCsv(onPage, 'selected_devices_visible', inventoryExportOptions('visible'));
      return;
    }

    try {
      const data = await ApiService.getDevices({
        ...buildDeviceQueryParams(),
        skip: 0,
        take: totalCount || ids.length,
      });
      const list: Device[] = (data?.devices || []).filter((d: Device) => selectedIds.has(d.id));
      exportDevicesToCsv(list, 'selected_devices_visible', inventoryExportOptions('visible'));
    } catch (err) {
      console.error("Export selected failed:", err);
    }
  };

  const inventoryExportOptions = useCallback((mode: 'visible' | 'all') => ({
    portable: isPortable,
    nodesMap,
    parentNameById,
    columnKeys: resolveInventoryExportColumns(effectiveColumnOrder, effectiveVisibility, isPortable, mode),
  }), [effectiveColumnOrder, effectiveVisibility, isPortable, nodesMap, parentNameById]);

  const exportFilteredToCSV = (mode: 'visible' | 'all' = 'visible') => {
    if (viewMode === "removed") {
      exportRemovedDevicesToCsv(removedFiltered, 'removed_devices', { portable: isPortable, nodesMap });
      return;
    }
    const prefix = mode === 'visible' ? 'filtered_devices_visible' : 'filtered_devices_full';
    exportDevicesToCsv(filtered, prefix, inventoryExportOptions(mode));
  };

  const exportFilteredAssetsCSV = () => {
    if (viewMode === "removed") return;
    downloadAssetCsv(filtered, 'asset_inventory', isPortable);
  };

  const resolveSelectedDevices = async (): Promise<Device[]> => {
    const ids = Array.from(selectedIds);
    const onPage = filtered.filter(d => selectedIds.has(d.id));
    if (onPage.length === ids.length) return onPage;

    const data = await ApiService.getDevices({
      ...buildDeviceQueryParams(),
      skip: 0,
      take: totalCount || ids.length,
    });
    return (data?.devices || []).filter((d: Device) => selectedIds.has(d.id));
  };

  const handleExportSelectedAssets = async () => {
    const ids = Array.from(selectedIds);
    if (ids.length === 0 || viewMode === 'removed') return;

    try {
      const list = await resolveSelectedDevices();
      downloadAssetCsv(list, 'selected_assets', isPortable);
    } catch (err) {
      console.error("Asset export failed:", err);
    }
  };

  const handleImportAssets = async (file: File) => {
    try {
      const csv = await file.text();
      const result = await ApiService.importDeviceAssets({ csv }) as {
        updated: number;
        notFound: number;
        failed: number;
        skippedOpenAvc?: number;
      };
      const skipped = result.skippedOpenAvc ?? 0;
      showToast(
        `Asset import: ${result.updated} updated, ${result.notFound} not found, ${result.failed} failed${skipped ? `, ${skipped} OpenAVC field(s) kept` : ''}.`,
      );
      if (result.updated > 0) fetchDevices();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : String(err);
      showToast(`Asset import failed: ${message}`);
    }
  };

  // --- EXPORT TO CSV ENGINE ---

  // --- DANGER ZONE HANDLERS ---
  const handleDeleteDevice = async () => {
    if (!selectedDevice?.id) return;
    const idToKill = selectedDevice.id;

    try {
      await ApiService.deleteDevice(idToKill);
      setSnackbarMessage("Device removed from registry");
      setLastDeletedId(idToKill);
      setShowUndo(true);
      setDrawerOpen(false);
      fetchDevices();
    } catch (err: any) {
      console.error("Delete failed", err);
    }
  };

  const handleUndo = async () => {
    const idToRestore = selectedDevice?.id || lastDeletedId;
    if (!idToRestore) return;

    try {
      await ApiService.restoreDevice(idToRestore);
      setSnackbarMessage("Device restored to inventory");
      setShowUndo(false);
      setDrawerOpen(false);
      fetchDevices();
    } catch (err: any) {
      console.error("Restore failed:", err);
    }
  };

  const handleHardDeleteDevice = async () => {
    if (!selectedDevice?.id) return;
    const idToNuke = selectedDevice.id;

    const ok = await confirm({
      title: 'Remove from fleet',
      message:
        'Remove this device from fleet permanently? It will be hidden and blocked from rediscovery until restored.',
      confirmLabel: 'Remove',
      confirmColor: 'error',
    });
    if (!ok) return;

    try {
      await ApiService.deleteDevice(idToNuke, true);
      setSnackbarMessage("Device removed from fleet.");
      setShowUndo(false);
      setDrawerOpen(false);
      fetchDevices();
    } catch (err: any) {
      console.error("Remove from fleet failed", err);
      showToast("Failed to remove device: " + err.message);
    }
  };

  const handleRestoreToFleet = async (device?: Device) => {
    const target = device ?? selectedDevice;
    if (!target?.id) return;

    const ok = await confirm({
      title: 'Restore to fleet',
      message: `Restore "${target.name || target.ipAddress}" to the active fleet?`,
    });
    if (!ok) return;

    try {
      await ApiService.restoreToFleet(target.id);
      setSnackbarMessage("Device restored to fleet.");
      setShowUndo(false);
      setDrawerOpen(false);
      setSelectedIds(new Set());
      fetchDevices();
    } catch (err: any) {
      console.error("Restore to fleet failed", err);
      showToast("Failed to restore device: " + (err.message || err));
    }
  };

  // --- DRAWER LIFECYCLE ---
  const openDrawer = (device: Device) => {
    setSelectedIds(new Set());
    setSelectedDevice(device);
    setDrawerOpen(true);
  };

  const closeDrawer = () => {
    setDrawerOpen(false);
    setSelectedDevice(null);
    setSearchParams({});
  };

  // --- SORT ACTION ---
  const handleSort = (key: string) => {
    let direction: "asc" | "desc" = "asc";
    if (sortConfig?.key === key && sortConfig.direction === "asc") {
      direction = "desc";
    }
    setSortConfig({ key, direction });
  };

  const deviceStats = [
    {
      label: "LIVE STATUS",
      value: autoRefreshPaused ? "PAUSED" : "ACTIVE",
      color: autoRefreshPaused ? "warning.main" : "success.main"
    },
    {
      label: viewMode === "removed" ? "REMOVED" : "TOTAL NODES",
      value: viewMode === "removed" ? removedFiltered.length : devices.length
    }
  ];



  return (
    <PageShell isMobile={isMobile}>
      {/* Header */}
      <PageHeader
        title="NETWORK INVENTORY"
        subtitle={scopedSiteLabel ? `SITE: ${scopedSiteLabel.toUpperCase()}` : "NODE REGISTRY"}
        stats={deviceStats}
      />

      {/* 3. Refactored Control Toolbar */}
      <DeviceToolbar
        isMobile={isMobile}
        search={search}
        setSearch={setSearch}
        filterStatus={filterStatus}
        setFilterStatus={setFilterStatus}
        filterVlanTags={filterVlanTags}
        setFilterVlanTags={setFilterVlanTags}
        availableVlanTags={availableVlanTags}
        filterAttachmentKind={filterAttachmentKind}
        setFilterAttachmentKind={setFilterAttachmentKind}
        filterAttachmentPort={filterAttachmentPort}
        setFilterAttachmentPort={setFilterAttachmentPort}
        filterAttachmentParentId={filterAttachmentParentId}
        setFilterAttachmentParentId={setFilterAttachmentParentId}
        attachmentParentOptions={attachmentParentOptions}
        onLoadAttachmentParents={loadAttachmentParents}
        filtersActive={
          filterStatus !== 'All'
          || filterVlanTags.length > 0
          || filterAttachmentKind !== 'All'
          || filterAttachmentPort.trim().length > 0
          || filterAttachmentParentId.length > 0
          || filterNodeId.length > 0
          || search.trim().length > 0
        }
        onClearFilters={() => {
          setSearch('');
          setFilterStatus('All');
          setFilterVlanTags([]);
          setFilterAttachmentKind('All');
          setFilterAttachmentPort('');
          setFilterAttachmentParentId('');
          setFilterNodeId('');
          setPage(0);
        }}
        viewMode={viewMode}
        setViewMode={setViewMode}
        isAdmin={isAdmin}
        groupBy={groupBy}
        setGroupBy={setGroupBy}
        onExpandAllGroups={isGroupedView ? expandAllGroups : undefined}
        onCollapseAllGroups={isGroupedView ? collapseAllGroups : undefined}
        isHub={isHub}
        filterNodeId={filterNodeId}
        setFilterNodeId={isHub ? setFilterNodeId : undefined}
        nodeFilterOptions={nodeFilterOptions}
        autoRefreshPaused={autoRefreshPaused}
        setAutoRefreshPaused={setAutoRefreshPaused}
        lastUpdated={lastUpdated}
        exportFilteredToCSV={exportFilteredToCSV}
        exportFilteredAssetsCSV={exportFilteredAssetsCSV}
        onImportAssets={canSelect ? handleImportAssets : undefined}
        onDownloadAssetTemplate={() => downloadAssetCsvTemplate(isPortable)}
        canImportAssets={canSelect && viewMode !== 'removed'}
        columnOrder={columnOrder}
        setColumnOrder={setColumnOrder}
        isPortable={isPortable}
        deviceCount={{ showing: filtered.length, total: totalCount }}
      />

      {canSelect && allPageSelected && totalCount > selectableOnPage.length && (
        <Box sx={{ mb: 1.5, textAlign: 'center' }}>
          <Typography variant="caption" color="text.secondary">
            All {selectableOnPage.length} devices on this page are selected.{' '}
            <Button size="small" disabled={bulkBusy} onClick={selectAllMatching}>
              Select all {totalCount} matching filters
            </Button>
          </Typography>
        </Box>
      )}

      {/* 4. Data grid  -  active/archive vs removed */}
      {viewMode === "removed" ? (
        isMobile ? (
          <MobileRemovedDeviceList
            devices={removedPaged}
            nodesMap={nodesMap}
            isPortable={isPortable}
            selectionEnabled={canSelect}
            isAdmin={isAdmin}
            selectedIds={selectedIds}
            busy={bulkBusy}
            onToggleSelect={toggleSelect}
            onDeviceClick={openDrawer}
            onRestoreToFleet={handleRestoreToFleet}
          />
        ) : (
        <RemovedDeviceGrid
          devices={removedPaged}
          nodesMap={nodesMap}
          isPortable={isPortable}
          selectionEnabled={canSelect}
          isAdmin={isAdmin}
          selectedIds={selectedIds}
          allPageSelected={allPageSelected}
          pageIndeterminate={pageIndeterminate}
          onToggleSelect={toggleSelect}
          onToggleSelectPage={toggleSelectPage}
          onRestoreToFleet={handleRestoreToFleet}
          onDeviceClick={openDrawer}
          busy={bulkBusy}
        />
        )
      ) : isMobile ? (
        <MobileDeviceList
          devices={filtered}
          groupBy={isPortable ? "none" : groupBy}
          ptpStatus={ptpStatus}
          nodesMap={nodesMap}
          parentNameById={parentNameById}
          openAvcLinksMap={openAvcLinksMap}
          openAvcIntegrationStatus={openAvcIntegrationStatus ?? undefined}
          isHub={isHub}
          selectionEnabled={canSelect}
          selectedIds={selectedIds}
          onToggleSelect={toggleSelect}
          onDeviceClick={openDrawer}
          groupExpansion={groupExpansion}
        />
      ) : (
        <DeviceGrid
          filtered={filtered}
          groupBy={isPortable ? "none" : groupBy}
          columnOrder={effectiveColumnOrder}
          visibility={effectiveVisibility}
          columnWidths={columnWidths}
          setColumnWidths={setColumnWidths}
          sortConfig={sortConfig}
          onSort={handleSort}
          onDeviceClick={openDrawer}
          ptpStatus={ptpStatus}
          nodesMap={nodesMap}
          openAvcLinksMap={openAvcLinksMap}
          openAvcIntegrationStatus={openAvcIntegrationStatus ?? undefined}
          selectionEnabled={canSelect}
          allPageSelected={allPageSelected}
          pageIndeterminate={pageIndeterminate}
          selectedIds={selectedIds}
          onToggleSelect={toggleSelect}
          onToggleSelectPage={toggleSelectPage}
          parentNameById={parentNameById}
          groupExpansion={groupExpansion}
        />
      )}

      <DeviceBulkActionBar
        selectedCount={selectedIds.size}
        viewMode={viewMode}
        isAdmin={isAdmin}
        canEditAssets={canSelect}
        busy={bulkBusy}
        progress={bulkProgress}
        attachmentParentOptions={attachmentParentOptions}
        onLoadAttachmentParents={loadAttachmentParents}
        uplinkMixedSites={bulkAttachmentScope.mixedSites}
        uplinkScopedSiteLabel={
          bulkAttachmentScope.nodeId
            ? (nodesMap[bulkAttachmentScope.nodeId] || bulkAttachmentScope.nodeId)
            : null
        }
        onClear={() => setSelectedIds(new Set())}
        onArchive={handleBulkArchive}
        onRestore={handleBulkRestore}
        onPermanentDelete={handleBulkPermanentDelete}
        onRestoreToFleet={handleBulkRestoreToFleet}
        onExportSelected={handleExportSelected}
        onExportAssetSelected={handleExportSelectedAssets}
        onEnableAlerts={handleBulkEnableAlerts}
        onDisableAlerts={handleBulkDisableAlerts}
        onClearWarranty={handleBulkClearWarranty}
        onClearAssetTag={handleBulkClearAssetTag}
        onClearManualAssetFields={handleBulkClearManualAssetFields}
        onBulkPing={handleBulkPing}
        onBulkDeepScan={handleBulkDeepScan}
        onBulkSetType={handleBulkSetType}
        onBulkSetLocation={handleBulkSetLocation}
        onBulkSetVendor={handleBulkSetVendor}
        onBulkSetModel={handleBulkSetModel}
        onBulkSetFirmware={handleBulkSetFirmware}
        onBulkSetWarranty={handleBulkSetWarranty}
        onBulkSetAttachmentKind={handleBulkSetAttachmentKind}
        onBulkSetAttachmentParent={handleBulkSetAttachmentParent}
        onBulkClearAttachmentParent={handleBulkClearAttachmentParent}
        onBulkSetSsid={handleBulkSetAttachmentPort}
        onBulkClearSsid={handleBulkClearAttachmentPort}
      />

      {/* 5. Pagination */}
      {!isGroupedView && (
      <TablePagination
        component="div"
        count={totalCount}
        page={page}
        onPageChange={(_, newPage) => setPage(newPage)}
        rowsPerPage={pageSize}
        onRowsPerPageChange={(e) => {
          setPageSize(parseInt(e.target.value, 10));
          setPage(0);
        }}
        rowsPerPageOptions={isMobile ? [25, 50, 100] : [50, 100, 250]}
        sx={{
          mt: 2,
          border: "1px solid rgba(255,255,255,0.1)",
          borderRadius: 2,
          bgcolor: "background.paper",
          overflowX: isMobile ? 'auto' : undefined,
          maxWidth: '100%',
          "& .MuiTablePagination-toolbar": {
            minHeight: 48,
            flexWrap: isMobile ? 'wrap' : 'nowrap',
            justifyContent: isMobile ? 'center' : 'flex-end',
            px: isMobile ? 1 : 2,
            gap: isMobile ? 0.5 : 0,
          },
          "& .MuiTablePagination-selectLabel, & .MuiTablePagination-displayedRows": {
            fontSize: isMobile ? '0.7rem' : undefined,
            m: 0,
          },
          "& .MuiTablePagination-actions": {
            ml: isMobile ? 0 : undefined,
          },
        }}
      />
      )}

      {/* 6. Side Drawer */}
      <DeviceDrawer
        open={drawerOpen}
        onClose={closeDrawer}
        device={selectedDevice}
        users={users}
        onDeviceUpdate={handleDrawerDeviceUpdate}
        onDelete={handleDeleteDevice}
        onRestore={handleUndo}
        onHardDelete={handleHardDeleteDevice}
        onRestoreToFleet={handleRestoreToFleet}
        viewMode={viewMode}
        nodesMap={nodesMap}
        onNotify={showToast}
      />

      {/* 7. Snackbar notifications */}
      <Snackbar
        open={showUndo}
        autoHideDuration={viewMode === "active" ? 8000 : 3000}
        onClose={() => setShowUndo(false)}
        message={snackbarMessage}
        anchorOrigin={{ vertical: 'top', horizontal: 'right' }}
        sx={{ zIndex: 2400, mt: 7, mr: 1 }}
        action={
          viewMode === "active" && lastDeletedId ? (
            <Button
              color="secondary"
              size="small"
              onClick={handleUndo}
              sx={{ fontWeight: 700, letterSpacing: 1 }}
            >
              UNDO
            </Button>
          ) : undefined
        }
      />
    </PageShell>
  );
}
