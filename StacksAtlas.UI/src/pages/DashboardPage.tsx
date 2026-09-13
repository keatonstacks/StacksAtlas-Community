import { useGlobalStats } from '../context/GlobalStatsContext';
import HubDashboard from './HubDashboard';
import NodeDashboard from './dashboard/NodeDashboard';

export default function DashboardPage() {
  const { isHub } = useGlobalStats();

  if (isHub) {
    return <HubDashboard />;
  }

  return <NodeDashboard />;
}
