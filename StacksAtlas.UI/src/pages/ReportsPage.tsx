import { useState, useEffect, useMemo, useCallback } from 'react';
import {
  Box, Typography, Grid, Button,
  alpha, useTheme, Stack, IconButton, Tooltip, Snackbar, Alert,
} from '@mui/material';
import { useNavigate } from 'react-router-dom';
import FileDownloadIcon from '@mui/icons-material/FileDownload';
import InventoryIcon from '@mui/icons-material/Inventory';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import GppMaybeIcon from '@mui/icons-material/GppMaybe';
import InsightsIcon from '@mui/icons-material/Insights';
import OpenInNewIcon from '@mui/icons-material/OpenInNew';
import ShieldIcon from '@mui/icons-material/Shield';
import UnfoldLessIcon from '@mui/icons-material/UnfoldLess';
import UnfoldMoreIcon from '@mui/icons-material/UnfoldMore';

import { ApiService } from '../services/apiService';
import { PageHeader } from '../components/pageheader';
import { FilterScopeToolbar, type FilterState } from '../components/FilterScopeToolbar';
import { ReportsSecurityPanel } from '../components/reports/ReportsSecurityPanel';
import { ReportsSectionAccordion } from '../components/reports/ReportsSectionAccordion';
import { usePortableMode } from '../hooks/usePortableMode';
import { useIsMobileLayout } from '../hooks/useIsMobileLayout';
import { PageShell } from '../components/mobile/PageShell';

interface Flapper {
  name?: string;
  ipAddress?: string;
  flapCount?: number;
  status?: string;
  severity?: 'Critical' | 'Warning';
}

interface SecurityRisk {
  deviceName: string;
  reason: string;
  level: 'Critical' | 'Warning';
  deviceId?: string;
  findingId?: string;
  title?: string;
  description?: string;
  mitigation?: string;
  severity?: string;
}

interface DataPoint {
  label: string;
  value: number;
}

interface DeltaDevice {
  ipAddress: string;
  name: string;
  vendor?: string;
  firstSeen?: string;
  lastSeen?: string;
  id: string;
}

interface NetworkDelta {
  newDevices: DeltaDevice[];
  persistentOffline: DeltaDevice[];
}

interface ServiceStat {
  port: number;
  name: string;
  count: number;
}

interface HealthInsight {
  name: string;
  ipAddress: string;
  id: string;
  value: string;
  label: string;
}

interface ReportSummary {
  totalAssets: number;
  stabilityScore: string;
  criticalAlerts: number;
  topFlappers: Flapper[];
  securityRisks: SecurityRisk[];
  distribution: {
    vendors: DataPoint[];
    types: DataPoint[];
  };
  deltas: NetworkDelta;
  services: ServiceStat[];
  insights: {
    lagging: HealthInsight[];
    flappers: HealthInsight[];
  };
}

const REPORT_SECTIONS = ['estate', 'security', 'audit', 'outliers', 'services'] as const;
type ReportSection = (typeof REPORT_SECTIONS)[number];

const REPORTS_EXPANDED_KEY = 'reports_section_expanded';

function allSectionsCollapsed(): Record<ReportSection, boolean> {
  return Object.fromEntries(REPORT_SECTIONS.map((s) => [s, false])) as Record<ReportSection, boolean>;
}

function loadPersistedExpanded(): Record<ReportSection, boolean> {
  try {
    const raw = localStorage.getItem(REPORTS_EXPANDED_KEY);
    if (!raw) return allSectionsCollapsed();
    const parsed = JSON.parse(raw) as Partial<Record<ReportSection, boolean>>;
    return REPORT_SECTIONS.reduce((acc, section) => {
      acc[section] = parsed[section] === true;
      return acc;
    }, {} as Record<ReportSection, boolean>);
  } catch {
    return allSectionsCollapsed();
  }
}

const EMPTY_SUMMARY: ReportSummary = {
  totalAssets: 0,
  stabilityScore: '0%',
  criticalAlerts: 0,
  topFlappers: [],
  securityRisks: [],
  distribution: { vendors: [], types: [] },
  deltas: { newDevices: [], persistentOffline: [] },
  services: [],
  insights: { lagging: [], flappers: [] },
};

