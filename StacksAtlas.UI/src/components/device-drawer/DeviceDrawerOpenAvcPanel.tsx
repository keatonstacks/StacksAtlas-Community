import { useCallback, useEffect, useMemo, useState, Fragment, type ReactNode } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Autocomplete,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Collapse,
  FormControlLabel,
  FormGroup,
  Link,
  MenuItem,
  Paper,
  Select,
  Stack,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import HubIcon from '@mui/icons-material/Hub';
import LinkIcon from '@mui/icons-material/Link';
import PushPinIcon from '@mui/icons-material/PushPin';
import type { Device } from '../../models/Device';
import { OPENAVC_LINKS } from '../../config/integrationLinks';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import { ApiService, type OpenAvcDrawerContext } from '../../services/apiService';

interface DeviceDrawerOpenAvcPanelProps {
  device: Device;
  isHub: boolean;
  isAdmin: boolean;
  onDeviceUpdate?: () => void;
}

interface CatalogOption {
  id: string;
  label: string;
  ip?: string;
  driverName?: string;
}

interface MacroOption {
  id: string;
  name: string;
}

function ReadingsGrid({ readings }: { readings: { label: string; value: string }[] }) {
  if (readings.length === 0) return null;
  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: { xs: '1fr', sm: 'minmax(96px, auto) 1fr' },
        gap: { xs: 0.5, sm: '6px 16px' },
        alignItems: 'start',
      }}
    >
      {readings.map((reading, index) => (
        <Fragment key={`${reading.label}-${index}`}>
          <Typography variant="caption" color="text.secondary" fontWeight={700}>
            {reading.label}
          </Typography>
          <Typography
            variant="caption"
            component="div"
            sx={{
              fontFamily: 'monospace',
              wordBreak: 'break-word',
              whiteSpace: 'pre-wrap',
              maxHeight: reading.value.includes('\n') ? 220 : undefined,
              overflowY: reading.value.includes('\n') ? 'auto' : undefined,
            }}
          >
            {reading.value}
          </Typography>
        </Fragment>
      ))}
    </Box>
  );
}

function OpenAvcReadingsAuthoringHint({ compact = false }: { compact?: boolean }) {
  return (
    <Alert severity="info" sx={{ py: compact ? 0.75 : 1, mt: compact ? 1 : 0 }}>
      <Typography variant="caption" component="div" color="text.secondary">
        Readings use OpenAVC <strong>device state</strong>, not the Activity log. In Programmer, end macros with a step that writes state (e.g.{' '}
        <code>last_message</code>, <code>installed_apps</code>). Driver query commands often do this for you.{' '}
        <Link
          href={OPENAVC_LINKS.VARIABLES_STATE}
          target="_blank"
          rel="noopener noreferrer"
          variant="caption"
          fontWeight={700}
        >
          Variables and State
        </Link>
      </Typography>
    </Alert>
  );
}

function OpenAvcDrawerSection({
  title,
  isMobile,
  expanded,
  onExpandedChange,
  accordionSx,
  children,
}: {
  title: string;
  isMobile: boolean;
  expanded: boolean;
  onExpandedChange: (expanded: boolean) => void;
  accordionSx: Record<string, unknown>;
  children: ReactNode;
}) {
  if (isMobile) {
    return (
      <Box sx={{ ...accordionSx, px: 1.5, py: 1.25 }}>
        <Typography variant="caption" fontWeight={800} display="block" sx={{ mb: 1 }}>
          {title}
        </Typography>
        {children}
      </Box>
    );
  }

  return (
    <Accordion
      disableGutters
      expanded={expanded}
      onChange={(_, exp) => onExpandedChange(exp)}
      sx={accordionSx}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ minHeight: 40, '& .MuiAccordionSummary-content': { my: 0.5 } }}>
        <Typography variant="caption" fontWeight={800}>{title}</Typography>
      </AccordionSummary>
      <AccordionDetails sx={{ pt: 0 }}>{children}</AccordionDetails>
    </Accordion>
  );
}

