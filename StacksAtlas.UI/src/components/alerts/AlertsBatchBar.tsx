import { Divider, Fade, IconButton, Paper, Button, Typography } from '@mui/material';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import CloseIcon from '@mui/icons-material/Close';
import ContentCopyIcon from '@mui/icons-material/ContentCopy';
import DeleteIcon from '@mui/icons-material/Delete';
import type { AlertsTabId, GroupedEvent } from './types';

interface AlertsBatchBarProps {
  activeTab: AlertsTabId;
  selectedGroupKeys: string[];
  selectedHistoryIds: string[];
  selectedAuditIds: string[];
  filteredGroups: GroupedEvent[];
  onClearLiveSelection: () => void;
  onClearHistorySelection: () => void;
  onClearAuditSelection: () => void;
  onCopyLive: (groups: GroupedEvent[]) => void;
  onDeleteLive: (keys: string[]) => void;
  onDeleteHistory: (ids: string[]) => void;
}

export function AlertsBatchBar({
  activeTab,
  selectedGroupKeys,
  selectedHistoryIds,
  selectedAuditIds,
  filteredGroups,
  onClearLiveSelection,
  onClearHistorySelection,
  onClearAuditSelection,
  onCopyLive,
  onDeleteLive,
  onDeleteHistory,
}: AlertsBatchBarProps) {
  const isMobile = useIsMobileLayout();
  const liveCount = selectedGroupKeys.length;
  const historyCount = selectedHistoryIds.length;
  const auditCount = selectedAuditIds.length;
  const visible =
    (activeTab === 'live' && liveCount > 0) ||
    (activeTab === 'history' && historyCount > 0) ||
    (activeTab === 'audit' && auditCount > 0);

  const selectedCount =
    activeTab === 'live' ? liveCount : activeTab === 'history' ? historyCount : auditCount;

  return (
    <Fade in={visible}>
      <Paper
        elevation={10}
        sx={{
          position: 'fixed',
          bottom: isMobile ? 16 : 40,
          left: '50%',
          transform: 'translateX(-50%)',
          bgcolor: 'grey.900',
          color: 'white',
          px: isMobile ? 2 : 3,
          py: 1.5,
          borderRadius: isMobile ? 3 : 10,
          display: visible ? 'flex' : 'none',
          alignItems: 'center',
          flexWrap: isMobile ? 'wrap' : 'nowrap',
          justifyContent: 'center',
          gap: isMobile ? 1.5 : 3,
          zIndex: 2000,
          maxWidth: isMobile ? 'calc(100% - 24px)' : 'none',
        }}
      >
        <Typography variant="caption" fontWeight={900}>
          {selectedCount} SELECTED
        </Typography>
        <Divider orientation="vertical" flexItem sx={{ bgcolor: 'grey.800' }} />

        {activeTab === 'live' ? (
          <>
            <Button
              size="small"
              startIcon={<ContentCopyIcon />}
              onClick={() =>
                onCopyLive(filteredGroups.filter((g) => selectedGroupKeys.includes(g.groupKey)))
              }
              sx={{ color: 'white', fontWeight: 900 }}
            >
              Copy
            </Button>
            <Button
              size="small"
              color="error"
              startIcon={<DeleteIcon />}
              onClick={() => onDeleteLive(selectedGroupKeys)}
              sx={{ fontWeight: 900 }}
            >
              Dismiss
            </Button>
            <IconButton size="small" onClick={onClearLiveSelection} sx={{ color: 'white', opacity: 0.5 }}>
              <CloseIcon fontSize="small" />
            </IconButton>
          </>
        ) : (
          <>
            <Button
              size="small"
              color="error"
              startIcon={<DeleteIcon />}
              onClick={() =>
                onDeleteHistory(activeTab === 'history' ? selectedHistoryIds : selectedAuditIds)
              }
              sx={{ fontWeight: 900 }}
            >
              Delete
            </Button>
            <IconButton
              size="small"
              onClick={activeTab === 'history' ? onClearHistorySelection : onClearAuditSelection}
              sx={{ color: 'white', opacity: 0.5 }}
            >
              <CloseIcon fontSize="small" />
            </IconButton>
          </>
        )}
      </Paper>
    </Fade>
  );
}
