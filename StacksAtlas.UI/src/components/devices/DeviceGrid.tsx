import React, { useState, useMemo, useCallback, useEffect } from "react";
import {
  Box,
  Typography,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  IconButton,
  Chip,
  Checkbox,
  Paper,
  alpha,
  useTheme
} from "@mui/material";
import ChevronRightIcon from "@mui/icons-material/ChevronRight";

import type { Device } from "../../models/Device";
import { type ColumnKey, columnLabels } from "./types";
import { DeviceRow } from "./DeviceRow";
import { type DeviceGroupBy, type DeviceGroupExpansion, resolveDeviceGroupKey } from "./deviceGrouping";

/** Minimum header widths so labels do not overlap adjacent columns. */
const COLUMN_MIN_WIDTHS: Partial<Record<ColumnKey, number>> = {
  status: 96,
  security: 76,
  name: 148,
  type: 100,
};

// -------------------------------------------------------------
//  Resizable Header Component
// -------------------------------------------------------------
const ResizableHeader = ({
  column,
  label,
  width,
  sortConfig,
  onSort,
  onResize
}: {
  column: ColumnKey;
  label: string;
  width: number;
  sortConfig: any;
  onSort: (column: ColumnKey) => void;
  onResize: (e: React.MouseEvent, column: ColumnKey) => void;
}) => {
  const effectiveWidth = Math.max(width, COLUMN_MIN_WIDTHS[column] ?? 64);
  return (
  <TableCell
    sx={{
      width: effectiveWidth,
      minWidth: effectiveWidth,
      maxWidth: effectiveWidth,
      position: "relative",
      whiteSpace: "nowrap",
      fontWeight: 600,
      fontSize: "0.8rem",
      p: 0,
      overflow: "hidden",
    }}
  >
    <TableSortLabel
      active={sortConfig?.key === column}
      direction={sortConfig?.direction}
      onClick={() => onSort(column)}
      sx={{
        px: 1.5,
        py: 1,
        width: '100%',
        display: 'flex',
        justifyContent: 'start',
        overflow: 'hidden',
        '& .MuiTableSortLabel-label': {
          overflow: 'hidden',
          textOverflow: 'ellipsis',
        },
      }}
    >
      {label}
    </TableSortLabel>

    {/* Resize handle */}
    <Box
      onMouseDown={(e) => onResize(e, column)}
      onClick={(e) => {
        e.preventDefault();
        e.stopPropagation();
      }}
      sx={{
        position: "absolute",
        right: -3,
        top: 0,
        width: 6,
        height: "100%",
        cursor: "col-resize",
        userSelect: "none",
        zIndex: 10,
        '&:hover': {
          bgcolor: 'primary.main',
          opacity: 0.5
        }
      }}
    />
  </TableCell>
  );
};

// -------------------------------------------------------------
//  DeviceGrid Component
// -------------------------------------------------------------
interface DeviceGridProps {
  filtered: Device[];
  groupBy: DeviceGroupBy;
  columnOrder: ColumnKey[];
  visibility: Record<ColumnKey, boolean>;
  columnWidths: Record<ColumnKey, number>;
  setColumnWidths: React.Dispatch<React.SetStateAction<Record<ColumnKey, number>>>;
  sortConfig: any;
  onSort: (key: any) => void;
  onDeviceClick: (device: Device) => void;
  ptpStatus: any;
  nodesMap?: Record<string, string>;
  openAvcLinksMap?: Record<string, import("../../services/apiService").OpenAvcLinkSummary>;
  openAvcIntegrationStatus?: string;
  selectionEnabled?: boolean;
  allPageSelected?: boolean;
  pageIndeterminate?: boolean;
  selectedIds?: Set<string>;
  onToggleSelect?: (id: string) => void;
  onToggleSelectPage?: () => void;
  parentNameById?: Record<string, string>;
  groupExpansion?: DeviceGroupExpansion | null;
}

