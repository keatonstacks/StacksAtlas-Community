import { Grid, useTheme, alpha } from '@mui/material';
import RouterIcon from '@mui/icons-material/Router';
import DevicesIcon from '@mui/icons-material/Devices';
import SyncIcon from '@mui/icons-material/Sync';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { KpiCard } from './KpiCard';
import type { HubFleetStats } from './types';
import { fleetHealthPercent } from './hubDashboardUtils';

interface HubKpiStripProps {
  stats: HubFleetStats;
}

export function HubKpiStrip({ stats }: HubKpiStripProps) {
  const theme = useTheme();
  const health = fleetHealthPercent(stats.onlineNodes, stats.totalNodes);
  const offlineNodes = Math.max(0, stats.totalNodes - stats.onlineNodes);

  return (
    <Grid container spacing={3}>
      <Grid item xs={12} sm={6} md={3}>
        <KpiCard
          icon={<RouterIcon />}
          label="MANAGED NODES"
          value={stats.totalNodes.toString()}
          subValue={
            stats.setupRequiredNodes > 0
              ? `${stats.onlineNodes} ONLINE · ${stats.setupRequiredNodes} SETUP`
              : `${stats.onlineNodes} ONLINE · ${health}% HEALTH`
          }
          color={theme.palette.primary.main}
          gradient={`linear-gradient(135deg, ${alpha(theme.palette.primary.main, 0.2)} 0%, ${alpha(theme.palette.primary.main, 0.05)} 100%)`}
        />
      </Grid>
      <Grid item xs={12} sm={6} md={3}>
        <KpiCard
          icon={<DevicesIcon />}
          label="FLEET DEVICES"
          value={stats.totalDevices.toString()}
          subValue="ACROSS ALL SITES"
          color={theme.palette.info.main}
          gradient={`linear-gradient(135deg, ${alpha(theme.palette.info.main, 0.2)} 0%, ${alpha(theme.palette.info.main, 0.05)} 100%)`}
        />
      </Grid>
      <Grid item xs={12} sm={6} md={3}>
        <KpiCard
          icon={<WarningAmberIcon />}
          label="SETUP REQUIRED"
          value={stats.setupRequiredNodes.toString()}
          subValue={offlineNodes > 0 ? `${offlineNodes} OFFLINE` : 'ALL NODES READY'}
          color={stats.setupRequiredNodes > 0 ? theme.palette.warning.main : theme.palette.success.main}
          gradient={`linear-gradient(135deg, ${alpha(stats.setupRequiredNodes > 0 ? theme.palette.warning.main : theme.palette.success.main, 0.2)} 0%, ${alpha(stats.setupRequiredNodes > 0 ? theme.palette.warning.main : theme.palette.success.main, 0.05)} 100%)`}
        />
      </Grid>
      <Grid item xs={12} sm={6} md={3}>
        <KpiCard
          icon={<SyncIcon />}
          label="FLEET SYNC"
          value={`${health}%`}
          subValue={stats.criticalAlerts > 0 ? `${stats.criticalAlerts} ALERTS (24H)` : 'NOMINAL'}
          color={health > 90 ? theme.palette.success.main : health > 70 ? theme.palette.warning.main : theme.palette.error.main}
          gradient={`linear-gradient(135deg, ${alpha(theme.palette.secondary.main, 0.2)} 0%, ${alpha(theme.palette.secondary.main, 0.05)} 100%)`}
        />
      </Grid>
    </Grid>
  );
}
