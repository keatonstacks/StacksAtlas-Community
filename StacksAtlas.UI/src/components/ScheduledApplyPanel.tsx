import {
  Alert,
  Box,
  FormControl,
  FormControlLabel,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Switch,
  TextField,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import type { UpdateScheduleConfig } from '../utils/updateSchedule';
import { formatScheduleLabel, WEEKDAY_LABELS } from '../utils/updateSchedule';

interface ScheduledApplyPanelProps {
  schedule: UpdateScheduleConfig;
  /** True when scheduled apply cannot run on this appliance (portable, Hub brain, non-Windows). */
  unsupported?: boolean;
  unsupportedReason?: string;
  onChange: (patch: Partial<UpdateScheduleConfig>) => void;
}

export function ScheduledApplyPanel({
  schedule,
  unsupported = false,
  unsupportedReason,
  onChange,
}: ScheduledApplyPanelProps) {
  const theme = useTheme();
  const canConfigure = !unsupported;

  return (
    <Box
      sx={{
        p: 2.5,
        borderRadius: 3,
        bgcolor: alpha(theme.palette.background.default, 0.5),
        border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
        mt: 2,
      }}
    >
      <Typography variant="subtitle2" fontWeight={800} sx={{ mb: 0.5 }}>
        Scheduled apply (maintenance window)
      </Typography>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 2 }}>
        Admin opt-in: automatically apply a pending update during a weekly window. Preference is stored on this
        appliance and evaluated by a background worker (Settings does not need to stay open). One-click apply is
        Windows installed appliances today.
      </Typography>

      <FormControlLabel
        control={
          <Switch
            checked={schedule.enabled && canConfigure}
            onChange={(e) => onChange({ enabled: e.target.checked })}
            disabled={!canConfigure}
            color="secondary"
          />
        }
        label={
          <Typography variant="body2" sx={{ fontWeight: 600 }}>
            Enable scheduled apply
          </Typography>
        }
      />

      {unsupported && (
        <Alert severity="info" sx={{ mt: 1.5, borderRadius: 2 }}>
          {unsupportedReason
            ?? 'Scheduled apply runs only on Windows installed (non-portable, non-Hub) appliances. Update manually from Software Updates.'}
        </Alert>
      )}

      {schedule.enabled && canConfigure && (
        <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ mt: 2 }}>
          <FormControl size="small" sx={{ minWidth: 160 }}>
            <InputLabel>Day</InputLabel>
            <Select
              label="Day"
              value={schedule.dayOfWeek}
              onChange={(e) => onChange({ dayOfWeek: Number(e.target.value) })}
            >
              {WEEKDAY_LABELS.map((label, idx) => (
                <MenuItem key={label} value={idx}>
                  {label}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
          <TextField
            label="Time"
            type="time"
            size="small"
            value={`${schedule.hour.toString().padStart(2, '0')}:${schedule.minute.toString().padStart(2, '0')}`}
            onChange={(e) => {
              const [h, m] = e.target.value.split(':').map((v) => parseInt(v, 10));
              if (Number.isFinite(h) && Number.isFinite(m)) onChange({ hour: h, minute: m });
            }}
            InputLabelProps={{ shrink: true }}
          />
        </Stack>
      )}

      {schedule.enabled && canConfigure && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1.5 }}>
          Next window: {formatScheduleLabel(schedule)}. Applies on the appliance even if this browser is closed.
        </Typography>
      )}
    </Box>
  );
}
