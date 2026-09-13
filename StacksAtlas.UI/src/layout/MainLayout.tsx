import {
  Box, Drawer,
  AppBar, Toolbar, Typography, Divider, IconButton, Tooltip,
  useTheme, alpha, Stack, Avatar, Menu, MenuItem, Chip, Button, ListItemIcon
} from "@mui/material";

import { useLocation, Outlet, useNavigate } from "react-router-dom";
import {
  Dashboard as DashboardIcon,
  Devices as DevicesIcon,
  Warning as WarningIcon,
  Settings as SettingsIcon,
  Menu as MenuIcon,
  DarkMode as DarkModeIcon,
  LightMode as LightModeIcon,
  Refresh as RefreshIcon,
  Assessment as AssessmentIcon,
  People as PeopleIcon,
  Logout as LogoutIcon,
  DeleteOutline as DeleteOutlineIcon,
  Person as PersonIcon,
  Storage as StorageIcon,
  Terminal as TerminalIcon,
  FactCheck as FactCheckIcon,
} from "@mui/icons-material";




import { useGlobalStats } from "../context/GlobalStatsContext";
import { useAuth } from "../context/AuthContext";
import { useLicense } from "../hooks/useLicense";
import { licenseTierLabel } from "../utils/federationMode";
import { InterfaceSelector } from "../components/InterfaceSelector";
import { APP_VERSION } from "../constants/version";
import { useState, useEffect, type Dispatch, type SetStateAction } from "react";
import { ApiService } from "../services/apiService";
import DependencyWarning from "../components/DependencyWarning";
import UpdateAvailableHeaderChip from "../components/UpdateAvailableHeaderChip";
import { useSoftwareUpdateNotifier } from "../hooks/useSoftwareUpdateNotifier";
import EndEvaluationDialog from "../components/EndEvaluationDialog";
import { useIsMobileLayout } from "../hooks/useIsMobileLayout";
import { AppSidebarContent } from "../components/layout/AppSidebarContent";
import { MobileHeaderOverflowMenu } from "../components/layout/MobileHeaderOverflowMenu";




interface MainLayoutProps {
  mode: "light" | "dark";
  setMode: Dispatch<SetStateAction<"light" | "dark">>;
}

