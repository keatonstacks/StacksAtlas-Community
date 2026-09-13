import {
  alpha,
  Box,
  Checkbox,
  Chip,
  IconButton,
  Paper,
  Stack,
  Tooltip,
  Typography,
  useTheme,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import ErrorIcon from '@mui/icons-material/Error';
import InfoIcon from '@mui/icons-material/Info';
import SuccessIcon from '@mui/icons-material/MarkEmailRead';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { isAuditOnlyAlert, normalizeIpAddress } from '../alertsUtils';
import type { AlertEvent } from '../types';

interface AlertsHistoryCardListProps {
  items: AlertEvent[];
  auditMode?: boolean;
  selectedIds: string[];
  onSelectedChange: (ids: string[]) => void;
  onCopy: (item: AlertEvent) => void;
  onDelete: (ids: string[]) => void;
}

export function AlertsHistoryCardList({
  items,
  auditMode = false,
  selectedIds,
  onSelectedChange,
  onCopy,
  onDelete,
}: AlertsHistoryCardListProps) {
  const theme = useTheme();
  const navigate = useNavigate();

  if (items.length === 0) {
    return (
      <Typography sx={{ py: 6, textAlign: 'center', color: 'text.secondary', fontWeight: 700 }}>
        {auditMode ? 'NO AUDIT / REPLAY RECORDS MATCH YOUR SEARCH' : 'NO NOTIFICATION LOGS MATCH YOUR SEARCH'}
      </Typography>
    );
  }

  return (
    <Stack spacing={1.25}>
      {items.map((item) => {
        const isSelected = selectedIds.includes(item.id);
        const isFleetEvent = item.alertType === 'NodeDecoupled';
        const isAuditOnly = isAuditOnlyAlert(item);
        const displayIp = normalizeIpAddress(item.deviceIp);
        const targetCount = item.sentToEmails.length + (item.sentToWebhooks?.length || 0);

        return (
          <Paper
            key={item.id}
            variant="outlined"
            sx={{
              p: 1.5,
              borderRadius: 2,
              bgcolor: isSelected ? alpha(theme.palette.primary.main, 0.05) : 'background.paper',
              borderColor: isSelected ? 'primary.main' : 'divider',
            }}
          >
            <Stack direction="row" spacing={1} alignItems="flex-start">
              <Checkbox
                size="small"
                checked={isSelected}
                onChange={() => {
                  onSelectedChange(
                    isSelected ? selectedIds.filter((id) => id !== item.id) : [...selectedIds, item.id],
                  );
                }}
                sx={{ p: 0.5, mt: -0.25 }}
              />
              <Box sx={{ flex: 1, minWidth: 0 }}>
                <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1} flexWrap="wrap">
                  <Box sx={{ minWidth: 0 }}>
                    <Typography variant="caption" color="text.secondary" fontWeight={700} display="block">
                      {new Date(item.triggeredAt).toLocaleString()}
                    </Typography>
                    <Typography variant="body2" fontWeight={800} sx={{ mt: 0.25 }} noWrap>
                      {item.deviceName}
                    </Typography>
                    {displayIp && (
                      <Typography variant="caption" color="text.secondary" display="block" noWrap>
                        {isFleetEvent ? `Node ${displayIp}` : displayIp}
                      </Typography>
                    )}
                  </Box>
                  <Chip
                    label={(item.alertType || '').toString().replace(/([A-Z])/g, ' $1').trim().toUpperCase()}
                    size="small"
                    variant="outlined"
                    color={
                      item.alertType === 'DeviceDown'
                        ? 'error'
                        : item.alertType === 'DeviceUp'
                          ? 'success'
                          : 'info'
                    }
                    sx={{ fontWeight: 900, fontSize: '0.6rem', maxWidth: '100%' }}
                  />
                </Stack>

                {item.nodeName && !isFleetEvent && (
                  <Chip
                    label={`${item.nodeName.toUpperCase()}${item.room ? ` · ${item.room.toUpperCase()}` : ''}`}
                    size="small"
                    variant="outlined"
                    sx={{
                      mt: 1,
                      height: 18,
                      fontSize: '0.55rem',
                      fontWeight: 900,
                      maxWidth: '100%',
                    }}
                  />
                )}

                <Typography
                  variant="body2"
                  color="text.secondary"
                  sx={{ mt: 1, fontWeight: 500, lineHeight: 1.45, wordBreak: 'break-word' }}
                >
                  {isAuditOnly
                    ? item.errorMessage || 'AUDIT ONLY  -  no dispatch'
                    : auditMode
                      ? item.errorMessage || 'Audit record'
                      : `${targetCount} target(s) · ${item.sentToEmails.length ? 'email' : ''}${item.sentToEmails.length && item.sentToWebhooks?.length ? ' + ' : ''}${item.sentToWebhooks?.length ? 'webhook' : ''}`}
                </Typography>

                <Stack direction="row" alignItems="center" justifyContent="space-between" sx={{ mt: 1.25 }}>
                  {isAuditOnly ? (
                    <InfoIcon color="info" fontSize="small" />
                  ) : item.success ? (
                    <SuccessIcon color="success" fontSize="small" />
                  ) : (
                    <Tooltip title={item.errorMessage || 'Failed to send'}>
                      <ErrorIcon color="error" fontSize="small" />
                    </Tooltip>
                  )}
                  <Stack direction="row" spacing={0.5}>
                    <IconButton size="small" onClick={() => onCopy(item)} aria-label="Copy report">
                      <ContentCopyIcon fontSize="small" />
                    </IconButton>
                    {item.deviceId && !isFleetEvent && (
                      <IconButton size="small" onClick={() => navigate(`/devices?id=${item.deviceId}`)} aria-label="View device">
                        <VisibilityIcon fontSize="small" />
                      </IconButton>
                    )}
                    <IconButton size="small" color="error" onClick={() => onDelete([item.id])} aria-label="Delete log">
                      <DeleteIcon fontSize="small" />
                    </IconButton>
                  </Stack>
                </Stack>
              </Box>
            </Stack>
          </Paper>
        );
      })}
    </Stack>
  );
}
