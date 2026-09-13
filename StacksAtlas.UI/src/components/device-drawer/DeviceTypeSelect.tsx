import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Autocomplete,
  Box,
  CircularProgress,
  TextField,
  Typography,
  createFilterOptions,
} from '@mui/material';
import CheckIcon from '@mui/icons-material/Check';
import type { Device } from '../../models/Device';
import { ApiService } from '../../services/apiService';
import {
  DEVICE_TYPE_OPTIONS,
  deviceTypeCategory,
  filterDeviceTypeOptions,
  formatDeviceTypeLabel,
} from '../../utils/deviceTypeLabels';

interface DeviceTypeSelectProps {
  device: Device;
  onUpdated?: () => void;
  disabled?: boolean;
}

const baseFilter = createFilterOptions<string>({
  stringify: (option) => formatDeviceTypeLabel(option),
});

export function DeviceTypeSelect({ device, onUpdated, disabled }: DeviceTypeSelectProps) {
  const [value, setValue] = useState(() => formatDeviceTypeLabel(device.type || 'Unknown'));
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const timerRef = useRef<number | null>(null);

  useEffect(() => {
    setValue(formatDeviceTypeLabel(device.type || 'Unknown'));
  }, [device.id, device.type]);

  useEffect(() => () => {
    if (timerRef.current)
      window.clearTimeout(timerRef.current);
  }, []);

  const options = useMemo(() => {
    const merged = new Set(DEVICE_TYPE_OPTIONS.map(formatDeviceTypeLabel));
    if (device.type?.trim())
      merged.add(formatDeviceTypeLabel(device.type));
    return Array.from(merged).sort((a, b) => a.localeCompare(b));
  }, [device.type]);

  const persist = (next: string) => {
    const trimmed = formatDeviceTypeLabel(next);
    if (!trimmed || trimmed === formatDeviceTypeLabel(device.type || ''))
      return;

    setSaving(true);
    setSaved(false);
    if (timerRef.current)
      window.clearTimeout(timerRef.current);

    timerRef.current = window.setTimeout(async () => {
      try {
        await ApiService.updateDeviceType(device.id, trimmed);
        setSaving(false);
        setSaved(true);
        onUpdated?.();
        window.setTimeout(() => setSaved(false), 1500);
      } catch {
        setSaving(false);
        setValue(formatDeviceTypeLabel(device.type || 'Unknown'));
      }
    }, 300);
  };

  return (
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, minWidth: 0 }}>
      <Autocomplete
        freeSolo
        disableClearable
        disabled={disabled}
        size="small"
        fullWidth
        options={options}
        value={value}
        openOnFocus
        autoHighlight
        selectOnFocus
        handleHomeEndKeys
        groupBy={(option) => deviceTypeCategory(option)}
        filterOptions={(opts, state) => {
          const filtered = filterDeviceTypeOptions(opts, state.inputValue);
          if (filtered.length > 0)
            return filtered;
          // Fall back to MUI default so freeSolo custom values still work.
          return baseFilter(opts, state);
        }}
        getOptionLabel={(option) => formatDeviceTypeLabel(option)}
        isOptionEqualToValue={(a, b) =>
          formatDeviceTypeLabel(a).toLowerCase() === formatDeviceTypeLabel(b).toLowerCase()
        }
        onChange={(_, newValue) => {
          const next = formatDeviceTypeLabel(newValue ?? '');
          if (!next)
            return;
          setValue(next);
          persist(next);
        }}
        onInputChange={(_, newInput, reason) => {
          if (reason === 'input')
            setValue(newInput);
        }}
        onBlur={() => persist(value)}
        ListboxProps={{ style: { maxHeight: 320 } }}
        renderInput={(params) => (
          <TextField
            {...params}
            placeholder="Search types (e.g. receiver, laptop)"
            inputProps={{ ...params.inputProps, maxLength: 64 }}
          />
        )}
      />
      {saving && <CircularProgress size={14} />}
      {saved && !saving && (
        <Typography variant="caption" color="success.main" sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
          <CheckIcon sx={{ fontSize: 14 }} />
          Saved
        </Typography>
      )}
    </Box>
  );
}
