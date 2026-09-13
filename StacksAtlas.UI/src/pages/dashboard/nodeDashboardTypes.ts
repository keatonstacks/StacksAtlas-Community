import type { Theme } from '@mui/material/styles';
import type { Device } from '../../models/Device';
import type { DrawerUser } from '../../components/device-drawer/types';
import type { useAuth } from '../../context/AuthContext';
import type { useNavigate } from 'react-router-dom';

export interface NodeDashboardSummary {
  totalDevices: number;
  onlineCount: number;
  offlineCount: number;
  vendors: { vendor: string; count: number }[];
  types: { type: string; count: number }[];
}

export interface NodeDashboardController {
  theme: Theme;
  auth: ReturnType<typeof useAuth>;
  navigate: ReturnType<typeof useNavigate>;
  devices: Device[];
  maintenance: { nextCleanup?: string } | null;
  sweeps: { start: string; totalOnline: number }[];
  searchTerm: string;
  setSearchTerm: (value: string) => void;
  selectedDevice: Device | null;
  setSelectedDevice: (device: Device | null) => void;
  alerts: { triggeredAt: string }[];
  users: DrawerUser[];
  ptpStatus: {
    sourceIp: string;
    clockIdentity: string;
    priority1: number;
    clockClass: number;
    domainNumber: number;
  } | null;
  isPortable: boolean;
  page: number;
  setPage: (page: number) => void;
  rowsPerPage: number;
  setRowsPerPage: (rows: number) => void;
  summary: NodeDashboardSummary | null;
  showUndo: boolean;
  setShowUndo: (open: boolean) => void;
  toastMessage: string;
  toastOpen: boolean;
  setToastOpen: (open: boolean) => void;
  showToast: (message: string) => void;
  handleIgnoreNode: (id: string) => Promise<void>;
  handleUndo: () => Promise<void>;
  filteredDevices: Device[];
  recentAlertCount: number;
}
