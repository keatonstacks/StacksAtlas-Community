import { useEffect, useMemo, useState, startTransition } from 'react';
import { useTheme } from '@mui/material';
import { useNavigate } from 'react-router-dom';
import { ApiService } from '../../services/apiService';
import { useAuth } from '../../context/AuthContext';
import type { Device } from '../../models/Device';
import type { DrawerUser } from '../../components/device-drawer/types';
import type { NodeDashboardController, NodeDashboardSummary } from './nodeDashboardTypes';

export function useNodeDashboard(): NodeDashboardController {
  const theme = useTheme();
  const auth = useAuth();
  const navigate = useNavigate();

  const [devices, setDevices] = useState<Device[]>([]);
  const [maintenance, setMaintenance] = useState<{ nextCleanup?: string } | null>(null);
  const [sweeps, setSweeps] = useState<{ start: string; totalOnline: number }[]>([]);
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedDevice, setSelectedDevice] = useState<Device | null>(null);
  const [alerts, setAlerts] = useState<{ triggeredAt: string }[]>([]);
  const [users, setUsers] = useState<DrawerUser[]>([]);
  const [ptpStatus, setPtpStatus] = useState<NodeDashboardController['ptpStatus']>(null);
  const [isPortable, setIsPortable] = useState(false);
  const [page, setPage] = useState(0);
  const [rowsPerPage, setRowsPerPage] = useState(25);
  const [summary, setSummary] = useState<NodeDashboardSummary | null>(null);
  const [showUndo, setShowUndo] = useState(false);
  const [lastDeletedId, setLastDeletedId] = useState<string | null>(null);
  const [toastMessage, setToastMessage] = useState('');
  const [toastOpen, setToastOpen] = useState(false);

  useEffect(() => {
    ApiService.getSystemRuntimeInfo()
      .then((info) => setIsPortable(!!info?.isPortable))
      .catch(() => setIsPortable(false));
  }, []);

  useEffect(() => {
    const load = async () => {
      if (!auth.isAuthenticated) return;

      try {
        const [summaryData, devicesData, maintenanceData, sweepsData, alertsData, usersData] = await Promise.all([
          ApiService.getDashboardSummary(),
          ApiService.getDevices({ take: 100 }),
          ApiService.getDatabaseHealth(),
          ApiService.getSweepHistory(40),
          isPortable ? Promise.resolve([]) : ApiService.getAlertHistory(5),
          auth.isAdmin && !isPortable ? ApiService.getUsers() : Promise.resolve([]),
        ]);

        startTransition(() => {
          setSummary(summaryData as NodeDashboardSummary);
          setDevices(Array.isArray(devicesData) ? devicesData : (devicesData?.devices || []));
          setMaintenance(maintenanceData);
          setSweeps(sweepsData || []);
          setAlerts(alertsData || []);
          setUsers(usersData || []);
        });

        ApiService.getPtpStatus().then((status) => {
          if (status) setPtpStatus(status);
        }).catch(() => { /* optional AV probe */ });
      } catch (err) {
        console.error('Dashboard load failed:', err);
      }
    };
    load();
    const interval = setInterval(load, 5000);
    return () => clearInterval(interval);
  }, [auth.isAuthenticated, isPortable, auth.isAdmin]);

  useEffect(() => {
    if (selectedDevice) {
      const updated = devices.find((d) => d.id === selectedDevice.id);
      if (updated && JSON.stringify(updated) !== JSON.stringify(selectedDevice)) {
        setSelectedDevice(updated);
      }
    }
  }, [devices, selectedDevice]);

  const showToast = (message: string) => {
    setToastMessage(message);
    setToastOpen(true);
  };

  const handleIgnoreNode = async (id: string) => {
    const previousDevices = [...devices];
    setLastDeletedId(id);
    setShowUndo(true);
    setDevices((prev) => prev.filter((d) => d.id !== id));
    if (selectedDevice?.id === id) setSelectedDevice(null);

    try {
      await ApiService.deleteDevice(id);
    } catch (err) {
      console.error('Ignore failed, rolling back:', err);
      setDevices(previousDevices);
      setShowUndo(false);
    }
  };

  const handleUndo = async () => {
    if (!lastDeletedId) return;

    try {
      await ApiService.restoreDevice(lastDeletedId);
      const data = await ApiService.getDevices();
      setDevices(Array.isArray(data) ? data : (data?.devices || []));
      setShowUndo(false);
      setLastDeletedId(null);
    } catch (err) {
      console.error('Undo failed:', err);
    }
  };

  const filteredDevices = useMemo(() => {
    const term = searchTerm.toLowerCase().trim();
    if (!term) return devices;

    return devices.filter((d) => {
      const nameMatch = d.name?.toLowerCase().includes(term);
      const ipMatch = d.ipAddress?.includes(term);
      const vendorMatch = d.vendor?.toLowerCase().includes(term);
      const macMatch = d.macAddress?.toLowerCase().includes(term);
      const portMatch = d.openPorts?.some((port) => port.toString().includes(term));
      const statusMatch = d.status?.toLowerCase() === term;
      return nameMatch || ipMatch || vendorMatch || macMatch || portMatch || statusMatch;
    });
  }, [devices, searchTerm]);

  const recentAlertCount = alerts.filter(
    (a) => new Date(a.triggeredAt) > new Date(Date.now() - 86400000)
  ).length;

  return {
    theme,
    auth,
    navigate,
    devices,
    maintenance,
    sweeps,
    searchTerm,
    setSearchTerm,
    selectedDevice,
    setSelectedDevice,
    alerts,
    users,
    ptpStatus,
    isPortable,
    page,
    setPage,
    rowsPerPage,
    setRowsPerPage,
    summary,
    showUndo,
    setShowUndo,
    toastMessage,
    toastOpen,
    setToastOpen,
    showToast,
    handleIgnoreNode,
    handleUndo,
    filteredDevices,
    recentAlertCount,
  };
}
