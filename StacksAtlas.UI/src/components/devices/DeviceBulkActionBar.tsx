import { useMemo, useState } from 'react';
import {
  Autocomplete,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Fade,
  IconButton,
  LinearProgress,
  Menu,
  MenuItem,
  Paper,
  TextField,
  Typography,
} from "@mui/material";
import DeleteIcon from "@mui/icons-material/Delete";
import RestoreFromTrashIcon from "@mui/icons-material/RestoreFromTrash";
import FileDownloadIcon from "@mui/icons-material/FileDownload";
import NotificationsActiveIcon from "@mui/icons-material/NotificationsActive";
import NotificationsOffIcon from "@mui/icons-material/NotificationsOff";
import CloseIcon from "@mui/icons-material/Close";
import Inventory2OutlinedIcon from '@mui/icons-material/Inventory2Outlined';
import ArrowDropDownIcon from '@mui/icons-material/ArrowDropDown';
import BuildOutlinedIcon from '@mui/icons-material/BuildOutlined';
import NetworkPingIcon from '@mui/icons-material/NetworkPing';
import RadarIcon from '@mui/icons-material/Radar';
import CategoryOutlinedIcon from '@mui/icons-material/CategoryOutlined';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import {
  DEVICE_TYPE_OPTIONS,
  deviceTypeCategory,
  filterDeviceTypeOptions,
  formatDeviceTypeLabel,
} from '../../utils/deviceTypeLabels';

import type { DeviceViewMode } from './types';

const menuSlotProps = {
  paper: { sx: { minWidth: 220, maxHeight: 360 } },
  root: { sx: { zIndex: 2200 } },
};

const menuAnchorUp = {
  anchorOrigin: { vertical: 'top' as const, horizontal: 'left' as const },
  transformOrigin: { vertical: 'bottom' as const, horizontal: 'left' as const },
};

export type DeviceBulkActionBarProps = {
  selectedCount: number;
  viewMode: DeviceViewMode;
  isAdmin: boolean;
  canEditAssets: boolean;
  busy: boolean;
  progress?: { current: number; total: number } | null;
  attachmentParentOptions: { id: string; label: string }[];
  onLoadAttachmentParents?: () => void;
  uplinkMixedSites?: boolean;
  uplinkScopedSiteLabel?: string | null;
  onClear: () => void;
  onArchive: () => void;
  onRestore: () => void;
  onPermanentDelete: () => void;
  onRestoreToFleet: () => void;
  onExportSelected: () => void;
  onExportAssetSelected: () => void;
  onEnableAlerts: () => void;
  onDisableAlerts: () => void;
  onClearWarranty: () => void;
  onClearAssetTag: () => void;
  onClearManualAssetFields: () => void;
  onBulkPing: () => void;
  onBulkDeepScan: () => void;
  onBulkSetType: (type: string) => void;
  onBulkSetLocation: (location: string) => void;
  onBulkSetVendor: (vendor: string) => void;
  onBulkSetModel: (model: string) => void;
  onBulkSetFirmware: (firmware: string) => void;
  onBulkSetWarranty: (warrantyUtc: string) => void;
  onBulkSetAttachmentKind: (kind: string) => void;
  onBulkSetAttachmentParent: (parentDeviceId: string) => void;
  onBulkClearAttachmentParent: () => void;
  onBulkSetSsid: (ssid: string) => void;
  onBulkClearSsid: () => void;
};

