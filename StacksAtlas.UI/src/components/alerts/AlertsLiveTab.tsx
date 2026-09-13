import {
  alpha,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  IconButton,
  keyframes,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Paper,
  Stack,
  TextField,
  Typography,
  useTheme,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import ErrorIcon from '@mui/icons-material/Error';
import InfoIcon from '@mui/icons-material/Info';
import SearchIcon from '@mui/icons-material/Search';
import VisibilityIcon from '@mui/icons-material/Visibility';
import WarningIcon from '@mui/icons-material/Warning';
import { getRelativeTime } from './alertsUtils';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import type { AlertsDevice, GroupedEvent } from './types';

const pulseCircle = keyframes`
  0% {
    transform: scale(0.95);
    box-shadow: 0 0 0 0 rgba(211, 47, 47, 0.7);
  }
  70% {
    transform: scale(1);
    box-shadow: 0 0 0 10px rgba(211, 47, 47, 0);
  }
  100% {
    transform: scale(0.95);
    box-shadow: 0 0 0 0 rgba(211, 47, 47, 0);
  }
`;

interface AlertsLiveTabProps {
  loading: boolean;
  groups: GroupedEvent[];
  devices: AlertsDevice[];
  severityFilter: string;
  search: string;
  selectedGroupKeys: string[];
  eventsCount: number;
  onSeverityChange: (value: string) => void;
  onSearchChange: (value: string) => void;
  onSelectedChange: (keys: string[]) => void;
  onClearAll: () => void;
  onCopy: (groups: GroupedEvent[]) => void;
  onDelete: (keys: string[]) => void;
}

export function AlertsLiveTab({
  loading,
  groups,
  devices,
  severityFilter,
  search,
  selectedGroupKeys,
  eventsCount,
  onSeverityChange,
  onSearchChange,
  onSelectedChange,
  onClearAll,
  onCopy,
  onDelete,
}: AlertsLiveTabProps) {
  const theme = useTheme();
  const navigate = useNavigate();
  const isMobile = useIsMobileLayout();

  const renderActions = (group: GroupedEvent, connectedDevice?: AlertsDevice) => (
    <Stack direction="row" spacing={0.5} justifyContent="flex-end">
      <IconButton size="small" onClick={() => onCopy([group])} aria-label="Copy">
        <ContentCopyIcon fontSize="small" />
      </IconButton>
      {connectedDevice && (
        <IconButton size="small" onClick={() => navigate(`/devices?id=${connectedDevice.id}`)} aria-label="View device">
          <VisibilityIcon fontSize="small" />
        </IconButton>
      )}
      <IconButton onClick={() => onDelete([group.groupKey])} size="small" color="error" aria-label="Dismiss">
        <DeleteIcon fontSize="small" />
      </IconButton>
    </Stack>
  );

  return (
    <>
      <Paper variant="outlined" sx={{ p: 2, mb: 4, borderRadius: 3, bgcolor: 'background.paper' }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems="center">
          <Checkbox
            size="small"
            checked={selectedGroupKeys.length === groups.length && groups.length > 0}
            indeterminate={selectedGroupKeys.length > 0 && selectedGroupKeys.length < groups.length}
            onChange={() => {
              if (selectedGroupKeys.length === groups.length) onSelectedChange([]);
              else onSelectedChange(groups.map((g) => g.groupKey));
            }}
          />

          <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
            {['All', 'Info', 'Warning', 'Critical'].map((label) => (
              <Chip
                key={label}
                label={label.toUpperCase()}
                size="small"
                variant={severityFilter === label ? 'filled' : 'outlined'}
                color={
                  severityFilter === label
                    ? label === 'Critical'
                      ? 'error'
                      : label === 'Warning'
                        ? 'warning'
                        : 'primary'
                    : 'default'
                }
                onClick={() => onSeverityChange(label)}
                sx={{ fontWeight: 800, fontSize: '0.65rem' }}
              />
            ))}
          </Stack>

          <TextField
            size="small"
            placeholder="Search live events..."
            value={search}
            onChange={(e) => onSearchChange(e.target.value)}
            InputProps={{
              startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: 'text.secondary' }} />,
              sx: { borderRadius: 2, fontSize: '0.85rem', bgcolor: alpha(theme.palette.background.default, 0.4) },
            }}
            sx={{ flexGrow: 1 }}
          />

          <Button
            variant="text"
            size="small"
            color="error"
            onClick={onClearAll}
            disabled={eventsCount === 0}
            sx={{ fontWeight: 900, letterSpacing: 1 }}
          >
            FLUSH DB
          </Button>
        </Stack>
      </Paper>

      {loading ? (
        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', py: 10, gap: 2 }}>
          <CircularProgress size={30} />
          <Typography variant="caption" sx={{ fontWeight: 900, letterSpacing: 2, color: 'text.secondary' }}>
            SYNCING STREAMS
          </Typography>
        </Box>
      ) : (
        <List disablePadding>
          {groups.map((group) => {
            const connectedDevice = devices.find((d) => d.ipAddress === group.deviceIp);
            const isSelected = selectedGroupKeys.includes(group.groupKey);
            const isCritical = group.severity === 'Critical';
            const isWarning = group.severity === 'Warning';

            return (
              <ListItem
                key={group.groupKey}
                alignItems="flex-start"
                sx={{
                  px: 2,
                  py: 1.5,
                  mb: 1,
                  borderRadius: 2,
                  display: 'block',
                  bgcolor: isSelected
                    ? alpha(theme.palette.primary.main, 0.05)
                    : isCritical
                      ? alpha(theme.palette.error.main, 0.02)
                      : 'background.paper',
                  border: '1px solid',
                  borderColor: isCritical ? 'error.main' : isSelected ? 'primary.main' : 'divider',
                  borderLeft: '4px solid',
                  borderLeftColor: isCritical ? 'error.main' : isWarning ? 'warning.main' : 'info.main',
                  transition: 'all 0.2s ease-in-out',
                  ...(isMobile ? {} : { pr: 12 }),
                }}
                secondaryAction={isMobile ? undefined : renderActions(group, connectedDevice)}
              >
                <Stack direction="row" alignItems="flex-start" spacing={1}>
                <Checkbox
                  size="small"
                  checked={isSelected}
                  onChange={() => {
                    onSelectedChange(
                      isSelected
                        ? selectedGroupKeys.filter((k) => k !== group.groupKey)
                        : [...selectedGroupKeys, group.groupKey],
                    );
                  }}
                  sx={{ mt: 0.25, p: 0.5 }}
                />

                <ListItemIcon sx={{ minWidth: 40, mt: 0.25, display: 'flex', justifyContent: 'center' }}>
                  {isCritical ? (
                    <Box
                      sx={{
                        width: 28,
                        height: 28,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        borderRadius: '50%',
                        bgcolor: alpha(theme.palette.error.main, 0.15),
                        animation: `${pulseCircle} 2s infinite ease-in-out`,
                      }}
                    >
                      <ErrorIcon color="error" sx={{ fontSize: 18 }} />
                    </Box>
                  ) : (
                    <Box
                      sx={{
                        width: 28,
                        height: 28,
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        borderRadius: '50%',
                        bgcolor: isWarning
                          ? alpha(theme.palette.warning.main, 0.1)
                          : alpha(theme.palette.info.main, 0.1),
                      }}
                    >
                      {isWarning ? (
                        <WarningIcon color="warning" sx={{ fontSize: 18 }} />
                      ) : (
                        <InfoIcon color="info" sx={{ fontSize: 18 }} />
                      )}
                    </Box>
                  )}
                </ListItemIcon>

                <ListItemText
                  sx={{ flex: 1, minWidth: 0, m: 0 }}
                  primaryTypographyProps={{ component: 'div' }}
                  secondaryTypographyProps={{ component: 'div' }}
                  primary={
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                      <Typography
                        variant="subtitle2"
                        fontWeight={850}
                        color={isCritical ? 'error.main' : 'text.primary'}
                        sx={{ wordBreak: 'break-word' }}
                      >
                        {connectedDevice?.name || group.deviceIp || 'System'}
                      </Typography>
                      {group.count > 1 && (
                        <Chip
                          label={`${group.count} events`}
                          size="small"
                          variant="outlined"
                          sx={{
                            height: 18,
                            fontSize: '0.6rem',
                            fontWeight: 900,
                            borderColor: isCritical ? 'error.main' : 'divider',
                            bgcolor: alpha(theme.palette.background.default, 0.4),
                          }}
                        />
                      )}
                    </Box>
                  }
                  secondary={
                    <Box sx={{ mt: 0.5 }}>
                      <Typography
                        variant="body2"
                        color="text.secondary"
                        sx={{ mb: 1, fontWeight: 500, lineHeight: 1.45, wordBreak: 'break-word' }}
                      >
                        {group.message}
                      </Typography>
                      <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
                        <Typography variant="caption" sx={{ color: 'primary.main', fontWeight: 900 }}>
                          {getRelativeTime(group.lastSeen)}
                        </Typography>
                        <Typography variant="caption" sx={{ textTransform: 'uppercase', fontWeight: 800, opacity: 0.6 }}>
                          {group.type}
                        </Typography>
                        {group.nodeName && (
                          <Chip
                            label={`${group.nodeName.toUpperCase()}${group.room ? ` · ${group.room.toUpperCase()}` : ''}`}
                            size="small"
                            variant="outlined"
                            sx={{
                              height: 16,
                              fontSize: '0.55rem',
                              fontWeight: 900,
                              borderColor: alpha(theme.palette.secondary.main, 0.4),
                              color: 'secondary.main',
                            }}
                          />
                        )}
                      </Stack>
                    </Box>
                  }
                />
                </Stack>
                {isMobile && (
                  <Box sx={{ mt: 1, pl: 5 }}>
                    {renderActions(group, connectedDevice)}
                  </Box>
                )}
              </ListItem>
            );
          })}
          {groups.length === 0 && !loading && (
            <Box sx={{ py: 8, textAlign: 'center', opacity: 0.5 }}>
              <Typography fontWeight={700}>No live events match your filters</Typography>
            </Box>
          )}
        </List>
      )}
    </>
  );
}
