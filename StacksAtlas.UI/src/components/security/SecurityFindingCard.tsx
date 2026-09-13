import { Box, Chip, IconButton, Stack, Tooltip, Typography, alpha, useTheme } from '@mui/material';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';

import {
  formatFindingForCopy,
  securitySeverityColor,
  type ParsedSecurityFinding,
} from '../../utils/securityFindings';
import { copyTextToClipboard } from '../../utils/clipboard';

export interface SecurityFindingCardProps {
  finding: ParsedSecurityFinding;
  deviceName?: string;
  deviceId?: string;
  onViewDevice?: (deviceId: string) => void;
  onIgnore?: () => void;
  showIgnore?: boolean;
  onCopied?: (message: string) => void;
}

export function SecurityFindingCard({
  finding,
  deviceName,
  deviceId,
  onViewDevice,
  onIgnore,
  showIgnore = false,
  onCopied,
}: SecurityFindingCardProps) {
  const theme = useTheme();
  const severityColor = securitySeverityColor(finding.severity);
  const accent =
    severityColor === 'error'
      ? theme.palette.error.main
      : severityColor === 'warning'
        ? theme.palette.warning.main
        : severityColor === 'info'
          ? theme.palette.info.main
          : theme.palette.grey[500];

  const handleCopy = () => {
    const text = formatFindingForCopy(finding, deviceName);
    void copyTextToClipboard(text).then((ok) => {
      onCopied?.(ok ? 'Finding copied to clipboard' : 'Could not copy. Select text manually.');
    });
  };

  return (
    <Box
      sx={{
        p: 1.5,
        borderRadius: 2,
        bgcolor: alpha(accent, 0.04),
        border: '1px solid',
        borderColor: alpha(accent, 0.15),
        userSelect: 'text',
      }}
    >
      <Stack direction="row" alignItems="flex-start" justifyContent="space-between" spacing={1}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          {deviceName && (
            <Typography variant="caption" fontWeight={800} color="text.secondary" display="block" sx={{ mb: 0.5 }}>
              {deviceName}
            </Typography>
          )}
          <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" mb={0.75}>
            <Chip
              label={finding.severity}
              size="small"
              color={severityColor === 'default' ? 'default' : severityColor}
              sx={{ height: 20, fontSize: '0.65rem', fontWeight: 800 }}
            />
            {finding.id && (
              <Typography variant="caption" color="text.disabled" fontFamily="monospace">
                {finding.id}
              </Typography>
            )}
          </Stack>
          <Typography variant="body2" fontWeight={700} gutterBottom>
            {finding.title}
          </Typography>
          <Typography variant="caption" color="text.secondary" display="block" sx={{ mb: 1 }}>
            <strong>What we found:</strong> {finding.description}
          </Typography>
          <Typography variant="caption" color="text.primary" display="block">
            <strong>Recommended action:</strong> {finding.mitigation}
          </Typography>
        </Box>

        <Stack direction="row" spacing={0.25} sx={{ flexShrink: 0, userSelect: 'none' }}>
          <Tooltip title="Copy finding text" arrow>
            <IconButton size="small" onClick={handleCopy} aria-label="Copy finding">
              <ContentCopyIcon fontSize="small" />
            </IconButton>
          </Tooltip>
          {deviceId && onViewDevice && (
            <Tooltip title="Open device" arrow>
              <IconButton
                size="small"
                onClick={() => onViewDevice(deviceId)}
                aria-label="Open device"
              >
                <OpenInNewIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
          {showIgnore && onIgnore && (
            <Tooltip title="Accept risk and hide this finding" arrow>
              <IconButton size="small" onClick={onIgnore} aria-label="Ignore finding">
                <VisibilityOffIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
        </Stack>
      </Stack>
    </Box>
  );
}