export function DeviceBulkActionBar({
  selectedCount,
  viewMode,
  isAdmin,
  canEditAssets,
  busy,
  progress,
  attachmentParentOptions,
  onLoadAttachmentParents,
  uplinkMixedSites = false,
  uplinkScopedSiteLabel = null,
  onClear,
  onArchive,
  onRestore,
  onPermanentDelete,
  onRestoreToFleet,
  onExportSelected,
  onExportAssetSelected,
  onEnableAlerts,
  onDisableAlerts,
  onClearWarranty,
  onClearAssetTag,
  onClearManualAssetFields,
  onBulkPing,
  onBulkDeepScan,
  onBulkSetType,
  onBulkSetLocation,
  onBulkSetVendor,
  onBulkSetModel,
  onBulkSetFirmware,
  onBulkSetWarranty,
  onBulkSetAttachmentKind,
  onBulkSetAttachmentParent,
  onBulkClearAttachmentParent,
  onBulkSetSsid,
  onBulkClearSsid,
}: DeviceBulkActionBarProps) {
  const isMobile = useIsMobileLayout();
  const [assetMenuAnchor, setAssetMenuAnchor] = useState<null | HTMLElement>(null);
  const [actionsMenuAnchor, setActionsMenuAnchor] = useState<null | HTMLElement>(null);
  const [typeDialogOpen, setTypeDialogOpen] = useState(false);
  const [typeDraft, setTypeDraft] = useState('Unknown');
  const [attachKindDialogOpen, setAttachKindDialogOpen] = useState(false);
  const [attachKindDraft, setAttachKindDraft] = useState('Ethernet');
  const [uplinkDialogOpen, setUplinkDialogOpen] = useState(false);
  const [uplinkDraft, setUplinkDraft] = useState<{ id: string; label: string } | null>(null);
  const [ssidDialogOpen, setSsidDialogOpen] = useState(false);
  const [ssidDraft, setSsidDraft] = useState('');
  const [locationDialogOpen, setLocationDialogOpen] = useState(false);
  const [locationDraft, setLocationDraft] = useState('');
  const [vendorDialogOpen, setVendorDialogOpen] = useState(false);
  const [vendorDraft, setVendorDraft] = useState('');
  const [modelDialogOpen, setModelDialogOpen] = useState(false);
  const [modelDraft, setModelDraft] = useState('');
  const [firmwareDialogOpen, setFirmwareDialogOpen] = useState(false);
  const [firmwareDraft, setFirmwareDraft] = useState('');
  const [warrantyDialogOpen, setWarrantyDialogOpen] = useState(false);
  const [warrantyDraft, setWarrantyDraft] = useState('');
  const typeOptions = useMemo(
    () => DEVICE_TYPE_OPTIONS.map(formatDeviceTypeLabel).sort((a, b) => a.localeCompare(b)),
    [],
  );

  const closeActionsMenu = () => {
    setActionsMenuAnchor(null);
  };

  const openTypeDialog = () => {
    closeActionsMenu();
    setTypeDraft('Unknown');
    setTypeDialogOpen(true);
  };

  const applyBulkType = () => {
    const next = formatDeviceTypeLabel(typeDraft);
    if (!next) return;
    setTypeDialogOpen(false);
    onBulkSetType(next);
  };

  const openAttachKindDialog = () => {
    closeActionsMenu();
    setAttachKindDraft('Ethernet');
    setAttachKindDialogOpen(true);
  };

  const openUplinkDialog = () => {
    closeActionsMenu();
    onLoadAttachmentParents?.();
    setUplinkDraft(null);
    setUplinkDialogOpen(true);
  };

  const openSsidDialog = () => {
    closeActionsMenu();
    setSsidDraft('');
    setSsidDialogOpen(true);
  };

  const openLocationDialog = () => {
    closeActionsMenu();
    setLocationDraft('');
    setLocationDialogOpen(true);
  };

  const openVendorDialog = () => {
    closeActionsMenu();
    setVendorDraft('');
    setVendorDialogOpen(true);
  };

  const openModelDialog = () => {
    closeActionsMenu();
    setModelDraft('');
    setModelDialogOpen(true);
  };

  const openFirmwareDialog = () => {
    closeActionsMenu();
    setFirmwareDraft('');
    setFirmwareDialogOpen(true);
  };

  const openWarrantyDialog = () => {
    closeActionsMenu();
    setWarrantyDraft('');
    setWarrantyDialogOpen(true);
  };

  const applyBulkAttachKind = () => {
    setAttachKindDialogOpen(false);
    onBulkSetAttachmentKind(attachKindDraft);
  };

  const applyBulkUplink = () => {
    if (!uplinkDraft?.id) return;
    setUplinkDialogOpen(false);
    onBulkSetAttachmentParent(uplinkDraft.id);
  };

  const applyBulkSsid = () => {
    const ssid = ssidDraft.trim();
    if (!ssid) return;
    setSsidDialogOpen(false);
    onBulkSetSsid(ssid);
  };

  if (selectedCount === 0 && !busy) return null;

  return (
    <>
      {busy && progress && (
        <LinearProgress
          variant="determinate"
          value={(progress.current / progress.total) * 100}
          sx={{ position: 'fixed', bottom: 0, left: 0, right: 0, zIndex: 2001, height: 3 }}
        />
      )}
      <Fade in={selectedCount > 0 || busy}>
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
            display: 'flex',
            alignItems: 'center',
            flexWrap: isMobile ? 'wrap' : 'nowrap',
            justifyContent: 'center',
            gap: isMobile ? 1.5 : 1,
            zIndex: 2000,
            maxWidth: isMobile ? 'calc(100% - 24px)' : '95vw',
          }}
        >
          <Typography variant="caption" fontWeight={900} sx={{ px: isMobile ? 0 : 1 }}>
            {busy && progress
              ? `${progress.current} / ${progress.total}`
              : `${selectedCount} SELECTED`}
          </Typography>
          <Divider orientation="vertical" flexItem sx={{ bgcolor: 'grey.700', mx: 0.5, display: { xs: 'none', sm: 'block' } }} />

          {viewMode === 'active' && (
            <>
              <Button
                size="small"
                color="warning"
                startIcon={<DeleteIcon />}
                disabled={busy}
                onClick={onArchive}
                sx={{ fontWeight: 800, color: 'warning.light' }}
              >
                Archive
              </Button>
              <Button
                size="small"
                startIcon={<NotificationsActiveIcon />}
                disabled={busy}
                onClick={onEnableAlerts}
                sx={{ color: 'white', fontWeight: 800 }}
              >
                Alerts On
              </Button>
              <Button
                size="small"
                startIcon={<NotificationsOffIcon />}
                disabled={busy}
                onClick={onDisableAlerts}
                sx={{ color: 'white', fontWeight: 800 }}
              >
                Alerts Off
              </Button>
            </>
          )}

          {viewMode === 'archive' && (
            <>
              <Button
                size="small"
                color="success"
                startIcon={<RestoreFromTrashIcon />}
                disabled={busy}
                onClick={onRestore}
                sx={{ fontWeight: 800 }}
              >
                Restore
              </Button>
              {isAdmin && (
                <Button
                  size="small"
                  color="error"
                  startIcon={<DeleteIcon />}
                  disabled={busy}
                  onClick={onPermanentDelete}
                  sx={{ fontWeight: 900 }}
                >
                  Remove from Fleet
                </Button>
              )}
            </>
          )}

          {viewMode === 'removed' && isAdmin && (
            <Button
              size="small"
              color="success"
              startIcon={<RestoreFromTrashIcon />}
              disabled={busy}
              onClick={onRestoreToFleet}
              sx={{ fontWeight: 800 }}
            >
              Restore to Fleet
            </Button>
          )}

          {viewMode !== 'removed' && (
            <>
              <Button
                size="small"
                endIcon={<ArrowDropDownIcon />}
                startIcon={<BuildOutlinedIcon />}
                disabled={busy}
                onClick={(e) => setActionsMenuAnchor(e.currentTarget)}
                sx={{ color: 'white', fontWeight: 800 }}
              >
                Actions
              </Button>
              <Menu
                anchorEl={actionsMenuAnchor}
                open={Boolean(actionsMenuAnchor)}
                onClose={closeActionsMenu}
                {...menuAnchorUp}
                slotProps={menuSlotProps}
              >
                <MenuItem
                  onClick={() => {
                    closeActionsMenu();
                    onBulkPing();
                  }}
                >
                  <NetworkPingIcon fontSize="small" sx={{ mr: 1, opacity: 0.8 }} />
                  Ping selected
                </MenuItem>
                <MenuItem
                  onClick={() => {
                    closeActionsMenu();
                    onBulkDeepScan();
                  }}
                >
                  <RadarIcon fontSize="small" sx={{ mr: 1, opacity: 0.8 }} />
                  Queue deep scan
                </MenuItem>
                <MenuItem onClick={openTypeDialog}>
                  <CategoryOutlinedIcon fontSize="small" sx={{ mr: 1, opacity: 0.8 }} />
                  Set device type...
                </MenuItem>
                <Divider />
                <MenuItem onClick={openAttachKindDialog}>
                  Set network mode...
                </MenuItem>
                <MenuItem onClick={openUplinkDialog}>
                  Set uplink...
                </MenuItem>
                <MenuItem
                  onClick={() => {
                    closeActionsMenu();
                    onBulkClearAttachmentParent();
                  }}
                >
                  Clear uplink
                </MenuItem>
                <MenuItem onClick={openSsidDialog}>
                  Set SSID (Wi-Fi)...
                </MenuItem>
                <MenuItem
                  onClick={() => {
                    closeActionsMenu();
                    onBulkClearSsid();
                  }}
                >
                  Clear SSID
                </MenuItem>
                <Divider />
                <MenuItem onClick={openLocationDialog}>
                  Set location...
                </MenuItem>
                <MenuItem onClick={openVendorDialog}>
                  Set vendor...
                </MenuItem>
                <MenuItem onClick={openModelDialog}>
                  Set model...
                </MenuItem>
                {canEditAssets && (
                  <>
                    <MenuItem onClick={openFirmwareDialog}>
                      Set firmware...
                    </MenuItem>
                    <MenuItem onClick={openWarrantyDialog}>
                      Set warranty date...
                    </MenuItem>
                  </>
                )}
              </Menu>
            </>
          )}

          {canEditAssets && viewMode !== 'removed' && (
            <>
              <Button
                size="small"
                endIcon={<ArrowDropDownIcon />}
                startIcon={<Inventory2OutlinedIcon />}
                disabled={busy}
                onClick={(e) => setAssetMenuAnchor(e.currentTarget)}
                sx={{ color: 'white', fontWeight: 800 }}
              >
                Assets
              </Button>
              <Menu
                anchorEl={assetMenuAnchor}
                open={Boolean(assetMenuAnchor)}
                onClose={() => setAssetMenuAnchor(null)}
                {...menuAnchorUp}
                slotProps={menuSlotProps}
              >
                <MenuItem
                  onClick={() => {
                    setAssetMenuAnchor(null);
                    onClearWarranty();
                  }}
                >
                  Clear warranty
                </MenuItem>
                <MenuItem
                  onClick={() => {
                    setAssetMenuAnchor(null);
                    onClearAssetTag();
                  }}
                >
                  Clear asset tag
                </MenuItem>
                <MenuItem
                  onClick={() => {
                    setAssetMenuAnchor(null);
                    onClearManualAssetFields();
                  }}
                >
                  Clear manual serial & firmware
                </MenuItem>
              </Menu>
            </>
          )}

          <Button
            size="small"
            startIcon={<FileDownloadIcon />}
            disabled={busy}
            onClick={onExportSelected}
            sx={{ color: 'white', fontWeight: 800 }}
          >
            Export view
          </Button>

          {viewMode !== 'removed' && (
            <Button
              size="small"
              startIcon={<FileDownloadIcon />}
              disabled={busy}
              onClick={onExportAssetSelected}
              sx={{ color: 'white', fontWeight: 800 }}
            >
              Asset fields
            </Button>
          )}

          <IconButton size="small" disabled={busy} onClick={onClear} sx={{ color: 'white', opacity: 0.6 }}>
            <CloseIcon fontSize="small" />
          </IconButton>
        </Paper>
      </Fade>

      <Dialog open={typeDialogOpen} onClose={() => setTypeDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set device type</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Apply to {selectedCount} selected device{selectedCount === 1 ? '' : 's'}. Type to search.
          </Typography>
          <Autocomplete
            freeSolo
            options={typeOptions}
            value={typeDraft}
            openOnFocus
            autoHighlight
            groupBy={(option) => deviceTypeCategory(option)}
            filterOptions={(opts, state) => filterDeviceTypeOptions(opts, state.inputValue)}
            getOptionLabel={(option) => formatDeviceTypeLabel(option)}
            onChange={(_, next) => setTypeDraft(formatDeviceTypeLabel(next ?? 'Unknown'))}
            onInputChange={(_, next, reason) => {
              if (reason === 'input') setTypeDraft(next);
            }}
            ListboxProps={{ style: { maxHeight: 280 } }}
            renderInput={(params) => (
              <TextField
                {...params}
                autoFocus
                label="Device type"
                placeholder="Search (receiver, laptop, switch...)"
              />
            )}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setTypeDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={applyBulkType} disabled={!typeDraft.trim()}>
            Apply type
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={attachKindDialogOpen} onClose={() => setAttachKindDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set network mode</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Apply to {selectedCount} selected device{selectedCount === 1 ? '' : 's'}. Port / SSID and uplink are kept.
          </Typography>
          <TextField
            select
            fullWidth
            label="Mode"
            value={attachKindDraft}
            onChange={(e) => setAttachKindDraft(e.target.value)}
          >
            <MenuItem value="Ethernet">Ethernet</MenuItem>
            <MenuItem value="WiFi">Wi-Fi</MenuItem>
            <MenuItem value="Fiber">Fiber</MenuItem>
            <MenuItem value="Other">Other</MenuItem>
            <MenuItem value="Unknown">Unknown</MenuItem>
          </TextField>
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setAttachKindDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={applyBulkAttachKind}>
            Apply kind
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={uplinkDialogOpen} onClose={() => setUplinkDialogOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle sx={{ fontWeight: 800 }}>Set uplink</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            {uplinkMixedSites
              ? 'Your selection spans multiple sites. Filter to one site or select devices from a single node before setting uplink.'
              : uplinkScopedSiteLabel
                ? `Assign the same parent switch or AP on ${uplinkScopedSiteLabel} to ${selectedCount} selected device${selectedCount === 1 ? '' : 's'}. Mode and port / SSID are kept.`
                : `Assign the same parent switch or AP to ${selectedCount} selected device${selectedCount === 1 ? '' : 's'}. Mode and port / SSID are kept.${selectedCount > 0 ? ' Site names prefix each option when viewing the full fleet.' : ''}`}
          </Typography>
          <Autocomplete
            options={attachmentParentOptions}
            getOptionLabel={(opt) => opt.label}
            isOptionEqualToValue={(a, b) => a.id === b.id}
            value={uplinkDraft}
            disabled={uplinkMixedSites}
            onOpen={() => onLoadAttachmentParents?.()}
            onChange={(_, next) => setUplinkDraft(next)}
            noOptionsText={
              uplinkMixedSites
                ? 'Select devices from one site first'
                : attachmentParentOptions.length === 0
                  ? 'Loading switches and APs...'
                  : 'No matching uplink devices'
            }
            renderInput={(params) => (
              <TextField {...params} autoFocus label="Uplink device" placeholder="Search switches and APs..." />
            )}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setUplinkDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={applyBulkUplink} disabled={uplinkMixedSites || !uplinkDraft?.id}>
            Apply uplink
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={ssidDialogOpen} onClose={() => setSsidDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set SSID (Wi-Fi)</DialogTitle>
        <DialogContent>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
            Apply to Wi-Fi devices in your selection. Ethernet uplink and mode are kept.
          </Typography>
          <TextField
            autoFocus
            fullWidth
            label="SSID"
            placeholder="e.g. Office-WiFi"
            value={ssidDraft}
            inputProps={{ maxLength: 64 }}
            onChange={(e) => setSsidDraft(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === 'Enter') {
                e.preventDefault();
                applyBulkSsid();
              }
            }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setSsidDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={applyBulkSsid} disabled={!ssidDraft.trim()}>
            Apply SSID
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={locationDialogOpen} onClose={() => setLocationDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set location</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            fullWidth
            label="Location"
            placeholder="e.g. Living Room rack"
            value={locationDraft}
            onChange={(e) => setLocationDraft(e.target.value)}
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setLocationDialogOpen(false)}>Cancel</Button>
          <Button
            variant="contained"
            onClick={() => {
              setLocationDialogOpen(false);
              onBulkSetLocation(locationDraft.trim());
            }}
            disabled={!locationDraft.trim()}
          >
            Apply location
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={vendorDialogOpen} onClose={() => setVendorDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set vendor</DialogTitle>
        <DialogContent>
          <TextField autoFocus fullWidth label="Vendor" value={vendorDraft} onChange={(e) => setVendorDraft(e.target.value)} sx={{ mt: 1 }} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setVendorDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={() => { setVendorDialogOpen(false); onBulkSetVendor(vendorDraft.trim()); }} disabled={!vendorDraft.trim()}>
            Apply vendor
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={modelDialogOpen} onClose={() => setModelDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set model</DialogTitle>
        <DialogContent>
          <TextField autoFocus fullWidth label="Model" value={modelDraft} onChange={(e) => setModelDraft(e.target.value)} sx={{ mt: 1 }} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setModelDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={() => { setModelDialogOpen(false); onBulkSetModel(modelDraft.trim()); }} disabled={!modelDraft.trim()}>
            Apply model
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={firmwareDialogOpen} onClose={() => setFirmwareDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set firmware</DialogTitle>
        <DialogContent>
          <TextField autoFocus fullWidth label="Firmware version" value={firmwareDraft} onChange={(e) => setFirmwareDraft(e.target.value)} sx={{ mt: 1 }} />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setFirmwareDialogOpen(false)}>Cancel</Button>
          <Button variant="contained" onClick={() => { setFirmwareDialogOpen(false); onBulkSetFirmware(firmwareDraft.trim()); }} disabled={!firmwareDraft.trim()}>
            Apply firmware
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={warrantyDialogOpen} onClose={() => setWarrantyDialogOpen(false)} fullWidth maxWidth="xs">
        <DialogTitle sx={{ fontWeight: 800 }}>Set warranty date</DialogTitle>
        <DialogContent>
          <TextField
            autoFocus
            fullWidth
            type="date"
            label="Warranty expires"
            value={warrantyDraft}
            onChange={(e) => setWarrantyDraft(e.target.value)}
            InputLabelProps={{ shrink: true }}
            sx={{ mt: 1 }}
          />
        </DialogContent>
        <DialogActions sx={{ px: 3, pb: 2 }}>
          <Button onClick={() => setWarrantyDialogOpen(false)}>Cancel</Button>
          <Button
            variant="contained"
            onClick={() => {
              if (!warrantyDraft) return;
              setWarrantyDialogOpen(false);
              onBulkSetWarranty(new Date(`${warrantyDraft}T12:00:00Z`).toISOString());
            }}
            disabled={!warrantyDraft}
          >
            Apply warranty
          </Button>
        </DialogActions>
      </Dialog>
    </>
  );
}