export default function MainLayout({ mode, setMode }: MainLayoutProps) {
  const location = useLocation();
  const navigate = useNavigate();
  const theme = useTheme();
  const isMobile = useIsMobileLayout();
  const auth = useAuth();
  const { status: licenseStatus } = useLicense();
  const [scanStatus, setScanStatus] = useState<{ isScanning: boolean, progress: number }>({ isScanning: false, progress: 0 });

  const {
    systemStatus,
    isScanning,
    isHub,
    federatedNodeCount,
    federatedOnlineNodes,
    applianceDbSize,
    fleetDbSize,
    hasFleetDatabase,
    applianceEngine,
    onlineCount,
    totalDevices,
    recentAlertCount
  } = useGlobalStats();

  const isAdmin = auth.isAdmin;
  const updateNotifier = useSoftwareUpdateNotifier(auth.isAuthenticated && isAdmin);
  const formatDbMb = (bytes: number) => (bytes / 1024 / 1024).toFixed(2);
  const showDualDb = isHub && hasFleetDatabase;

  const headerChipSx = {
    bgcolor: alpha(theme.palette.primary.main, 0.05),
    border: '1px solid',
    borderColor: alpha(theme.palette.primary.main, 0.12),
    borderRadius: 2,
    px: 1.5,
    py: 0.5,
    textTransform: 'none' as const,
    color: 'text.primary',
    minWidth: 0,
    alignItems: 'flex-start',
  };

  const openDatabaseSettings = () => {
    if (!isAdmin) return;
    navigate('/settings?tab=database');
  };

  const [collapsed, setCollapsed] = useState(false);
  const [mobileNavPath, setMobileNavPath] = useState<string | null>(null);
  const mobileNavOpen = isMobile && mobileNavPath === location.pathname;
  const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
  const [isPortable, setIsPortable] = useState<boolean | null>(null);
  const [portableDataDir, setPortableDataDir] = useState<string | null>(null);
  const [endEvalOpen, setEndEvalOpen] = useState(false);
  const openMenu = Boolean(anchorEl);

  const handleMenuClick = (event: React.MouseEvent<HTMLElement>) => {
    setAnchorEl(event.currentTarget);
  };
  const handleMenuClose = () => {
    setAnchorEl(null);
  };

  const handleOpenProfile = () => {
    handleMenuClose();
    navigate("/profile");
  };

  useEffect(() => {
    ApiService.getSystemRuntimeInfo()
      .then((info) => {
        setIsPortable(!!info?.isPortable);
        setPortableDataDir(info?.dataDirectory ?? null);
      })
      .catch(() => {
        setIsPortable(false);
        setPortableDataDir(null);
      });
  }, []);

  const handleEndEvaluationClick = () => {
    handleMenuClose();
    setEndEvalOpen(true);
  };

  const accountLabel = isPortable === true
    ? 'Portable'
    : (auth.user?.username?.toUpperCase() ?? '');
  const accountSublabel = auth.user?.role?.toUpperCase() ?? (isPortable === true ? 'ADMIN' : '');
  const accountInitial = isPortable === true
    ? 'P'
    : (auth.user?.username?.charAt(0).toUpperCase() ?? '?');

  const destructiveMenuSx = {
    py: 1.5,
    color: 'text.secondary',
    '& .MuiListItemIcon-root': { color: 'inherit', minWidth: 36 },
    '&:hover': {
      color: 'error.main',
      bgcolor: alpha(theme.palette.error.main, 0.08),
      '& .MuiListItemIcon-root': { color: 'error.main' },
    },
  };



  // Poll for scan status
  useEffect(() => {
    if (!auth.isAuthenticated) return;
    
    const interval = setInterval(async () => {
      try {
        const status = await ApiService.getScanStatus();
        if (status) setScanStatus(status);
      } catch {
        // ignore scan status poll errors
      }
    }, 2000);
    return () => clearInterval(interval);
  }, [auth.isAuthenticated]);
  const drawerWidth = collapsed ? 72 : 260;

  const sidebarPaperBackground = mode === 'dark'
    ? 'linear-gradient(180deg, #121212 0%, #1e1e1e 100%)'
    : '#f8f9fa';

  const navItems = [
    { label: "Dashboard", path: "/", icon: <DashboardIcon /> },
    { label: "Devices", path: "/devices", icon: <DevicesIcon /> },
    ...(auth.isAdmin && isPortable === false ? [{ label: "Users", path: "/users", icon: <PeopleIcon /> }] : []),
    ...(isPortable === false ? [{ label: "Alerts", path: "/alerts", icon: <WarningIcon /> }] : []),
    { label: "Reports", path: "/reports", icon: <AssessmentIcon /> },
    ...(auth.isAdmin ? [{ label: "Logs", path: "/logs", icon: <TerminalIcon /> }] : []),
    ...(auth.isAdmin ? [{ label: "Audit Log", path: "/audit", icon: <FactCheckIcon /> }] : []),
    ...(auth.isAdmin ? [{ label: "Settings", path: "/settings", icon: <SettingsIcon /> }] : [])
  ];

  const getPageTitle = () => {
    const item = navItems.find(i => i.path === location.pathname);
    return item ? item.label.toUpperCase() : "StacksAtlas";
  };

  const sidebarContent = (
    <AppSidebarContent
      isHub={isHub}
      collapsed={collapsed}
      isMobile={isMobile}
      currentPath={location.pathname}
      navItems={navItems}
      recentAlertCount={recentAlertCount}
      settingsUpdateDot={updateNotifier.visible}
      onToggleCollapsed={() => setCollapsed(!collapsed)}
      onCloseMobile={() => setMobileNavPath(null)}
    />
  );

  const statusLabel = isScanning
    ? (isMobile
      ? `SCAN${scanStatus.progress > 0 ? ` ${scanStatus.progress}%` : ''}`
      : `SCANNING ${scanStatus.progress > 0 ? `(${scanStatus.progress}%)` : '...'}`)
    : isHub
      ? (isMobile ? `HUB ${federatedOnlineNodes}` : `HUB • ${federatedOnlineNodes} NODE${federatedOnlineNodes !== 1 ? 'S' : ''}`)
      : systemStatus.toUpperCase();

  return (
    <Box sx={{ display: "flex", bgcolor: "background.default", minHeight: "100vh" }}>

      {!isMobile && (
      <Drawer
        variant="permanent"
        sx={{
          width: drawerWidth,
          flexShrink: 0,
          [`& .MuiDrawer-paper`]: {
            width: drawerWidth,
            boxSizing: "border-box",
            transition: theme.transitions.create("width", {
              easing: theme.transitions.easing.sharp,
              duration: 250
            }),
            overflowX: "hidden",
            borderRight: `1px solid ${theme.palette.divider}`,
            zIndex: theme.zIndex.drawer,
            background: sidebarPaperBackground,
            display: 'flex',
            flexDirection: 'column'
          }
        }}
      >
        {sidebarContent}
      </Drawer>
      )}

      {isMobile && (
        <Drawer
          variant="temporary"
          open={mobileNavOpen}
          onClose={() => setMobileNavPath(null)}
          ModalProps={{ keepMounted: true }}
          sx={{
            [`& .MuiDrawer-paper`]: {
              width: 280,
              boxSizing: 'border-box',
              borderRight: `1px solid ${theme.palette.divider}`,
              background: sidebarPaperBackground,
              display: 'flex',
              flexDirection: 'column',
            },
          }}
        >
          {sidebarContent}
        </Drawer>
      )}

      {/* 2. TOP BAR (APPBAR) - Configured to offset for the drawer */}
      <AppBar
        position="fixed"
        elevation={0}
        sx={{
          width: isMobile ? '100%' : `calc(100% - ${drawerWidth}px)`,
          ml: isMobile ? 0 : `${drawerWidth}px`,
          transition: theme.transitions.create(["width", "margin"], {
            easing: theme.transitions.easing.sharp,
            duration: 250
          }),
          bgcolor: alpha(theme.palette.background.default, 0.7),
          backdropFilter: "blur(12px)",
          color: "text.primary",
          borderBottom: `1px solid ${theme.palette.divider}`,
          zIndex: theme.zIndex.drawer - 1,
        }}
      >
        <Toolbar sx={{ display: "flex", justifyContent: "space-between", gap: 1, minHeight: { xs: 56, sm: 64 } }}>
          <Stack direction="row" alignItems="center" spacing={1} sx={{ minWidth: 0, flex: 1 }}>
            {isMobile && (
              <IconButton
                onClick={() => setMobileNavPath(location.pathname)}
                edge="start"
                size="small"
                aria-label="Open navigation"
                sx={{ flexShrink: 0 }}
              >
                <MenuIcon fontSize="small" />
              </IconButton>
            )}
            <Typography variant="overline" fontWeight={900} sx={{ letterSpacing: { xs: 1, sm: 2 }, opacity: 0.8 }} noWrap>
              {getPageTitle()}
            </Typography>
          </Stack>

          <Stack direction="row" spacing={{ xs: 0.75, sm: 2 }} alignItems="center" sx={{ flexShrink: 0 }}>
            {/* System Status Indicator */}
            <Tooltip title={isScanning ? "Active Network Sweep" : `System ${systemStatus}`}>
              <Box sx={{
                display: "flex",
                alignItems: "center",
                gap: { xs: 0.75, sm: 1.5 },
                px: { xs: 1, sm: 2 },
                py: 0.5,
                borderRadius: 5,
                bgcolor: alpha(theme.palette.action.hover, 0.05),
                border: '1px solid',
                borderColor: 'divider',
                maxWidth: { xs: 120, sm: 'none' },
              }}>
                <Box
                  sx={{
                    width: 8, height: 8, borderRadius: "50%",
                    bgcolor: systemStatus === "online" ? "success.main" : systemStatus === "degraded" ? "warning.main" : "error.main",
                    boxShadow: systemStatus === "online" ? `0 0 8px ${alpha(theme.palette.success.main, 0.6)}` : 'none',
                    animation: systemStatus === "online" ? "pulseStatus 2s infinite" : "none",
                    "@keyframes pulseStatus": {
                      "0%": { opacity: 1 },
                      "50%": { opacity: 0.4 },
                      "100%": { opacity: 1 },
                    },
                  }}
                />

                {isScanning && (
                  <RefreshIcon
                    sx={{
                      fontSize: 14,
                      color: "primary.main",
                      animation: "spin 2s linear infinite",
                      "@keyframes spin": {
                        "to": { transform: "rotate(360deg)" }
                      }
                    }}
                  />
                )}

                <Typography variant="caption" fontWeight={900} sx={{ letterSpacing: { xs: 0.5, sm: 1 }, whiteSpace: 'nowrap' }} noWrap>
                  {statusLabel}
                </Typography>
              </Box>
            </Tooltip>

            <MobileHeaderOverflowMenu
              isHub={isHub}
              isAdmin={isAdmin}
              onlineCount={onlineCount}
              totalDevices={totalDevices}
              federatedOnlineNodes={federatedOnlineNodes}
              federatedNodeCount={federatedNodeCount}
              applianceDbSize={applianceDbSize}
              fleetDbSize={fleetDbSize}
              showDualDb={showDualDb}
              applianceEngine={applianceEngine}
              formatDbMb={formatDbMb}
              onOpenDatabase={openDatabaseSettings}
              onOpenNetwork={() => navigate('/settings?tab=scanning')}
              onOpenUpdates={() => navigate('/settings?tab=updates')}
              showUpdateAction={updateNotifier.visible && !!updateNotifier.availableUpdate}
            />

            <Stack direction="row" spacing={1.5} alignItems="center" sx={{ display: { xs: 'none', md: 'flex' }, mr: 1 }}>
              {updateNotifier.visible && updateNotifier.availableUpdate && (
                <UpdateAvailableHeaderChip
                  result={updateNotifier.availableUpdate}
                  isPortable={isPortable === true}
                  onDismiss={updateNotifier.dismiss}
                />
              )}

              {!isHub && <InterfaceSelector />}

              <Tooltip
                arrow
                title={
                  isAdmin
                    ? showDualDb
                      ? `Appliance (LiteDB): ${formatDbMb(applianceDbSize)} MB · Fleet: ${formatDbMb(fleetDbSize)} MB  -  open Database Infrastructure`
                      : `Local store (${applianceEngine}): ${formatDbMb(applianceDbSize)} MB  -  open Database Infrastructure`
                    : `Local store (${applianceEngine}): ${formatDbMb(applianceDbSize)} MB (view only)`
                }
              >
                {isAdmin ? (
                <Button
                  onClick={openDatabaseSettings}
                  size="small"
                  startIcon={<StorageIcon sx={{ fontSize: '1rem !important', mt: 0.25 }} />}
                  sx={{
                    ...headerChipSx,
                    '&:hover': {
                      bgcolor: alpha(theme.palette.primary.main, 0.1),
                      borderColor: alpha(theme.palette.primary.main, 0.22),
                    },
                  }}
                >
                  <Box sx={{ textAlign: 'left' }}>
                    <Typography variant="caption" sx={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, color: 'primary.main', lineHeight: 1 }}>
                      DATABASE
                    </Typography>
                    {showDualDb ? (
                      <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.25, fontSize: '0.78rem' }}>
                        <Box component="span" sx={{ color: 'text.primary' }}>{formatDbMb(applianceDbSize)}</Box>
                        <Box component="span" sx={{ opacity: 0.45, mx: 0.5 }}>+</Box>
                        <Box component="span" sx={{ color: 'primary.main' }}>{formatDbMb(fleetDbSize)} MB</Box>
                      </Typography>
                    ) : (
                      <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.2 }}>
                        {formatDbMb(applianceDbSize)} MB · {applianceEngine}
                      </Typography>
                    )}
                  </Box>
                </Button>
                ) : (
                <Box
                  sx={{
                    ...headerChipSx,
                    display: 'flex',
                    alignItems: 'flex-start',
                    gap: 1,
                  }}
                >
                  <StorageIcon sx={{ fontSize: '1rem', mt: 0.25, color: 'primary.main', opacity: 0.85 }} />
                  <Box sx={{ textAlign: 'left' }}>
                    <Typography variant="caption" sx={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, color: 'primary.main', lineHeight: 1 }}>
                      DATABASE
                    </Typography>
                    {showDualDb ? (
                      <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.25, fontSize: '0.78rem' }}>
                        <Box component="span" sx={{ color: 'text.primary' }}>{formatDbMb(applianceDbSize)}</Box>
                        <Box component="span" sx={{ opacity: 0.45, mx: 0.5 }}>+</Box>
                        <Box component="span" sx={{ color: 'primary.main' }}>{formatDbMb(fleetDbSize)} MB</Box>
                      </Typography>
                    ) : (
                      <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.2 }}>
                        {formatDbMb(applianceDbSize)} MB · {applianceEngine}
                      </Typography>
                    )}
                  </Box>
                </Box>
                )}
              </Tooltip>

              <Box sx={{ ...headerChipSx, display: 'flex' }}>
                <Box sx={{ textAlign: 'left' }}>
                  <Typography variant="caption" sx={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, color: 'text.secondary', lineHeight: 1 }}>
                    {isHub ? 'SITES' : 'DEVICES'}
                  </Typography>
                  <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.2 }}>
                    {isHub ? (
                      <><Box component="span" sx={{ color: 'success.main' }}>{federatedOnlineNodes}</Box> / {federatedNodeCount}</>
                    ) : (
                      <><Box component="span" sx={{ color: 'success.main' }}>{onlineCount}</Box> / {totalDevices}</>
                    )}
                    <Box component="span" sx={{ fontSize: '0.68rem', opacity: 0.65, ml: 0.5 }}>
                      {isHub ? 'CONNECTED' : 'ONLINE'}
                    </Box>
                  </Typography>
                </Box>
              </Box>
            </Stack>

            <Divider orientation="vertical" flexItem sx={{ my: 2, display: { xs: 'none', sm: 'block' } }} />

            <Stack direction="row" spacing={1} alignItems="center">
              <Box sx={{ textAlign: 'right', display: { xs: 'none', sm: 'block' } }}>
                <Typography variant="body2" fontWeight={800} sx={{ lineHeight: 1 }}>
                  {accountLabel}
                </Typography>
                <Typography variant="caption" color="text.secondary" fontWeight={700} fontSize="0.6rem">
                  {accountSublabel}
                </Typography>
              </Box>
              
              <Tooltip title={isPortable === true ? 'Portable session' : 'Account settings'}>
                <IconButton
                  onClick={handleMenuClick}
                  size="small"
                  sx={{ 
                    p: 0.5,
                    border: '1px solid', 
                    borderColor: alpha(theme.palette.primary.main, 0.2),
                    bgcolor: alpha(theme.palette.primary.main, 0.05),
                    '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.1) }
                  }}
                >
                  <Avatar 
                    sx={{ 
                      width: 32, 
                      height: 32, 
                      bgcolor: 'primary.main',
                      fontSize: '0.9rem',
                      fontWeight: 900
                    }}
                  >
                    {accountInitial}
                  </Avatar>
                </IconButton>
              </Tooltip>
            </Stack>

            <Menu
              anchorEl={anchorEl}
              open={openMenu}
              onClose={handleMenuClose}
              onClick={handleMenuClose}
              transformOrigin={{ horizontal: 'right', vertical: 'top' }}
              anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}
              PaperProps={{
                elevation: 0,
                sx: {
                  width: 196,
                  maxWidth: '92vw',
                  overflow: 'hidden',
                  filter: 'drop-shadow(0px 2px 8px rgba(0,0,0,0.32))',
                  mt: 1.5,
                  borderRadius: 2,
                  border: '1px solid',
                  borderColor: 'divider',
                  '& .MuiAvatar-root': {
                    width: 32,
                    height: 32,
                    ml: -0.5,
                    mr: 1,
                  },
                  '&::before': {
                    content: '""',
                    display: 'block',
                    position: 'absolute',
                    top: 0,
                    right: 14,
                    width: 10,
                    height: 10,
                    bgcolor: 'background.paper',
                    transform: 'translateY(-50%) rotate(45deg)',
                    zIndex: 0,
                  },
                },
              }}
            >
              {isPortable === false && (
                <MenuItem onClick={handleOpenProfile} sx={{ py: 1.5 }}>
                  <ListItemIcon>
                    <PersonIcon fontSize="small" />
                  </ListItemIcon>
                  <Typography variant="body2" fontWeight={700}>My Profile</Typography>
                </MenuItem>
              )}

              <MenuItem 
                onClick={() => {
                  const next = mode === "light" ? "dark" : "light";
                  setMode(next);
                  localStorage.setItem("themeMode", next);
                }}
                sx={{ py: 1.5 }}
              >
                <ListItemIcon>
                  {mode === "light" ? <DarkModeIcon fontSize="small" /> : <LightModeIcon fontSize="small" />}
                </ListItemIcon>
                <Typography variant="body2" fontWeight={700}>{mode === 'light' ? 'Dark Mode' : 'Light Mode'}</Typography>
              </MenuItem>

              <Divider sx={{ my: 1 }} />

              {isPortable === true ? (
                <MenuItem
                  onClick={handleEndEvaluationClick}
                  sx={{
                    py: 1.25,
                    px: 1.5,
                    color: 'error.main',
                    '& .MuiListItemIcon-root': { color: 'error.main', minWidth: 32 },
                    '&:hover': { bgcolor: alpha(theme.palette.error.main, 0.1) },
                  }}
                >
                  <ListItemIcon>
                    <DeleteOutlineIcon sx={{ fontSize: 18 }} />
                  </ListItemIcon>
                  <Typography variant="body2" fontWeight={700} sx={{ fontSize: '0.8rem', lineHeight: 1.2 }}>
                    Remove data
                  </Typography>
                </MenuItem>
              ) : isPortable === false ? (
                <MenuItem onClick={auth.logout} sx={destructiveMenuSx}>
                  <ListItemIcon>
                    <LogoutIcon fontSize="small" />
                  </ListItemIcon>
                  <Typography variant="body2" fontWeight={700}>Logout</Typography>
                </MenuItem>
              ) : null}
            </Menu>


            <Chip
              size="small"
              label={isPortable === true ? 'PORTABLE' : licenseTierLabel(licenseStatus?.tier, !!licenseStatus?.isActive)}
              color={isPortable === true ? 'info' : (licenseStatus?.isActive ? 'success' : 'default')}
              variant="outlined"
              sx={{ fontWeight: 700, fontSize: '0.65rem', height: 22, display: { xs: 'none', sm: 'flex' } }}
            />
            <Typography variant="caption" sx={{ opacity: 0.4, fontFamily: 'monospace', fontWeight: 700, display: { xs: 'none', md: 'block' } }}>
              v{APP_VERSION}
            </Typography>
          </Stack>
        </Toolbar>
      </AppBar>

      {/* 3. MAIN CONTENT - Respects drawer width and transition */}
      <Box
        component="main"
        sx={{
          flexGrow: 1,
          p: 0,
          mt: { xs: 7, sm: 8 },
          width: isMobile ? '100%' : `calc(100% - ${drawerWidth}px)`,
          transition: theme.transitions.create(["width", "margin"], {
            easing: theme.transitions.easing.sharp,
            duration: 250
          }),
          minHeight: '100vh',
          bgcolor: 'background.default'
        }}
      >
        <Box sx={{ p: isMobile ? 0 : 4 }}>
          <DependencyWarning />
          <Outlet />
        </Box>
      </Box>

      <EndEvaluationDialog
        open={endEvalOpen}
        onClose={() => setEndEvalOpen(false)}
        dataDirectory={portableDataDir}
        onComplete={() => {
          setEndEvalOpen(false);
          navigate('/portable-closed', { replace: true });
        }}
      />

    </Box>

  );
}
