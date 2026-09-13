import { lazy, Suspense } from "react";
import {
  Grid, Paper, Typography, Box, List, ListItem,
  LinearProgress, Chip, IconButton, InputBase, Snackbar,
  Avatar, Badge, Button, alpha, Divider, Tooltip as MuiTooltip,
  Stack, TablePagination
} from "@mui/material";
import {
  Search,
  Lan, Speed, ContentCopy, Visibility, Delete,
  Radar as RadarIcon, Sensors as SensorsIcon,
  NotificationsActive as AlertIcon
} from "@mui/icons-material";

import {
  AreaChart, Area, XAxis, YAxis, ResponsiveContainer, Tooltip, CartesianGrid, ReferenceLine
} from "recharts";
import { PageHeader } from "../../components/pageheader";
import { DeviceDrawer } from "../../components/DeviceDrawer";
import { getDeviceIcon } from "../../utils/deviceUtils";
import { APP_VERSION } from "../../constants/version";

import { PortableUpgradeBanner } from "../../components/PortableUpgradeBanner";
import { useNodeDashboard } from "./useNodeDashboard";

const TopologyGraph = lazy(() =>
  import("../../components/network/TopologyGraph").then((m) => ({ default: m.TopologyGraph }))
);

export function NodeDashboardDesktop() {
  const {
    theme,
    auth,
    navigate,
    devices,
    maintenance,
    sweeps,
    searchTerm,
    setSearchTerm,
    selectedDevice,
    setSelectedDevice,
    users,
    ptpStatus,
    isPortable,
    page,
    setPage,
    rowsPerPage,
    setRowsPerPage,
    summary,
    showUndo,
    setShowUndo,
    toastMessage,
    toastOpen,
    setToastOpen,
    showToast,
    handleIgnoreNode,
    handleUndo,
    filteredDevices,
    recentAlertCount,
  } = useNodeDashboard();

  const StabilityIntel = () => (
    <Box sx={{ p: 1, maxWidth: 220 }}>
      <Typography variant="caption" sx={{ fontWeight: 900, display: 'flex', alignItems: 'center', gap: 1, mb: 1, color: 'primary.main' }}>
        <Speed sx={{ fontSize: 14 }} /> STABILITY DIAGNOSTIC
      </Typography>
      <Stack spacing={1}>
        <Box>
          <Typography variant="caption" sx={{ display: 'block', fontWeight: 800, color: 'success.main', fontSize: '0.65rem' }}>90-100% • NOMINAL</Typography>
          <Typography variant="caption" sx={{ display: 'block', opacity: 0.7, fontSize: '0.6rem', lineHeight: 1.1 }}>Solid heartbeat. No packet loss detected.</Typography>
        </Box>
        <Box>
          <Typography variant="caption" sx={{ display: 'block', fontWeight: 800, color: 'warning.main', fontSize: '0.65rem' }}>70-89% • FLAPPING</Typography>
          <Typography variant="caption" sx={{ display: 'block', opacity: 0.7, fontSize: '0.6rem', lineHeight: 1.1 }}>Intermittent connectivity or high jitter.</Typography>
        </Box>
        <Box>
          <Typography variant="caption" sx={{ display: 'block', fontWeight: 800, color: 'error.main', fontSize: '0.65rem' }}>&lt; 70% • CRITICAL</Typography>
          <Typography variant="caption" sx={{ display: 'block', opacity: 0.7, fontSize: '0.6rem', lineHeight: 1.1 }}>Frequent dropouts. Power/Link failure likely.</Typography>
        </Box>
      </Stack>
    </Box>
  );

  return (
    <Box sx={{ pb: 5, pt: 2, px: { xs: 2, md: 4 }, bgcolor: "background.default", minHeight: "100vh" }}>

      {isPortable && (
        <PortableUpgradeBanner
          variant="dashboard"
          onOpenSettings={() => navigate('/settings?tab=portable')}
        />
      )}

      <PageHeader
        title="CORE DASHBOARD"
        subtitle="ENGINE STATUS: NOMINAL"
        stats={[
          ...(!isPortable ? [{
            label: "Recent Alerts",
            value: recentAlertCount.toString(),
            color: recentAlertCount > 0 ? "error.main" : "text.secondary"
          }] : []),
          {
            label: "Next Cleanup",
            value: maintenance?.nextCleanup ? new Date(maintenance.nextCleanup).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : "N/A"
          }
        ]}
      />
      <Grid container spacing={3}>
        {/* NETWORK VISUALIZER */}
        <Grid item xs={12} md={8}>
          <Paper
            variant="outlined"
            sx={{
              p: 3,
              bgcolor: "background.paper",
              borderRadius: 3,
              height: 300,
              position: 'relative',
              overflow: 'hidden'
            }}
          >
            <Stack direction="row" justifyContent="space-between" mb={2}>
              <Box display="flex" alignItems="center" gap={1}>
                <SensorsIcon color="primary" sx={{ fontSize: 18 }} />
                <Typography variant="overline" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5 }}>
                  NETWORK STABILITY & AVAILABILITY
                </Typography>
              </Box>
              <Stack direction="row" gap={1}>
                <Chip label={`PEAK: ${Math.max(...sweeps.map(s => s.totalOnline), 0)}`} size="small" sx={{ fontWeight: 700, borderRadius: 1 }} />
                <Chip
                  icon={<RadarIcon sx={{ fontSize: '14px !important' }} />}
                  label="LIVE"
                  size="small"
                  color="primary"
                  sx={{
                    fontWeight: 900,
                    borderRadius: 1,
                    "@keyframes pulseOpacity": {
                      "0%": { opacity: 1 },
                      "50%": { opacity: 0.4 },
                      "100%": { opacity: 1 },
                    },
                    animation: "pulseOpacity 2s infinite ease-in-out"
                  }}
                />
              </Stack>
            </Stack>

            <ResponsiveContainer width="100%" height="80%">
              {/* Normalized margins since we removed the right-side text */}
              <AreaChart data={sweeps} margin={{ top: 10, right: 10, left: -20, bottom: 0 }}>
                <defs>
                  <linearGradient id="colorSweep" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor={theme.palette.primary.main} stopOpacity={0.3} />
                    <stop offset="95%" stopColor={theme.palette.primary.main} stopOpacity={0} />
                  </linearGradient>
                </defs>

                <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={theme.palette.divider} />

                <XAxis dataKey="start" hide />

                <YAxis
                  tick={{ fontSize: 10, fill: theme.palette.text.secondary }}
                  axisLine={false}
                  tickLine={false}
                  domain={[0, (dataMax: number) => Math.max(dataMax + 2, devices.length + 2)]}
                />

                <Tooltip
                  cursor={{ stroke: theme.palette.primary.main, strokeWidth: 1 }}
                  content={({ active, payload }) => {
                    if (active && payload && payload.length) {
                      const data = payload[0].payload;
                      const offline = Math.max(0, devices.length - data.totalOnline);
                      return (
                        <Box sx={{
                          bgcolor: 'background.paper',
                          p: 1.5,
                          border: `1px solid ${theme.palette.divider}`,
                          borderRadius: 2,
                          boxShadow: '0 4px 20px rgba(0,0,0,0.4)',
                        }}>
                          <Typography variant="caption" color="primary" sx={{ fontWeight: 900 }}>
                            {new Date(data.start).toLocaleTimeString()}
                          </Typography>
                          <Divider sx={{ my: 1 }} />
                          <Stack spacing={0.5}>
                            <Typography variant="caption" sx={{ display: 'flex', justifyContent: 'space-between', gap: 3 }}>
                              <span>ONLINE:</span> <strong>{data.totalOnline}</strong>
                            </Typography>
                            <Typography variant="caption" sx={{ display: 'flex', justifyContent: 'space-between', color: offline > 0 ? 'error.main' : 'text.secondary' }}>
                              <span>OFFLINE:</span> <strong>{offline}</strong>
                            </Typography>
                          </Stack>
                        </Box>
                      );
                    }
                    return null;
                  }}
                />

                {/* Reference Line - Now without the label for a cleaner look */}
                <ReferenceLine
                  y={devices.length}
                  stroke={theme.palette.success.main}
                  strokeDasharray="5 5"
                  strokeOpacity={0.5}
                />

                <Area
                  type="stepAfter"
                  dataKey="totalOnline"
                  stroke={theme.palette.primary.main}
                  strokeWidth={3}
                  fillOpacity={1}
                  fill="url(#colorSweep)"
                  isAnimationActive={false}
                  dot={(props: { cx?: number; cy?: number; payload?: { totalOnline: number }; index?: number }) => {
                    const { cx, cy, payload, index } = props;
                    if (cx == null || cy == null || payload == null || index == null) return null;
                    const prev = sweeps[index - 1];
                    if (prev && prev.totalOnline !== payload.totalOnline) {
                      return <circle cx={cx} cy={cy} r={3} fill={theme.palette.primary.main} stroke="white" />;
                    }
                    return null;
                  }}
                />
              </AreaChart>
            </ResponsiveContainer>
          </Paper>
        </Grid>

        {/* VENDOR DISTRIBUTION CHART */}
        <Grid item xs={12} md={4}>
          <Paper variant="outlined" sx={{ p: 3, borderRadius: 3, height: 300, display: 'flex', flexDirection: 'column' }}>
            <Typography variant="overline" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5, mb: 2 }}>
              VENDOR COMPOSITION
            </Typography>
            <Box sx={{
              flexGrow: 1,
              overflowY: 'auto',
              pr: 2,
              '&::-webkit-scrollbar': { width: 6 },
              '&::-webkit-scrollbar-thumb': { bgcolor: 'divider', borderRadius: 3 },
              '&::-webkit-scrollbar-track': { bgcolor: 'transparent' },
            }}>
              {(summary?.vendors || []).map(({ vendor, count }) => (
                <Box key={vendor} sx={{ display: 'flex', justifyContent: 'space-between', mb: 1, alignItems: 'center' }}>
                  <Typography variant="caption" fontWeight={700} noWrap sx={{ maxWidth: '70%' }}>{vendor}</Typography>
                  <Box sx={{ display: 'flex', alignItems: 'center', gap: 1 }}>
                    <Box sx={{ width: 60, height: 4, bgcolor: 'action.hover', borderRadius: 2, overflow: 'hidden' }}>
                      <Box sx={{ width: `${(count / (summary?.totalDevices || 1)) * 100}%`, height: '100%', bgcolor: 'primary.main' }} />
                    </Box>
                    <Typography variant="caption" fontWeight={900} color="text.secondary">{count}</Typography>
                  </Box>
                </Box>
              ))}
              {!summary?.vendors?.length && <Typography variant="caption" color="text.secondary">No data available</Typography>}
            </Box>

          </Paper>
        </Grid>

        {/* LIVE TOPOLOGY MAP */}
        <Grid item xs={12}>
            <Paper
            variant="outlined"
            sx={{
              p: 0,
              borderRadius: 3,
              height: { xs: 440, md: 600 },
              overflow: 'hidden',
              bgcolor: 'background.paper',
              display: 'flex',
              flexDirection: 'column',
            }}
          >
            <Box sx={{ px: 2, pt: 1.25, pb: 0.5, flexShrink: 0 }}>
              <Typography variant="overline" fontWeight={900} color="text.secondary" sx={{ letterSpacing: 1.5 }}>
                LIVE TOPOLOGY MAP
              </Typography>
            </Box>
            <Box sx={{ flex: 1, minHeight: 0, width: '100%' }}>
              <Suspense fallback={
                <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'center', height: '100%' }}>
                  <Typography variant="caption" color="text.secondary">Loading topology…</Typography>
                </Box>
              }>
                <TopologyGraph
                  onNodeClick={(id) => {
                  const device = devices.find(d => d.id === id);
                  if (device) setSelectedDevice(device);
                }}
                  selectedNodeId={selectedDevice?.id ?? null}
                />
              </Suspense>
            </Box>
          </Paper>
        </Grid>

        {/* RECENT ALERTS BANNER */}
        {!isPortable && recentAlertCount > 0 && (
          <Grid item xs={12}>
            <Paper
              variant="outlined"
              sx={{
                p: 2,
                borderRadius: 2,
                bgcolor: alpha(theme.palette.error.main, 0.02),
                borderColor: alpha(theme.palette.error.main, 0.1),
                display: 'flex',
                alignItems: 'center',
                gap: 3
              }}
            >
              <Box sx={{
                bgcolor: alpha(theme.palette.error.main, 0.1),
                p: 1, borderRadius: 2,
                display: 'flex', alignItems: 'center', justifyContent: 'center'
              }}>
                <AlertIcon color="error" />
              </Box>
              <Box sx={{ flexGrow: 1 }}>
                <Typography variant="subtitle2" fontWeight={900} color="error.main" sx={{ letterSpacing: 0.5 }}>
                  CRITICAL EVENTS DETECTED
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {recentAlertCount} event(s) triggered in the last 24 hours.
                </Typography>
              </Box>
              <Button
                size="small"
                variant="outlined"
                color="error"
                onClick={() => navigate('/alerts?tab=history')}
                sx={{ fontWeight: 900, borderRadius: 2 }}
              >
                VIEW LOGS
              </Button>
            </Paper>
          </Grid>
        )}

        {/* PTP GRANDMASTER WIDGET (AV INTELLIGENCE) */}
        {ptpStatus && (
          <Grid item xs={12} md={6}>
            <Paper
              variant="outlined"
              sx={{
                p: 2,
                borderRadius: 3,
                bgcolor: alpha(theme.palette.info.main, 0.03),
                borderColor: alpha(theme.palette.info.main, 0.2),
                position: 'relative',
                overflow: 'hidden'
              }}
            >
              <Box sx={{ position: 'absolute', right: -20, top: -20, opacity: 0.05 }}>
                <Lan sx={{ fontSize: 150, color: 'info.main' }} />
              </Box>

              <Stack direction="row" alignItems="center" gap={2} mb={2}>
                <Box sx={{ bgcolor: 'info.main', p: 1, borderRadius: 2, color: 'white', display: 'flex' }}>
                  <Speed sx={{ fontSize: 20 }} />
                </Box>
                <Box>
                  <Typography variant="overline" fontWeight={900} color="info.main" sx={{ letterSpacing: 1.5, lineHeight: 1 }}>
                    PTP GRANDMASTER FOUND
                  </Typography>
                  <Typography variant="caption" display="block" color="text.secondary" fontWeight={500}>
                    IEEE 1588 Precision Time Protocol (Dante/AES67)
                  </Typography>
                </Box>
              </Stack>

              <Grid container spacing={2}>
                <Grid item xs={6}>
                  <Typography variant="caption" fontWeight={800} color="text.secondary">MASTER IP</Typography>
                  <Typography variant="body1" fontWeight={900} fontFamily="monospace" color="text.primary">
                    {ptpStatus.sourceIp}
                  </Typography>
                </Grid>
                <Grid item xs={6}>
                  <Typography variant="caption" fontWeight={800} color="text.secondary">CLOCK ID</Typography>
                  <Typography variant="body2" fontWeight={700} fontFamily="monospace" color="text.secondary" sx={{ fontSize: '0.7rem' }}>
                    {ptpStatus.clockIdentity}
                  </Typography>
                </Grid>
                <Grid item xs={4}>
                  <Typography variant="caption" fontWeight={800} color="text.secondary">PRIORITY 1</Typography>
                  <Typography variant="body2" fontWeight={900} color={ptpStatus.priority1 < 128 ? "success.main" : "text.primary"}>
                    {ptpStatus.priority1}
                  </Typography>
                </Grid>
                <Grid item xs={4}>
                  <Typography variant="caption" fontWeight={800} color="text.secondary">CLASS</Typography>
                  <Typography variant="body2" fontWeight={900}>
                    {ptpStatus.clockClass}
                  </Typography>
                </Grid>
                <Grid item xs={4}>
                  <Typography variant="caption" fontWeight={800} color="text.secondary">DOMAIN</Typography>
                  <Typography variant="body2" fontWeight={900}>
                    {ptpStatus.domainNumber}
                  </Typography>
                </Grid>
              </Grid>
            </Paper>
          </Grid>
        )}

        {/* NODE REGISTRY */}
        <Grid item xs={12} md={ptpStatus ? 6 : 12}>
          <Paper
            variant="outlined"
            sx={{
              bgcolor: "background.paper",
              borderRadius: 3,
              overflow: 'hidden',
              boxShadow: '0 4px 12px rgba(0,0,0,0.05)'
            }}
          >
            {/* REGISTRY HEADER */}
            <Box
              px={3} py={2}
              display="flex"
              justifyContent="space-between"
              alignItems="center"
              sx={{
                borderBottom: '1px solid',
                borderColor: 'divider',
                bgcolor: alpha(theme.palette.primary.main, 0.02)
              }}
            >
              <Box display="flex" alignItems="center" gap={1.5}>
                <Lan color="primary" sx={{ fontSize: 20 }} />
                <Typography variant="subtitle2" fontWeight={900} sx={{ letterSpacing: 1 }}>
                  NODE REGISTRY
                </Typography>
                <Chip
                  label={filteredDevices.length}
                  size="small"
                  sx={{
                    height: 20,
                    fontSize: '0.65rem',
                    fontWeight: 900,
                    bgcolor: alpha(theme.palette.primary.main, 0.1),
                    color: 'primary.main',
                    border: '1px solid',
                    borderColor: alpha(theme.palette.primary.main, 0.2)
                  }}
                />
              </Box>

              <Box sx={{
                bgcolor: "background.default",
                px: 2, py: 0.5, borderRadius: 2,
                display: 'flex', alignItems: 'center', width: { xs: 200, sm: 350 },
                border: "1px solid", borderColor: "divider",
                transition: 'all 0.2s ease',
                '&:focus-within': { borderColor: 'primary.main', boxShadow: `0 0 0 3px ${alpha(theme.palette.primary.main, 0.1)}` }
              }}>
                <Search sx={{ color: "text.secondary", mr: 1, fontSize: 18 }} />
                <InputBase
                  placeholder="SEARCH IP, VENDOR, NAME..."
                  fullWidth
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  sx={{ fontSize: '0.75rem', fontWeight: 700, fontFamily: 'monospace' }}
                />
              </Box>
            </Box>

            {/* REGISTRY LIST */}
            <List disablePadding>
              {filteredDevices.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage).map((d) => {
                const isPtpMaster = ptpStatus && d.ipAddress === ptpStatus.sourceIp;
                const score = d.stabilityScore ?? 0;
                const statusColor = score > 80 ? theme.palette.success.main : score > 50 ? theme.palette.warning.main : theme.palette.error.main;

                return (
                  <ListItem
                    key={d.id}
                    sx={{
                      py: 2, px: 3,
                      borderBottom: "1px solid",
                      borderColor: "divider",
                      transition: 'all 0.2s',
                      bgcolor: isPtpMaster ? alpha(theme.palette.info.main, 0.08) : 'transparent',
                      borderLeft: isPtpMaster ? `4px solid ${theme.palette.info.main}` : 'none',
                      '&:hover': {
                        bgcolor: alpha(theme.palette.primary.main, 0.04),
                        '& .row-actions': { opacity: 1, transform: 'translateX(0)' }
                      }
                    }}
                  >
                    <Grid container alignItems="center">
                      {/* IDENTITY COLUMN - Opens Intel Drawer */}
                      <Grid
                        item xs={12} sm={4}
                        display="flex"
                        alignItems="center"
                        gap={2}
                        onClick={() => setSelectedDevice(d)}
                        sx={{
                          cursor: 'pointer',
                          '&:hover .node-name': { color: 'primary.main' },
                          '&:hover .node-avatar': { borderColor: alpha(theme.palette.primary.main, 0.4), bgcolor: alpha(theme.palette.primary.main, 0.04) }
                        }}
                      >
                        <Badge
                          overlap="circular"
                          variant="dot"
                          sx={{
                            "& .MuiBadge-badge": {
                              bgcolor: d.status?.toLowerCase() === 'online' ? 'success.main' : 'error.main',
                              width: 10,
                              height: 10,
                              borderRadius: '50%',
                              border: `2px solid ${theme.palette.background.paper}`,
                              // Optional: Add a pulse effect if online
                              ...(d.status?.toLowerCase() === 'online' && {
                                '&::after': {
                                  position: 'absolute',
                                  top: 0, left: 0,
                                  width: '100%', height: '100%',
                                  borderRadius: '50%',
                                  animation: 'ripple 1.2s infinite ease-in-out',
                                  border: '1px solid currentColor',
                                  content: '""',
                                },
                              }),
                            },
                            '@keyframes ripple': {
                              '0%': { transform: 'scale(.8)', opacity: 1 },
                              '100%': { transform: 'scale(2.4)', opacity: 0 },
                            },
                          }}
                        >
                          <Avatar
                            className="node-avatar"
                            sx={{
                              bgcolor: 'background.default',
                              border: '1px solid',
                              borderColor: 'divider',
                              width: 38,
                              height: 38,
                              color: "primary.main",
                              transition: 'all 0.2s ease'
                            }}
                          >
                            {getDeviceIcon(d.type)}
                          </Avatar>
                        </Badge>
                        <Box>
                          <Typography
                            className="node-name"
                            variant="body2"
                            fontWeight={800}
                            sx={{
                              color: 'text.primary',
                              lineHeight: 1.2,
                              transition: 'color 0.2s ease',
                            }}
                          >
                            {d.name || "UNIDENTIFIED NODE"}
                          </Typography>
                          <Typography
                            variant="caption"
                            color="text.secondary"
                            sx={{
                              textTransform: 'uppercase',
                              fontSize: '0.65rem',
                              fontWeight: 700,
                              letterSpacing: 0.5
                            }}
                          >
                            {d.vendor || "GENERIC"} • {d.type || "UNKNOWN"}
                          </Typography>
                        </Box>
                      </Grid>

                      {/* NETWORK DATA */}
                      <Grid item xs={6} sm={2.5}>
                        <Typography variant="body2" sx={{ fontFamily: 'monospace', color: "primary.main", fontWeight: 700 }}>
                          {d.ipAddress}
                        </Typography>
                        <Typography variant="caption" sx={{ color: "text.disabled", fontFamily: 'monospace', fontSize: '0.7rem' }}>
                          {d.macAddress}
                        </Typography>
                      </Grid>

                      {/* UPDATED STABILITY COLUMN */}
                      <Grid item xs={4} sm={2} sx={{ display: 'flex', alignItems: 'center' }}>
                        <MuiTooltip arrow placement="top" title={<StabilityIntel />}>
                          <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, cursor: 'help' }}>
                            <LinearProgress
                              variant="determinate"
                              value={score}
                              sx={{
                                width: 40, height: 4, borderRadius: 2,
                                bgcolor: alpha(theme.palette.divider, 0.1),
                                "& .MuiLinearProgress-bar": { bgcolor: statusColor }
                              }}
                            />
                            <Typography
                              variant="caption"
                              sx={{
                                fontFamily: 'monospace', fontWeight: 900, color: statusColor,
                                minWidth: '35px', display: 'flex', alignItems: 'center', gap: 0.5
                              }}
                            >
                              {score}% <Lan sx={{ fontSize: 10, opacity: 0.4 }} />
                            </Typography>
                          </Box>
                        </MuiTooltip>
                      </Grid>

                      {/* ACTIONS */}
                      <Grid item xs={12} sm={3.5} sx={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <Stack
                          className="row-actions"
                          direction="row"
                          spacing={1}
                          sx={{
                            opacity: { xs: 1, sm: 0 },
                            transform: { xs: 'none', sm: 'translateX(10px)' },
                            transition: 'all 0.2s ease-in-out'
                          }}
                        >
                          {/* PRIMARY ACTION: DEEP DIVE */}
                          <MuiTooltip title="Full Device Analytics">
                            <IconButton
                              size="small"
                              onClick={(e) => {
                                e.stopPropagation();
                                navigate(`/devices?id=${d.id}`);
                              }}
                              sx={{
                                bgcolor: alpha(theme.palette.primary.main, 0.05),
                                '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.1) }
                              }}
                            >
                              <Visibility fontSize="inherit" sx={{ fontSize: '1.1rem', color: 'primary.main' }} />
                            </IconButton>
                          </MuiTooltip>

                          {/* SECONDARY: COPY IP */}
                          <MuiTooltip title="Copy IP Address">
                            <IconButton
                              size="small"
                              onClick={(e) => {
                                e.stopPropagation();
                                navigator.clipboard.writeText(d.ipAddress);
                              }}
                            >
                              <ContentCopy fontSize="inherit" sx={{ fontSize: '1.1rem' }} />
                            </IconButton>
                          </MuiTooltip>

                          <Divider orientation="vertical" flexItem sx={{ height: 16, my: 'auto', mx: 0.5 }} />

                          {/* DANGER ACTION: IGNORE */}
                          {auth.isAdmin && (
                            <MuiTooltip title="Ignore/Remove Node">
                              <IconButton
                                size="small"
                                color="error"
                                onClick={(e) => {
                                  e.stopPropagation();
                                  handleIgnoreNode(d.id);
                                }}
                                sx={{
                                  '&:hover': { bgcolor: alpha(theme.palette.error.main, 0.1) }
                                }}
                              >
                                <Delete fontSize="inherit" sx={{ fontSize: '1.1rem' }} />
                              </IconButton>
                            </MuiTooltip>
                          )}
                        </Stack>
                      </Grid>
                    </Grid>
                  </ListItem>
                );
              })}
            </List>
            <TablePagination
              component="div"
              count={filteredDevices.length}
              page={page}
              onPageChange={(_, newPage) => setPage(newPage)}
              rowsPerPage={rowsPerPage}
              onRowsPerPageChange={(e) => { setRowsPerPage(parseInt(e.target.value, 10)); setPage(0); }}
              rowsPerPageOptions={[10, 25, 50, 100]}
              sx={{ borderTop: '1px solid', borderColor: 'divider' }}
            />
          </Paper>
        </Grid>
      </Grid>

      {/* ASSET INTEL DRAWER */}
      <DeviceDrawer
        open={Boolean(selectedDevice)}
        onClose={() => setSelectedDevice(null)}
        device={selectedDevice}
        users={users}
        onDeviceUpdate={() => {
          // Dashboard auto-refreshes
        }}
        onDelete={(d) => handleIgnoreNode(d.id)}
        onRestore={handleUndo}
        viewMode="active"
        onNotify={showToast}
      />

      <Snackbar
        open={toastOpen}
        autoHideDuration={5000}
        onClose={() => setToastOpen(false)}
        message={toastMessage}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      />

      <Snackbar
        open={showUndo}
        autoHideDuration={5000}
        onClose={() => setShowUndo(false)}
        message="Node removed from active registry"
        action={
          <Button color="primary" size="small" onClick={handleUndo} sx={{ fontWeight: 900 }}>
            UNDO
          </Button>
        }
        sx={{
          '& .MuiPaper-root': {
            bgcolor: 'background.paper',
            color: 'text.primary',
            border: '1px solid',
            borderColor: 'divider',
            fontWeight: 700
          }
        }}
      />

      {/* VERSION FOOTER */}
      <Box sx={{ mt: 4, textAlign: 'center', opacity: 0.5 }}>
        <Typography variant="caption" color="text.secondary">
          StacksAtlas v{APP_VERSION} • Commercial Installer Build
        </Typography>
      </Box>
    </Box>
  );
}
