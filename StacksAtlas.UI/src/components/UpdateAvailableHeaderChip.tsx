import {
  Box,
  Button,
  IconButton,
  Stack,
  Tooltip,
  Typography,
  alpha,
  useTheme,
} from '@mui/material';
import {
  Close as CloseIcon,
  Launch as LaunchIcon,
  SystemUpdateAlt as SystemUpdateIcon,
} from '@mui/icons-material';
import { useNavigate } from 'react-router-dom';
import type { UpdateCheckResult } from '../models/Updates';
import { isUpdateCriticalityStrong } from '../utils/updateCheckUi';
import { releaseNotesPreview } from '../utils/updatePreFlight';

interface UpdateAvailableHeaderChipProps {
  result: UpdateCheckResult;
  isPortable?: boolean;
  onDismiss: () => void;
}

export default function UpdateAvailableHeaderChip({
  result,
  isPortable = false,
  onDismiss,
}: UpdateAvailableHeaderChipProps) {
  const theme = useTheme();
  const navigate = useNavigate();
  const isCritical = isUpdateCriticalityStrong(result.criticality);
  const accent = isCritical ? theme.palette.warning.main : theme.palette.success.main;
  const versionLabel = result.availableVersion ? `v${result.availableVersion}` : 'update';
  const preview = releaseNotesPreview(result.message, result.availableVersion);

  const openUpdates = () => {
    navigate('/settings?tab=infrastructure');
  };

  return (
    <Stack
      direction="row"
      alignItems="center"
      spacing={0.5}
      sx={{
        px: 1.25,
        py: 0.35,
        borderRadius: 2,
        border: `1px solid ${alpha(accent, isCritical ? 0.45 : 0.35)}`,
        bgcolor: alpha(accent, isCritical ? 0.14 : 0.08),
        maxWidth: { xs: 260, md: 420 },
      }}
    >
      <SystemUpdateIcon sx={{ fontSize: '1rem', color: accent, flexShrink: 0 }} />
      <Tooltip title={preview}>
        <Button
          onClick={openUpdates}
          size="small"
          sx={{
            minWidth: 0,
            px: 0.75,
            py: 0.25,
            textTransform: 'none',
            color: 'text.primary',
            '&:hover': { bgcolor: alpha(accent, 0.12) },
          }}
        >
          <Box sx={{ textAlign: 'left' }}>
            <Typography
              variant="caption"
              sx={{ display: 'block', fontSize: '0.62rem', fontWeight: 900, color: accent, lineHeight: 1 }}
            >
              {isCritical ? 'CRITICAL UPDATE' : 'UPDATE AVAILABLE'}
            </Typography>
            <Typography
              variant="body2"
              sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.2, fontSize: '0.78rem' }}
            >
              {versionLabel}
            </Typography>
            <Typography
              variant="caption"
              color="text.secondary"
              sx={{
                display: { xs: 'none', lg: 'block' },
                fontSize: '0.62rem',
                lineHeight: 1.2,
                maxWidth: 220,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
              }}
            >
              {preview}
            </Typography>
            {isPortable && (
              <Typography variant="caption" sx={{ display: 'block', fontSize: '0.58rem', color: 'info.main', fontWeight: 700 }}>
                One-click apply in Settings · Business for always-on
              </Typography>
            )}
          </Box>
        </Button>
      </Tooltip>
      {result.releaseNotesUrl && (
        <Tooltip title="View release notes">
          <IconButton
            size="small"
            component="a"
            href={result.releaseNotesUrl}
            target="_blank"
            rel="noopener noreferrer"
            sx={{ color: accent, p: 0.35 }}
          >
            <LaunchIcon sx={{ fontSize: '0.9rem' }} />
          </IconButton>
        </Tooltip>
      )}
      <Tooltip title="Remind me in 7 days">
        <IconButton size="small" onClick={onDismiss} sx={{ color: 'text.secondary', p: 0.35 }}>
          <CloseIcon sx={{ fontSize: '0.95rem' }} />
        </IconButton>
      </Tooltip>
    </Stack>
  );
}
