import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import { NodeDashboardDesktop } from './NodeDashboardDesktop';
import { NodeDashboardMobile } from './NodeDashboardMobile';

export default function NodeDashboard() {
  const isMobile = useIsMobileLayout();
  return isMobile ? <NodeDashboardMobile /> : <NodeDashboardDesktop />;
}
