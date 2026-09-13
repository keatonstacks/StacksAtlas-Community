import {

  Box,

  CircularProgress,

  Drawer,

  Tab,

  Tabs,

  Typography,

} from '@mui/material';

import { useEffect, useRef, useState } from 'react';

import { DeviceDrawerHeader } from './DeviceDrawerHeader';

import { DeviceDrawerOverviewTab } from './DeviceDrawerOverviewTab';

import { DeviceDrawerManageTab } from './DeviceDrawerManageTab';

import { DeviceDrawerNetworkTab } from './DeviceDrawerNetworkTab';

import { DeviceDrawerSecurityTab } from './DeviceDrawerSecurityTab';

import { DeviceDrawerControlsTab } from './DeviceDrawerControlsTab';

import { DeviceDrawerHistoryTab } from './DeviceDrawerHistoryTab';

import { useDeviceDrawer } from './useDeviceDrawer';

import { visibleDeviceDrawerTabs, type DeviceDrawerProps, type DeviceDrawerTab } from './types';



function normalizeDeviceId(id: string | undefined | null): string | null {

  if (!id) return null;

  return String(id).toLowerCase();

}



export function DeviceDrawer(props: DeviceDrawerProps) {

  const {

    open,

    onClose,

    device,

    users,

    onDeviceUpdate,

    onDelete,

    onRestore,

    onHardDelete,

    onRestoreToFleet,

    viewMode = 'active',

    nodesMap,

    onNotify,

  } = props;



  const [activeTab, setActiveTab] = useState<DeviceDrawerTab>('overview');

  const drawerDeviceKeyRef = useRef<string | null>(null);



  useEffect(() => {

    if (!open) {

      drawerDeviceKeyRef.current = null;

      return;

    }

    const key = normalizeDeviceId(device?.id);

    if (!key) return;

    if (drawerDeviceKeyRef.current !== key) {

      drawerDeviceKeyRef.current = key;

      setActiveTab('overview');

    }

  }, [open, device?.id]);



  const isArchive = viewMode === 'archive';

  const isRemoved = viewMode === 'removed';

  const drawer = useDeviceDrawer(device, open, onDeviceUpdate, onNotify);

  const canAct = drawer.role?.toLowerCase() !== 'viewer' && !isRemoved;

  const drawerTabs = visibleDeviceDrawerTabs(drawer.isPortable);

  useEffect(() => {
    if (!drawerTabs.some((t) => t.id === activeTab)) {
      setActiveTab('overview');
    }
  }, [activeTab, drawerTabs]);



  return (

    <Drawer

      anchor="right"

      open={open}

      onClose={onClose}

      PaperProps={{

        sx: {

          width: { xs: '100%', sm: 520, md: 560 },

          borderTopLeftRadius: { sm: 16 },

          borderBottomLeftRadius: { sm: 16 },

          display: 'flex',

          flexDirection: 'column',

          overflow: 'hidden',

        },

      }}

    >

      {device ? (

        <>

          <DeviceDrawerHeader

            device={device}

            onClose={onClose}

            isPinging={drawer.isPinging}

            pingResult={drawer.pingResult}

            isWaking={drawer.isWaking}

            wakeResult={drawer.wakeResult}

            isDeepScanning={drawer.isDeepScanning}

            scanProgress={drawer.scanProgress}

            deepScanStatus={drawer.deepScanStatus}

            hasDeepScanReady={drawer.hasDeepScanReady}

            deepScanBlockedReason={drawer.deepScanBlockedReason}

            canAct={canAct}

            onPing={drawer.handlePingTest}

            onWake={drawer.handleWake}

            onDeepScan={drawer.handleDeepScan}

          />



          <Tabs

            value={activeTab}

            onChange={(_, v) => setActiveTab(v)}

            variant="scrollable"

            scrollButtons="auto"

            sx={{

              flexShrink: 0,

              minHeight: 40,

              borderBottom: 1,

              borderColor: 'divider',

              px: 1,

              '& .MuiTab-root': { minHeight: 40, fontWeight: 700, fontSize: '0.75rem', textTransform: 'none' },

            }}

          >

            {drawerTabs.map((tab) => (

              <Tab key={tab.id} value={tab.id} label={tab.label} />

            ))}

          </Tabs>



          <Box sx={{ flex: 1, overflowY: 'auto', p: 2 }}>

            {activeTab === 'overview' && (

              <DeviceDrawerOverviewTab

                device={device}

                isHub={drawer.isHub}

                nodesMap={nodesMap}

                isRemoved={isRemoved}

                isArchive={isArchive}

                role={drawer.role}

                isAdmin={!!drawer.isAdmin}

                onDeviceUpdate={onDeviceUpdate}

                onNotify={onNotify}

                onDelete={onDelete}

                onRestore={onRestore}

                onHardDelete={onHardDelete}

                onRestoreToFleet={onRestoreToFleet}

              />

            )}

            {activeTab === 'manage' && !drawer.isPortable && (

              <DeviceDrawerManageTab

                device={device}

                role={drawer.role}

                isAdmin={!!drawer.isAdmin}

                userId={drawer.userId}

                user={drawer.user}

                users={users}

                deviceAlertsEnabled={drawer.deviceAlertsEnabled}

                userNotificationsGloballyEnabled={drawer.userNotificationsGloballyEnabled}

                isViewer={drawer.role?.toLowerCase() === 'viewer'}

                onDeviceUpdate={onDeviceUpdate}

                onToggleAlerts={drawer.handleToggleAlerts}

              />

            )}

            {activeTab === 'network' && (

              <DeviceDrawerNetworkTab

                device={device}

                isHub={drawer.isHub}

                serviceDetails={drawer.serviceDetails}

                isDeepScanning={drawer.isDeepScanning}

                scanProgress={drawer.scanProgress}

                isIntelligenceOpen={drawer.isIntelligenceOpen}

                onToggleIntelligence={() => drawer.setIsIntelligenceOpen((o) => !o)}

              />

            )}

            {activeTab === 'security' && (

              <DeviceDrawerSecurityTab

                device={device}

                role={drawer.role}

                onIgnoreRisk={drawer.handleIgnoreRisk}

                onDeviceUpdate={onDeviceUpdate}

                onNotify={onNotify}

              />

            )}

            {activeTab === 'controls' && (

              <DeviceDrawerControlsTab

                device={device}

                isHub={drawer.isHub}

                isAdmin={!!drawer.isAdmin}

                onDeviceUpdate={onDeviceUpdate}

              />

            )}

            {activeTab === 'history' && (

              <DeviceDrawerHistoryTab

                device={device}

                role={drawer.role}

                isHub={drawer.isHub}

                onResetMetrics={drawer.handleResetMetrics}

              />

            )}

          </Box>

        </>

      ) : (

        <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', pt: 10, gap: 2, p: 3 }}>

          <CircularProgress size={24} />

          <Typography variant="caption" color="text.secondary">Loading device…</Typography>

        </Box>

      )}

    </Drawer>

  );

}


