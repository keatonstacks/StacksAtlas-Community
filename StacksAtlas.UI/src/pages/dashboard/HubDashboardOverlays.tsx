import { Alert, Snackbar } from '@mui/material';
import { HubManageSiteDialog } from '../../components/hub-dashboard/HubManageSiteDialog';
import { HubEnrollDialog } from '../../components/hub-dashboard/HubEnrollDialog';
import type { HubDashboardController } from './useHubDashboard';

interface HubDashboardOverlaysProps {
  ctrl: HubDashboardController;
}

export function HubDashboardOverlays({ ctrl }: HubDashboardOverlaysProps) {
  const {
    editNode,
    setEditNode,
    manageForm,
    setManageForm,
    saving,
    handleUpdateNode,
    enrollOpen,
    setEnrollOpen,
    enrollStep,
    setEnrollStep,
    enrollMethod,
    setEnrollMethod,
    enrolling,
    enrollUrl,
    enrollPassword,
    enrollName,
    enrollClient,
    enrollBuilding,
    enrollRoom,
    handleEnrollDirect,
    handleEnrollFieldChange,
    toast,
    setToast,
  } = ctrl;

  return (
    <>
      <HubManageSiteDialog
        open={!!editNode}
        node={editNode}
        form={manageForm}
        saving={saving}
        onClose={() => setEditNode(null)}
        onSave={handleUpdateNode}
        onChange={(patch) => setManageForm((f) => ({ ...f, ...patch }))}
      />

      <HubEnrollDialog
        open={enrollOpen}
        step={enrollStep}
        method={enrollMethod}
        enrolling={enrolling}
        enrollUrl={enrollUrl}
        enrollPassword={enrollPassword}
        enrollName={enrollName}
        enrollClient={enrollClient}
        enrollBuilding={enrollBuilding}
        enrollRoom={enrollRoom}
        onClose={() => setEnrollOpen(false)}
        onMethodChange={setEnrollMethod}
        onStepChange={setEnrollStep}
        onFieldChange={handleEnrollFieldChange}
        onEnrollDirect={handleEnrollDirect}
        onCopied={() => setToast({ open: true, message: 'Enrollment string copied.', severity: 'success' })}
        onError={(message) => setToast({ open: true, message, severity: 'error' })}
      />

      <Snackbar open={toast.open} autoHideDuration={4000} onClose={() => setToast((t) => ({ ...t, open: false }))}>
        <Alert severity={toast.severity} sx={{ borderRadius: 3, fontWeight: 700 }}>
          {toast.message}
        </Alert>
      </Snackbar>
    </>
  );
}