export default function ReportsPage() {
  const theme = useTheme();
  const navigate = useNavigate();
  const { isPortable } = usePortableMode();
  const isMobile = useIsMobileLayout();

  const [appliedFilters, setAppliedFilters] = useState<FilterState>({ nodeIds: [], client: '', building: '', room: '' });
  const [toastOpen, setToastOpen] = useState(false);
  const [toastMsg, setToastMsg] = useState('');
  const [summary, setSummary] = useState<ReportSummary>(EMPTY_SUMMARY);
  const [expanded, setExpanded] = useState<Record<ReportSection, boolean>>(loadPersistedExpanded);

  useEffect(() => {
    localStorage.setItem(REPORTS_EXPANDED_KEY, JSON.stringify(expanded));
  }, [expanded]);

  useEffect(() => {
    let isMounted = true;
    const fetchSummary = async () => {
      try {
        const data = await ApiService.getReportSummary(
          isPortable
            ? {}
            : {
                nodeIds: appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined,
                client: appliedFilters.client || undefined,
                building: appliedFilters.building || undefined,
                room: appliedFilters.room || undefined,
              }
        ) as ReportSummary;
        if (isMounted && data) {
          const securityRisks = data.securityRisks ?? [];
          setSummary({
            totalAssets: data.totalAssets ?? 0,
            stabilityScore: data.stabilityScore ?? '0%',
            criticalAlerts: data.criticalAlerts ?? 0,
            topFlappers: data.topFlappers ?? [],
            securityRisks,
            distribution: data.distribution ?? { vendors: [], types: [] },
            deltas: data.deltas ?? { newDevices: [], persistentOffline: [] },
            services: data.services ?? [],
            insights: data.insights ?? { lagging: [], flappers: [] },
          });
        }
      } catch (err: unknown) {
        console.error('Failed to fetch report summary:', err);
      }
    };
    fetchSummary();
    return () => { isMounted = false; };
  }, [appliedFilters, isPortable]);

  const setSectionExpanded = useCallback((section: ReportSection, open: boolean) => {
    setExpanded((prev) => ({ ...prev, [section]: open }));
  }, []);

  const collapseAll = () => {
    setExpanded(Object.fromEntries(REPORT_SECTIONS.map((s) => [s, false])) as Record<ReportSection, boolean>);
  };

  const expandAll = () => {
    setExpanded(Object.fromEntries(REPORT_SECTIONS.map((s) => [s, true])) as Record<ReportSection, boolean>);
  };

  const handleExportCsv = async () => {
    try {
      await ApiService.downloadInventoryCsv(
        isPortable
          ? {}
          : {
              nodeIds: appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined,
              client: appliedFilters.client || undefined,
              building: appliedFilters.building || undefined,
              room: appliedFilters.room || undefined,
            }
      );
    } catch (err: unknown) {
      console.error('CSV Export failed:', err);
    }
  };

  const handleExportPdf = async () => {
    try {
      await ApiService.downloadAuditPdf(
        isPortable
          ? {}
          : {
              nodeIds: appliedFilters.nodeIds.length > 0 ? appliedFilters.nodeIds : undefined,
              client: appliedFilters.client || undefined,
              building: appliedFilters.building || undefined,
              room: appliedFilters.room || undefined,
            }
      );
    } catch (err: unknown) {
      console.error('PDF Export failed:', err);
    }
  };

  const handleDeviceClick = (id?: string) => {
    if (id) navigate(`/devices?id=${id}`);
  };

  const showToast = (message: string) => {
    setToastMsg(message);
    setToastOpen(true);
  };

  const headerStats = useMemo(() => [
    { label: 'ENGINE STATUS', value: 'READY', color: 'success.main' },
    {
      label: 'SECURITY ALERT',
      value: summary.criticalAlerts > 0 ? 'YES' : 'NO',
      color: summary.criticalAlerts > 0 ? 'error.main' : 'text.secondary',
    },
    { label: 'HEALTH INDEX', value: summary.stabilityScore },
  ], [summary.criticalAlerts, summary.stabilityScore]);

  const outlierCount = summary.insights.flappers.length + summary.insights.lagging.length;
  const auditCount = summary.deltas.newDevices.length + summary.deltas.persistentOffline.length;
  const criticalSecurity = summary.securityRisks.filter((r) => r.level === 'Critical').length;

  return (
    <PageShell isMobile={isMobile} pb={4}>
      <PageHeader
        title="SYSTEM REPORTS"
        subtitle="ASSET INVENTORY & COMPLIANCE"
        stats={headerStats}
      />

      {!isPortable && (
        <FilterScopeToolbar
          initialState={appliedFilters}
          onFilterChange={setAppliedFilters}
        />
      )}

      <Stack direction="row" justifyContent="flex-end" spacing={1} sx={{ mb: 2 }}>
        <Button size="small" startIcon={<UnfoldLessIcon />} onClick={collapseAll} sx={{ fontWeight: 700 }}>
          Collapse all
        </Button>
        <Button size="small" startIcon={<UnfoldMoreIcon />} onClick={expandAll} sx={{ fontWeight: 700 }}>
          Expand all
        </Button>
      </Stack>

      <ReportsSectionAccordion
        id="estate"
        title="ESTATE COMPOSITION"
        icon={<TrendingUpIcon color="primary" />}
        badge={`${summary.totalAssets} assets`}
        expanded={expanded.estate}
        onExpandedChange={(open) => setSectionExpanded('estate', open)}
      >
        <Grid container spacing={4}>
          <Grid item xs={12} sm={6}>
            <Typography variant="overline" color="text.secondary" sx={{ fontWeight: 900 }}>Top Vendors</Typography>
            <Stack spacing={1.5} sx={{ mt: 1 }}>
              {summary.distribution.vendors.map((v) => (
                <Box key={v.label}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Typography variant="caption" fontWeight={700}>{v.label}</Typography>
                    <Typography variant="caption" fontWeight={900}>{v.value.toString()}</Typography>
                  </Box>
                  <Box sx={{ width: '100%', height: 6, bgcolor: alpha(theme.palette.primary.main, 0.1), borderRadius: 3 }}>
                    <Box sx={{
                      width: `${summary.totalAssets > 0 ? (v.value / summary.totalAssets) * 100 : 0}%`,
                      height: '100%',
                      bgcolor: 'primary.main',
                      borderRadius: 3,
                      transition: 'width 1s ease-in-out',
                    }} />
                  </Box>
                </Box>
              ))}
            </Stack>
          </Grid>
          <Grid item xs={12} sm={6}>
            <Typography variant="overline" color="text.secondary" sx={{ fontWeight: 900 }}>Device Types</Typography>
            <Stack spacing={1.5} sx={{ mt: 1 }}>
              {summary.distribution.types.slice(0, 5).map((t) => (
                <Box key={t.label}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', mb: 0.5 }}>
                    <Typography variant="caption" fontWeight={700}>{t.label}</Typography>
                    <Typography variant="caption" fontWeight={900}>{t.value.toString()}</Typography>
                  </Box>
                  <Box sx={{ width: '100%', height: 6, bgcolor: alpha(theme.palette.success.main, 0.1), borderRadius: 3 }}>
                    <Box sx={{
                      width: `${summary.totalAssets > 0 ? (t.value / summary.totalAssets) * 100 : 0}%`,
                      height: '100%',
                      bgcolor: 'success.main',
                      borderRadius: 3,
                      transition: 'width 1s ease-in-out',
                    }} />
                  </Box>
                </Box>
              ))}
            </Stack>
          </Grid>
        </Grid>
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="security"
        title="SECURITY HYGIENE"
        icon={<ShieldIcon color="error" />}
        badge={
          summary.securityRisks.length > 0
            ? `${summary.securityRisks.length} finding${summary.securityRisks.length === 1 ? '' : 's'}${criticalSecurity > 0 ? ` · ${criticalSecurity} critical` : ''}`
            : 'Clean'
        }
        expanded={expanded.security}
        onExpandedChange={(open) => setSectionExpanded('security', open)}
      >
        <ReportsSecurityPanel
          risks={summary.securityRisks}
          onViewDevice={handleDeviceClick}
          onCopied={showToast}
        />
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="audit"
        title="AUDIT FEED (LAST 24H)"
        icon={<InventoryIcon color="info" />}
        badge={auditCount > 0 ? `${auditCount} change${auditCount === 1 ? '' : 's'}` : 'No changes'}
        expanded={expanded.audit}
        onExpandedChange={(open) => setSectionExpanded('audit', open)}
      >
        <Typography variant="caption" sx={{ fontWeight: 900, color: 'success.main', display: 'flex', alignItems: 'center', gap: 0.5, mb: 1 }}>
          <Box component="span" sx={{ width: 8, height: 8, bgcolor: 'success.main', borderRadius: '50%' }} />
          NEW NEIGHBORS
        </Typography>
        <Stack spacing={1} sx={{ mb: 3 }}>
          {summary.deltas.newDevices.length > 0 ? summary.deltas.newDevices.map((d) => (
            <Box
              key={d.id}
              sx={{ p: 1, bgcolor: alpha(theme.palette.success.main, 0.05), borderRadius: 1.5, display: 'flex', justifyContent: 'space-between', alignItems: 'center', userSelect: 'text' }}
            >
              <Box>
                <Typography variant="caption" fontWeight={700}>{d.name || d.ipAddress}</Typography>
                <Typography variant="caption" sx={{ opacity: 0.6, display: 'block' }}>{d.vendor || 'Unknown'}</Typography>
              </Box>
              <Tooltip title="Open device">
                <IconButton size="small" onClick={() => handleDeviceClick(d.id)} aria-label="Open device">
                  <OpenInNewIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Box>
          )) : (
            <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic', pl: 2 }}>No new devices detected.</Typography>
          )}
        </Stack>

        <Typography variant="caption" sx={{ fontWeight: 900, color: 'error.main', display: 'flex', alignItems: 'center', gap: 0.5, mb: 1 }}>
          <Box component="span" sx={{ width: 8, height: 8, bgcolor: 'error.main', borderRadius: '50%' }} />
          MISSING FRIENDS {'(OFFLINE > 24H)'}
        </Typography>
        <Stack spacing={1}>
          {summary.deltas.persistentOffline.length > 0 ? summary.deltas.persistentOffline.map((d) => (
            <Box
              key={d.id}
              sx={{ p: 1, bgcolor: alpha(theme.palette.error.main, 0.05), borderRadius: 1.5, display: 'flex', justifyContent: 'space-between', alignItems: 'center', userSelect: 'text' }}
            >
              <Box>
                <Typography variant="caption" fontWeight={700}>{d.name || d.ipAddress}</Typography>
                <Typography variant="caption" sx={{ opacity: 0.6, display: 'block' }}>
                  Lost {d.lastSeen ? new Date(d.lastSeen).toLocaleDateString() : 'unknown'}
                </Typography>
              </Box>
              <Tooltip title="Open device">
                <IconButton size="small" onClick={() => handleDeviceClick(d.id)} aria-label="Open device">
                  <OpenInNewIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Box>
          )) : (
            <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic', pl: 2 }}>Zero persistent outages.</Typography>
          )}
        </Stack>
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="outliers"
        title="HEALTH OUTLIERS"
        icon={<GppMaybeIcon color="warning" />}
        badge={outlierCount > 0 ? `${outlierCount} outlier${outlierCount === 1 ? '' : 's'}` : 'Optimal'}
        expanded={expanded.outliers}
        onExpandedChange={(open) => setSectionExpanded('outliers', open)}
      >
        <Grid container spacing={2}>
          {[...summary.insights.flappers, ...summary.insights.lagging].map((insight) => (
            <Grid item xs={12} key={insight.id}>
              <Box
                sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.warning.main, 0.03), border: '1px solid', borderColor: alpha(theme.palette.warning.main, 0.1), display: 'flex', justifyContent: 'space-between', alignItems: 'center', userSelect: 'text' }}
              >
                <Box>
                  <Typography variant="body2" fontWeight={800}>{insight.name || insight.ipAddress}</Typography>
                  <Typography variant="caption" sx={{ opacity: 0.6 }}>{insight.label}</Typography>
                </Box>
                <Stack direction="row" alignItems="center" spacing={1}>
                  <Typography variant="h6" fontWeight={900} color="warning.main">{insight.value}</Typography>
                  <Tooltip title="Open device">
                    <IconButton size="small" onClick={() => handleDeviceClick(insight.id)} aria-label="Open device">
                      <OpenInNewIcon fontSize="small" />
                    </IconButton>
                  </Tooltip>
                </Stack>
              </Box>
            </Grid>
          ))}
          {outlierCount === 0 && (
            <Grid item xs={12}>
              <Typography variant="body2" color="text.secondary" sx={{ py: 4, textAlign: 'center' }}>
                System performance is optimal. No outliers.
              </Typography>
            </Grid>
          )}
        </Grid>
      </ReportsSectionAccordion>

      <ReportsSectionAccordion
        id="services"
        title="GLOBAL SERVICE MATRIX"
        icon={<InsightsIcon color="primary" />}
        badge={`${summary.services.length} services`}
        expanded={expanded.services}
        onExpandedChange={(open) => setSectionExpanded('services', open)}
      >
        <Grid container spacing={2}>
          {summary.services.length > 0 ? summary.services.map((s) => (
            <Grid item xs={6} sm={4} md={2} lg={1.5} key={s.port}>
              <Box sx={{ p: 2, textAlign: 'center', borderRadius: 2, border: '1px solid', borderColor: 'divider', bgcolor: alpha(theme.palette.primary.main, 0.02) }}>
                <Typography variant="h5" fontWeight={900}>{s.count}</Typography>
                <Typography variant="caption" fontWeight={800} sx={{ opacity: 0.5 }}>{s.name}</Typography>
                <Typography variant="caption" display="block" sx={{ fontSize: '0.6rem', opacity: 0.3 }}>PORT {s.port}</Typography>
              </Box>
            </Grid>
          )) : (
            <Grid item xs={12}>
              <Typography variant="body2" color="text.secondary" sx={{ py: 2, textAlign: 'center' }}>
                No open services recorded in the current scope.
              </Typography>
            </Grid>
          )}
        </Grid>
      </ReportsSectionAccordion>

      <Box
        sx={{
          position: 'sticky',
          bottom: 0,
          zIndex: 10,
          py: 2,
          mt: 2,
          mx: { xs: -2, md: -4 },
          px: { xs: 2, md: 4 },
          bgcolor: alpha(theme.palette.background.default, 0.94),
          backdropFilter: 'blur(8px)',
          borderTop: '1px solid',
          borderColor: 'divider',
        }}
      >
        <Stack
          direction={{ xs: 'column', sm: 'row' }}
          justifyContent="center"
          alignItems="center"
          spacing={2}
        >
          <Button
            variant="contained"
            size="large"
            startIcon={<FileDownloadIcon />}
            onClick={handleExportCsv}
            sx={{ borderRadius: 3, px: 4, py: 1.5, fontWeight: 900, width: { xs: '100%', sm: 'auto' } }}
          >
            EXPORT FULL AUDIT (CSV)
          </Button>
          <Button
            variant="outlined"
            size="large"
            startIcon={<FileDownloadIcon />}
            onClick={handleExportPdf}
            sx={{ borderRadius: 3, px: 4, py: 1.5, fontWeight: 900, width: { xs: '100%', sm: 'auto' } }}
          >
            GENERATE LEAVE-BEHIND (PDF)
          </Button>
        </Stack>
        <Typography variant="caption" display="block" textAlign="center" sx={{ mt: 1.5, opacity: 0.5 }}>
          Exports are full-fidelity. On-screen filters are view-only.
        </Typography>
      </Box>

      <Snackbar open={toastOpen} autoHideDuration={3000} onClose={() => setToastOpen(false)}>
        <Alert severity="success" onClose={() => setToastOpen(false)} sx={{ width: '100%' }}>
          {toastMsg}
        </Alert>
      </Snackbar>

      <Box sx={{ mt: 6, pt: 4, borderTop: '1px solid', borderColor: 'divider', textAlign: 'center', opacity: 0.5 }}>
        <Typography variant="caption" display="block" fontWeight={800}>
          StacksAtlas GOVERNANCE REPORT | TECHNICAL CONFIDENTIAL
        </Typography>
        <Typography variant="caption">
          {'©'} {new Date().getFullYear()} StacksAtlas Professional Services. System Commissioning Audit.
        </Typography>
      </Box>
    </PageShell>
  );
}
