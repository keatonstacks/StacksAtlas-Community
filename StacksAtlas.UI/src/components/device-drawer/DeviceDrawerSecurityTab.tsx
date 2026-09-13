import { Paper, Stack, Typography, alpha, useTheme } from '@mui/material';
import ShieldIcon from '@mui/icons-material/Shield';

import { SecurityFindingCard } from '../security/SecurityFindingCard';
import {
  collectSecurityFindings,
  normalizeSecurityGrade,
} from './deviceDrawerUtils';

import type { Device } from '../../models/Device';

interface DeviceDrawerSecurityTabProps {
  device: Device;
  role?: string | null;
  onIgnoreRisk: (issue: string) => void;
  onDeviceUpdate: () => void;
  onNotify?: (message: string) => void;
}

export function DeviceDrawerSecurityTab({
  device,
  role,
  onIgnoreRisk,
  onDeviceUpdate,
  onNotify,
}: DeviceDrawerSecurityTabProps) {
  const theme = useTheme();
  const grade = normalizeSecurityGrade(device.securityGrade);
  const findings = collectSecurityFindings(device);
  const isViewer = role?.toLowerCase() === 'viewer';

  if (grade === 0 && findings.length === 0) {
    return (
      <Paper variant="outlined" sx={{ p: 3, borderRadius: 3, textAlign: 'center' }}>
        <ShieldIcon color="success" sx={{ fontSize: 40, mb: 1 }} />
        <Typography variant="body2" fontWeight={700}>Healthy security posture</Typography>
        <Typography variant="caption" color="text.secondary">No open risks for this device.</Typography>
      </Paper>
    );
  }

  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        borderRadius: 3,
        bgcolor:
          grade === 2
            ? alpha(theme.palette.error.main, 0.05)
            : grade === 1
              ? alpha(theme.palette.warning.main, 0.05)
              : alpha(theme.palette.success.main, 0.05),
        borderColor:
          grade === 2
            ? alpha(theme.palette.error.main, 0.3)
            : grade === 1
              ? alpha(theme.palette.warning.main, 0.3)
              : 'divider',
      }}
    >
      <Stack direction="row" spacing={2} alignItems="center" mb={findings.length > 0 ? 2 : 0}>
        <ShieldIcon
          sx={{
            fontSize: 32,
            color: grade === 2 ? 'error.main' : grade === 1 ? 'warning.main' : 'success.main',
          }}
        />
        <Stack>
          <Typography variant="caption" fontWeight={900} color="text.secondary">
            SECURITY HYGIENE
          </Typography>
          <Typography
            variant="body1"
            fontWeight={800}
            color={grade === 2 ? 'error.main' : grade === 1 ? 'warning.main' : 'text.primary'}
          >
            {grade === 2 ? 'Critical risks' : grade === 1 ? 'Warnings' : 'Healthy'}
          </Typography>
        </Stack>
      </Stack>

      {grade > 0 && findings.length === 0 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 1 }}>
          Risk details sync from the enrolled node on the next telemetry update. Run a deep scan on the node if this persists.
        </Typography>
      )}

      <Stack spacing={1.5}>
        {findings.map((finding) => (
          <SecurityFindingCard
            key={finding.raw}
            finding={finding}
            showIgnore={!isViewer}
            onCopied={onNotify}
            onIgnore={async () => {
              if (device.securityIssues) {
                device.securityIssues = device.securityIssues.filter((i) => i !== finding.raw);
                if (device.securityIssues.length === 0) device.securityGrade = 0;
              }
              await onIgnoreRisk(finding.raw);
              onDeviceUpdate();
            }}
          />
        ))}
      </Stack>
    </Paper>
  );
}
