import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Box, Chip, Collapse, IconButton, Stack, Typography, alpha, useTheme,
} from '@mui/material';
import ChevronRightIcon from '@mui/icons-material/ChevronRight';
import type { Device } from '../../../models/Device';
import { buildDeviceGroups, type DeviceGroupBy, type DeviceGroupExpansion } from '../deviceGrouping';
import { MobileDeviceCard } from './MobileDeviceCard';

interface MobileDeviceListProps {
  devices: Device[];
  groupBy?: DeviceGroupBy;
  ptpStatus?: { sourceIp: string } | null;
  nodesMap?: Record<string, string>;
  parentNameById?: Record<string, string>;
  openAvcLinksMap?: Record<string, import('../../../services/apiService').OpenAvcLinkSummary>;
  openAvcIntegrationStatus?: string;
  isHub?: boolean;
  selectionEnabled?: boolean;
  selectedIds: Set<string>;
  onToggleSelect: (id: string) => void;
  onDeviceClick: (device: Device) => void;
  groupExpansion?: DeviceGroupExpansion | null;
}

function DeviceCards({
  devices,
  ptpStatus,
  nodesMap,
  openAvcLinksMap,
  openAvcIntegrationStatus,
  isHub,
  selectionEnabled,
  selectedIds,
  onToggleSelect,
  onDeviceClick,
}: Omit<MobileDeviceListProps, 'groupBy'>) {
  return (
    <>
      {devices.map((device) => (
        <MobileDeviceCard
          key={device.id}
          device={device}
          isPtpMaster={!!ptpStatus && device.ipAddress === ptpStatus.sourceIp}
          nodeLabel={isHub && device.nodeId && nodesMap?.[device.nodeId] ? nodesMap[device.nodeId] : undefined}
          openAvcDriver={openAvcLinksMap?.[device.id.toLowerCase()]?.driverName}
          openAvcIntegrationStatus={openAvcIntegrationStatus}
          selectionEnabled={selectionEnabled}
          selected={selectedIds.has(device.id)}
          onToggleSelect={onToggleSelect}
          onOpen={() => onDeviceClick(device)}
        />
      ))}
    </>
  );
}

export function MobileDeviceList({
  devices,
  groupBy = 'none',
  ptpStatus,
  nodesMap,
  parentNameById,
  openAvcLinksMap,
  openAvcIntegrationStatus,
  isHub = false,
  selectionEnabled = false,
  selectedIds,
  onToggleSelect,
  onDeviceClick,
  groupExpansion,
}: MobileDeviceListProps) {
  const theme = useTheme();
  const grouped = useMemo(
    () => buildDeviceGroups(devices, groupBy, nodesMap, parentNameById),
    [devices, groupBy, nodesMap, parentNameById],
  );
  const [expandedGroups, setExpandedGroups] = useState<Record<string, boolean>>({});

  useEffect(() => {
    setExpandedGroups({});
  }, [groupBy]);

  useEffect(() => {
    if (!groupExpansion || !grouped) return;
    const expanded = groupExpansion.action === 'expand';
    const next: Record<string, boolean> = {};
    Object.keys(grouped).forEach((groupName) => {
      next[groupName] = expanded;
    });
    setExpandedGroups(next);
  }, [groupExpansion, grouped]);

  const toggleGroup = useCallback((groupName: string) => {
    setExpandedGroups((prev) => ({
      ...prev,
      [groupName]: prev[groupName] === false,
    }));
  }, []);

  if (devices.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ py: 4 }}>
        No devices match your filters.
      </Typography>
    );
  }

  if (!grouped) {
    return (
      <Stack spacing={1.25}>
        <DeviceCards
          devices={devices}
          ptpStatus={ptpStatus}
          nodesMap={nodesMap}
          openAvcLinksMap={openAvcLinksMap}
          openAvcIntegrationStatus={openAvcIntegrationStatus}
          isHub={isHub}
          selectionEnabled={selectionEnabled}
          selectedIds={selectedIds}
          onToggleSelect={onToggleSelect}
          onDeviceClick={onDeviceClick}
        />
      </Stack>
    );
  }

  return (
    <Stack spacing={1.5}>
      {Object.keys(grouped).sort().map((groupName) => {
        const devicesInGroup = grouped[groupName];
        const isExpanded = expandedGroups[groupName] !== false;
        const onlineCount = devicesInGroup.filter((d) => d.status === 'online').length;
        const offlineCount = devicesInGroup.length - onlineCount;

        return (
          <Box key={groupName}>
            <Box
              onClick={() => toggleGroup(groupName)}
              sx={{
                px: 1.5,
                py: 1.25,
                borderRadius: 2,
                bgcolor: alpha(theme.palette.action.hover, 0.04),
                border: '1px solid',
                borderColor: 'divider',
                cursor: 'pointer',
                userSelect: 'none',
              }}
            >
              <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1}>
                <Stack direction="row" alignItems="center" spacing={1} sx={{ minWidth: 0, flex: 1 }}>
                  <IconButton
                    size="small"
                    sx={{
                      p: 0,
                      transition: 'transform 0.2s',
                      transform: isExpanded ? 'rotate(90deg)' : 'rotate(0deg)',
                    }}
                  >
                    <ChevronRightIcon fontSize="small" />
                  </IconButton>
                  <Typography variant="subtitle2" fontWeight={800} noWrap sx={{ letterSpacing: 0.5 }}>
                    {groupName}
                  </Typography>
                  <Chip
                    label={devicesInGroup.length}
                    size="small"
                    sx={{ height: 20, fontSize: '0.65rem', fontWeight: 900 }}
                  />
                </Stack>
                <Typography variant="caption" color="text.secondary" fontWeight={700} noWrap>
                  {onlineCount} on · {offlineCount} off
                </Typography>
              </Stack>
            </Box>
            <Collapse in={isExpanded}>
              <Stack spacing={1.25} sx={{ mt: 1.25, pl: 0.5 }}>
                <DeviceCards
                  devices={devicesInGroup}
                  ptpStatus={ptpStatus}
                  nodesMap={nodesMap}
                  openAvcLinksMap={openAvcLinksMap}
                  openAvcIntegrationStatus={openAvcIntegrationStatus}
                  isHub={isHub}
                  selectionEnabled={selectionEnabled}
                  selectedIds={selectedIds}
                  onToggleSelect={onToggleSelect}
                  onDeviceClick={onDeviceClick}
                />
              </Stack>
            </Collapse>
          </Box>
        );
      })}
    </Stack>
  );
}

