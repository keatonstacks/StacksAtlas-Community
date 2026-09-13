import { useState, useEffect, useRef, useCallback } from 'react';
import type { Device, ServiceDetail } from '../../models/Device';
import { ApiService } from '../../services/apiService';
import { useAuth } from '../../context/AuthContext';
import { useGlobalStats } from '../../context/GlobalStatsContext';
import { useConfirm } from '../../context/ConfirmContext';
import { usePortableMode } from '../../hooks/usePortableMode';

function normalizeDeviceId(id: string | undefined | null): string | null {
  if (!id) return null;
  return String(id).toLowerCase();
}

export function useDeviceDrawer(
  device: Device | null,
  open: boolean,
  onDeviceUpdate: () => void,
  onNotify?: (message: string) => void
) {
  const { user, userId, isAdmin, role } = useAuth();
  const { isHub, missingDependencies } = useGlobalStats();
  const { isPortable } = usePortableMode();
  const { confirm } = useConfirm();

  const [isPinging, setIsPinging] = useState(false);
  const [pingResult, setPingResult] = useState<string | null>(null);
  const [isWaking, setIsWaking] = useState(false);
  const [wakeResult, setWakeResult] = useState<string | null>(null);
  const [deviceAlertsEnabled, setDeviceAlertsEnabled] = useState(true);
  const [userNotificationsGloballyEnabled, setUserNotificationsGloballyEnabled] = useState(true);
  const [isDeepScanning, setIsDeepScanning] = useState(false);
  const [deepScanStatus, setDeepScanStatus] = useState<string>('idle');
  const [scanProgress, setScanProgress] = useState(0);
  const [serviceDetails, setServiceDetails] = useState<ServiceDetail[]>([]);
  const [isIntelligenceOpen, setIsIntelligenceOpen] = useState(true);
  const deepScanPollRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const loadedDeviceIdRef = useRef<string | null>(null);
  const onDeviceUpdateRef = useRef(onDeviceUpdate);
  const onNotifyRef = useRef(onNotify);
  const userIdRef = useRef(userId);

  useEffect(() => {
    onDeviceUpdateRef.current = onDeviceUpdate;
  }, [onDeviceUpdate]);

  useEffect(() => {
    onNotifyRef.current = onNotify;
  }, [onNotify]);

  useEffect(() => {
    userIdRef.current = userId;
  }, [userId]);

  const hasDeepScanReady =
    isHub ||
    !missingDependencies?.some((d: string) => {
      const lower = d.toLowerCase();
      return lower.includes('npcap') || lower.includes('nmap');
    });

  const deepScanBlockedReason = !hasDeepScanReady
    ? missingDependencies?.some((d: string) => d.toLowerCase().includes('nmap'))
      ? 'Deep Scan requires Nmap (and Npcap on Windows). See the dashboard warning for install links.'
      : 'Deep Scan requires Npcap on Windows. See the dashboard warning for install links.'
    : '';

  const stopDeepScanPoll = useCallback(() => {
    if (deepScanPollRef.current) {
      clearInterval(deepScanPollRef.current);
      deepScanPollRef.current = null;
    }
  }, []);

  const pollDeepScan = useCallback(
    (deviceId: string) => {
      stopDeepScanPoll();
      deepScanPollRef.current = setInterval(async () => {
        try {
          const res = await ApiService.getDeepScanStatus(deviceId);
          if (res.status !== 'running') {
            stopDeepScanPoll();
            setIsDeepScanning(false);
            setDeepScanStatus(res.status);
            setScanProgress(100);
            const details = await ApiService.getDeepScanResults(deviceId);
            setServiceDetails(details);
            onDeviceUpdateRef.current();
          } else {
            setScanProgress(res.progress || 0);
          }
        } catch {
          stopDeepScanPoll();
          setIsDeepScanning(false);
          setDeepScanStatus('error');
          onNotifyRef.current?.('Deep scan status check failed  -  node may be offline.');
        }
      }, 1000);
    },
    [stopDeepScanPoll]
  );

  useEffect(() => () => stopDeepScanPoll(), [stopDeepScanPoll]);

  useEffect(() => {
    stopDeepScanPoll();

    if (!open || !device) {
      if (!open) loadedDeviceIdRef.current = null;
      return;
    }

    const deviceKey = normalizeDeviceId(device.id);
    if (!deviceKey) return;

    const isNewDevice = loadedDeviceIdRef.current !== deviceKey;
    loadedDeviceIdRef.current = deviceKey;

    if (isNewDevice) {
      setServiceDetails([]);
      setDeepScanStatus('idle');
      setIsDeepScanning(false);
      setScanProgress(0);
      setPingResult(null);
      setWakeResult(null);
      setIsWaking(false);
    }

    setDeviceAlertsEnabled(device.alertsEnabled ?? true);

    if (!isPortable) {
      ApiService.getAlertPreferences(userIdRef.current || '')
        .then((prefs) => {
          const hasActivePersonalChannel =
            (prefs.alertEmail && prefs.alertEmail.length > 0) ||
            (prefs.preferredWebhookIds && prefs.preferredWebhookIds.length > 0);
          const isProfileEnabled =
            (prefs.alertsEnabled || prefs.webhookEnabled) && hasActivePersonalChannel;
          setUserNotificationsGloballyEnabled(isProfileEnabled || prefs.systemWideWebhooksActive);
        })
        .catch(() => setUserNotificationsGloballyEnabled(true));
    }

    ApiService.getDeepScanResults(device.id)
      .then((details) => {
        setServiceDetails(details);
        if (details.length > 0) setIsIntelligenceOpen(true);
      })
      .catch(console.error);

    ApiService.getDeepScanStatus(device.id)
      .then((res) => {
        if (res.status === 'running') {
          setIsDeepScanning(true);
          setDeepScanStatus('running');
          setScanProgress(res.progress || 0);
          pollDeepScan(device.id);
        }
      })
      .catch(console.error);
  }, [open, device?.id, isPortable, pollDeepScan, stopDeepScanPoll]);

  const handleDeepScan = async () => {
    if (!device || !hasDeepScanReady) return;
    try {
      setIsDeepScanning(true);
      setDeepScanStatus('running');
      setScanProgress(0);
      await ApiService.triggerDeepScan(device.id);
      pollDeepScan(device.id);
    } catch (err: unknown) {
      setIsDeepScanning(false);
      setDeepScanStatus('error');
      const message = err instanceof Error ? err.message : undefined;
      if (message) onNotify?.(message);
    }
  };

  const handlePingTest = async () => {
    if (!device) return;
    setIsPinging(true);
    setPingResult(null);
    try {
      const result = await ApiService.runDevicePing(device.id);
      setPingResult(result.success ? `REPLY: ${result.latency}ms` : `TIMEOUT: ${result.status || ''}`);
    } catch {
      setPingResult('ERROR');
    } finally {
      setIsPinging(false);
      setTimeout(() => setPingResult(null), 3000);
    }
  };

  const handleWake = async () => {
    if (!device) return;
    setIsWaking(true);
    setWakeResult(null);
    try {
      await ApiService.wakeDevice(device.id);
      setWakeResult('PACKET SENT');
    } catch {
      setWakeResult('FAILED');
    } finally {
      setIsWaking(false);
      setTimeout(() => setWakeResult(null), 3000);
    }
  };

  const handleResetMetrics = async () => {
    if (!device) return;
    const ok = await confirm({
      title: 'Reset performance metrics',
      message:
        "Reset all performance metrics (Flap Count, Latency History, Stability Score)? This checks the device as 'Healthy'.",
      confirmColor: 'warning',
    });
    if (!ok) return;
    try {
      await ApiService.resetMetrics(device.id);
      onDeviceUpdate();
    } catch {
      onNotify?.('Failed to reset metrics');
    }
  };

  const handleIgnoreRisk = async (issue: string) => {
    if (!device) return;
    const ok = await confirm({
      title: 'Ignore security risk',
      message: `Ignore this security risk?\n"${issue}"\n\nIt will no longer affect the Integrity Grade.`,
      confirmColor: 'warning',
    });
    if (!ok) return;
    try {
      await ApiService.acknowledgeRisk(device.id, issue);
      onDeviceUpdate();
    } catch {
      onNotify?.('Failed to acknowledge risk');
    }
  };

  const handleToggleAlerts = async () => {
    if (!device) return;
    const newState = !deviceAlertsEnabled;
    setDeviceAlertsEnabled(newState);
    try {
      await ApiService.updateDeviceAlerts(device.id, newState);
      onDeviceUpdate();
    } catch {
      setDeviceAlertsEnabled(!newState);
    }
  };

  return {
    user,
    userId,
    isAdmin,
    role,
    isHub,
    isPortable,
    isPinging,
    pingResult,
    isWaking,
    wakeResult,
    deviceAlertsEnabled,
    userNotificationsGloballyEnabled,
    isDeepScanning,
    deepScanStatus,
    scanProgress,
    serviceDetails,
    isIntelligenceOpen,
    setIsIntelligenceOpen,
    hasDeepScanReady,
    deepScanBlockedReason,
    handleDeepScan,
    handlePingTest,
    handleWake,
    handleResetMetrics,
    handleIgnoreRisk,
    handleToggleAlerts,
  };
}
