import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  Autocomplete,
  Box,
  CircularProgress,
  MenuItem,
  TextField,
  Typography,
} from '@mui/material';
import type { Device } from '../../models/Device';
import { ApiService } from '../../services/apiService';
import { formatDeviceTypeLabel } from '../../utils/deviceTypeLabels';

const ATTACHMENT_KINDS = ['Unknown', 'Ethernet', 'WiFi', 'Fiber', 'Other'] as const;
type AttachmentKind = (typeof ATTACHMENT_KINDS)[number];

function kindLabel(kind: string): string {
  if (kind === 'WiFi') return 'Wi-Fi';
  return kind;
}

interface DeviceAttachmentFieldsProps {
  device: Device;
  onUpdated?: (device?: Device) => void;
  onError?: (message: string) => void;
  disabled?: boolean;
}

function parentLabel(d: Device): string {
  const name = d.name?.trim() || d.hostname?.trim() || d.ipAddress;
  const type = formatDeviceTypeLabel(d.type);
  return `${name} (${type})`;
}

function portPlaceholder(kind: AttachmentKind): string {
  switch (kind) {
    case 'Ethernet':
      return 'e.g. 1/0/12 or Gi1/0/12';
    case 'WiFi':
      return 'SSID or band (optional)';
    case 'Fiber':
      return 'e.g. SFP1 or 1/1/1';
    case 'Other':
      return 'Port or label (optional)';
    default:
      return 'Port or SSID';
  }
}

function coerceKind(raw?: string | null): AttachmentKind {
  const k = (raw || 'Unknown').trim();
  if ((ATTACHMENT_KINDS as readonly string[]).includes(k)) return k as AttachmentKind;
  return 'Unknown';
}

function placeholderParent(device: Device, parentId: string): Device {
  const label =
    device.attachmentParentName?.trim()
    || device.attachmentParentIp?.trim()
    || device.attachmentParentMac?.trim()
    || parentId;
  return {
    id: parentId,
    name: label,
    ipAddress: '',
    hostname: null,
    nodeId: device.nodeId,
    macAddress: device.attachmentParentMac || '',
    openPorts: [],
    vendor: '',
    status: 'online',
    type: 'Network Switch',
    model: '',
    confidenceScore: 0,
    location: '',
    firstSeen: '',
    lastSeen: '',
    scanCount: 0,
    totalSweepsSeen: 0,
    totalSweepsOnline: 0,
    uptimePercent: 0,
    flapCount: 0,
    lastStateChangeUtc: null,
    averageLatencyMs: null,
    lastSweepId: null,
    managedByUserId: null,
    managedByUsername: null,
    alertsEnabled: true,
  };
}

