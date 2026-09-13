import React, { createContext, useContext, useState, useEffect, type ReactNode } from 'react';
import { ApiService } from '../services/apiService';
import { useAuth } from './AuthContext';
import { coalesceStorageBytes } from '../utils/databaseStorage';

interface MaintenanceStats {
    sizeBytes: number;
    applianceSizeBytes?: number;
    fleetSizeBytes?: number;
    hasFleetDatabase?: boolean;
    applianceEngine?: string;
    fleetEngine?: string;
    nextCleanupUtc?: string;
}



interface GlobalStatsContextType {
    totalDevices: number;
    onlineCount: number;
    offlineCount: number;
    dbSize: number;
    applianceDbSize: number;
    fleetDbSize: number;
    hasFleetDatabase: boolean;
    applianceEngine: string;
    fleetEngine: string | null;
    nextCleanup: string | null;
    recentAlertCount: number;
    systemStatus: "online" | "offline" | "degraded";
    isScanning: boolean;
    isHub: boolean;
    federatedNodeCount: number;
    federatedOnlineNodes: number;
    missingDependencies: string[];
    activeInterface: string | null;
    scanningRange: string | null;
    refresh: () => Promise<void>;
    loading: boolean;
}

const GlobalStatsContext = createContext<GlobalStatsContextType | undefined>(undefined);

export const GlobalStatsProvider: React.FC<{ children: ReactNode }> = ({ children }) => {
    const [totalDevices, setTotalDevices] = useState(0);
    const [onlineCount, setOnlineCount] = useState(0);
    const [offlineCount, setOfflineCount] = useState(0);
    const [maintenance, setMaintenance] = useState<MaintenanceStats | null>(null);
    const [recentAlertCount, setRecentAlertCount] = useState(0);
    const [systemStatus, setSystemStatus] = useState<"online" | "offline" | "degraded">("offline");
    const [isScanning, setIsScanning] = useState(false);
    const [isHub, setIsHub] = useState(false);
    const [federatedNodeCount, setFederatedNodeCount] = useState(0);
    const [federatedOnlineNodes, setFederatedOnlineNodes] = useState(0);
    const [missingDependencies, setMissingDependencies] = useState<string[]>([]);
    const [activeInterface, setActiveInterface] = useState<string | null>(null);
    const [scanningRange, setScanningRange] = useState<string | null>(null);
    const [loading, setLoading] = useState(true);
    const auth = useAuth();

    const loadData = async () => {
        if (!auth.isAuthenticated) return;
        
        try {
            // Parallel fetch: Using Summary API instead of full Device List
            const [summaryData, maintenanceData, eventsData, statusData] = await Promise.all([
                ApiService.getDashboardSummary(),
                ApiService.getDatabaseHealth(),
                ApiService.getRecentEvents(20),
                ApiService.getSystemStatus()
            ]);

            if (summaryData) {
                setTotalDevices(summaryData.totalDevices || 0);
                setOnlineCount(summaryData.onlineCount || 0);
                setOfflineCount(summaryData.offlineCount || 0);
                setIsHub(!!summaryData.isHub);

                // If Hub, also fetch federated node count
                if (summaryData.isHub) {
                    try {
                        const fedStats = await ApiService.getFederationStats();
                        if (fedStats) {
                            setFederatedNodeCount(fedStats.totalNodes || 0);
                            setFederatedOnlineNodes(fedStats.onlineNodes || 0);
                        }
                    } catch { /* Hub stats are best-effort */ }
                }
            }

            // Prefer health metrics; merge database/status when health lacks dual-store fields
            let mergedMaintenance = maintenanceData as MaintenanceStats | null;
            try {
                const dbStatus = await ApiService.getDatabaseStatus();
                if (dbStatus) {
                    const applianceBytes = coalesceStorageBytes(
                        maintenanceData?.applianceSizeBytes,
                        maintenanceData?.sizeBytes,
                        dbStatus.applianceDatabaseSize,
                        dbStatus.databaseSize,
                    );
                    const fleetBytes = coalesceStorageBytes(
                        maintenanceData?.fleetSizeBytes,
                        dbStatus.fleetDatabaseSize,
                    );
                    mergedMaintenance = {
                        ...(maintenanceData ?? { sizeBytes: 0 }),
                        sizeBytes: applianceBytes,
                        applianceSizeBytes: applianceBytes,
                        fleetSizeBytes: fleetBytes,
                        hasFleetDatabase:
                            maintenanceData?.hasFleetDatabase ??
                            dbStatus.hasFleetDatabase ??
                            false,
                        applianceEngine:
                            maintenanceData?.applianceEngine ??
                            dbStatus.applianceEngine ??
                            'LiteDB',
                        fleetEngine:
                            maintenanceData?.fleetEngine ??
                            dbStatus.fleetEngine ??
                            undefined,
                    };
                }
            } catch { /* database status is best-effort */ }

            setMaintenance(mergedMaintenance);

            const recentAlerts = Array.isArray(eventsData) ? eventsData : [];
            setRecentAlertCount(recentAlerts.length);

            if (statusData) {
                setSystemStatus("online");
                setIsScanning(statusData.isScanning);
                setMissingDependencies(statusData.missingDependencies || []);
                setActiveInterface(statusData.activeInterface || null);
                setScanningRange(statusData.scanningRange || null);
            } else {
                setSystemStatus("degraded");
                setMissingDependencies([]);
            }

        } catch (err) {
            console.error("Global stats load failed:", err);
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadData();
        const interval = setInterval(loadData, 5000); // 5s poll
        return () => clearInterval(interval);
    }, [auth.isAuthenticated]);

    const applianceDbSize = maintenance?.applianceSizeBytes ?? maintenance?.sizeBytes ?? 0;
    const fleetDbSize = maintenance?.fleetSizeBytes ?? 0;
    const hasFleetDatabase = !!maintenance?.hasFleetDatabase && isHub;

    return (
        <GlobalStatsContext.Provider value={{
            totalDevices,
            onlineCount,
            offlineCount,
            dbSize: applianceDbSize,
            applianceDbSize,
            fleetDbSize,
            hasFleetDatabase,
            applianceEngine: maintenance?.applianceEngine ?? 'LiteDB',
            fleetEngine: maintenance?.fleetEngine ?? null,
            nextCleanup: maintenance?.nextCleanupUtc ?? null,
            recentAlertCount,
            systemStatus,
            isScanning,
            isHub,
            federatedNodeCount,
            federatedOnlineNodes,
            missingDependencies,
            activeInterface,
            scanningRange,
            refresh: loadData,
            loading
        }}>
            {children}
        </GlobalStatsContext.Provider>
    );
};

export const useGlobalStats = () => {
    const context = useContext(GlobalStatsContext);
    if (!context) {
        throw new Error('useGlobalStats must be used within a GlobalStatsProvider');
    }
    return context;
};
