import { useCallback, useEffect, useRef, useState } from 'react';
import {
  Box,
  Chip,
  CircularProgress,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import type { Device } from '../../models/Device';
import { ApiService } from '../../services/apiService';
import { formatWarrantyDate, getWarrantyStatus } from '../../utils/warrantyStatus';

interface DeviceAssetFieldsProps {
  device: Device;
  onUpdated?: (device?: Device) => void;
  onError?: (message: string) => void;
  disabled?: boolean;
}

function AssetField({
  label,
  value,
  placeholder,
  disabled,
  onSave,
}: {
  label: string;
  value: string;
  placeholder: string;
  disabled?: boolean;
  onSave: (next: string) => Promise<void>;
}) {
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const isDirtyRef = useRef(false);
  const savingRef = useRef(false);

  // Sync from server only when the user is not mid-edit.
  useEffect(() => {
    if (!isDirtyRef.current && !savingRef.current) {
      setDraft(value);
    }
  }, [value]);

  const commit = useCallback(async () => {
    if (draft === value) {
      isDirtyRef.current = false;
      return;
    }
    savingRef.current = true;
    setSaving(true);
    setSaved(false);
    try {
      await onSave(draft);
      isDirtyRef.current = false;
      setSaved(true);
      setTimeout(() => setSaved(false), 1500);
    } catch {
      setDraft(value);
      isDirtyRef.current = false;
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  }, [draft, onSave, value]);

  return (
    <>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
        <TextField
          size="small"
          fullWidth
          value={draft}
          disabled={disabled}
          placeholder={placeholder}
          variant="standard"
          inputProps={{ 'aria-busy': saving || undefined }}
          onChange={(e) => {
            isDirtyRef.current = true;
            setSaved(false);
            setDraft(e.target.value);
          }}
          onBlur={() => void commit()}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault();
              void commit();
            }
          }}
          sx={{ '& .MuiInput-root': { fontSize: '0.875rem' } }}
        />
        {saving && <CircularProgress size={14} aria-label="Saving" />}
        {saved && <Typography sx={{ color: '#4caf50', fontSize: '0.7rem', flexShrink: 0 }}>Saved</Typography>}
      </Box>
    </>
  );
}

function WarrantyField({
  value,
  disabled,
  onSave,
}: {
  value: string;
  disabled?: boolean;
  onSave: (next: string | null) => Promise<void>;
}) {
  const [draft, setDraft] = useState(value);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const isDirtyRef = useRef(false);
  const savingRef = useRef(false);

  useEffect(() => {
    if (!isDirtyRef.current && !savingRef.current) {
      setDraft(value);
    }
  }, [value]);

  const commit = useCallback(async (next: string) => {
    const normalized = next || null;
    const current = value || null;
    if (normalized === current) {
      isDirtyRef.current = false;
      return;
    }
    savingRef.current = true;
    setSaving(true);
    setSaved(false);
    try {
      await onSave(normalized);
      isDirtyRef.current = false;
      setSaved(true);
      setTimeout(() => setSaved(false), 1500);
    } catch {
      setDraft(value);
      isDirtyRef.current = false;
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  }, [onSave, value]);

  useEffect(() => () => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
  }, []);

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
      <TextField
        size="small"
        fullWidth
        type="date"
        disabled={disabled}
        value={draft}
        variant="standard"
        inputProps={{ 'aria-busy': saving || undefined }}
        onChange={(e) => {
          const next = e.target.value;
          isDirtyRef.current = true;
          setSaved(false);
          setDraft(next);
          if (debounceRef.current) clearTimeout(debounceRef.current);
          debounceRef.current = setTimeout(() => {
            void commit(next);
          }, 600);
        }}
        onBlur={() => {
          if (debounceRef.current) {
            clearTimeout(debounceRef.current);
            debounceRef.current = null;
          }
          void commit(draft);
        }}
        sx={{ '& .MuiInput-root': { fontSize: '0.875rem' } }}
      />
      {saving && <CircularProgress size={14} aria-label="Saving" />}
      {saved && <Typography sx={{ color: '#4caf50', fontSize: '0.7rem', flexShrink: 0 }}>Saved</Typography>}
    </Box>
  );
}

export function DeviceAssetFields({ device, onUpdated, onError, disabled }: DeviceAssetFieldsProps) {
  const theme = useTheme();
  const warrantyStatus = getWarrantyStatus(device.warrantyExpiresUtc);
  const fromOpenAvc = device.assetMetadataSource === 2;

  const saveAsset = useCallback(
    async (patch: {
      serialNumber?: string | null;
      assetTag?: string | null;
      firmwareVersion?: string | null;
      warrantyExpiresUtc?: string | null;
    }) => {
      try {
        const updated = await ApiService.updateDeviceAsset(device.id, patch) as Device;
        onUpdated?.(updated);
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : 'Failed to save asset field';
        onError?.(message);
        throw err;
      }
    },
    [device.id, onError, onUpdated],
  );

  const warrantyChip =
    warrantyStatus === 'expired'
      ? { label: 'Warranty expired', color: 'error' as const }
      : warrantyStatus === 'expiring'
        ? { label: 'Warranty expiring soon', color: 'warning' as const }
        : warrantyStatus === 'ok'
          ? { label: 'Warranty active', color: 'default' as const }
          : null;

  return (
    <Box
      sx={{
        display: 'grid',
        gridTemplateColumns: '110px 1fr',
        rowGap: 2,
        alignItems: 'center',
      }}
    >
      {fromOpenAvc && (
        <>
          <Box />
          <Typography variant="caption" color="text.secondary">
            Serial and firmware may auto-update from OpenAVC when not manually set.
          </Typography>
        </>
      )}

      <AssetField
        label="Serial"
        value={device.serialNumber || ''}
        placeholder="Not set"
        disabled={disabled}
        onSave={(next) => saveAsset({ serialNumber: next })}
      />

      <AssetField
        label="Asset tag"
        value={device.assetTag || ''}
        placeholder="Site inventory tag"
        disabled={disabled}
        onSave={(next) => saveAsset({ assetTag: next })}
      />

      <AssetField
        label="Firmware"
        value={device.firmwareVersion || ''}
        placeholder="Not set"
        disabled={disabled}
        onSave={(next) => saveAsset({ firmwareVersion: next })}
      />

      <Typography variant="body2" color="text.secondary">Warranty</Typography>
      <Box>
        <WarrantyField
          value={device.warrantyExpiresUtc ? device.warrantyExpiresUtc.slice(0, 10) : ''}
          disabled={disabled}
          onSave={(next) => saveAsset({ warrantyExpiresUtc: next })}
        />
        {warrantyChip && (
          <Chip
            size="small"
            label={warrantyChip.label}
            color={warrantyChip.color}
            variant="outlined"
            sx={{
              mt: 1,
              fontWeight: 700,
              ...(warrantyChip.color === 'default' && {
                borderColor: alpha(theme.palette.success.main, 0.4),
                color: theme.palette.success.light,
              }),
            }}
          />
        )}
        {device.warrantyExpiresUtc && (
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mt: 0.5 }}>
            Expires {formatWarrantyDate(device.warrantyExpiresUtc)}
          </Typography>
        )}
      </Box>
    </Box>
  );
}
