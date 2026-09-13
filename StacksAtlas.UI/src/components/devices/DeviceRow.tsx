import type { ReactNode } from "react";
import {
  Box,
  Typography,
  TableCell,
  TableRow,
  Tooltip,
  Chip,
  Checkbox,
  alpha,
  useTheme
} from "@mui/material";
import ShieldIcon from '@mui/icons-material/Shield';
import SettingsEthernetIcon from '@mui/icons-material/SettingsEthernet';
import WifiIcon from '@mui/icons-material/Wifi';
import CableIcon from '@mui/icons-material/Cable';
import DeviceHubIcon from '@mui/icons-material/DeviceHub';
import { getVendorIcon } from "../../utils/vendorUtils";
import { getDeviceIcon, getPortServiceName, isDanteDevice, DanteBadge } from "../../utils/deviceUtils";
import { summarizeSecurityIssuesForTooltip } from "../../utils/securityFindings";
import type { Device } from "../../models/Device";
import type { OpenAvcLinkSummary } from "../../services/apiService";
import type { ColumnKey } from "./types";

// -------------------------------------------------------------
//  Sub-Components (Cell Renderers)
// -------------------------------------------------------------
const UptimeBar = ({ percent }: { percent: number }) => (
  <Tooltip title={`${percent}% availability`} arrow>
    <Box sx={{ width: '100%' }}>
      <Box sx={{ height: 4, width: '100%', bgcolor: 'action.focus', borderRadius: 2, overflow: 'hidden' }}>
        <Box
          sx={{
            height: '100%',
            width: `${percent}%`,
            bgcolor: percent > 95 ? 'success.main' : percent > 80 ? 'warning.main' : 'error.main',
            transition: 'width 1s ease-in-out'
          }}
        />
      </Box>
    </Box>
  </Tooltip>
);

const PortBadge = ({ port }: { port: number }) => (
  <Tooltip
    key={port}
    title={getPortServiceName(port)}
    arrow
    placement="top"
  >
    <Box
      sx={{
        fontFamily: "monospace",
        bgcolor: 'action.selected',
        color: 'text.primary',
        border: '1px solid',
        borderColor: 'divider',
        px: 0.6,
        borderRadius: 1,
        fontSize: '0.75rem',
        fontWeight: 600,
        cursor: 'help',
        '&:hover': {
          bgcolor: 'action.hover',
          borderColor: 'primary.main'
        }
      }}
    >
      {port}
    </Box>
  </Tooltip>
);

const StatusDot = ({ status }: { status: "online" | "offline" }) => (
  <Box sx={{ display: "flex", alignItems: "center", gap: 1, whiteSpace: "nowrap", pl: 0.5 }}>
    <Box
      sx={{
        width: 9,
        height: 9,
        flexShrink: 0,
        borderRadius: "50%",
        bgcolor: status === "online" ? "success.main" : "error.main",
        boxShadow: status === "online" ? '0 0 6px rgba(76, 175, 80, 0.4)' : 'none',
        animation: status === "online" ? "pulse 2s infinite" : "none",
        "@keyframes pulse": {
          "0%": { transform: "scale(1)", opacity: 1 },
          "50%": { transform: "scale(1.4)", opacity: 0.5 },
          "100%": { transform: "scale(1)", opacity: 1 },
        },
      }}
    />
    <Typography
      variant="body2"
      sx={{
        fontWeight: 600,
        fontSize: "0.8rem",
        color: status === "online" ? "success.main" : "error.main",
      }}
    >
      {status === "online" ? "Online" : "Offline"}
    </Typography>
  </Box>
);

