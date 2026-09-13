import { Box, IconButton, Paper, Stack } from '@mui/material';
import SyncIcon from '@mui/icons-material/Sync';
import { PageHeader } from '../../components/pageheader';
import { HubFleetToolbar } from '../../components/hub-dashboard/HubFleetToolbar';
import { HubNodeTable } from '../../components/hub-dashboard/HubNodeTable';
import { HubAlertSnippet } from '../../components/hub-dashboard/HubAlertSnippet';
import { HubTopologyPanel } from '../../components/hub-dashboard/HubTopologyPanel';
import { MobileKpiStrip } from '../../components/mobile/MobileKpiStrip';
import { fleetHealthPercent } from '../../components/hub-dashboard/hubDashboardUtils';
import { useHubDashboard } from './useHubDashboard';
import { HubDashboardOverlays } from './HubDashboardOverlays';
import { useHubAlertHandlers } from './useHubAlertHandlers';

export function HubDashboardMobile() {
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
  const offlineNodes = Math.max(0, stats.totalNodes - stats.onlineNodes);

  return (
    <Box sx={{ pb: 4, pt: 2, px: 2, bgcolor: 'background.default', minHeight: '100vh', overflowX: 'hidden' }}>
      <PageHeader
        title="GLOBAL FLEET COMMAND"
        subtitle={`${stats.onlineNodes}/${stats.totalNodes} nodes online`}
        stats={[
          {
            label: 'Fleet health',
            value: `${fleetHealth}%`,
            color: fleetHealth > 90 ? 'success.main' : fleetHealth > 70 ? 'warning.main' : 'error.main',
          },
          {
            label: 'Alerts (24h)',
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

      <MobileKpiStrip
        items={[
          { label: 'NODES', value: stats.totalNodes.toString() },
          { label: 'ONLINE', value: stats.onlineNodes.toString(), color: 'success.main' },
          { label: 'DEVICES', value: stats.totalDevices.toString(), color: 'info.main' },
          {
            label: 'HEALTH',
            value: `${fleetHealthPercent(stats.onlineNodes, stats.totalNodes)}%`,
            color: fleetHealth > 90 ? 'success.main' : fleetHealth > 70 ? 'warning.main' : 'error.main',
          },
        ]}
      />

      {stats.setupRequiredNodes > 0 && (
        <Paper variant="outlined" sx={{ p: 1.5, mb: 2, borderRadius: 2, textAlign: 'center' }}>
          <Box component="span" sx={{ fontSize: '0.75rem', fontWeight: 800, color: 'warning.main' }}>
            {stats.setupRequiredNodes} site(s) need setup
            {offlineNodes > 0 ? ` · ${offlineNodes} offline` : ''}
          </Box>
        </Paper>
      )}

      <Stack spacing={2}>
        <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
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

        <HubAlertSnippet alerts={alerts} {...alertHandlers} />

        <HubTopologyPanel
          nodes={filteredNodes}
          siteId={topologySiteId}
          onSiteIdChange={setTopologySiteId}
        />
      </Stack>

      <HubDashboardOverlays ctrl={ctrl} />
    </Box>
  );
}
