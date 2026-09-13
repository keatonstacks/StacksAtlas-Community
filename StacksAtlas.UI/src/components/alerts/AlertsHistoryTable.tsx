import {
  alpha,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  IconButton,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  TextField,
  Tooltip,
  Typography,
  useTheme,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import ErrorIcon from '@mui/icons-material/Error';
import InfoIcon from '@mui/icons-material/Info';
import SearchIcon from '@mui/icons-material/Search';
import SuccessIcon from '@mui/icons-material/MarkEmailRead';
import VisibilityIcon from '@mui/icons-material/Visibility';
import { isAuditOnlyAlert, normalizeIpAddress } from './alertsUtils';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import { AlertsHistoryCardList } from './mobile/AlertsHistoryCardList';
import type { AlertEvent, HistorySortKey } from './types';

interface AlertsHistoryTableProps {
  items: AlertEvent[];
  loading: boolean;
  auditMode?: boolean;
  orderBy: HistorySortKey;
  order: 'asc' | 'desc';
  selectedIds: string[];
  onSelectedChange: (ids: string[]) => void;
  onRequestSort: (property: HistorySortKey) => void;
  onCopy: (item: AlertEvent) => void;
  onDelete: (ids: string[]) => void;
}

export function AlertsHistoryTable({
  items,
  loading,
  auditMode = false,
  orderBy,
  order,
  selectedIds,
  onSelectedChange,
  onRequestSort,
  onCopy,
  onDelete,
}: AlertsHistoryTableProps) {
  const theme = useTheme();
  const navigate = useNavigate();
  const isMobile = useIsMobileLayout();

  if (isMobile) {
    if (loading) {
      return (
        <Box sx={{ py: 8, textAlign: 'center' }}>
          <CircularProgress size={32} />
          <Typography sx={{ mt: 2, color: 'text.secondary', fontWeight: 700 }}>
            RETRIEVING LOGS
          </Typography>
        </Box>
      );
    }

    return (
      <AlertsHistoryCardList
        items={items}
        auditMode={auditMode}
        selectedIds={selectedIds}
        onSelectedChange={onSelectedChange}
        onCopy={onCopy}
        onDelete={onDelete}
      />
    );
  }

  return (
    <TableContainer component={Paper} variant="outlined" sx={{ borderRadius: 3, overflow: 'hidden' }}>
      <Table>
        <TableHead sx={{ backgroundColor: alpha(theme.palette.primary.main, 0.05) }}>
          <TableRow>
            <TableCell padding="checkbox" />
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }}>
              <TableSortLabel
                active={orderBy === 'triggeredAt'}
                direction={orderBy === 'triggeredAt' ? order : 'desc'}
                onClick={() => onRequestSort('triggeredAt')}
              >
                TIMESTAMP
              </TableSortLabel>
            </TableCell>
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }}>
              <TableSortLabel
                active={orderBy === 'alertType'}
                direction={orderBy === 'alertType' ? order : 'asc'}
                onClick={() => onRequestSort('alertType')}
              >
                EVENT TYPE
              </TableSortLabel>
            </TableCell>
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }}>
              <TableSortLabel
                active={orderBy === 'deviceName'}
                direction={orderBy === 'deviceName' ? order : 'asc'}
                onClick={() => onRequestSort('deviceName')}
              >
                {auditMode ? 'SUBJECT' : 'DEVICE'}
              </TableSortLabel>
            </TableCell>
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }}>
              {auditMode ? 'DETAILS' : 'RECIPIENTS'}
            </TableCell>
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }} align="center">
              <TableSortLabel
                active={orderBy === 'success'}
                direction={orderBy === 'success' ? order : 'asc'}
                onClick={() => onRequestSort('success')}
              >
                STATUS
              </TableSortLabel>
            </TableCell>
            <TableCell sx={{ fontWeight: 900, letterSpacing: 1, fontSize: '0.7rem' }} align="right">
              ACTIONS
            </TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {loading ? (
            <TableRow>
              <TableCell colSpan={7} align="center" sx={{ py: 8 }}>
                <CircularProgress size={32} />
                <Typography sx={{ mt: 2, color: 'text.secondary', fontWeight: 700 }}>
                  RETRIEVING LOGS
                </Typography>
              </TableCell>
            </TableRow>
          ) : items.length === 0 ? (
            <TableRow>
              <TableCell colSpan={7} align="center" sx={{ py: 8 }}>
                <Typography sx={{ color: 'text.secondary', fontWeight: 700 }}>
                  {auditMode ? 'NO AUDIT / REPLAY RECORDS MATCH YOUR SEARCH' : 'NO NOTIFICATION LOGS MATCH YOUR SEARCH'}
                </Typography>
              </TableCell>
            </TableRow>
          ) : (
            items.map((item) => {
              const isSelected = selectedIds.includes(item.id);
              const isFleetEvent = item.alertType === 'NodeDecoupled';
              const isAuditOnly = isAuditOnlyAlert(item);
              const targetCount = item.sentToEmails.length + (item.sentToWebhooks?.length || 0);
              const displayIp = normalizeIpAddress(item.deviceIp);

              return (
                <TableRow
                  key={item.id}
                  hover
                  selected={isSelected}
                  sx={{ '&.Mui-selected': { bgcolor: alpha(theme.palette.primary.main, 0.05) } }}
                >
                  <TableCell padding="checkbox">
                    <Checkbox
                      size="small"
                      checked={isSelected}
                      onChange={() => {
                        onSelectedChange(
                          isSelected ? selectedIds.filter((id) => id !== item.id) : [...selectedIds, item.id],
                        );
                      }}
                    />
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2" fontWeight={700}>
                      {new Date(item.triggeredAt).toLocaleDateString()}
                    </Typography>
                    <Typography variant="caption" color="text.secondary">
                      {new Date(item.triggeredAt).toLocaleTimeString()}
                    </Typography>
                  </TableCell>
                  <TableCell>
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
                      sx={{ fontWeight: 900, fontSize: '0.6rem' }}
                    />
                  </TableCell>
                  <TableCell>
                    <Typography variant="body2" sx={{ fontWeight: 700 }}>
                      {item.deviceName}
                    </Typography>
                    {displayIp && (
                      <Typography variant="caption" color="text.secondary" display="block">
                        {isFleetEvent ? `Node ${displayIp}` : displayIp}
                      </Typography>
                    )}
                    {item.nodeName && !isFleetEvent && (
                      <Chip
                        label={`${item.nodeName.toUpperCase()}${item.room ? ` · ${item.room.toUpperCase()}` : ''}`}
                        size="small"
                        variant="outlined"
                        sx={{
                          mt: 0.5,
                          height: 16,
                          fontSize: '0.55rem',
                          fontWeight: 900,
                          borderColor: alpha(theme.palette.secondary.main, 0.4),
                          color: 'secondary.main',
                        }}
                      />
                    )}
                  </TableCell>
                  <TableCell>
                    {isAuditOnly ? (
                      <Tooltip title={item.errorMessage || 'Recorded for audit  -  no notification dispatch'}>
                        <Typography variant="body2" fontWeight={700} color="text.secondary" sx={{ maxWidth: 280 }}>
                          {item.errorMessage || 'AUDIT ONLY  -  no dispatch'}
                        </Typography>
                      </Tooltip>
                    ) : (
                      <Tooltip
                        title={
                          <Box sx={{ p: 0.5 }}>
                            {item.sentToEmails.length > 0 && (
                              <Box sx={{ mb: 1 }}>
                                <Typography variant="caption" fontWeight={900} display="block">
                                  EMAILS:
                                </Typography>
                                {item.sentToEmails.map((e) => (
                                  <Typography key={e} variant="caption" display="block">
                                    • {e}
                                  </Typography>
                                ))}
                              </Box>
                            )}
                            {item.sentToWebhooks?.length > 0 && (
                              <Box>
                                <Typography variant="caption" fontWeight={900} display="block">
                                  WEBHOOKS:
                                </Typography>
                                {item.sentToWebhooks.map((w) => (
                                  <Typography key={w} variant="caption" display="block">
                                    • {w}
                                  </Typography>
                                ))}
                              </Box>
                            )}
                          </Box>
                        }
                      >
                        <Box>
                          <Typography variant="body2" fontWeight={700}>
                            {targetCount} TARGETS
                          </Typography>
                          <Stack direction="row" spacing={0.5}>
                            {item.sentToEmails.length > 0 && (
                              <Chip label="EMAIL" size="small" sx={{ height: 16, fontSize: '0.55rem', fontWeight: 900 }} />
                            )}
                            {item.sentToWebhooks?.length > 0 && (
                              <Chip
                                label="WEBHOOK"
                                size="small"
                                color="secondary"
                                sx={{ height: 16, fontSize: '0.55rem', fontWeight: 900 }}
                              />
                            )}
                          </Stack>
                        </Box>
                      </Tooltip>
                    )}
                  </TableCell>
                  <TableCell align="center">
                    {isAuditOnly ? (
                      <Tooltip title={item.errorMessage || 'Recorded for audit  -  no notification dispatch'}>
                        <InfoIcon color="info" fontSize="small" />
                      </Tooltip>
                    ) : item.success ? (
                      <Tooltip title="Sent successfully">
                        <SuccessIcon color="success" fontSize="small" />
                      </Tooltip>
                    ) : (
                      <Tooltip title={item.errorMessage || 'Failed to send'}>
                        <ErrorIcon color="error" fontSize="small" />
                      </Tooltip>
                    )}
                  </TableCell>
                  <TableCell align="right">
                    <Stack direction="row" spacing={0.5} justifyContent="flex-end">
                      <Tooltip title="Copy Report">
                        <IconButton size="small" onClick={() => onCopy(item)}>
                          <ContentCopyIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                      {item.deviceId && !isFleetEvent && (
                        <Tooltip title="View Device">
                          <IconButton size="small" onClick={() => navigate(`/devices?id=${item.deviceId}`)}>
                            <VisibilityIcon fontSize="small" />
                          </IconButton>
                        </Tooltip>
                      )}
                      <Tooltip title="Delete Log">
                        <IconButton size="small" color="error" onClick={() => onDelete([item.id])}>
                          <DeleteIcon fontSize="small" />
                        </IconButton>
                      </Tooltip>
                    </Stack>
                  </TableCell>
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
    </TableContainer>
  );
}

interface AlertsHistoryToolbarProps {
  items: AlertEvent[];
  selectedIds: string[];
  search: string;
  typeFilter: string;
  typeOptions: string[];
  totalCount: number;
  auditMode?: boolean;
  onSearchChange: (value: string) => void;
  onTypeFilterChange: (value: string) => void;
  onSelectedChange: (ids: string[]) => void;
  onClearAll: () => void;
}

export function AlertsHistoryToolbar({
  items,
  selectedIds,
  search,
  typeFilter,
  typeOptions,
  totalCount,
  auditMode = false,
  onSearchChange,
  onTypeFilterChange,
  onSelectedChange,
  onClearAll,
}: AlertsHistoryToolbarProps) {
  const theme = useTheme();

  return (
    <Paper variant="outlined" sx={{ p: 2, mb: 4, borderRadius: 3, bgcolor: 'background.paper' }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} alignItems="center">
        <Checkbox
          size="small"
          checked={selectedIds.length === items.length && items.length > 0}
          indeterminate={selectedIds.length > 0 && selectedIds.length < items.length}
          onChange={() => {
            if (selectedIds.length === items.length) onSelectedChange([]);
            else onSelectedChange(items.map((a) => a.id));
          }}
        />

        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
          {typeOptions.map((label) => (
            <Chip
              key={label}
              label={
                label === 'All'
                  ? 'ALL'
                  : (label || '').toString().replace(/([A-Z])/g, ' $1').trim().toUpperCase()
              }
              size="small"
              variant={typeFilter === label ? 'filled' : 'outlined'}
              color={
                typeFilter === label
                  ? label === 'DeviceDown' || label === 'NodeDecoupled'
                    ? 'error'
                    : label === 'DeviceUp'
                      ? 'success'
                      : 'primary'
                  : 'default'
              }
              onClick={() => onTypeFilterChange(label)}
              sx={{ fontWeight: 800, fontSize: '0.65rem' }}
            />
          ))}
        </Stack>

        <TextField
          size="small"
          placeholder={auditMode ? 'Search audit records...' : 'Search history...'}
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          InputProps={{
            startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: 'text.secondary' }} />,
            sx: { borderRadius: 2, fontSize: '0.85rem', bgcolor: alpha(theme.palette.background.default, 0.4) },
          }}
          sx={{ flexGrow: 1 }}
        />

        {!auditMode && (
          <Button
            variant="text"
            size="small"
            color="error"
            onClick={onClearAll}
            disabled={totalCount === 0}
            sx={{ fontWeight: 900, letterSpacing: 1 }}
          >
            FLUSH HISTORY
          </Button>
        )}
      </Stack>
    </Paper>
  );
}