function formatStateLabel(key: string): string {
  return key.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

function mapCatalogOptions(context: OpenAvcDrawerContext | null): CatalogOption[] {
  const raw = context?.availableDevices ?? [];
  return raw.map((d) => {
    const item = d as {
      id?: string;
      Id?: string;
      name?: string;
      Name?: string;
      ip?: string;
      Ip?: string;
      driverName?: string;
      DriverName?: string;
    };
    const id = item.id ?? item.Id ?? '';
    const name = item.name ?? item.Name ?? id;
    const ip = item.ip ?? item.Ip;
    const driverName = item.driverName ?? item.DriverName;
    const suffix = [ip, driverName].filter(Boolean).join(' · ');
    return {
      id,
      label: suffix ? `${name} (${suffix})` : name,
      ip,
      driverName,
    };
  }).filter((o) => o.id);
}

export function DeviceDrawerOpenAvcPanel({ device, isHub, isAdmin, onDeviceUpdate }: DeviceDrawerOpenAvcPanelProps) {
  const theme = useTheme();
  const isMobile = useIsMobileLayout();
  const [loading, setLoading] = useState(true);
  const [context, setContext] = useState<OpenAvcDrawerContext | null>(null);
  const [selectedCatalog, setSelectedCatalog] = useState<CatalogOption | null>(null);
  const [linkInput, setLinkInput] = useState('');
  const [selectedCommand, setSelectedCommand] = useState('');
  const [busy, setBusy] = useState(false);
  const [macroRunning, setMacroRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [pinDraft, setPinDraft] = useState<string[]>([]);
  const [showPinEditor, setShowPinEditor] = useState(false);
  const [showDriverState, setShowDriverState] = useState(false);
  const [readingsExpanded, setReadingsExpanded] = useState(true);
  const [runExpanded, setRunExpanded] = useState(true);

  const catalogOptions = useMemo(() => mapCatalogOptions(context), [context]);
  const macroCatalog = useMemo((): MacroOption[] => {
    const raw = context?.allMacros?.length ? context.allMacros : context?.macros ?? [];
    return raw.map((m) => ({ id: m.id, name: m.name || m.id }));
  }, [context?.allMacros, context?.macros]);

  const refreshContext = useCallback(async () => {
    const data = await ApiService.getOpenAvcDrawerContext(
      device.id,
      device.ipAddress,
      device.hostname ?? undefined,
    );
    setContext(data);
    if (data.openAvcDeviceId) {
      setLinkInput(data.openAvcDeviceId);
    }
    return data;
  }, [device.id, device.ipAddress, device.hostname]);

  useEffect(() => {
    setPinDraft(context?.pinnedMacroIds ?? []);
  }, [context?.pinnedMacroIds]);

  useEffect(() => {
    if ((context?.readings?.length ?? 0) > 0)
      setReadingsExpanded(true);
  }, [context?.readings?.length]);

  const loadContext = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await refreshContext();
      if (!data.openAvcDeviceId && data.suggestion?.openAvcDeviceId) {
        setLinkInput(data.suggestion.openAvcDeviceId);
        const suggested: CatalogOption = {
          id: data.suggestion.openAvcDeviceId,
          label: [
            data.suggestion.name ?? data.suggestion.openAvcDeviceId,
            data.suggestion.ip,
            data.suggestion.driverName,
          ].filter(Boolean).join(' · '),
          ip: data.suggestion.ip,
          driverName: data.suggestion.driverName,
        };
        setSelectedCatalog(suggested);
      }
    } catch {
      setError('Could not load OpenAVC context.');
    } finally {
      setLoading(false);
      onDeviceUpdate?.();
    }
  }, [refreshContext, onDeviceUpdate]);

  useEffect(() => {
    void loadContext();
  }, [loadContext]);

  const handleLink = async (openAvcDeviceId?: string) => {
    const targetId = (openAvcDeviceId ?? selectedCatalog?.id ?? linkInput).trim();
    if (!targetId) return;
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await ApiService.saveOpenAvcDeviceLink(device.id, targetId);
      setSuccess(`Linked to OpenAVC device "${targetId}".`);
      await loadContext();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to save link.');
    } finally {
      setBusy(false);
    }
  };

  const handleUnlink = async () => {
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await ApiService.deleteOpenAvcDeviceLink(device.id);
      setSuccess('OpenAVC link removed.');
      setSelectedCatalog(null);
      await loadContext();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to remove link.');
    } finally {
      setBusy(false);
    }
  };

  const handleCommand = async (command: string) => {
    if (!command) return;
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      const result = await ApiService.sendOpenAvcDeviceCommand(device.id, command);
      setSuccess(result.message || 'Command sent.');
      await refreshContext();
      onDeviceUpdate?.();
      setSelectedCommand('');
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'OpenAVC command failed.');
    } finally {
      setBusy(false);
    }
  };

  const handleMacro = async (macroId: string) => {
    if (!macroId) return;
    setMacroRunning(true);
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      const result = await ApiService.executeOpenAvcMacro(device.id, macroId);
      setSuccess(result.message || 'Macro completed.');
      await refreshContext();
      onDeviceUpdate?.();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'OpenAVC macro failed.');
    } finally {
      setMacroRunning(false);
      setBusy(false);
    }
  };

  const handleSavePins = async () => {
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      const result = await ApiService.saveOpenAvcMacroPins(device.id, pinDraft);
      setSuccess(result.message || 'Macro pins saved.');
      await loadContext();
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : 'Failed to save macro pins.');
    } finally {
      setBusy(false);
    }
  };

  const hubRelay = isHub || context?.hubRelay;
  const configured = context?.configured ?? false;
  const linked = context?.linked ?? false;
  const integrationStatus = context?.integrationStatus ?? (configured ? 'offline' : 'disabled');
  const openAvcOnline = integrationStatus === 'online';
  const openAvcUnreachable =
    (configured || hubRelay) && integrationStatus === 'offline';
  const openAvcAuthFailed = integrationStatus === 'auth_failed';
  const commands = context?.commands ?? [];
  const macros = context?.macros ?? [];
  const hasMacroPins = context?.hasMacroPins ?? false;
  const readings = useMemo(() => {
    const raw = context?.readings ?? [];
    return raw.map((r) => ({
      label: r.label ?? (r as { Label?: string }).Label ?? '',
      value: r.value ?? (r as { Value?: string }).Value ?? '',
    })).filter((r) => r.label && r.value);
  }, [context?.readings]);
  const statusEntries = Object.entries(context?.displayState ?? {}).filter(
    ([key]) => ['connected', 'online', 'power', 'input', 'source'].includes(key.toLowerCase()),
  );
  const lastError = context?.lastError;
  const suggestion = context?.suggestion;
  const canControl = isAdmin && (configured || hubRelay) && openAvcOnline && !openAvcAuthFailed;

  const accordionSx = {
    bgcolor: 'transparent',
    boxShadow: 'none',
    '&:before': { display: 'none' },
    border: '1px solid',
    borderColor: 'divider',
    borderRadius: '8px !important',
    mb: 1,
    '&.Mui-expanded': { mb: 1 },
  };

  const integrationChip = (() => {
    if (hubRelay) {
      if (integrationStatus === 'offline')
        return { label: 'Node OpenAVC offline', color: 'default' as const };
      if (integrationStatus === 'auth_failed')
        return { label: 'Node login failed', color: 'error' as const };
      return null;
    }
    switch (integrationStatus) {
      case 'online':
        return { label: 'OpenAVC online', color: 'success' as const };
      case 'offline':
        return { label: 'OpenAVC offline', color: 'default' as const };
      case 'auth_failed':
        return { label: 'Login failed', color: 'error' as const };
      case 'disabled':
        return { label: 'Not configured', color: 'default' as const };
      default:
        return null;
    }
  })();

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        borderRadius: 3,
        bgcolor: alpha(theme.palette.info.main, 0.04),
        borderColor: alpha(theme.palette.info.main, 0.2),
        overflow: 'hidden',
      }}
    >
      <Stack direction="row" spacing={1.5} alignItems="flex-start" sx={{ minWidth: 0 }}>
        <HubIcon color="info" sx={{ mt: 0.25, flexShrink: 0 }} />
        <Box flex={1} sx={{ minWidth: 0 }}>
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
            <Typography variant="body2" fontWeight={800}>OpenAVC Control Bridge</Typography>
            {linked && (
              <Chip size="small" label="Linked" color="success" sx={{ height: 20, fontSize: '0.65rem', fontWeight: 800 }} />
            )}
            {context?.driverName && (
              <Chip
                size="small"
                variant="outlined"
                label={context.driverName}
                sx={{ height: 20, fontSize: '0.65rem', fontWeight: 700 }}
              />
            )}
            {hubRelay && (
              <Chip
                size="small"
                label="Hub relay"
                color="info"
                variant="outlined"
                sx={{ height: 20, fontSize: '0.65rem', fontWeight: 700 }}
              />
            )}
            {linked && hasMacroPins && (
              <Chip
                size="small"
                icon={<PushPinIcon sx={{ fontSize: '0.75rem !important' }} />}
                label={`${pinDraft.length} pinned`}
                variant="outlined"
                sx={{ height: 20, fontSize: '0.65rem', fontWeight: 700 }}
              />
            )}
            {integrationChip && (
              <Chip
                size="small"
                label={integrationChip.label}
                color={integrationChip.color}
                variant={integrationChip.color === 'default' ? 'outlined' : 'filled'}
                sx={{ height: 20, fontSize: '0.65rem', fontWeight: 700 }}
              />
            )}
          </Stack>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
            Run pinned macros here. Output shows under Device readings when the macro writes OpenAVC state.
          </Typography>

          {loading ? (
            <Stack direction="row" alignItems="center" spacing={1} mt={1.5}>
              <CircularProgress size={16} />
              <Typography variant="caption" color="text.secondary">Loading…</Typography>
            </Stack>
          ) : (
            <Stack spacing={1.5} mt={1.5}>
              {!configured && !hubRelay && (
                <Alert severity="warning" sx={{ py: 0 }}>
                  Enable OpenAVC under Settings → OpenAVC on the site node first.
                </Alert>
              )}

              {openAvcUnreachable && (
                <Alert severity="warning" sx={{ py: 0 }}>
                  Cannot reach OpenAVC on the site node. Verify the service is running and the base URL under Settings → OpenAVC.
                </Alert>
              )}

              {openAvcAuthFailed && (
                <Alert severity="error" sx={{ py: 0 }}>
                  OpenAVC rejected the saved credentials. Update username/password under Settings → OpenAVC.
                </Alert>
              )}

              {linked && !isAdmin && (
                <Typography variant="caption" color="text.secondary">
                  View only. Macros and driver commands require an admin account.
                </Typography>
              )}

              {error && (
                <Alert severity="error" sx={{ py: 0 }} onClose={() => setError(null)}>
                  {error}
                </Alert>
              )}
              {success && (
                <Alert severity="success" sx={{ py: 0 }} onClose={() => setSuccess(null)}>
                  {success}
                </Alert>
              )}

              {!linked && suggestion && isAdmin && (
                <Alert
                  severity="info"
                  sx={{ py: 0.5, alignItems: 'center' }}
                  action={
                    <Button
                      size="small"
                      color="inherit"
                      disabled={busy}
                      onClick={() => void handleLink(suggestion.openAvcDeviceId)}
                      sx={{ fontWeight: 800, whiteSpace: 'nowrap' }}
                    >
                      Link suggested
                    </Button>
                  }
                >
                  <Typography variant="caption" component="span">
                    {suggestion.matchReason === 'hostname' ? 'Name match' : 'IP match'}:{' '}
                    <strong>{suggestion.openAvcDeviceId}</strong>
                    {suggestion.ip ? ` @ ${suggestion.ip}` : ''}
                    {suggestion.driverName ? ` · ${suggestion.driverName}` : ''}
                  </Typography>
                </Alert>
              )}

              {linked ? (
                <>
                  <Typography variant="caption" color="text.secondary">
                    OpenAVC device <strong>{context?.openAvcDeviceId}</strong>
                    {context?.deviceName ? ` (${context.deviceName})` : ''}
                  </Typography>

                  {statusEntries.length > 0 && (
                    <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap>
                      {statusEntries.map(([key, value]) => (
                        <Chip
                          key={key}
                          size="small"
                          variant="outlined"
                          label={`${formatStateLabel(key)}: ${value}`}
                          sx={{ fontSize: '0.65rem', height: 22 }}
                        />
                      ))}
                    </Stack>
                  )}

                  {lastError && (
                    <Typography variant="caption" color="error.main" display="block">
                      <strong>Last error:</strong> {lastError}
                    </Typography>
                  )}

                  <OpenAvcDrawerSection
                    title={`Device readings${readings.length > 0 ? ` (${readings.length})` : ''}`}
                    isMobile={isMobile}
                    expanded={readingsExpanded}
                    onExpandedChange={setReadingsExpanded}
                    accordionSx={accordionSx}
                  >
                    {readings.length > 0 ? (
                      <>
                        <ReadingsGrid readings={readings} />
                        <OpenAvcReadingsAuthoringHint compact />
                      </>
                    ) : (
                      <>
                        <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
                          No readings yet. Run a macro under <strong>Run actions</strong> after it writes OpenAVC state.
                        </Typography>
                        <OpenAvcReadingsAuthoringHint />
                      </>
                    )}
                  </OpenAvcDrawerSection>

                  {canControl && (macros.length > 0 || commands.length > 0) && (
                    <OpenAvcDrawerSection
                      title="Run actions"
                      isMobile={isMobile}
                      expanded={runExpanded}
                      onExpandedChange={setRunExpanded}
                      accordionSx={accordionSx}
                    >
                      <Stack spacing={1.25} sx={{ width: '100%' }}>
                        {macros.length > 0 && (
                          <Stack spacing={0.75}>
                            <Select
                              size="small"
                              displayEmpty
                              disabled={busy || macroRunning}
                              value=""
                              onChange={(e) => {
                                const macroId = e.target.value;
                                if (macroId) void handleMacro(macroId);
                              }}
                              sx={{ width: '100%', fontSize: '0.85rem', fontWeight: 700 }}
                            >
                              <MenuItem value="" disabled>Run macro…</MenuItem>
                              {macros.map((m) => (
                                <MenuItem key={m.id} value={m.id}>{m.name}</MenuItem>
                              ))}
                            </Select>
                            {macroRunning && (
                              <Stack direction="row" alignItems="center" spacing={1}>
                                <CircularProgress size={14} />
                                <Typography variant="caption" color="text.secondary">
                                  Running macro… up to 2 minutes.
                                </Typography>
                              </Stack>
                            )}
                          </Stack>
                        )}
                        {commands.length > 0 && (
                          <Stack direction={isMobile ? 'column' : 'row'} spacing={1} sx={{ width: '100%' }}>
                            <Select
                              size="small"
                              displayEmpty
                              disabled={busy}
                              value={selectedCommand}
                              onChange={(e) => setSelectedCommand(e.target.value)}
                              sx={{ width: '100%', fontSize: '0.8rem' }}
                            >
                              <MenuItem value=""><em>Driver command…</em></MenuItem>
                              {commands.map((cmd) => (
                                <MenuItem key={cmd.id} value={cmd.id}>{cmd.label}</MenuItem>
                              ))}
                            </Select>
                            <Button
                              size="small"
                              variant="outlined"
                              disabled={busy || !selectedCommand}
                              onClick={() => void handleCommand(selectedCommand)}
                              sx={{ fontWeight: 700, flexShrink: 0 }}
                              fullWidth={isMobile}
                            >
                              Run
                            </Button>
                          </Stack>
                        )}
                      </Stack>
                    </OpenAvcDrawerSection>
                  )}

                  {linked && isAdmin && macroCatalog.length > 0 && (
                    <OpenAvcDrawerSection
                      title={`Pinned macros${hasMacroPins ? ` (${pinDraft.length})` : ''}`}
                      isMobile={isMobile}
                      expanded={showPinEditor}
                      onExpandedChange={setShowPinEditor}
                      accordionSx={accordionSx}
                    >
                      <Stack spacing={1} sx={{ width: '100%', minWidth: 0 }}>
                        {!hasMacroPins && (
                          <Typography variant="caption" color="text.secondary">
                            Optional: pin macros so operators only see actions for this device. Until pinned, the full project list is shown.
                          </Typography>
                        )}
                        <Box
                          sx={{
                            maxHeight: isMobile ? 180 : 220,
                            overflowY: 'auto',
                            border: '1px solid',
                            borderColor: 'divider',
                            borderRadius: 1,
                            p: 0.5,
                            width: '100%',
                          }}
                        >
                          <FormGroup>
                            {macroCatalog.map((m) => (
                              <FormControlLabel
                                key={m.id}
                                sx={{ alignItems: 'flex-start', mx: 0, width: '100%' }}
                                control={
                                  <Checkbox
                                    size="small"
                                    disabled={busy}
                                    checked={pinDraft.includes(m.id)}
                                    onChange={(e) => {
                                      setPinDraft((prev) =>
                                        e.target.checked
                                          ? [...prev, m.id]
                                          : prev.filter((id) => id !== m.id),
                                      );
                                    }}
                                  />
                                }
                                label={
                                  <Typography variant="caption" sx={{ wordBreak: 'break-word' }}>
                                    {m.name !== m.id ? `${m.name} (${m.id})` : m.name}
                                  </Typography>
                                }
                              />
                            ))}
                          </FormGroup>
                        </Box>
                        <Stack direction={isMobile ? 'column' : 'row'} spacing={1} useFlexGap>
                          <Button
                            size="small"
                            variant="contained"
                            disabled={busy}
                            onClick={() => void handleSavePins()}
                            sx={{ fontWeight: 700 }}
                            fullWidth={isMobile}
                          >
                            Save pins
                          </Button>
                          <Button
                            size="small"
                            color="inherit"
                            disabled={busy || pinDraft.length === 0}
                            onClick={() => setPinDraft([])}
                            sx={{ fontWeight: 600 }}
                            fullWidth={isMobile}
                          >
                            Clear all
                          </Button>
                        </Stack>
                      </Stack>
                    </OpenAvcDrawerSection>
                  )}

                  {hasMacroPins && macros.length === 0 && (
                    <Alert severity="warning" sx={{ py: 0 }}>
                      Pinned macros are missing from the OpenAVC project. Update pins or restore macros in Programmer.
                    </Alert>
                  )}

                  {Object.keys(context?.state ?? {}).length > 0 && (
                    <>
                      <Button
                        size="small"
                        color="inherit"
                        onClick={() => setShowDriverState((v) => !v)}
                        sx={{ alignSelf: 'flex-start', fontWeight: 600, fontSize: '0.7rem' }}
                      >
                        {showDriverState ? 'Hide raw driver state' : 'Show raw driver state'}
                      </Button>
                      <Collapse in={showDriverState}>
                        <Box
                          sx={{
                            maxHeight: 160,
                            overflowY: 'auto',
                            p: 1,
                            borderRadius: 1,
                            bgcolor: alpha(theme.palette.background.paper, 0.5),
                            border: '1px solid',
                            borderColor: 'divider',
                          }}
                        >
                          {Object.entries(context?.state ?? {}).map(([key, value]) => (
                            <Typography key={key} variant="caption" display="block" sx={{ fontFamily: 'monospace', fontSize: '0.65rem' }}>
                              {key}: {value}
                            </Typography>
                          ))}
                        </Box>
                      </Collapse>
                    </>
                  )}

                  {canControl && (
                    <Button
                      size="small"
                      color="inherit"
                      disabled={busy}
                      onClick={() => void handleUnlink()}
                      sx={{ alignSelf: 'flex-start', fontWeight: 600 }}
                    >
                      Unlink
                    </Button>
                  )}
                </>
              ) : isAdmin ? (
                <Stack spacing={1}>
                  {catalogOptions.length > 0 ? (
                    <Autocomplete
                      size="small"
                      options={catalogOptions}
                      value={selectedCatalog}
                      onChange={(_, value) => {
                        setSelectedCatalog(value);
                        if (value) setLinkInput(value.id);
                      }}
                      getOptionLabel={(o) => o.label}
                      isOptionEqualToValue={(a, b) => a.id === b.id}
                      renderInput={(params) => (
                        <TextField
                          {...params}
                          label="OpenAVC device"
                          placeholder="Pick from your OpenAVC project"
                        />
                      )}
                    />
                  ) : (
                    <TextField
                      size="small"
                      label="OpenAVC device id"
                      placeholder="device_id from OpenAVC"
                      value={linkInput}
                      onChange={(e) => setLinkInput(e.target.value)}
                      helperText="Configure OpenAVC first, or enter a device id manually."
                    />
                  )}
                  <Button
                    size="small"
                    variant="contained"
                    disabled={busy || (!configured && !hubRelay) || !(selectedCatalog?.id || linkInput.trim())}
                    startIcon={<LinkIcon />}
                    onClick={() => void handleLink()}
                    sx={{ alignSelf: 'flex-start', fontWeight: 700 }}
                  >
                    Link device
                  </Button>
                </Stack>
              ) : (
                <Typography variant="caption" color="text.secondary">
                  Not linked to OpenAVC. An admin can link this device here.
                </Typography>
              )}
            </Stack>
          )}
        </Box>
      </Stack>
    </Paper>
  );
}