const SecurityShield = ({ grade, issues }: { grade?: number; issues?: string[] }) => {
  const theme = useTheme();

  // 0=Green, 1=Yellow, 2=Red
  const colors = [
    theme.palette.success.main,
    theme.palette.warning.main,
    theme.palette.error.main
  ];

  const labels = ["HEALTHY", "WARNING", "CRITICAL"];
  const color = colors[grade ?? 0] || colors[0];
  const label = labels[grade ?? 0] || labels[0];
  const { lines, overflow } = summarizeSecurityIssuesForTooltip(issues);

  return (
    <Tooltip
      arrow
      slotProps={{ tooltip: { sx: { maxWidth: 280 } } }}
      title={
        <Box sx={{ p: 0.5 }}>
          <Typography variant="caption" fontWeight={900} display="block" sx={{ mb: lines.length ? 0.5 : 0 }}>
            {label}
          </Typography>
          {lines.length > 0 ? (
            <>
              {lines.map((line, idx) => (
                <Typography
                  key={idx}
                  variant="caption"
                  display="block"
                  sx={{ fontSize: '0.7rem', lineHeight: 1.35 }}
                >
                  {line}
                </Typography>
              ))}
              {overflow > 0 && (
                <Typography
                  variant="caption"
                  display="block"
                  sx={{ fontSize: '0.65rem', opacity: 0.75, mt: 0.5 }}
                >
                  +{overflow} more  -  open Security tab for details
                </Typography>
              )}
            </>
          ) : (
            <Typography variant="caption">No security risks detected.</Typography>
          )}
        </Box>
      }
    >
      <Box sx={{ display: 'flex', alignItems: 'center', cursor: 'help' }}>
        <ShieldIcon sx={{ color, fontSize: 18, filter: grade && grade > 0 ? `drop-shadow(0 0 4px ${alpha(color, 0.4)})` : 'none' }} />
      </Box>
    </Tooltip>
  );
};

// -------------------------------------------------------------
//  Helper Utility Functions
// -------------------------------------------------------------
function formatRelativeTime(dateString: string): string {
  const now = new Date();
  const then = new Date(dateString);
  const diff = (now.getTime() - then.getTime()) / 1000; // seconds

  if (diff < 5) return "Just now";
  if (diff < 60) return `${Math.floor(diff)}s ago`;

  const minutes = diff / 60;
  if (minutes < 60) return `${Math.floor(minutes)}m ago`;

  const hours = minutes / 60;
  if (hours < 24) return `${Math.floor(hours)}h ago`;

  const days = hours / 24;
  if (days < 7) return `${Math.floor(days)}d ago`;

  const weeks = days / 7;
  if (weeks < 4) return `${Math.floor(weeks)}w ago`;

  const months = days / 30;
  if (months < 12) return `${Math.floor(months)}mo ago`;

  const years = days / 365;
  return `${Math.floor(years)}y ago`;
}

// -------------------------------------------------------------
//  DeviceRow Component
// -------------------------------------------------------------
interface DeviceRowProps {
  device: Device;
  columnOrder: ColumnKey[];
  visibility: Record<ColumnKey, boolean>;
  columnWidths: Record<ColumnKey, number>;
  onDeviceClick: (device: Device) => void;
  ptpStatus: any;
  nodesMap?: Record<string, string>;
  openAvcLinksMap?: Record<string, OpenAvcLinkSummary>;
  openAvcIntegrationStatus?: string;
  selectionEnabled?: boolean;
  selected?: boolean;
  onToggleSelect?: (id: string) => void;
  /** Parent device id → display name (current page + known inventory). */
  parentNameById?: Record<string, string>;
}

