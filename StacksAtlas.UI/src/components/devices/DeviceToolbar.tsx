import React, { useRef, useState } from "react";
import {
  Box,
  Typography,
  Paper,
  TextField,
  IconButton,
  Divider,
  Button,
  Popover,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  FormControl,
  Select,
  MenuItem,
  Checkbox,
  OutlinedInput,
  ToggleButton,
  ToggleButtonGroup,
  Menu,
  Tooltip,
  Chip,
  InputAdornment,
  alpha,
  useTheme,
} from "@mui/material";
import SearchIcon from "@mui/icons-material/Search";
import ViewColumnIcon from "@mui/icons-material/ViewColumn";
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import VisibilityIcon from '@mui/icons-material/Visibility';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import FileDownloadIcon from "@mui/icons-material/FileDownload";
import FileUploadIcon from "@mui/icons-material/FileUpload";
import ArrowDropDownIcon from "@mui/icons-material/ArrowDropDown";
import PauseIcon from "@mui/icons-material/Pause";
import PlayArrowIcon from "@mui/icons-material/PlayArrow";
import UnfoldMoreIcon from "@mui/icons-material/UnfoldMore";
import UnfoldLessIcon from "@mui/icons-material/UnfoldLess";

import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent
} from '@dnd-kit/core';
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
  useSortable
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';

import { useColumnVisibility } from "../../context/ColumnVisibilityContext";
import {
  type ColumnKey,
  columnLabels,
  DEFAULT_COLUMN_ORDER,
  PORTABLE_DEFAULT_COLUMN_ORDER,
  filterColumnsForPortable,
  buildEssentialColumnVisibility,
  buildShowAllColumnVisibility,
  type DeviceViewMode,
} from "./types";
import type { DeviceGroupBy } from "./deviceGrouping";
import { HUB_GROUP_BY_OPTIONS, NODE_GROUP_BY_OPTIONS } from "./deviceGrouping";

// -------------------------------------------------------------
//  DND Sortable Item Component
// -------------------------------------------------------------
function SortableItem(props: { id: string; label: string; visible: boolean; onToggle: () => void }) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging
  } = useSortable({ id: props.id });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
    zIndex: isDragging ? 999 : 'auto',
    position: 'relative' as const,
    backgroundColor: isDragging ? '#f5f5f5' : 'transparent'
  };

  return (
    <ListItem
      ref={setNodeRef}
      style={style}
      secondaryAction={
        <IconButton edge="end" onClick={(e) => { e.stopPropagation(); props.onToggle(); }} size="small">
          {props.visible ? <VisibilityIcon fontSize="small" color="primary" /> : <VisibilityOffIcon fontSize="small" color="disabled" />}
        </IconButton>
      }
      sx={{
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 1,
        mb: 0.5,
        bgcolor: 'background.paper'
      }}
    >
      <ListItemIcon sx={{ minWidth: 30, cursor: 'grab', '&:active': { cursor: 'grabbing' } }} {...attributes} {...listeners}>
        <DragIndicatorIcon fontSize="small" sx={{ color: 'text.secondary', opacity: 0.6 }} />
      </ListItemIcon>
      <ListItemText
        primary={props.label}
        primaryTypographyProps={{
          variant: 'body2',
          fontWeight: 600,
          color: props.visible ? 'text.primary' : 'text.disabled'
        }}
      />
    </ListItem>
  );
}

function ToolbarLabel({ children }: { children: React.ReactNode }) {
  return (
    <Typography
      variant="caption"
      sx={{
        fontWeight: 800,
        letterSpacing: '0.08em',
        color: 'text.disabled',
        textTransform: 'uppercase',
        fontSize: '0.65rem',
        mr: 0.25,
        flexShrink: 0,
      }}
    >
      {children}
    </Typography>
  );
}

// Helper time formatting for update badge
function timeSince(date: Date | null): string {
  if (!date) return " - ";
  const diff = Math.floor((Date.now() - date.getTime()) / 1000);
  if (diff < 5) return "Just now";
  if (diff < 60) return `${diff}s ago`;
  return `${Math.floor(diff / 60)}m ago`;
}

type StatusFilter = "All" | "online" | "offline" | "new";

