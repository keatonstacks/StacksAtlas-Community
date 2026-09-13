import { useIsMobileLayout } from '../hooks/useIsMobileLayout';
import { HubDashboardDesktop } from './dashboard/HubDashboardDesktop';
import { HubDashboardMobile } from './dashboard/HubDashboardMobile';

export default function HubDashboard() {
  const isMobile = useIsMobileLayout();
  return isMobile ? <HubDashboardMobile /> : <HubDashboardDesktop />;
}