export function DeviceRow({
  device,
  columnOrder,
  visibility,
  columnWidths,
  onDeviceClick,
  ptpStatus,
  nodesMap,
  openAvcLinksMap,
  openAvcIntegrationStatus,
  selectionEnabled = false,
  selected = false,
  onToggleSelect,
  parentNameById,
}: DeviceRowProps) {
  const theme = useTheme();

  const lastSeenVal = device.lastSeen || (device as any).last_seen;
  const uptime = device.uptimePercent ?? (device as any).uptime_percent ?? 0;
  const stability = device.stabilityScore ?? (device as any).stability_score ?? 0;
  // Archived rows keep last-known status in DB; never show them as live online.
  const displayStatus = device.isDeleted ? "offline" : device.status;

  const isNew = lastSeenVal
    ? Date.now() - new Date(lastSeenVal).getTime() < 5 * 60 * 1000
    : false;

  const isPtpMaster = ptpStatus && device.ipAddress === ptpStatus.sourceIp;
  const openAvcLink = openAvcLinksMap?.[device.id.toLowerCase()];
  const openAvcChipColor =
    openAvcIntegrationStatus === 'auth_failed'
      ? 'error'
      : openAvcIntegrationStatus === 'offline' || openAvcIntegrationStatus === 'disabled'
        ? 'default'
        : 'info';

  return (
    <TableRow
      hover
      onClick={() => onDeviceClick(device)}
      sx={{
        cursor: "pointer",
        opacity: displayStatus === "offline" ? 0.7 : 1,
        backgroundColor: isNew && !device.isDeleted ? "rgba(76,175,80,0.04)" :
          isPtpMaster ? alpha(theme.palette.info.main, 0.08) : "inherit",
        borderLeft: isPtpMaster ? `4px solid ${theme.palette.info.main}` : 'none',
        borderBottom: "1px solid rgba(255,255,255,0.08)",
        "&:last-child td, &:last-child th": { border: 0 },
      }}
    >
      {selectionEnabled && (
        <TableCell padding="checkbox" onClick={(e) => e.stopPropagation()} sx={{ pr: 0.5, width: 44 }}>
          <Checkbox
            size="small"
            checked={selected}
            onChange={() => onToggleSelect?.(device.id)}
          />
        </TableCell>
      )}
      {columnOrder.map((key) => {
        if (!visibility[key]) return null;

        switch (key) {
          case "name":
            return (
              <TableCell key={key} sx={{ width: columnWidths.name, py: 1.1 }}>
                <Box sx={{ display: "flex", alignItems: "center", gap: 1.25 }}>
                  <Box sx={{ color: "text.secondary", opacity: 0.8, flexShrink: 0 }}>
                    {getDeviceIcon(device.type, device.model, device.name)}
                  </Box>
                  <Box sx={{ overflow: 'hidden', minWidth: 0 }}>
                    <Typography variant="body2" sx={{ fontWeight: 600, whiteSpace: 'nowrap', textOverflow: 'ellipsis', overflow: 'hidden' }}>
                      {device.name || device.ipAddress}
                    </Typography>
                    <Typography variant="caption" sx={{ opacity: 0.5, display: 'block', fontSize: '0.65rem', whiteSpace: 'nowrap', textOverflow: 'ellipsis', overflow: 'hidden' }}>
                      {device.model || device.vendor || "Unknown"}
                    </Typography>
                    {openAvcLink && (
                      <Chip
                        size="small"
                        label={openAvcLink.driverName || 'OpenAVC'}
                        color={openAvcChipColor}
                        variant={openAvcChipColor === 'default' ? 'outlined' : 'outlined'}
                        sx={{
                          mt: 0.5,
                          height: 18,
                          fontSize: '0.6rem',
                          fontWeight: 800,
                          opacity: openAvcChipColor === 'default' ? 0.65 : 1,
                        }}
                      />
                    )}
                  </Box>
                </Box>
              </TableCell>
            );

          case "ipAddress":
            return (
              <TableCell key={key} sx={{ width: columnWidths.ipAddress, fontFamily: 'monospace', fontSize: '0.8rem' }}>
                {device.ipAddress}
              </TableCell>
            );

          case "macAddress":
            return (
              <TableCell key={key} sx={{ width: columnWidths.macAddress, fontFamily: 'monospace', fontSize: '0.8rem', opacity: 0.8 }}>
                {device.macAddress}
              </TableCell>
            );

          case "hostname":
            return (
              <TableCell key={key} sx={{
                width: columnWidths.hostname,
                fontFamily: 'monospace',
                fontSize: '0.75rem',
                maxWidth: columnWidths.hostname,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap'
              }}>
                {device.hostname || " - "}
              </TableCell>
            );

          case "nodeId":
            return (
              <TableCell key={key} sx={{ width: columnWidths.nodeId }}>
                <Chip 
                  label={
                    device.nodeId
                      ? (nodesMap?.[device.nodeId] || device.nodeId).toUpperCase()
                      : "LOCAL"
                  } 
                  size="small" 
                  sx={{ 
                    fontSize: '0.65rem', 
                    fontWeight: 900,
                    bgcolor: device.nodeId ? alpha(theme.palette.primary.main, 0.1) : alpha(theme.palette.success.main, 0.1),
                    color: device.nodeId ? 'primary.main' : 'success.main',
                    border: '1px solid',
                    borderColor: 'divider'
                  }} 
                />
              </TableCell>
            );

          case "vendor":
            return (
              <TableCell key={key} sx={{ width: columnWidths.vendor, textTransform: "capitalize", fontSize: '0.85rem' }}>
                <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                  {getVendorIcon(device.vendor)}
                  <Typography variant="body2" sx={{ fontSize: 'inherit', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                    {device.vendor || " - "}
                  </Typography>
                </Box>
              </TableCell>
            );

          case "model":
            return (
              <TableCell key={key} sx={{ width: columnWidths.model, opacity: 0.8 }}>{device.model}</TableCell>
            );

          case "type":
            return (
              <TableCell key={key} sx={{ width: columnWidths.type }}>
                <Box sx={{ display: 'flex', alignItems: 'center' }}>
                  <Typography
                    variant="body2"
                    sx={{ textTransform: "capitalize", opacity: 0.8 }}
                  >
                    {device.type}
                  </Typography>
                  {isDanteDevice(device) && <DanteBadge />}
                </Box>
              </TableCell>
            );

          case "location":
            return (
              <TableCell key={key} sx={{ width: columnWidths.location }}>{device.location || " - "}</TableCell>
            );

          case "status":
            return (
              <TableCell key={key} sx={{ width: columnWidths.status, px: 1.5, pl: 2 }}>
                <StatusDot status={displayStatus as "online" | "offline"} />
              </TableCell>
            );

          case "security":
            return (
              <TableCell key={key} sx={{ width: columnWidths.security, px: 1 }}>
                <SecurityShield grade={device.securityGrade} issues={device.securityIssues} />
              </TableCell>
            );

          case "uptimePercent":
            return (
              <TableCell key={key} sx={{ width: columnWidths.uptimePercent }}>
                <UptimeBar percent={uptime} />
              </TableCell>
            );

          case "stabilityScore":
            return (
              <TableCell key={key} sx={{ width: columnWidths.stabilityScore, textAlign: 'center' }}>
                <Typography variant="body2" sx={{
                  fontWeight: 700,
                  color: stability >= 90 ? 'success.main' : 'warning.main'
                }}>
                  {stability}%
                </Typography>
              </TableCell>
            );

          case "lastSeen":
            return (
              <TableCell key={key} sx={{ width: columnWidths.lastSeen, whiteSpace: "nowrap" }}>
                {lastSeenVal ? (
                  <Tooltip title={new Date(lastSeenVal).toLocaleString()}>
                    <Typography variant="body2" sx={{ fontSize: '0.85rem' }}>
                      {formatRelativeTime(lastSeenVal)}
                    </Typography>
                  </Tooltip>
                ) : (
                  <Typography variant="body2" sx={{ opacity: 0.3 }}> - </Typography>
                )}
              </TableCell>
            );

          case "openPorts":
            return (
              <TableCell key={key} sx={{ width: columnWidths.openPorts }}>
                <Box sx={{ display: 'flex', gap: 0.5, alignItems: 'center' }}>
                  {(device.openPorts || []).slice(0, 2).map(p => <PortBadge key={p} port={p} />)}
                  {(device.openPorts || []).length > 2 && (
                    <Typography variant="caption" sx={{ fontWeight: 700, ml: 0.5 }}>
                      +{(device.openPorts || []).length - 2}
                    </Typography>
                  )}
                </Box>
              </TableCell>
            );

          case "managedBy":
            return (
              <TableCell key={key} sx={{ width: columnWidths.managedBy }}>
                {device.managedByUsername ? (
                  <Chip
                    label={device.managedByUsername.toUpperCase()}
                    size="small"
                    variant="outlined"
                    sx={{ fontWeight: 800, fontSize: '0.65rem', borderWeight: 2 }}
                  />
                ) : (
                  <Typography variant="caption" sx={{ opacity: 0.3 }}>UNASSIGNED</Typography>
                )}
              </TableCell>
            );

          case "client":
            return (
              <TableCell key={key} sx={{ width: columnWidths.client, fontSize: '0.85rem' }}>
                {device.client || " - "}
              </TableCell>
            );

          case "building":
            return (
              <TableCell key={key} sx={{ width: columnWidths.building, fontSize: '0.85rem' }}>
                {device.building || " - "}
              </TableCell>
            );

          case "room":
            return (
              <TableCell key={key} sx={{ width: columnWidths.room, fontSize: '0.85rem' }}>
                {device.room || " - "}
              </TableCell>
            );

          case "discoveryVlanTag":
            return (
              <TableCell key={key} sx={{ width: columnWidths.discoveryVlanTag, fontSize: '0.85rem' }}>
                {device.discoveryVlanTag ? (
                  <Chip label={device.discoveryVlanTag} size="small" variant="outlined" sx={{ fontWeight: 800, fontSize: '0.65rem' }} />
                ) : (
                  <Typography variant="caption" sx={{ opacity: 0.35 }}> - </Typography>
                )}
              </TableCell>
            );

          case "serialNumber":
            return (
              <TableCell key={key} sx={{ width: columnWidths.serialNumber, fontFamily: 'monospace', fontSize: '0.8rem' }}>
                {device.serialNumber || " - "}
              </TableCell>
            );

          case "firmwareVersion":
            return (
              <TableCell key={key} sx={{ width: columnWidths.firmwareVersion, fontFamily: 'monospace', fontSize: '0.8rem' }}>
                {device.firmwareVersion || " - "}
              </TableCell>
            );

          case "assetTag":
            return (
              <TableCell key={key} sx={{ width: columnWidths.assetTag, fontSize: '0.8rem' }}>
                {device.assetTag || " - "}
              </TableCell>
            );

          case "warrantyExpiresUtc":
            return (
              <TableCell key={key} sx={{ width: columnWidths.warrantyExpiresUtc, fontFamily: 'monospace', fontSize: '0.8rem' }}>
                {device.warrantyExpiresUtc ? device.warrantyExpiresUtc.slice(0, 10) : " - "}
              </TableCell>
            );

          case "attachmentKind": {
            const kind = device.attachmentKind || "Unknown";
            const iconSx = { fontSize: 18, opacity: 0.9 };
            let icon: ReactNode = null;
            let title = kind;
            if (kind === "Ethernet") {
              icon = <SettingsEthernetIcon sx={{ ...iconSx, color: "primary.main" }} />;
              title = "Ethernet";
            } else if (kind === "WiFi") {
              icon = <WifiIcon sx={{ ...iconSx, color: "info.main" }} />;
              title = "Wi-Fi";
            } else if (kind === "Fiber") {
              icon = <CableIcon sx={{ ...iconSx, color: "secondary.main" }} />;
              title = "Fiber";
            } else if (kind === "Other") {
              icon = <DeviceHubIcon sx={{ ...iconSx, color: "text.secondary" }} />;
              title = "Other";
            }
            return (
              <TableCell key={key} sx={{ width: columnWidths.attachmentKind }}>
                {icon ? (
                  <Tooltip title={title}>
                    <Box sx={{ display: "flex", alignItems: "center" }}>{icon}</Box>
                  </Tooltip>
                ) : (
                  <Typography variant="caption" sx={{ opacity: 0.35 }}>-</Typography>
                )}
              </TableCell>
            );
          }

          case "attachmentPort":
            return (
              <TableCell
                key={key}
                sx={{
                  width: columnWidths.attachmentPort,
                  fontFamily: "monospace",
                  fontSize: "0.8rem",
                }}
              >
                {device.attachmentPort || (
                  <Typography variant="caption" sx={{ opacity: 0.35 }}> - </Typography>
                )}
              </TableCell>
            );

          case "attachmentParent": {
            const parentId = device.attachmentParentDeviceId;
            const parentName = parentId
              ? device.attachmentParentName
                || parentNameById?.[parentId]
                || device.attachmentParentMac
                || parentId.slice(0, 8)
              : null;
            return (
              <TableCell key={key} sx={{ width: columnWidths.attachmentParent, fontSize: "0.85rem" }}>
                {parentName ? (
                  <Typography
                    variant="body2"
                    sx={{
                      fontSize: "inherit",
                      overflow: "hidden",
                      textOverflow: "ellipsis",
                      whiteSpace: "nowrap",
                    }}
                  >
                    {parentName}
                  </Typography>
                ) : (
                  <Typography variant="caption" sx={{ opacity: 0.35 }}> - </Typography>
                )}
              </TableCell>
            );
          }

          default:
            return null;
        }
      })}
    </TableRow>
  );
}
