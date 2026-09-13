import {
  Box,
  Grid,
  IconButton,
  Paper,
  alpha,
  useTheme,
} from '@mui/material';
import SyncIcon from '@mui/icons-material/Sync';
import { PageHeader } from '../../components/pageheader';
import { HubKpiStrip } from '../../components/hub-dashboard/HubKpiStrip';
import { HubFleetToolbar } from '../../components/hub-dashboard/HubFleetToolbar';
import { HubNodeTable } from '../../components/hub-dashboard/HubNodeTable';
import { HubAlertSnippet } from '../../components/hub-dashboard/HubAlertSnippet';
import { HubTopologyPanel } from '../../components/hub-dashboard/HubTopologyPanel';
import { useHubDashboard } from './useHubDashboard';
import { HubDashboardOverlays } from './HubDashboardOverlays';
import { useHubAlertHandlers } from './useHubAlertHandlers';

export function HubDashboardDesktop() {
  const theme = useTheme();
  const ctrl = useHubDashboard();
  const {
    navigate,
    isAdmin,
    alerts,
    stats,
    filters,
    setFilters,
    filteredNodes,
    hubVersion,
    stagedUpdateVersion,
    fleetHealth,
    topologySiteId,
    setTopologySiteId,
    focusTopologySite,
    clients,
    buildings,
    loadData,
    openManage,
    handleForceSync,
    handleNotifyUpdate,
    handleTriggerUpdate,
    openEnroll,
    openEnrollHint,
    handleResetSite,
    handleDeleteNode,
  } = ctrl;
  const alertHandlers = useHubAlertHandlers(ctrl);

  return (
    <Box sx={{ pb: 5, pt: 2, px: { xs: 2, md: 4 }, bgcolor: 'background.default', minHeight: '100vh' }}>
      <PageHeader
        title="GLOBAL FLEET COMMAND"
        subtitle={`Fleet health ${fleetHealth}% · ${stats.onlineNodes}/${stats.totalNodes} nodes online`}
        stats={[
          {
            label: 'Fleet health',
            value: `${fleetHealth}%`,
            color: fleetHealth > 90 ? 'success.main' : fleetHealth > 70 ? 'warning.main' : 'error.main',
          },
          {
            label: 'Critical alerts (24h)',
            value: stats.criticalAlerts.toString(),
            color: stats.criticalAlerts > 0 ? 'error.main' : 'text.secondary',
          },
        ]}
        action={
          <IconButton onClick={loadData} color="primary" aria-label="Refresh fleet">
            <SyncIcon />
          </IconButton>
        }
      />

      <Grid container spacing={3}>
        <Grid item xs={12}>
          <HubKpiStrip stats={stats} />
        </Grid>

        <Grid item xs={12} xl={8}>
          <Paper
            variant="outlined"
            sx={{
              p: 3,
              borderRadius: 4,
              bgcolor: alpha(theme.palette.background.paper, 0.8),
              backdropFilter: 'blur(20px)',
              border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            }}
          >
            <HubFleetToolbar
              filters={filters}
              clients={clients}
              buildings={buildings}
              isAdmin={!!isAdmin}
              onFiltersChange={(patch) => setFilters((f) => ({ ...f, ...patch }))}
              onEnrollClick={openEnroll}
            />
            <HubNodeTable
              nodes={filteredNodes}
              isAdmin={!!isAdmin}
              onEditNode={openManage}
              onScan={(id) => navigate(`/devices?nodeId=${encodeURIComponent(id)}`)}
              onForceSync={handleForceSync}
              onNotifyUpdate={handleNotifyUpdate}
              onTriggerUpdate={handleTriggerUpdate}
              onEnrollHint={openEnrollHint}
              onResetSite={handleResetSite}
              onDeleteNode={handleDeleteNode}
              onViewTopology={focusTopologySite}
              topologySiteId={topologySiteId}
              hubVersion={hubVersion}
              stagedUpdateVersion={stagedUpdateVersion}
            />
          </Paper>
        </Grid>

        <Grid item xs={12} xl={4}>
          <HubAlertSnippet
            alerts={alerts}
            {...alertHandlers}
          />
        </Grid>

        <Grid item xs={12}>
          <HubTopologyPanel
            nodes={filteredNodes}
            siteId={topologySiteId}
            onSiteIdChange={setTopologySiteId}
          />
        </Grid>
      </Grid>

      <HubDashboardOverlays ctrl={ctrl} />
    </Box>
  );
}
