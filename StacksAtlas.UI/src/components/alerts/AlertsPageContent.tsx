import { Alert, Snackbar, Tab, Tabs } from '@mui/material';
import RadarIcon from '@mui/icons-material/Radar';
import HistoryIcon from '@mui/icons-material/NotificationsActive';
import GavelIcon from '@mui/icons-material/Gavel';
import { PageShell } from '../mobile/PageShell';
import { PageHeader } from '../pageheader';
import { useIsMobileLayout } from '../../hooks/useIsMobileLayout';
import { AlertsFleetScopeBar } from './AlertsFleetScopeBar';
import { AlertsLiveTab } from './AlertsLiveTab';
import { AlertsHistoryTable, AlertsHistoryToolbar } from './AlertsHistoryTable';
import { AlertsBatchBar } from './AlertsBatchBar';
import { useAlertsPage } from './useAlertsPage';
import { ALERTS_TABS } from './types';

const HISTORY_TYPE_FILTERS = ['All', 'DeviceDown', 'DeviceUp', 'NewDeviceDiscovered'];
const AUDIT_TYPE_FILTERS = ['All', 'NodeDecoupled', 'SiteResetInitiated', 'NodeIdentityReconciled'];

export function AlertsPageContent() {
  const alerts = useAlertsPage();
  const isMobile = useIsMobileLayout();

  const tabIcons = {
    live: <RadarIcon sx={{ fontSize: 18 }} />,
    history: <HistoryIcon sx={{ fontSize: 18 }} />,
    audit: <GavelIcon sx={{ fontSize: 18 }} />,
  };

  return (
    <PageShell isMobile={isMobile} pb={isMobile ? 12 : 15}>
      <PageHeader
        title="SYSTEM INTELLIGENCE"
        subtitle={isMobile ? 'LIVE · HISTORY · AUDIT' : 'LIVE EVENTS · NOTIFICATION HISTORY · AUDIT / REPLAY'}
        stats={
          isMobile
            ? [
                {
                  label: 'LIVE',
                  value: alerts.filteredGroups.length,
                  color: alerts.filteredGroups.some((g) => g.severity === 'Critical')
                    ? 'error.main'
                    : 'text.primary',
                },
                {
                  label: 'LOGS',
                  value: alerts.dispatchedHistory.length,
                },
              ]
            : [
                {
                  label: 'STATUS',
                  value: alerts.loading ? 'SYNCING' : 'LIVE',
                  color: alerts.loading ? 'warning.main' : 'success.main',
                },
                {
                  label: 'LIVE EVENTS',
                  value: alerts.filteredGroups.length,
                  color: alerts.filteredGroups.some((g) => g.severity === 'Critical')
                    ? 'error.main'
                    : 'text.primary',
                },
                {
                  label: 'NOTIFICATIONS',
                  value: alerts.dispatchedHistory.length,
                },
                {
                  label: 'AUDIT',
                  value: alerts.auditHistory.length,
                  color: alerts.auditHistory.length > 0 ? 'info.main' : 'text.secondary',
                },
              ]
        }
      />

      <AlertsFleetScopeBar
        filters={alerts.appliedFilters}
        onFilterChange={alerts.setAppliedFilters}
        scopeActive={alerts.scopeActive}
      />

      <Tabs
        value={alerts.activeTab}
        onChange={(_, v) => alerts.setActiveTab(v)}
        variant="scrollable"
        scrollButtons="auto"
        allowScrollButtonsMobile
        sx={{
          mb: 3,
          maxWidth: '100%',
          '& .MuiTabs-scroller': { overflow: 'auto !important' },
          '& .MuiTabs-indicator': { height: 3, borderRadius: '3px 3px 0 0' },
          '& .MuiTab-root': {
            fontWeight: 900,
            letterSpacing: 1,
            fontSize: '0.75rem',
            minHeight: 48,
            minWidth: { xs: 'auto', sm: 120 },
            px: { xs: 1.5, sm: 2 },
          },
        }}
      >
        {ALERTS_TABS.map((tab) => (
          <Tab
            key={tab.id}
            value={tab.id}
            icon={tabIcons[tab.id]}
            iconPosition="start"
            label={tab.label.toUpperCase()}
          />
        ))}
      </Tabs>

      {alerts.activeTab === 'live' && (
        <AlertsLiveTab
          loading={alerts.loading}
          groups={alerts.filteredGroups}
          devices={alerts.devices}
          severityFilter={alerts.severityFilter}
          search={alerts.search}
          selectedGroupKeys={alerts.selectedGroupKeys}
          eventsCount={alerts.events.length}
          onSeverityChange={alerts.setSeverityFilter}
          onSearchChange={alerts.setSearch}
          onSelectedChange={alerts.setSelectedGroupKeys}
          onClearAll={alerts.clearAllEvents}
          onCopy={alerts.copyDetailedBriefs}
          onDelete={alerts.deleteGroups}
        />
      )}

      {alerts.activeTab === 'history' && (
        <>
          <AlertsHistoryToolbar
            items={alerts.filteredHistory}
            selectedIds={alerts.selectedHistoryIds}
            search={alerts.historySearch}
            typeFilter={alerts.historyFilter}
            typeOptions={HISTORY_TYPE_FILTERS}
            totalCount={alerts.dispatchedHistory.length}
            onSearchChange={alerts.setHistorySearch}
            onTypeFilterChange={alerts.setHistoryFilter}
            onSelectedChange={alerts.setSelectedHistoryIds}
            onClearAll={alerts.clearHistory}
          />
          <AlertsHistoryTable
            items={alerts.filteredHistory}
            loading={alerts.historyLoading}
            orderBy={alerts.orderBy}
            order={alerts.order}
            selectedIds={alerts.selectedHistoryIds}
            onSelectedChange={alerts.setSelectedHistoryIds}
            onRequestSort={alerts.handleRequestSort}
            onCopy={alerts.copyNotificationBrief}
            onDelete={alerts.deleteAlerts}
          />
        </>
      )}

      {alerts.activeTab === 'audit' && (
        <>
          <AlertsHistoryToolbar
            items={alerts.filteredAudit}
            selectedIds={alerts.selectedAuditIds}
            search={alerts.auditSearch}
            typeFilter={alerts.auditFilter}
            typeOptions={AUDIT_TYPE_FILTERS}
            totalCount={alerts.auditHistory.length}
            auditMode
            onSearchChange={alerts.setAuditSearch}
            onTypeFilterChange={alerts.setAuditFilter}
            onSelectedChange={alerts.setSelectedAuditIds}
            onClearAll={alerts.clearHistory}
          />
          <AlertsHistoryTable
            items={alerts.filteredAudit}
            loading={alerts.historyLoading}
            auditMode
            orderBy={alerts.orderBy}
            order={alerts.order}
            selectedIds={alerts.selectedAuditIds}
            onSelectedChange={alerts.setSelectedAuditIds}
            onRequestSort={alerts.handleRequestSort}
            onCopy={alerts.copyNotificationBrief}
            onDelete={alerts.deleteAlerts}
          />
        </>
      )}

      <AlertsBatchBar
        activeTab={alerts.activeTab}
        selectedGroupKeys={alerts.selectedGroupKeys}
        selectedHistoryIds={alerts.selectedHistoryIds}
        selectedAuditIds={alerts.selectedAuditIds}
        filteredGroups={alerts.filteredGroups}
        onClearLiveSelection={() => alerts.setSelectedGroupKeys([])}
        onClearHistorySelection={() => alerts.setSelectedHistoryIds([])}
        onClearAuditSelection={() => alerts.setSelectedAuditIds([])}
        onCopyLive={alerts.copyDetailedBriefs}
        onDeleteLive={alerts.deleteGroups}
        onDeleteHistory={alerts.deleteAlerts}
      />

      <Snackbar
        open={alerts.toastOpen}
        autoHideDuration={3000}
        onClose={() => alerts.setToastOpen(false)}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="success" variant="filled" sx={{ borderRadius: 2, fontWeight: 800 }}>
          {alerts.toastMsg}
        </Alert>
      </Snackbar>
    </PageShell>
  );
}
