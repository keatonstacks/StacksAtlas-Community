import { useState, lazy, Suspense } from 'react';
import {
  Box, Button, Chip, Dialog, Grid, InputAdornment, Snackbar,
  Stack, TextField, Typography,
} from '@mui/material';
import SearchIcon from '@mui/icons-material/Search';
import SensorsIcon from '@mui/icons-material/Sensors';
import LanIcon from '@mui/icons-material/Lan';
import HubIcon from '@mui/icons-material/Hub';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import SpeedIcon from '@mui/icons-material/Speed';
import { PageHeader } from '../../components/pageheader';
import { PortableUpgradeBanner } from '../../components/PortableUpgradeBanner';
import { DeviceDrawer } from '../../components/DeviceDrawer';
import { ReportsSectionAccordion } from '../../components/reports/ReportsSectionAccordion';
import { NetworkStabilityChart } from '../../components/dashboard/NetworkStabilityChart';
import { MobileNodeRegistryCard } from '../../components/dashboard/mobile/MobileNodeRegistryCard';
import { MobileKpiStrip } from '../../components/mobile/MobileKpiStrip';
import { APP_VERSION } from '../../constants/version';
import { useNodeDashboard } from './useNodeDashboard';

const TopologyGraph = lazy(() =>
  import('../../components/network/TopologyGraph').then((m) => ({ default: m.TopologyGraph }))
);

type SectionKey = 'stability' | 'vendors' | 'topology' | 'registry' | 'alerts' | 'ptp';