export function DeviceAttachmentFields({
  device,
  onUpdated,
  onError,
  disabled,
}: DeviceAttachmentFieldsProps) {
  const siteNodeId = device.nodeId?.trim() || undefined;

  const [kind, setKind] = useState<AttachmentKind>(() => coerceKind(device.attachmentKind));
  const [port, setPort] = useState(device.attachmentPort ?? '');
  const [parentId, setParentId] = useState<string | null>(
    device.attachmentParentDeviceId ?? null,
  );
  const [parentOptions, setParentOptions] = useState<Device[]>([]);
  const [parentLoading, setParentLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const portDirtyRef = useRef(false);
  const savingRef = useRef(false);

  // Reset local draft when switching devices.
  useEffect(() => {
    portDirtyRef.current = false;
    savingRef.current = false;
    setKind(coerceKind(device.attachmentKind));
    setPort(device.attachmentPort ?? '');
    setParentId(device.attachmentParentDeviceId ?? null);
    setParentOptions([]);
    setSaved(false);
  }, [device.id]);

  useEffect(() => {
    if (!portDirtyRef.current && !savingRef.current) {
      setKind(coerceKind(device.attachmentKind));
      setPort(device.attachmentPort ?? '');
      setParentId(device.attachmentParentDeviceId ?? null);
    }
  }, [
    device.attachmentKind,
    device.attachmentPort,
    device.attachmentParentDeviceId,
  ]);

  const loadParents = useCallback(async () => {
    setParentLoading(true);
    try {
      const rows = await ApiService.getAttachmentParents({
        nodeId: siteNodeId,
        excludeDeviceId: device.id,
      });
      const keepParentIds = new Set(
        [device.attachmentParentDeviceId, parentId].filter(Boolean) as string[],
      );
      const mapped: Device[] = (rows || []).map((row) => ({
        id: row.id,
        name: row.label,
        hostname: null,
        ipAddress: row.ipAddress || '',
        type: row.type || 'Network Switch',
        nodeId: row.nodeId || device.nodeId,
        macAddress: '',
        openPorts: [],
        vendor: '',
        status: 'online',
        model: '',
        confidenceScore: 0,
        location: '',
        firstSeen: '',
        lastSeen: '',
        scanCount: 0,
        totalSweepsSeen: 0,
        totalSweepsOnline: 0,
        uptimePercent: 0,
        flapCount: 0,
        lastStateChangeUtc: null,
        averageLatencyMs: null,
        lastSweepId: null,
        managedByUserId: null,
        managedByUsername: null,
        alertsEnabled: true,
      }));
      setParentOptions((prev) => {
        const extras = keepParentIds.size > 0
          ? prev.filter((d) => keepParentIds.has(d.id))
          : [];
        const byId = new Map<string, Device>();
        for (const d of [...mapped, ...extras]) byId.set(d.id, d);
        return [...byId.values()];
      });
    } catch {
      setParentOptions([]);
    } finally {
      setParentLoading(false);
    }
  }, [
    device.id,
    device.nodeId,
    device.attachmentParentDeviceId,
    parentId,
    siteNodeId,
  ]);

  useEffect(() => {
    void loadParents();
  }, [device.id, siteNodeId, loadParents]);

  const selectedParent = useMemo(() => {
    const found = parentOptions.find((d) => d.id === parentId);
    if (found) return found;
    if (parentId) {
      return placeholderParent(device, parentId);
    }
    if (device.attachmentParentName || device.attachmentParentIp || device.attachmentParentMac) {
      return placeholderParent(device, device.attachmentParentDeviceId || 'saved-uplink');
    }
    return null;
  }, [parentOptions, parentId, device]);

  const persist = useCallback(
    async (next: {
      kind: AttachmentKind;
      port: string;
      parentDeviceId: string | null;
    }) => {
      const currentKind = coerceKind(device.attachmentKind);
      const currentPort = device.attachmentPort ?? '';
      const currentParent = device.attachmentParentDeviceId ?? null;
      if (
        next.kind === currentKind &&
        next.port === currentPort &&
        next.parentDeviceId === currentParent
      ) {
        portDirtyRef.current = false;
        return;
      }

      savingRef.current = true;
      setSaving(true);
      setSaved(false);
      try {
        const updated = (await ApiService.updateDeviceAttachment(device.id, {
          kind: next.kind,
          port: next.port,
          parentDeviceId: next.parentDeviceId,
        })) as Device;
        portDirtyRef.current = false;
        setKind(coerceKind(updated.attachmentKind));
        setPort(updated.attachmentPort ?? '');
        setParentId(updated.attachmentParentDeviceId ?? null);
        if (updated.attachmentParentDeviceId) {
          setParentOptions((prev) => {
            if (prev.some((d) => d.id === updated.attachmentParentDeviceId)) return prev;
            return [
              ...prev,
              placeholderParent(updated, updated.attachmentParentDeviceId!),
            ];
          });
        }
        setSaved(true);
        onUpdated?.(updated);
        window.setTimeout(() => setSaved(false), 1500);
      } catch (err: unknown) {
        const message =
          err instanceof Error ? err.message : 'Failed to save network link';
        onError?.(message);
        setKind(currentKind);
        setPort(currentPort);
        setParentId(currentParent);
        portDirtyRef.current = false;
      } finally {
        savingRef.current = false;
        setSaving(false);
      }
    },
    [device, onError, onUpdated],
  );

  const commitPort = useCallback(() => {
    void persist({ kind, port: port.trim(), parentDeviceId: parentId });
  }, [kind, parentId, persist, port]);

  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: '110px 1fr', rowGap: 2, alignItems: 'center' }}>
      <Typography variant="body2" color="text.secondary">Mode</Typography>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <TextField
          select
          size="small"
          fullWidth
          value={kind}
          disabled={disabled || saving}
          variant="standard"
          onChange={(e) => {
            const next = e.target.value as AttachmentKind;
            setKind(next);
            void persist({ kind: next, port: port.trim(), parentDeviceId: parentId });
          }}
          sx={{ '& .MuiInput-root': { fontSize: '0.875rem' } }}
        >
          {ATTACHMENT_KINDS.map((k) => (
            <MenuItem key={k} value={k}>
              {kindLabel(k)}
            </MenuItem>
          ))}
        </TextField>
        {saving && <CircularProgress size={14} aria-label="Saving" />}
        {saved && (
          <Typography sx={{ color: '#4caf50', fontSize: '0.7rem', flexShrink: 0 }}>Saved</Typography>
        )}
      </Box>

      <Typography variant="body2" color="text.secondary">Port / SSID</Typography>
      <TextField
        size="small"
        fullWidth
        value={port}
        disabled={disabled || saving}
        placeholder={portPlaceholder(kind)}
        variant="standard"
        inputProps={{ maxLength: 64, 'aria-busy': saving || undefined }}
        onChange={(e) => {
          portDirtyRef.current = true;
          setSaved(false);
          setPort(e.target.value);
        }}
        onBlur={() => void commitPort()}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault();
            void commitPort();
          }
        }}
        sx={{ '& .MuiInput-root': { fontSize: '0.875rem', fontFamily: 'monospace' } }}
      />

      <Typography variant="body2" color="text.secondary">Uplink</Typography>
      <Autocomplete
        size="small"
        options={parentOptions}
        loading={parentLoading}
        value={selectedParent}
        disabled={disabled || saving}
        clearOnBlur={false}
        getOptionLabel={(option) => parentLabel(option)}
        isOptionEqualToValue={(a, b) => a.id === b.id}
        onOpen={() => {
          void loadParents();
        }}
        noOptionsText={
          parentLoading
            ? 'Loading uplink devices...'
            : 'No switches, APs, or routers on this site'
        }
        onChange={(_, next) => {
          const nextId = next?.id ?? null;
          setParentId(nextId);
          void persist({ kind, port: port.trim(), parentDeviceId: nextId });
        }}
        renderInput={(params) => (
          <TextField
            {...params}
            variant="standard"
            placeholder="Switch, AP, router, or modem"
            InputProps={{
              ...params.InputProps,
              endAdornment: (
                <>
                  {parentLoading ? <CircularProgress color="inherit" size={14} /> : null}
                  {params.InputProps.endAdornment}
                </>
              ),
            }}
            sx={{ '& .MuiInput-root': { fontSize: '0.875rem' } }}
          />
        )}
      />

      {!siteNodeId && (
        <>
          <Box />
          <Typography variant="caption" color="warning.main">
            Site identity is missing on this row. Uplink choices may be incomplete until the next fleet sync.
          </Typography>
        </>
      )}

      {device.isAttachmentManuallySet && (
        <>
          <Box />
          <Typography variant="caption" color="text.secondary">
            Protected from discovery overwrites
          </Typography>
        </>
      )}
    </Box>
  );
}
