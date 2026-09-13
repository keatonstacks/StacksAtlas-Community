import { useCallback, useEffect, useState } from 'react';
import type { PendingHubUpdateDto, UpdateCheckResult } from '../models/Updates';
import { ApiService } from '../services/apiService';
import {
  isUpdateBannerSnoozed,
  normalizeUpdateChannel,
  resolveCheckUiState,
  snoozeUpdateBanner,
} from '../utils/updateCheckUi';
import { compareSemver } from '../utils/fleetVersionDrift';

function isActionablePending(
  pending: PendingHubUpdateDto | null | undefined,
  currentVersion: string | null | undefined,
): pending is PendingHubUpdateDto {
  if (!pending?.active || !pending.availableVersion) return false;
  const cmp = compareSemver(pending.availableVersion, currentVersion);
  if (cmp === null) {
    return pending.availableVersion.trim() !== (currentVersion ?? '').trim();
  }
  return cmp > 0;
}

export function useSoftwareUpdateNotifier(enabled: boolean) {
  const [availableUpdate, setAvailableUpdate] = useState<UpdateCheckResult | null>(null);
  const [pendingHub, setPendingHub] = useState<PendingHubUpdateDto | null>(null);
  const [snoozed, setSnoozed] = useState(() => isUpdateBannerSnoozed());
  const [checking, setChecking] = useState(false);

  const runCheck = useCallback(async () => {
    if (!enabled) {
      setAvailableUpdate(null);
      setPendingHub(null);
      return;
    }

    setChecking(true);
    try {
      const [pending, info] = await Promise.all([
        ApiService.getPendingHubUpdate().catch(() => null),
        ApiService.getSystemVersion().catch(() => null),
      ]);

      if (isActionablePending(pending, info?.version)) {
        setPendingHub(pending);
        setAvailableUpdate(null);
        setSnoozed(false);
        return;
      }
      setPendingHub(null);

      if (isUpdateBannerSnoozed()) {
        setSnoozed(true);
        setAvailableUpdate(null);
        return;
      }

      const versionInfo = info ?? await ApiService.getSystemVersion();
      const channel = normalizeUpdateChannel(versionInfo.channel);
      const result = await ApiService.checkForUpdates(channel);
      if (resolveCheckUiState(result) === 'available') {
        setAvailableUpdate(result);
        setSnoozed(false);
      } else {
        setAvailableUpdate(null);
      }
    } catch {
      setAvailableUpdate(null);
    } finally {
      setChecking(false);
    }
  }, [enabled]);

  useEffect(() => {
    if (!enabled) {
      setAvailableUpdate(null);
      setPendingHub(null);
      return;
    }
    void runCheck();
  }, [enabled, runCheck]);

  const dismiss = useCallback(() => {
    if (pendingHub?.active) {
      void ApiService.clearPendingHubUpdate().catch(() => undefined);
      setPendingHub(null);
      return;
    }
    snoozeUpdateBanner(7);
    setSnoozed(true);
    setAvailableUpdate(null);
  }, [pendingHub?.active]);

  const hubBannerResult: UpdateCheckResult | null =
    pendingHub?.active && pendingHub.availableVersion
      ? {
          status: 'updateAvailable',
          updateAvailable: true,
          currentVersion: '',
          channel: pendingHub.channel ?? 'stable',
          artifactKey: '',
          availableVersion: pendingHub.availableVersion,
          downloadUrl: null,
          sha256: null,
          image: null,
          digest: null,
          criticality: pendingHub.requestedApply ? 'high' : null,
          releaseNotesUrl: null,
          publishedUtc: pendingHub.notifiedUtc,
          message:
            pendingHub.message
            ?? (pendingHub.requestedApply
              ? 'Hub requested this update. Confirm apply in Software Updates.'
              : 'Hub staged an update for this site.'),
          applySupported: true,
          applyMode: 'inApp',
        }
      : null;

  const displayResult = hubBannerResult ?? availableUpdate;
  const visible =
    enabled &&
    !checking &&
    displayResult !== null &&
    (hubBannerResult !== null || (!snoozed && resolveCheckUiState(displayResult) === 'available'));

  return {
    visible,
    availableUpdate: displayResult,
    checking,
    dismiss,
    refresh: runCheck,
    fromHub: hubBannerResult !== null,
  };
}