export function NodeDashboardMobile() {
  const ctrl = useNodeDashboard();
  const {
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
  } = ctrl;

  const [expanded, setExpanded] = useState<Record<SectionKey, boolean>>({
    stability: true,
    vendors: false,
    topology: false,
    registry: true,
    alerts: recentAlertCount > 0,
    ptp: false,
  });
  const [topologyOpen, setTopologyOpen] = useState(false);

  const setSection = (key: SectionKey, open: boolean) => {
    setExpanded((prev) => ({ ...prev, [key]: open }));
  };

  const onlineCount = summary?.onlineCount ?? devices.filter((d) => d.status?.toLowerCase() === 'online').length;
  const offlineCount = summary?.offlineCount ?? Math.max(0, devices.length - onlineCount);

  return (
    <Box sx={{ pb: 4, pt: 2, px: 2, bgcolor: 'background.default', minHeight: '100vh', overflowX: 'hidden' }}>
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
            label: 'Recent Alerts',
            value: recentAlertCount.toString(),
            color: recentAlertCount > 0 ? 'error.main' : 'text.secondary',
          }] : []),
          {
            label: 'Next Cleanup',
            value: maintenance?.nextCleanup
              ? new Date(maintenance.nextCleanup).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
              : 'N/A',
          },
        ]}
      />

      <MobileKpiStrip
        items={[
          { label: 'ONLINE', value: onlineCount.toString(), color: 'success.main' },
          { label: 'OFFLINE', value: offlineCount.toString(), color: offlineCount > 0 ? 'error.main' : 'text.secondary' },
          { label: 'TOTAL', value: (summary?.totalDevices ?? devices.length).toString() },
          { label: 'PEAK', value: Math.max(...sweeps.map((s) => s.totalOnline), 0).toString() },
        ]}
      />

      <TextField
        size="small"
        fullWidth
        placeholder="Search IP, vendor, name, port..."
        value={searchTerm}
        onChange={(e) => setSearchTerm(e.target.value)}
        sx={{ my: 2 }}
        InputProps={{
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" />
            </InputAdornment>
          ),
        }}
      />

      <ReportsSectionAccordion
        id="stability"
        title="NETWORK STABILITY"
        icon={<SensorsIcon color="primary" fontSize="small" />}
        badge={`${onlineCount} online`}
        expanded={expanded.stability}
        onExpandedChange={(open) => setSection('stability', open)}
      >
        <NetworkStabilityChart
          sweeps={sweeps}
          deviceCount={devices.length}
          height={200}
          compact
          hideHeader
        />
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="vendors"
        title="VENDOR COMPOSITION"
        icon={<HubIcon color="primary" fontSize="small" />}
        badge={`${summary?.vendors?.length ?? 0} vendors`}
        expanded={expanded.vendors}
        onExpandedChange={(open) => setSection('vendors', open)}
      >
        <Stack spacing={1}>
          {(summary?.vendors || []).map(({ vendor, count }) => (
            <Box key={vendor} sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <Typography variant="caption" fontWeight={700} noWrap sx={{ maxWidth: '65%' }}>{vendor}</Typography>
              <Chip label={count} size="small" sx={{ fontWeight: 800 }} />
            </Box>
          ))}
          {!summary?.vendors?.length && (
            <Typography variant="caption" color="text.secondary">No vendor data yet.</Typography>
          )}
        </Stack>
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="topology"
        title="TOPOLOGY MAP"
        icon={<LanIcon color="primary" fontSize="small" />}
        badge="Full screen"
        expanded={expanded.topology}
        onExpandedChange={(open) => setSection('topology', open)}
      >
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
          Open the live topology map in full screen for pinch and pan.
        </Typography>
        <Button variant="outlined" fullWidth onClick={() => setTopologyOpen(true)} sx={{ fontWeight: 800 }}>
          Open topology map
        </Button>
      </ReportsSectionAccordion>

      {!isPortable && recentAlertCount > 0 && (
        <ReportsSectionAccordion
          id="alerts"
          title="CRITICAL EVENTS"
          icon={<NotificationsActiveIcon color="error" fontSize="small" />}
          badge={`${recentAlertCount} in 24h`}
          expanded={expanded.alerts}
          onExpandedChange={(open) => setSection('alerts', open)}
        >
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
            {recentAlertCount} event(s) in the last 24 hours.
          </Typography>
          <Button
            variant="outlined"
            color="error"
            fullWidth
            onClick={() => navigate('/alerts?tab=history')}
            sx={{ fontWeight: 800 }}
          >
            View alert logs
          </Button>
        </ReportsSectionAccordion>
      )}

      {ptpStatus && (
        <ReportsSectionAccordion
          id="ptp"
          title="PTP GRANDMASTER"
          icon={<SpeedIcon color="info" fontSize="small" />}
          badge={ptpStatus.sourceIp}
          expanded={expanded.ptp}
          onExpandedChange={(open) => setSection('ptp', open)}
        >
          <Grid container spacing={1.5}>
            <Grid item xs={6}>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>MASTER IP</Typography>
              <Typography variant="body2" fontWeight={900} fontFamily="monospace">{ptpStatus.sourceIp}</Typography>
            </Grid>
            <Grid item xs={6}>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>PRIORITY</Typography>
              <Typography variant="body2" fontWeight={900}>{ptpStatus.priority1}</Typography>
            </Grid>
            <Grid item xs={12}>
              <Typography variant="caption" color="text.secondary" fontWeight={800}>CLOCK ID</Typography>
              <Typography variant="caption" fontFamily="monospace" display="block">{ptpStatus.clockIdentity}</Typography>
            </Grid>
          </Grid>
        </ReportsSectionAccordion>
      )}

      <ReportsSectionAccordion
        id="registry"
        title="NODE REGISTRY"
        icon={<LanIcon color="primary" fontSize="small" />}
        badge={`${filteredDevices.length} nodes`}
        expanded={expanded.registry}
        onExpandedChange={(open) => setSection('registry', open)}
      >
        <Stack spacing={1.25}>
          {filteredDevices.map((device) => (
            <MobileNodeRegistryCard
              key={device.id}
              device={device}
              isPtpMaster={!!ptpStatus && device.ipAddress === ptpStatus.sourceIp}
              isAdmin={!!auth.isAdmin}
              onOpen={() => setSelectedDevice(device)}
              onViewDevices={() => navigate(`/devices?id=${device.id}`)}
              onCopyIp={() => navigator.clipboard.writeText(device.ipAddress)}
              onIgnore={() => handleIgnoreNode(device.id)}
            />
          ))}
          {filteredDevices.length === 0 && (
            <Typography variant="body2" color="text.secondary" textAlign="center" sx={{ py: 2 }}>
              No nodes match your search.
            </Typography>
          )}
        </Stack>
      </ReportsSectionAccordion>

      <Dialog fullScreen open={topologyOpen} onClose={() => setTopologyOpen(false)}>
        <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', bgcolor: 'background.default' }}>
          <Stack direction="row" justifyContent="space-between" alignItems="center" sx={{ p: 2, borderBottom: 1, borderColor: 'divider' }}>
            <Typography variant="subtitle2" fontWeight={900}>LIVE TOPOLOGY MAP</Typography>
            <Button onClick={() => setTopologyOpen(false)} sx={{ fontWeight: 800 }}>Close</Button>
          </Stack>
          <Box sx={{ flex: 1, minHeight: 0 }}>
            <Suspense fallback={<Box sx={{ p: 3 }}><Typography variant="caption">Loading topology…</Typography></Box>}>
              <TopologyGraph
                onNodeClick={(id) => {
                const device = devices.find((d) => d.id === id);
                if (device) {
                  setSelectedDevice(device);
                  setTopologyOpen(false);
                }
              }}
                selectedNodeId={selectedDevice?.id ?? null}
              />
            </Suspense>
          </Box>
        </Box>
      </Dialog>

      <DeviceDrawer
        open={Boolean(selectedDevice)}
        onClose={() => setSelectedDevice(null)}
        device={selectedDevice}
        users={users}
        onDeviceUpdate={() => { /* poll refreshes */ }}
        onDelete={(d) => handleIgnoreNode(d.id)}
        onRestore={handleUndo}
        viewMode="active"
        onNotify={showToast}
      />

      <Snackbar open={toastOpen} autoHideDuration={5000} onClose={() => setToastOpen(false)} message={toastMessage} />
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
      />

      <Box sx={{ mt: 3, textAlign: 'center', opacity: 0.5 }}>
        <Typography variant="caption" color="text.secondary">
          StacksAtlas v{APP_VERSION}
        </Typography>
      </Box>
    </Box>
  );
}