const STATUS_FILTERS: { value: StatusFilter; label: string; tone?: "success" | "error" | "info" }[] = [
  { value: "All", label: "All" },
  { value: "online", label: "Online", tone: "success" },
  { value: "offline", label: "Offline", tone: "error" },
  { value: "new", label: "New", tone: "info" },
];

// -------------------------------------------------------------
//  DeviceToolbar Props & Component
// -------------------------------------------------------------
interface DeviceToolbarProps {
  isMobile?: boolean;
  search: string;
  setSearch: (s: string) => void;
  filterStatus: StatusFilter;
  setFilterStatus: (status: StatusFilter) => void;
  filterVlanTags: string[];
  setFilterVlanTags: (tags: string[]) => void;
  availableVlanTags: string[];
  filterAttachmentKind: string;
  setFilterAttachmentKind: (kind: string) => void;
  filterAttachmentPort: string;
  setFilterAttachmentPort: (port: string) => void;
  filterAttachmentParentId: string;
  setFilterAttachmentParentId: (id: string) => void;
  attachmentParentOptions: { id: string; label: string }[];
  onLoadAttachmentParents?: () => void;
  onClearFilters?: () => void;
  filtersActive?: boolean;
  viewMode: DeviceViewMode;
  setViewMode: (mode: DeviceViewMode) => void;
  isAdmin: boolean;
  groupBy: DeviceGroupBy;
  setGroupBy: (mode: DeviceGroupBy) => void;
  onExpandAllGroups?: () => void;
  onCollapseAllGroups?: () => void;
  isHub: boolean;
  filterNodeId?: string;
  setFilterNodeId?: (nodeId: string) => void;
  nodeFilterOptions?: { id: string; label: string }[];
  autoRefreshPaused: boolean;
  setAutoRefreshPaused: React.Dispatch<React.SetStateAction<boolean>>;
  lastUpdated: Date | null;
  exportFilteredToCSV: (mode?: 'visible' | 'all') => void;
  exportFilteredAssetsCSV: () => void;
  onImportAssets?: (file: File) => void;
  onDownloadAssetTemplate?: () => void;
  canImportAssets?: boolean;
  columnOrder: ColumnKey[];
  setColumnOrder: React.Dispatch<React.SetStateAction<ColumnKey[]>>;
  isPortable?: boolean;
  deviceCount?: { showing: number; total: number };
}

