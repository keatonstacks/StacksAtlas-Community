import React, { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  CircularProgress,
  FormControlLabel,
  Paper,
  Stack,
  Switch,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import {
  Launch as LaunchIcon,
  SystemUpdateAlt as SystemUpdateIcon,
} from '@mui/icons-material';
import { ApiService } from '../../services/apiService';
import type {
  ApplianceVersionInfo,
  PendingHubUpdateDto,
  UpdateApplyResult,
  UpdateChannel,
  UpdateCheckResult,
} from '../../models/Updates';
import { PreUpdateChecklistDialog } from '../PreUpdateChecklistDialog';
import { ScheduledApplyPanel } from '../ScheduledApplyPanel';
import { PortableUpgradeBanner } from '../PortableUpgradeBanner';
import { HubUpdateDepotPanel } from './HubUpdateDepotPanel';
import { buildPreUpdateChecklist } from '../../utils/updatePreFlight';
import {
  normalizeSchedule,
  type UpdateScheduleConfig,
} from '../../utils/updateSchedule';
import { normalizeUpdateChannel, resolveCheckUiState } from '../../utils/updateCheckUi';
import { compareSemver } from '../../utils/fleetVersionDrift';
import {
  getPreferredUpdateChannel,
  setPreferredUpdateChannel,
} from '../../utils/preferredUpdateChannel';
import { buildDockerUpdateGuide } from '../../utils/dockerUpdateGuide';
import { isHubMode } from '../../utils/federationMode';

function isActionablePendingHub(
  pending: PendingHubUpdateDto | null | undefined,
  currentVersion: string | null | undefined,
): boolean {
  if (!pending?.active || !pending.availableVersion) return false;
  const cmp = compareSemver(pending.availableVersion, currentVersion);
  if (cmp === null) {
    return pending.availableVersion.trim() !== (currentVersion ?? '').trim();
  }
  return cmp > 0;
}

function ReleaseNotesButton({ url }: { url: string }) {
  return (
    <Button
      variant="outlined"
      size="small"
      endIcon={<LaunchIcon fontSize="small" />}
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      component="a"
      sx={{ fontWeight: 600, alignSelf: 'flex-start' }}
    >
      View changelog
    </Button>
  );
}

const SoftwareUpdatesBlock: React.FC = () => {
  const theme = useTheme();

  const [currentVersionInfo, setCurrentVersionInfo] = useState<ApplianceVersionInfo | null>(null);
  const [versionLoading, setVersionLoading] = useState(true);
  const [includePreview, setIncludePreview] = useState(
    () => getPreferredUpdateChannel() === 'preview',
  );
  const [checking, setChecking] = useState(false);
  const [checkResult, setCheckResult] = useState<UpdateCheckResult | null>(null);
  const [checkError, setCheckError] = useState<string | null>(null);
  const [applying, setApplying] = useState(false);
  const [applyResult, setApplyResult] = useState<UpdateApplyResult | null>(null);
  const [applyError, setApplyError] = useState<string | null>(null);
  const [awaitingRestart, setAwaitingRestart] = useState(false);
  const [checklistOpen, setChecklistOpen] = useState(false);
  const [schedule, setSchedule] = useState<UpdateScheduleConfig>(() => normalizeSchedule(null));
  const [pendingHub, setPendingHub] = useState<PendingHubUpdateDto | null>(null);
  const [isHub, setIsHub] = useState(false);

  const checkChannel: UpdateChannel = includePreview ? 'preview' : 'stable';

  const loadSystemVersion = useCallback(async () => {
    setVersionLoading(true);
    try {
      const [info, fed, scheduleDto, pending] = await Promise.all([
        ApiService.getSystemVersion(),
        ApiService.getFederationSettings().catch(() => null),
        ApiService.getUpdateSchedule().catch(() => null),
        ApiService.getPendingHubUpdate().catch(() => null),
      ]);
      setCurrentVersionInfo(info);
      // Hub preview offers should drive the Check channel so operators do not miss the staged package.
      const pendingActionable = isActionablePendingHub(pending, info.version) ? pending : null;
      if (pendingActionable && normalizeUpdateChannel(pendingActionable.channel) === 'preview') {
        setIncludePreview(true);
        setPreferredUpdateChannel('preview');
      } else {
        setIncludePreview(getPreferredUpdateChannel() === 'preview');
      }
      setIsHub(isHubMode(fed?.mode));
      if (scheduleDto) setSchedule(normalizeSchedule(scheduleDto));
      setPendingHub(pendingActionable);
    } finally {
      setVersionLoading(false);
    }
  }, []);

  useEffect(() => {
    void loadSystemVersion();
  }, [loadSystemVersion]);

  const handlePreviewToggle = (event: React.ChangeEvent<HTMLInputElement>) => {
    const next = event.target.checked;
    setIncludePreview(next);
    setPreferredUpdateChannel(next ? 'preview' : 'stable');
    setCheckResult(null);
    setCheckError(null);
    setApplyResult(null);
    setApplyError(null);
    setAwaitingRestart(false);
  };

  const pollForRestart = useCallback(async (previousVersion: string) => {
    setAwaitingRestart(true);
    const deadline = Date.now() + 5 * 60 * 1000;
    while (Date.now() < deadline) {
      await new Promise((resolve) => window.setTimeout(resolve, 3000));
      try {
        await ApiService.getHealth();
        const info = await ApiService.getSystemVersion();
        if (info.version !== previousVersion) {
          setCurrentVersionInfo(info);
          setIncludePreview(normalizeUpdateChannel(info.channel) === 'preview');
          setAwaitingRestart(false);
          setApplyResult(null);
          setCheckResult(null);
          return;
        }
      } catch {
        // Service restarting  -  keep polling
      }
    }
    setAwaitingRestart(false);
  }, []);

  const handleApplyUpdate = async () => {
    if (!currentVersionInfo) return;
    setApplying(true);
    setApplyResult(null);
    setApplyError(null);
    const previousVersion = currentVersionInfo.version;
    try {
      const result = await ApiService.applyUpdate(checkChannel);
      setApplyResult(result);
      if (result.status === 'applying' && result.restartRequired) {
        void pollForRestart(previousVersion);
      }
      if (checkResult?.availableVersion) {
        const next = { ...schedule, lastAppliedVersion: checkResult.availableVersion };
        setSchedule(next);
        void ApiService.putUpdateSchedule({
          enabled: next.enabled,
          dayOfWeek: next.dayOfWeek,
          hour: next.hour,
          minute: next.minute,
          lastAppliedVersion: next.lastAppliedVersion,
        }).catch(() => undefined);
      }
    } catch (err) {
      setApplyError(err instanceof Error ? err.message : 'Update apply failed.');
    } finally {
      setApplying(false);
      setChecklistOpen(false);
    }
  };

  const requestApplyUpdate = () => {
    setChecklistOpen(true);
  };

  const handleCheckForUpdates = async () => {
    setChecking(true);
    setCheckResult(null);
    setCheckError(null);
    try {
      const result = await ApiService.checkForUpdates(checkChannel);
      setCheckResult(result);
    } catch (err) {
      setCheckError(err instanceof Error ? err.message : 'Update check failed.');
    } finally {
      setChecking(false);
    }
  };

  const uiState = checkResult ? resolveCheckUiState(checkResult) : null;
  const checklistItems = buildPreUpdateChecklist(currentVersionInfo);
  const dockerGuide = checkResult ? buildDockerUpdateGuide(checkResult) : null;

  const patchSchedule = (patch: Partial<UpdateScheduleConfig>) => {
    const next = { ...schedule, ...patch };
    setSchedule(next);
    void ApiService.putUpdateSchedule({
      enabled: next.enabled,
      dayOfWeek: next.dayOfWeek,
      hour: next.hour,
      minute: next.minute,
      lastAppliedVersion: next.lastAppliedVersion,
    }).catch(() => {
      // Keep local UI; next reload will reconcile from appliance.
    });
  };

  const dismissPendingHub = () => {
    setPendingHub(null);
    void ApiService.clearPendingHubUpdate().catch(() => undefined);
  };

  // Browser timer removed: UpdateScheduleWorker evaluates the window on the appliance.

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 3,
        borderRadius: 4,
        bgcolor: alpha(theme.palette.background.paper, 0.8),
        backdropFilter: 'blur(20px)',
        border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
        boxShadow: `0 8px 16px ${alpha('#000', 0.1)}`,
      }}
    >
      <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
        <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
          <SystemUpdateIcon color="primary" sx={{ fontSize: 24 }} />
        </Box>
        <Box>
          <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>
            SOFTWARE UPDATES
          </Typography>
          <Typography variant="caption" color="text.secondary">
            CHECK STACKSATLAS RELEASE CHANNELS (SIGNED MANIFEST)
          </Typography>
        </Box>
      </Stack>

      {pendingHub?.active && (
        <Alert
          severity={pendingHub.requestedApply ? 'warning' : 'info'}
          sx={{ mb: 2, borderRadius: 2 }}
          onClose={dismissPendingHub}
        >
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            {pendingHub.requestedApply ? 'Hub requested an update' : 'Hub update available'}
            {pendingHub.availableVersion ? ` (v${pendingHub.availableVersion})` : ''}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
            {pendingHub.message
              ?? 'A Hub-staged package is ready. Check for updates, then confirm Download & Install locally.'}
            {pendingHub.initiatedBy ? ` Initiated by ${pendingHub.initiatedBy}.` : ''}
          </Typography>
        </Alert>
      )}

      <Box
        sx={{
          p: 2.5,
          borderRadius: 3,
          bgcolor: alpha(theme.palette.background.default, 0.5),
          border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
          mb: 3,
        }}
      >
        <Stack direction="row" alignItems="center" justifyContent="space-between" flexWrap="wrap" gap={2}>
          <Box>
            <Typography variant="caption" fontWeight={900} color="primary" sx={{ display: 'block', mb: 0.5, letterSpacing: 0.5, opacity: 0.7 }}>
              INSTALLED VERSION
            </Typography>
            {versionLoading ? (
              <Stack direction="row" alignItems="center" spacing={1}>
                <CircularProgress size={18} />
                <Typography variant="body2" color="text.secondary">
                  Loading...
                </Typography>
              </Stack>
            ) : (
              <Typography variant="h5" sx={{ fontWeight: 800, fontFamily: 'monospace', letterSpacing: 0.5 }}>
                v{currentVersionInfo?.version ?? '-'}
              </Typography>
            )}
          </Box>
          <Chip
            label={checkChannel.toUpperCase()}
            color={checkChannel === 'preview' ? 'warning' : 'primary'}
            variant="outlined"
            sx={{ fontWeight: 900, borderRadius: 1.5, fontSize: '0.65rem', height: 24 }}
          />
        </Stack>
      </Box>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} alignItems={{ xs: 'stretch', sm: 'center' }} mb={2}>
        <FormControlLabel
          control={
            <Switch
              checked={includePreview}
              onChange={handlePreviewToggle}
              disabled={checking || versionLoading}
              color="warning"
            />
          }
          label={
            <Typography variant="body2" sx={{ fontWeight: 600 }}>
              Include preview / beta releases
            </Typography>
          }
        />
        <Box sx={{ flex: 1 }} />
        <Button
          variant="contained"
          onClick={() => void handleCheckForUpdates()}
          disabled={checking || versionLoading || !currentVersionInfo}
          sx={{ fontWeight: 600, minWidth: 200, py: 1.1 }}
          startIcon={checking ? <CircularProgress size={20} color="inherit" /> : undefined}
        >
          {checking ? 'CHECKING...' : 'Check for Updates'}
        </Button>
      </Stack>

      {(checkError || checkResult || applyError || applyResult || awaitingRestart) && (
        <Stack spacing={2} mt={1}>
          {checkError && (
            <Alert severity="error" sx={{ borderRadius: 2 }}>
              {checkError}
            </Alert>
          )}

          {applyError && (
            <Alert severity="error" sx={{ borderRadius: 2 }}>
              {applyError}
            </Alert>
          )}

          {(applyResult?.status === 'applying' || awaitingRestart) && (
            <Alert severity="info" sx={{ borderRadius: 2 }}>
              <Stack spacing={1}>
                <Stack direction="row" alignItems="center" spacing={1}>
                  <CircularProgress size={18} />
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {awaitingRestart
                      ? 'Waiting for the appliance to restart...'
                      : applyResult?.message ?? 'Applying update...'}
                  </Typography>
                </Stack>
                {applyResult?.snapshotApplianceFileName && !currentVersionInfo?.isPortable && (
                  <Typography variant="caption" color="text.secondary">
                    Pre-update snapshot: {applyResult.snapshotApplianceFileName}
                    {applyResult.snapshotFleetFileName ? `, ${applyResult.snapshotFleetFileName}` : ''}
                  </Typography>
                )}
                {awaitingRestart && currentVersionInfo?.isPortable && (
                  <Typography variant="caption" color="text.secondary">
                    Portable launcher backup: StacksAtlas-Portable.exe.pre-update.bak (same folder as the exe)
                  </Typography>
                )}
              </Stack>
            </Alert>
          )}

          {applyResult?.status === 'failed' && (
            <Alert severity="error" sx={{ borderRadius: 2 }}>
              {applyResult.message}
            </Alert>
          )}

          {checkResult && uiState === 'uptodate' && (
            <Alert severity="success" sx={{ borderRadius: 2 }}>
              <Stack spacing={1.5}>
                <Typography variant="body2">{checkResult.message}</Typography>
                {checkResult.releaseNotesUrl && (
                  <ReleaseNotesButton url={checkResult.releaseNotesUrl} />
                )}
              </Stack>
            </Alert>
          )}

          {checkResult && uiState === 'available' && !awaitingRestart && !applying && applyResult?.status !== 'applying' && (
            <Box
              sx={{
                p: 2.5,
                borderRadius: 3,
                bgcolor: alpha(theme.palette.success.main, 0.08),
                border: `1px solid ${alpha(theme.palette.success.main, 0.35)}`,
              }}
            >
              <Stack spacing={2}>
                {currentVersionInfo?.isPortable && (
                  <PortableUpgradeBanner variant="compact" onOpenSettings={() => {}} />
                )}
                <Stack direction="row" alignItems="center" flexWrap="wrap" gap={1}>
                  <Typography variant="overline" color="success.main" sx={{ fontWeight: 900, lineHeight: 1 }}>
                    Update available
                  </Typography>
                  {checkResult.availableVersion && (
                    <Chip
                      label={`v${checkResult.availableVersion}`}
                      color="success"
                      sx={{ fontWeight: 900, fontFamily: 'monospace' }}
                    />
                  )}
                  {checkResult.criticality && (
                    <Chip
                      label={checkResult.criticality.toUpperCase()}
                      size="small"
                      variant="outlined"
                      sx={{ fontWeight: 700, fontSize: '0.65rem' }}
                    />
                  )}
                </Stack>
                <Typography variant="body2">{checkResult.message}</Typography>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ xs: 'flex-start', sm: 'center' }}>
                  {checkResult.releaseNotesUrl && (
                    <ReleaseNotesButton url={checkResult.releaseNotesUrl} />
                  )}
                  {checkResult.applySupported ? (
                    <Button
                      variant="contained"
                      color="secondary"
                      disabled={applying || awaitingRestart}
                      onClick={requestApplyUpdate}
                      sx={{ fontWeight: 600 }}
                      startIcon={applying ? <CircularProgress size={18} color="inherit" /> : undefined}
                    >
                      {applying ? 'Starting install...' : 'Download & Install'}
                    </Button>
                  ) : (
                    <Stack spacing={1.5} sx={{ width: '100%' }}>
                      <Stack direction="row" alignItems="center" spacing={1} flexWrap="wrap">
                        {checkResult.downloadUrl && (
                          <Button
                            variant="outlined"
                            color="secondary"
                            href={checkResult.downloadUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            component="a"
                            sx={{ fontWeight: 600 }}
                          >
                            Download manually
                          </Button>
                        )}
                        <Chip
                          label={
                            checkResult.applyMode === 'guided'
                              ? 'Host Docker update'
                              : 'Manual install required'
                          }
                          size="small"
                          variant="outlined"
                          sx={{ fontWeight: 700, fontSize: '0.65rem' }}
                        />
                      </Stack>
                      {dockerGuide && (
                        <Box
                          sx={{
                            p: 1.5,
                            borderRadius: 2,
                            border: `1px solid ${alpha(theme.palette.divider, 0.15)}`,
                            bgcolor: alpha(theme.palette.background.default, 0.45),
                          }}
                        >
                          <Typography variant="caption" fontWeight={800} display="block" mb={0.75}>
                            Run on the Docker host (not inside the container)
                          </Typography>
                          <Typography
                            component="pre"
                            variant="caption"
                            sx={{
                              m: 0,
                              mb: 1,
                              p: 1,
                              borderRadius: 1,
                              fontFamily: 'monospace',
                              whiteSpace: 'pre-wrap',
                              wordBreak: 'break-all',
                              bgcolor: alpha(theme.palette.common.black, 0.25),
                            }}
                          >
                            {dockerGuide.pullCommand}
                            {'\n'}
                            {dockerGuide.recreateCommand}
                          </Typography>
                          {dockerGuide.digestNote && (
                            <Typography variant="caption" color="text.secondary" display="block">
                              {dockerGuide.digestNote}
                            </Typography>
                          )}
                          <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
                            If compose uses <code>image: stacksatlas:deep-scan</code> with a <code>build:</code> block,
                            you must rebuild or the container stays on the old version. Plain{' '}
                            <code>stacksatlas:latest</code> only needs recreate. Full steps:{' '}
                            <Box
                              component="a"
                              href="https://stacksatlas.com/docs/installation#enrolled-docker-node-hub-depot"
                              target="_blank"
                              rel="noopener noreferrer"
                              sx={{ color: 'primary.main', fontWeight: 700 }}
                            >
                              Installation → Enrolled Docker Node
                            </Box>
                            .
                          </Typography>
                        </Box>
                      )}
                    </Stack>
                  )}
                </Stack>
              </Stack>
            </Box>
          )}

          {checkResult && uiState === 'unavailable' && (
            <Alert severity="info" sx={{ borderRadius: 2 }}>
              {checkResult.message}
            </Alert>
          )}

          {checkResult && uiState === 'blocked' && (
            <Alert severity="error" sx={{ borderRadius: 2 }}>
              {checkResult.message}
            </Alert>
          )}
        </Stack>
      )}

      {isHub && <HubUpdateDepotPanel key={checkChannel} channel={checkChannel} />}

      <ScheduledApplyPanel
        schedule={schedule}
        unsupported={
          !!currentVersionInfo?.isPortable
          || isHub
          || (!!currentVersionInfo && currentVersionInfo.artifactKey !== 'win-x64-msi')
        }
        unsupportedReason={
          currentVersionInfo?.isPortable
            ? 'Scheduled apply is available on installed appliances. Portable users can update manually from Software Updates.'
            : isHub
              ? 'Scheduled apply does not run on the Hub brain. Stage packages in the depot and Notify / Update enrolled sites.'
              : currentVersionInfo && currentVersionInfo.artifactKey !== 'win-x64-msi'
                ? 'Scheduled apply runs only on Windows installed (MSI) appliances. Mac and Docker update manually or via host tooling.'
                : undefined
        }
        onChange={patchSchedule}
      />

      <PreUpdateChecklistDialog
        open={checklistOpen}
        targetVersion={checkResult?.availableVersion ?? null}
        items={checklistItems}
        applying={applying}
        onCancel={() => setChecklistOpen(false)}
        onConfirm={() => void handleApplyUpdate()}
      />
    </Paper>
  );
};

export default SoftwareUpdatesBlock;
