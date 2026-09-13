import { buildAlertsPageUrl } from '../../components/alerts/alertsUtils';
import { buildAlertClipboardText } from '../../components/hub-dashboard/hubDashboardUtils';
import { ApiService } from '../../services/apiService';
import type { HubDashboardController } from './useHubDashboard';

export function useHubAlertHandlers(ctrl: HubDashboardController) {
  const { navigate, filters, setToast, setAlerts } = ctrl;

  return {
    onViewAll: () =>
      navigate(
        buildAlertsPageUrl({
          tab: 'live',
          client: filters.client || undefined,
          building: filters.building || undefined,
        }),
      ),
    onCopy: (alert: Parameters<typeof buildAlertClipboardText>[0]) => {
      navigator.clipboard.writeText(buildAlertClipboardText(alert));
      setToast({ open: true, message: 'Copied to clipboard.', severity: 'success' });
    },
    onViewDevice: (id: string) => navigate(`/devices?id=${id}`),
    onOpenInAlerts: (alert: { alertType?: string; nodeId?: string }) => {
      const isAudit = alert.alertType === 'NodeDecoupled' || alert.alertType === 'SiteResetInitiated';
      navigate(
        buildAlertsPageUrl({
          tab: isAudit ? 'audit' : 'history',
          nodeId: alert.nodeId,
          client: filters.client || undefined,
          building: filters.building || undefined,
        }),
      );
    },
    onDismiss: async (id: string) => {
      try {
        await ApiService.deleteAlert(id);
        setAlerts((prev) => prev.filter((a) => a.id !== id));
        setToast({ open: true, message: 'Alert dismissed.', severity: 'success' });
      } catch (err: unknown) {
        const message = err instanceof Error ? err.message : 'Unknown error';
        setToast({ open: true, message: `Dismiss failed: ${message}`, severity: 'error' });
      }
    },
  };
}