export function DeviceToolbar({
  isMobile = false,
  search,
  setSearch,
  filterStatus,
  setFilterStatus,
  filterVlanTags,
  setFilterVlanTags,
  availableVlanTags,
  filterAttachmentKind,
  setFilterAttachmentKind,
  filterAttachmentPort,
  setFilterAttachmentPort,
  filterAttachmentParentId,
  setFilterAttachmentParentId,
  attachmentParentOptions,
  onLoadAttachmentParents,
  onClearFilters,
  filtersActive = false,
  viewMode,
  setViewMode,
  isAdmin,
  groupBy,
  setGroupBy,
  onExpandAllGroups,
  onCollapseAllGroups,
  isHub,
  filterNodeId = '',
  setFilterNodeId,
  nodeFilterOptions = [],
  autoRefreshPaused,
  setAutoRefreshPaused,
  lastUpdated,
  exportFilteredToCSV,
  exportFilteredAssetsCSV,
  onImportAssets,
  onDownloadAssetTemplate,
  canImportAssets = false,
  columnOrder,
  setColumnOrder,
  isPortable = false,
  deviceCount,
}: DeviceToolbarProps) {
  const theme = useTheme();
  const { visibility, setVisibility } = useColumnVisibility();
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const [exportMenuAnchor, setExportMenuAnchor] = useState<null | HTMLElement>(null);
  const importInputRef = useRef<HTMLInputElement>(null);

  const openMenu = (e: React.MouseEvent<HTMLButtonElement>) => setAnchorEl(e.currentTarget);
  const closeMenu = () => setAnchorEl(null);

  const toggleColumn = (col: ColumnKey) => {
    setVisibility({
      ...visibility,
      [col]: !visibility[col]
    });
  };

  const statusButtonSx = (tone: "success" | "error" | "info" | undefined, selected: boolean) => {
    if (!tone) {
      return selected
        ? { bgcolor: 'primary.main', color: 'primary.contrastText', '&:hover': { bgcolor: 'primary.dark' } }
        : {};
    }
    const main = theme.palette[tone].main;
    const contrast = theme.palette[tone].contrastText;
    return selected
      ? { bgcolor: main, color: contrast, borderColor: main, '&:hover': { bgcolor: theme.palette[tone].dark } }
      : { color: main, borderColor: alpha(main, 0.35), '&:hover': { bgcolor: alpha(main, 0.08), borderColor: main } };
  };

  // --- DND SENSORS ---
  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  const handleDragEnd = (event: DragEndEvent) => {
    const { active, over } = event;
    if (over && active.id !== over.id) {
      setColumnOrder((items) => {
        const oldIndex = items.indexOf(active.id as ColumnKey);
        const newIndex = items.indexOf(over.id as ColumnKey);
        return arrayMove(items, oldIndex, newIndex);
      });
    }
  };

  const filtersDisabled = viewMode !== 'active';

  return (
    <Paper
      variant="outlined"
      sx={{
        p: { xs: 1.5, md: 2 },
        mb: 3,
        borderRadius: 3,
        bgcolor: 'background.paper',
        borderColor: 'divider',
        display: "flex",
        flexDirection: "column",
        gap: 1.5,
      }}
    >
      {/* Row 1  -  search & scope */}
      <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, flexWrap: "wrap" }}>
        <TextField
          placeholder="Search registry..."
          variant="outlined"
          size="small"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          InputProps={{
            startAdornment: (
              <InputAdornment position="start">
                <SearchIcon fontSize="small" sx={{ opacity: 0.5 }} />
              </InputAdornment>
            ),
          }}
          sx={{
            width: { xs: '100%', sm: 240 },
            "& .MuiOutlinedInput-root": { borderRadius: 2, bgcolor: 'action.hover' }
          }}
        />

        {!isPortable && (
          <FormControl size="small" sx={{ minWidth: 150, maxWidth: 200 }}>
            <Select
              multiple
              displayEmpty
              value={filterVlanTags}
              onChange={(e) => {
                const value = e.target.value;
                setFilterVlanTags(typeof value === 'string' ? value.split(',') : value);
              }}
              input={<OutlinedInput />}
              renderValue={(selected) => {
                if (selected.length === 0) return <Typography variant="body2" sx={{ opacity: 0.6 }}>All VLANs</Typography>;
                if (selected.length === 1) return selected[0] === "__untagged__" ? "Untagged" : selected[0];
                return `${selected.length} VLANs`;
              }}
              sx={{ borderRadius: 2, bgcolor: 'action.hover', fontSize: '0.8rem' }}
              disabled={filtersDisabled}
            >
              <MenuItem value="__untagged__">
                <Checkbox checked={filterVlanTags.includes("__untagged__")} size="small" />
                <ListItemText primary="Untagged" secondary="No VLAN label assigned" />
              </MenuItem>
              {availableVlanTags.map(tag => (
                <MenuItem key={tag} value={tag}>
                  <Checkbox checked={filterVlanTags.includes(tag)} size="small" />
                  <ListItemText primary={tag} />
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        )}

        {isHub && !isPortable && nodeFilterOptions.length > 0 && setFilterNodeId && (
          <FormControl size="small" sx={{ minWidth: { xs: 140, sm: 168 } }}>
            <Select
              value={filterNodeId}
              onChange={(e) => setFilterNodeId(e.target.value)}
              displayEmpty
              sx={{
                borderRadius: 2,
                height: 32,
                fontSize: '0.75rem',
                fontWeight: 700,
                bgcolor: filterNodeId ? alpha(theme.palette.primary.main, 0.08) : 'action.hover',
              }}
            >
              <MenuItem value="">
                <Typography variant="body2" sx={{ fontWeight: 700 }}>All sites</Typography>
              </MenuItem>
              {nodeFilterOptions.map((opt) => (
                <MenuItem key={opt.id} value={opt.id}>
                  {opt.label}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        )}

        {!isPortable && (
          <FormControl size="small" sx={{ minWidth: 130 }}>
            <Select
              value={groupBy}
              onChange={(e) => setGroupBy(e.target.value as typeof groupBy)}
              sx={{
                borderRadius: 2,
                height: 32,
                fontSize: '0.75rem',
                fontWeight: 700,
                bgcolor: 'action.hover',
              }}
            >
              {(isHub ? HUB_GROUP_BY_OPTIONS : NODE_GROUP_BY_OPTIONS).map((mode) => (
                <MenuItem key={mode} value={mode}>
                  {mode === 'none' && 'Flat list'}
                  {mode === 'node' && 'Group by node'}
                  {mode === 'client' && 'Group by client'}
                  {mode === 'location' && 'Group by location'}
                  {mode === 'uplink' && 'Group by uplink'}
                  {mode === 'status' && 'Group by status'}
                  {mode === 'vendor' && 'Group by vendor'}
                </MenuItem>
              ))}
            </Select>
          </FormControl>
        )}

        {!isPortable && groupBy !== 'none' && onExpandAllGroups && onCollapseAllGroups && (
          <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.25 }}>
            <Tooltip title="Expand all groups">
              <IconButton
                size="small"
                onClick={onExpandAllGroups}
                aria-label="Expand all groups"
                sx={{
                  width: 32,
                  height: 32,
                  borderRadius: 2,
                  bgcolor: 'action.hover',
                }}
              >
                <UnfoldMoreIcon fontSize="small" />
              </IconButton>
            </Tooltip>
            <Tooltip title="Collapse all groups">
              <IconButton
                size="small"
                onClick={onCollapseAllGroups}
                aria-label="Collapse all groups"
                sx={{
                  width: 32,
                  height: 32,
                  borderRadius: 2,
                  bgcolor: 'action.hover',
                }}
              >
                <UnfoldLessIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Box>
        )}

        {deviceCount && (
          <Chip
            size="small"
            variant="outlined"
            label={
              deviceCount.showing === deviceCount.total
                ? `${deviceCount.total} devices`
                : `${deviceCount.showing} of ${deviceCount.total}`
            }
            sx={{ fontWeight: 700, fontSize: '0.7rem', height: 28 }}
          />
        )}

        <Box sx={{ flexGrow: 1 }} />

        {/* Actions  -  right-aligned on wide screens */}
        <Box sx={{ display: "flex", alignItems: "center", gap: 1, flexWrap: "wrap" }}>
          {canImportAssets && viewMode !== 'removed' && (
            <>
              <input
                ref={importInputRef}
                type="file"
                accept=".csv,text/csv"
                hidden
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  if (file && onImportAssets) onImportAssets(file);
                  e.target.value = '';
                }}
              />
              <Tooltip title="Import asset CSV">
                <Button
                  variant="outlined"
                  size="small"
                  startIcon={<FileUploadIcon />}
                  onClick={() => importInputRef.current?.click()}
                  sx={{ fontWeight: 700, borderRadius: 2, height: 32, display: { xs: 'none', sm: 'inline-flex' } }}
                >
                  Import
                </Button>
              </Tooltip>
              <Tooltip title="Import asset CSV">
                <IconButton
                  size="small"
                  onClick={() => importInputRef.current?.click()}
                  sx={{ display: { xs: 'inline-flex', sm: 'none' }, borderRadius: 2, border: '1px solid', borderColor: 'divider' }}
                >
                  <FileUploadIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </>
          )}

          <Button
            variant="outlined"
            size="small"
            endIcon={<ArrowDropDownIcon />}
            startIcon={<FileDownloadIcon />}
            onClick={(e) => setExportMenuAnchor(e.currentTarget)}
            sx={{ fontWeight: 700, borderRadius: 2, height: 32 }}
          >
            Export
          </Button>
          <Menu
            anchorEl={exportMenuAnchor}
            open={Boolean(exportMenuAnchor)}
            onClose={() => setExportMenuAnchor(null)}
            slotProps={{ paper: { sx: { minWidth: 280 } } }}
          >
            <MenuItem
              onClick={() => {
                setExportMenuAnchor(null);
                exportFilteredToCSV('visible');
              }}
            >
              <ListItemText
                primary="Current view"
                secondary="Columns visible in your table"
                primaryTypographyProps={{ fontSize: '0.875rem', fontWeight: 700 }}
                secondaryTypographyProps={{ fontSize: '0.7rem' }}
              />
            </MenuItem>
            <MenuItem
              onClick={() => {
                setExportMenuAnchor(null);
                exportFilteredToCSV('all');
              }}
            >
              <ListItemText
                primary="Full registry"
                secondary="Every inventory field"
                primaryTypographyProps={{ fontSize: '0.875rem', fontWeight: 700 }}
                secondaryTypographyProps={{ fontSize: '0.7rem' }}
              />
            </MenuItem>
            {viewMode !== 'removed' && (
              <MenuItem
                onClick={() => {
                  setExportMenuAnchor(null);
                  exportFilteredAssetsCSV();
                }}
              >
                <ListItemText
                  primary="Asset fields"
                  secondary="Edit in Excel, then Import assets"
                  primaryTypographyProps={{ fontSize: '0.875rem', fontWeight: 700 }}
                  secondaryTypographyProps={{ fontSize: '0.7rem' }}
                />
              </MenuItem>
            )}
            {canImportAssets && onDownloadAssetTemplate && viewMode !== 'removed' && (
              <MenuItem
                onClick={() => {
                  setExportMenuAnchor(null);
                  onDownloadAssetTemplate();
                }}
              >
                <ListItemText
                  primary="Import template"
                  secondary="Blank CSV with example row"
                  primaryTypographyProps={{ fontSize: '0.875rem', fontWeight: 700 }}
                  secondaryTypographyProps={{ fontSize: '0.7rem' }}
                />
              </MenuItem>
            )}
          </Menu>

          <Tooltip title={autoRefreshPaused ? "Resume auto-refresh" : "Pause auto-refresh"}>
            <IconButton
              size="small"
              onClick={(e) => {
                e.stopPropagation();
                setAutoRefreshPaused((p) => !p);
              }}
              aria-label={autoRefreshPaused ? "Resume auto-refresh" : "Pause auto-refresh"}
              sx={{
                borderRadius: 2,
                height: 32,
                width: 32,
                border: '1px solid',
                borderColor: autoRefreshPaused ? 'warning.main' : 'divider',
                bgcolor: autoRefreshPaused ? alpha(theme.palette.warning.main, 0.12) : 'action.hover',
                color: autoRefreshPaused ? 'warning.main' : 'text.secondary',
                '&:hover': {
                  bgcolor: autoRefreshPaused
                    ? alpha(theme.palette.warning.main, 0.2)
                    : alpha(theme.palette.action.hover, 0.12),
                },
              }}
            >
              {autoRefreshPaused ? <PlayArrowIcon fontSize="small" /> : <PauseIcon fontSize="small" />}
            </IconButton>
          </Tooltip>

          <Box sx={{ textAlign: 'right', display: { xs: 'none', lg: 'block' }, px: 0.5 }}>
            <Typography variant="caption" color="text.secondary" display="block" sx={{ fontWeight: 800, lineHeight: 1, fontSize: '0.6rem', letterSpacing: '0.06em' }}>
              UPDATED
            </Typography>
            <Typography variant="caption" sx={{ fontFamily: 'monospace', opacity: 0.8 }}>
              {timeSince(lastUpdated)}
            </Typography>
          </Box>

          {!isMobile && (
            <Tooltip title="Customize columns">
              <IconButton
                onClick={openMenu}
                size="small"
                sx={{
                  bgcolor: anchorEl ? 'primary.main' : 'action.hover',
                  color: anchorEl ? 'white' : 'inherit',
                  borderRadius: 2,
                  height: 32,
                  width: 32,
                  border: '1px solid',
                  borderColor: anchorEl ? 'primary.main' : 'divider',
                  '&:hover': { bgcolor: 'primary.dark', color: 'white', borderColor: 'primary.dark' }
                }}
              >
                <ViewColumnIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}
        </Box>
      </Box>

      <Divider sx={{ opacity: 0.5 }} />

      {/* Row 2  -  filters & inventory view */}
      <Box sx={{ display: "flex", alignItems: "center", gap: 1.5, flexWrap: "wrap" }}>
        <ToolbarLabel>Status</ToolbarLabel>
        <ToggleButtonGroup
          exclusive
          size="small"
          value={filterStatus}
          onChange={(_, value: StatusFilter | null) => {
            if (value) setFilterStatus(value);
          }}
          disabled={filtersDisabled}
          sx={{
            '& .MuiToggleButton-root': {
              borderRadius: '8px !important',
              mx: 0.25,
              fontWeight: 800,
              fontSize: '0.7rem',
              textTransform: 'uppercase',
              px: 1.25,
              py: 0.5,
              border: '1px solid',
              borderColor: 'divider',
            },
          }}
        >
          {STATUS_FILTERS.map(({ value, label, tone }) => (
            <ToggleButton
              key={value}
              value={value}
              sx={statusButtonSx(tone, filterStatus === value)}
            >
              {label}
            </ToggleButton>
          ))}
        </ToggleButtonGroup>

        <Divider orientation="vertical" flexItem sx={{ mx: 0.5, display: { xs: 'none', sm: 'block' } }} />

        <ToolbarLabel>Link</ToolbarLabel>
        <FormControl size="small" sx={{ minWidth: 110 }} disabled={filtersDisabled}>
          <Select
            displayEmpty
            value={filterAttachmentKind}
            onChange={(e) => setFilterAttachmentKind(e.target.value)}
            sx={{ borderRadius: 2, bgcolor: 'action.hover', fontSize: '0.8rem', height: 32 }}
          >
            <MenuItem value="All">All modes</MenuItem>
            <MenuItem value="Ethernet">Ethernet</MenuItem>
            <MenuItem value="WiFi">Wi-Fi</MenuItem>
            <MenuItem value="Fiber">Fiber</MenuItem>
            <MenuItem value="Other">Other</MenuItem>
            <MenuItem value="Unknown">Unknown</MenuItem>
          </Select>
        </FormControl>
        <TextField
          size="small"
          placeholder="Port / SSID..."
          value={filterAttachmentPort}
          onChange={(e) => setFilterAttachmentPort(e.target.value)}
          disabled={filtersDisabled}
          sx={{
            width: 120,
            "& .MuiOutlinedInput-root": { borderRadius: 2, bgcolor: 'action.hover', height: 32, fontSize: '0.8rem' },
          }}
        />
        <FormControl size="small" sx={{ minWidth: 140 }} disabled={filtersDisabled}>
          <Select
            displayEmpty
            value={filterAttachmentParentId}
            onOpen={() => onLoadAttachmentParents?.()}
            onChange={(e) => setFilterAttachmentParentId(e.target.value)}
            renderValue={(selected) => {
              if (!selected) return <Typography variant="body2" sx={{ opacity: 0.6 }}>All uplinks</Typography>;
              if (selected === 'none') return 'No uplink';
              if (selected === 'any') return 'Has uplink';
              const match = attachmentParentOptions.find((o) => o.id === selected);
              return match?.label ?? selected.slice(0, 8);
            }}
            sx={{ borderRadius: 2, bgcolor: 'action.hover', fontSize: '0.8rem', height: 32 }}
          >
            <MenuItem value="">All uplinks</MenuItem>
            <MenuItem value="none">No uplink</MenuItem>
            <MenuItem value="any">Has uplink</MenuItem>
            {attachmentParentOptions.map((opt) => (
              <MenuItem key={opt.id} value={opt.id}>{opt.label}</MenuItem>
            ))}
          </Select>
        </FormControl>

        {filtersActive && onClearFilters && (
          <Button
            size="small"
            onClick={onClearFilters}
            disabled={filtersDisabled}
            sx={{ fontWeight: 800, fontSize: '0.7rem', height: 32, borderRadius: 2 }}
          >
            Clear filters
          </Button>
        )}

        <Divider orientation="vertical" flexItem sx={{ mx: 0.5, display: { xs: 'none', sm: 'block' } }} />

        <ToolbarLabel>Registry</ToolbarLabel>
        <ToggleButtonGroup
          exclusive
          size="small"
          value={viewMode}
          onChange={(_, value: DeviceViewMode | null) => {
            if (value) setViewMode(value);
          }}
          sx={{
            height: 32,
            '& .MuiToggleButton-root': {
              borderRadius: '8px !important',
              mx: 0.25,
              fontWeight: 800,
              fontSize: '0.7rem',
              textTransform: 'uppercase',
              px: 1.25,
              border: '1px solid',
              borderColor: 'divider',
            },
          }}
        >
          <ToggleButton
            value="active"
            sx={{
              '&.Mui-selected': {
                bgcolor: alpha(theme.palette.success.main, 0.2),
                color: 'success.light',
                borderColor: 'success.main',
                '&:hover': { bgcolor: alpha(theme.palette.success.main, 0.28) },
              },
            }}
          >
            Active
          </ToggleButton>
          <ToggleButton
            value="archive"
            sx={{
              '&.Mui-selected': {
                bgcolor: 'warning.main',
                color: 'warning.contrastText',
                borderColor: 'warning.main',
                '&:hover': { bgcolor: 'warning.dark' },
              },
            }}
          >
            Archive
          </ToggleButton>
          {isAdmin && (
            <ToggleButton
              value="removed"
              sx={{
                '&.Mui-selected': {
                  bgcolor: 'error.main',
                  color: 'error.contrastText',
                  borderColor: 'error.main',
                  '&:hover': { bgcolor: 'error.dark' },
                },
              }}
            >
              Removed
            </ToggleButton>
          )}
        </ToggleButtonGroup>

        {viewMode === 'removed' && (
          <Typography variant="caption" color="error.main" sx={{ fontWeight: 600, maxWidth: 320 }}>
            Governance view  -  suppressed from discovery until restored.
          </Typography>
        )}
        {viewMode === 'archive' && (
          <Typography variant="caption" color="warning.main" sx={{ fontWeight: 600, display: { xs: 'none', md: 'block' } }}>
            Archived devices are hidden from active monitoring.
          </Typography>
        )}
      </Box>

      {/* Column customize popover */}
      {!isMobile && (
        <Popover
          open={Boolean(anchorEl)}
          anchorEl={anchorEl}
          onClose={closeMenu}
          anchorOrigin={{ vertical: "bottom", horizontal: "right" }}
          transformOrigin={{ vertical: "top", horizontal: "right" }}
          PaperProps={{
            sx: {
              width: 280,
              maxHeight: 400,
              borderRadius: 3,
              boxShadow: '0px 8px 32px rgba(0,0,0,0.2)',
              border: '1px solid',
              borderColor: 'divider',
              p: 0,
              overflow: 'hidden',
              display: 'flex',
              flexDirection: 'column'
            }
          }}
        >
          <Box sx={{ p: 2, borderBottom: '1px solid', borderColor: 'divider', bgcolor: 'background.default' }}>
            <Typography variant="subtitle2" fontWeight={800}>Columns</Typography>
            <Typography variant="caption" color="text.secondary">Drag to reorder. Toggle to show or hide.</Typography>
          </Box>

          <Box sx={{ overflowY: 'auto', flexGrow: 1, p: 1 }}>
            <DndContext
              sensors={sensors}
              collisionDetection={closestCenter}
              onDragEnd={handleDragEnd}
            >
              <SortableContext
                items={isPortable ? filterColumnsForPortable(columnOrder) : columnOrder}
                strategy={verticalListSortingStrategy}
              >
                <List dense>
                  {(isPortable ? filterColumnsForPortable(columnOrder) : columnOrder).map((key) => (
                    <SortableItem
                      key={key}
                      id={key}
                      label={columnLabels[key]}
                      visible={visibility[key]}
                      onToggle={() => toggleColumn(key)}
                    />
                  ))}
                </List>
              </SortableContext>
            </DndContext>
          </Box>
          <Box sx={{ p: 1.5, borderTop: '1px solid', borderColor: 'divider', bgcolor: 'background.default', display: 'flex', flexWrap: 'wrap', gap: 0.75, justifyContent: 'center' }}>
            <Button size="small" variant="contained" onClick={() => setVisibility(buildEssentialColumnVisibility())}>
              Essential
            </Button>
            <Button size="small" onClick={() => setVisibility(buildShowAllColumnVisibility())}>
              Show all
            </Button>
            <Button size="small" onClick={() => {
              setColumnOrder(isPortable ? PORTABLE_DEFAULT_COLUMN_ORDER : DEFAULT_COLUMN_ORDER);
            }}>
              Reset order
            </Button>
          </Box>
        </Popover>
      )}
    </Paper>
  );
}