export function DeviceGrid({
  filtered,
  groupBy,
  columnOrder,
  visibility,
  columnWidths,
  setColumnWidths,
  sortConfig,
  onSort,
  onDeviceClick,
  ptpStatus,
  nodesMap,
  openAvcLinksMap,
  openAvcIntegrationStatus,
  selectionEnabled = false,
  allPageSelected = false,
  pageIndeterminate = false,
  selectedIds,
  onToggleSelect,
  onToggleSelectPage,
  parentNameById,
  groupExpansion,
}: DeviceGridProps) {
  const theme = useTheme();
  
  // Track expanded/collapsed groups: key -> expanded boolean
  const [expandedGroups, setExpandedGroups] = useState<Record<string, boolean>>({});

  // 1. DYNAMIC COLUMN STRIPPING
  // Filter columns based on visibility AND active grouping field
  const visibleColumns = useMemo(() => {
    return columnOrder.filter((key) => {
      if (!visibility[key]) return false;
      // Strip redundant grouped fields
      if (groupBy === "node" && key === "nodeId") return false;
      if (groupBy === "client" && key === "client") return false;
      if (groupBy === "location" && (key === "building" || key === "room" || key === "location")) return false;
      if (groupBy === "uplink" && key === "attachmentParent") return false;
      if (groupBy === "status" && key === "status") return false;
      if (groupBy === "vendor" && key === "vendor") return false;
      return true;
    });
  }, [columnOrder, visibility, groupBy]);

  const colSpan = visibleColumns.length + (selectionEnabled ? 1 : 0);

  // 2. MEMOIZED GROUPING ALGORITHM
  const grouped = useMemo(() => {
    if (groupBy === "none") return null;

    const map: Record<string, Device[]> = {};

    filtered.forEach((device) => {
      const groupKey = resolveDeviceGroupKey(device, groupBy, nodesMap, parentNameById);

      if (!map[groupKey]) {
        map[groupKey] = [];
      }
      map[groupKey].push(device);
    });

    return map;
  }, [filtered, groupBy, nodesMap, parentNameById]);

  useEffect(() => {
    setExpandedGroups({});
  }, [groupBy]);

  useEffect(() => {
    if (!groupExpansion || !grouped) return;
    const expanded = groupExpansion.action === "expand";
    const next: Record<string, boolean> = {};
    Object.keys(grouped).forEach((groupName) => {
      next[groupName] = expanded;
    });
    setExpandedGroups(next);
  }, [groupExpansion, grouped]);

  // 3. COLLAPSE / EXPAND HANDLE
  const toggleGroup = useCallback((groupName: string) => {
    setExpandedGroups((prev) => ({
      ...prev,
      [groupName]: prev[groupName] === false ? true : false // default is expanded
    }));
  }, []);

  // 4. COLUMN RESIZING HANDLER
  const handleResize = useCallback((e: React.MouseEvent, column: ColumnKey) => {
    e.preventDefault();
    e.stopPropagation();

    const startX = e.clientX;
    const startWidth = columnWidths[column] || 100;

    const onMouseMove = (moveEvent: MouseEvent) => {
      const delta = moveEvent.clientX - startX;
      setColumnWidths((prev) => ({
        ...prev,
        [column]: Math.max(80, startWidth + delta)
      }));
    };

    const onMouseUp = () => {
      document.removeEventListener("mousemove", onMouseMove);
      document.removeEventListener("mouseup", onMouseUp);
    };

    document.addEventListener("mousemove", onMouseMove);
    document.addEventListener("mouseup", onMouseUp);
  }, [columnWidths, setColumnWidths]);

  return (
    <Paper elevation={0} sx={{ border: "1px solid rgba(255,255,255,0.1)", borderRadius: 2, overflow: 'hidden' }}>
      <TableContainer sx={{
        maxHeight: "70vh",
        overflowX: 'auto',
        // Custom Scrollbar Styling
        '&::-webkit-scrollbar': {
          width: '8px',
          height: '8px'
        },
        '&::-webkit-scrollbar-track': {
          background: 'transparent'
        },
        '&::-webkit-scrollbar-thumb': {
          background: alpha(theme.palette.text.secondary, 0.2),
          borderRadius: '10px'
        },
        '&::-webkit-scrollbar-thumb:hover': {
          background: alpha(theme.palette.text.secondary, 0.4)
        }
      }}>
        <Table size="small" sx={{ tableLayout: "fixed", width: "100%" }}>
          <TableHead
            sx={{
              position: "sticky",
              top: 0,
              zIndex: 2,
              backgroundColor: "background.paper",
              "& th": {
                borderBottom: "1px solid rgba(255,255,255,0.15)",
                fontWeight: 600,
                fontSize: "0.85rem",
                letterSpacing: "0.5px"
              }
            }}
          >
            <TableRow>
              {selectionEnabled && (
                <TableCell padding="checkbox" sx={{ width: 48 }}>
                  <Checkbox
                    size="small"
                    checked={allPageSelected}
                    indeterminate={pageIndeterminate}
                    onChange={onToggleSelectPage}
                  />
                </TableCell>
              )}
              {visibleColumns.map((key) => (
                <ResizableHeader
                  key={key}
                  column={key}
                  label={columnLabels[key]}
                  width={columnWidths[key]}
                  sortConfig={sortConfig}
                  onSort={onSort}
                  onResize={handleResize}
                />
              ))}
            </TableRow>
          </TableHead>

          <TableBody
            sx={{
              '& .MuiTableRow-root:not(:has([colspan])):nth-of-type(even)': {
                bgcolor: alpha(theme.palette.action.hover, 0.025),
              },
            }}
          >
            {groupBy === "none" ? (
              // FLAT RENDERING
              filtered.map((device) => (
                <DeviceRow
                  key={device.id}
                  device={device}
                  columnOrder={visibleColumns}
                  visibility={visibility}
                  columnWidths={columnWidths}
                  onDeviceClick={onDeviceClick}
                  ptpStatus={ptpStatus}
                  nodesMap={nodesMap}
                  openAvcLinksMap={openAvcLinksMap}
                  openAvcIntegrationStatus={openAvcIntegrationStatus}
                  selectionEnabled={selectionEnabled}
                  selected={selectedIds?.has(device.id)}
                  onToggleSelect={onToggleSelect}
                  parentNameById={parentNameById}
                />
              ))
            ) : (
              // GROUPED ACCORDION RENDERING
              grouped && Object.keys(grouped).sort().map((groupName) => {
                const devicesInGroup = grouped[groupName];
                const isExpanded = expandedGroups[groupName] !== false; // default expanded

                // Group Metrics
                const onlineCount = devicesInGroup.filter(d => d.status === "online").length;
                const offlineCount = devicesInGroup.filter(d => d.status !== "online").length;

                return (
                  <React.Fragment key={groupName}>
                    {/* Collapsible Section Header Row */}
                    <TableRow
                      onClick={() => toggleGroup(groupName)}
                      sx={{
                        bgcolor: alpha(theme.palette.action.hover, 0.03),
                        cursor: 'pointer',
                        '&:hover': { bgcolor: alpha(theme.palette.action.hover, 0.06) },
                        borderBottom: "1px solid rgba(255,255,255,0.06)",
                        userSelect: "none"
                      }}
                    >
                      <TableCell colSpan={colSpan} sx={{ py: 1.5, px: 2 }}>
                        <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', width: '100%' }}>
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
                            <IconButton 
                              size="small" 
                              sx={{ 
                                p: 0, 
                                transition: 'transform 0.2s', 
                                transform: isExpanded ? 'rotate(90deg)' : 'rotate(0deg)' 
                              }}
                            >
                              <ChevronRightIcon />
                            </IconButton>
                            <Typography 
                              variant="subtitle2" 
                              sx={{ 
                                fontWeight: 800, 
                                fontFamily: 'Outfit, sans-serif', 
                                textTransform: 'uppercase', 
                                letterSpacing: '0.5px' 
                              }}
                            >
                              {groupName}
                            </Typography>
                            <Chip 
                              label={`${devicesInGroup.length} ${devicesInGroup.length === 1 ? 'Device' : 'Devices'}`}
                              size="small" 
                              sx={{ fontSize: '0.65rem', fontWeight: 900, bgcolor: 'action.selected' }}
                            />
                          </Box>

                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                            <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                              <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
                              <Typography variant="caption" sx={{ fontWeight: 700, color: 'success.main' }}>
                                {onlineCount} Online
                              </Typography>
                            </Box>
                            {offlineCount > 0 && (
                              <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5 }}>
                                <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'error.main' }} />
                                <Typography variant="caption" sx={{ fontWeight: 700, color: 'error.main' }}>
                                  {offlineCount} Offline
                                </Typography>
                              </Box>
                            )}
                          </Box>
                        </Box>
                      </TableCell>
                    </TableRow>

                    {/* Collapsible Device Rows */}
                    {isExpanded && devicesInGroup.map((device) => (
                      <DeviceRow
                        key={device.id}
                        device={device}
                        columnOrder={visibleColumns}
                        visibility={visibility}
                        columnWidths={columnWidths}
                        onDeviceClick={onDeviceClick}
                        ptpStatus={ptpStatus}
                        nodesMap={nodesMap}
                        openAvcLinksMap={openAvcLinksMap}
                  openAvcIntegrationStatus={openAvcIntegrationStatus}
                        selectionEnabled={selectionEnabled}
                        selected={selectedIds?.has(device.id)}
                        onToggleSelect={onToggleSelect}
                        parentNameById={parentNameById}
                      />
                    ))}
                  </React.Fragment>
                );
              })
            )}

            {filtered.length === 0 && (
              <TableRow>
                <TableCell colSpan={colSpan} align="center" sx={{ py: 8 }}>
                  <Typography variant="body1" color="text.secondary" sx={{ fontStyle: 'italic' }}>
                    No devices match the current filters.
                  </Typography>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </TableContainer>
    </Paper>
  );
}
