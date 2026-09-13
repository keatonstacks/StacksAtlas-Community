import { useCallback, useEffect, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  alpha,
  Box,
  Chip,
  CircularProgress,
  FormControl,
  IconButton,
  InputBase,
  MenuItem,
  Paper,
  Select,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Tooltip,
  Typography,
  useTheme,
  Button,
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Search as SearchIcon,
  FactCheck as FactCheckIcon,
  Terminal as TerminalIcon,
} from '@mui/icons-material';

import { ApiService } from '../services/apiService';
import { PageHeader } from '../components/pageheader';
import { PageShell } from '../components/mobile/PageShell';
import { useIsMobileLayout } from '../hooks/useIsMobileLayout';
import type { AuditEvent, AuditActionFilter, AuditOutcomeFilter } from '../models/AuditEvent';
import {
  AUDIT_ACTION_FILTERS,
  formatAuditAction,
  outcomeColor,
} from '../models/AuditEvent';
import { normalizeIpAddress } from '../components/alerts/alertsUtils';

const PAGE_SIZE = 100;

type TimeRange = '24h' | '7d' | '30d' | 'all';

const TIME_RANGE_OPTIONS: { value: TimeRange; label: string }[] = [
  { value: '24h', label: 'Last 24 hours' },
  { value: '7d', label: 'Last 7 days' },
  { value: '30d', label: 'Last 30 days' },
  { value: 'all', label: 'All time' },
];

function sinceIsoForRange(range: TimeRange): string | undefined {
  if (range === 'all') return undefined;
  const hours = range === '24h' ? 24 : range === '7d' ? 24 * 7 : 24 * 30;
  return new Date(Date.now() - hours * 60 * 60 * 1000).toISOString();
}

function formatTimestamp(iso: string): string {
  try {
    return new Date(iso).toLocaleString(undefined, {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
  } catch {
    return iso;
  }
}

function AuditEventCard({ event }: { event: AuditEvent }) {
  const theme = useTheme();
  return (
    <Paper
      variant="outlined"
      sx={{
        p: 2,
        borderRadius: 2.5,
        borderColor: alpha(theme.palette.divider, 0.12),
      }}
    >
      <Stack spacing={1}>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" gap={1}>
          <Typography variant="caption" fontWeight={800} color="text.secondary">
            {formatTimestamp(event.timestampUtc)}
          </Typography>
          <Chip
            size="small"
            label={event.outcome}
            color={outcomeColor(event.outcome)}
            sx={{ fontWeight: 800, fontSize: '0.65rem' }}
          />
        </Stack>
        <Typography variant="body2" fontWeight={800}>
          {formatAuditAction(event.action)}
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {event.actorUsername}
          {event.actorRole ? ` · ${event.actorRole}` : ''}
          {event.clientIp ? ` · ${normalizeIpAddress(event.clientIp)}` : ''}
        </Typography>
        {(event.detail || event.resourceId) && (
          <Typography variant="caption" sx={{ wordBreak: 'break-word' }}>
            {[event.resourceType, event.resourceId, event.detail].filter(Boolean).join(' · ')}
          </Typography>
        )}
        {event.nodeName && (
          <Typography variant="caption" color="text.secondary">
            Node: {event.nodeName}
          </Typography>
        )}
      </Stack>
    </Paper>
  );
}

type AuditPageResponse = {
  items?: AuditEvent[];
  hasMore?: boolean;
  count?: number;
};

function parseAuditResponse(data: unknown): { items: AuditEvent[]; hasMore: boolean } {
  if (Array.isArray(data)) {
    return { items: data, hasMore: data.length >= PAGE_SIZE };
  }
  const page = data as AuditPageResponse;
  const items = Array.isArray(page?.items) ? page.items : [];
  return { items, hasMore: Boolean(page?.hasMore) };
}

export default function AuditPage() {
  const theme = useTheme();
  const navigate = useNavigate();
  const isMobile = useIsMobileLayout();
  const [events, setEvents] = useState<AuditEvent[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [hasMore, setHasMore] = useState(false);
  const [search, setSearch] = useState('');
  const [actionFilter, setActionFilter] = useState<AuditActionFilter>('All');
  const [outcomeFilter, setOutcomeFilter] = useState<AuditOutcomeFilter>('All');
  const [timeRange, setTimeRange] = useState<TimeRange>('24h');
  const [nodes, setNodes] = useState<{ id: string; name: string }[]>([]);
  const [selectedNode, setSelectedNode] = useState('all');
  const [isHub, setIsHub] = useState(false);

  const fetchNodes = async () => {
    try {
      const data = await ApiService.getFederationNodes();
      if (Array.isArray(data)) {
        setNodes(data);
        setIsHub(true);
      }
    } catch {
      setIsHub(false);
    }
  };

  const buildQuery = useCallback(
    (skip: number) => {
      const actionPrefix = actionFilter === 'All' ? undefined : actionFilter;
      const hubOnly = isHub && selectedNode === 'local';
      const nodeIds =
        isHub && selectedNode !== 'all' && selectedNode !== 'local'
          ? [selectedNode]
          : undefined;
      return {
        skip,
        take: PAGE_SIZE,
        action: actionPrefix,
        nodeIds,
        since: sinceIsoForRange(timeRange),
        hubOnly,
      };
    },
    [actionFilter, isHub, selectedNode, timeRange],
  );

  const fetchEvents = useCallback(async () => {
    setLoading(true);
    try {
      const data = await ApiService.getAuditEvents(buildQuery(0));
      const page = parseAuditResponse(data);
      setEvents(page.items);
      setHasMore(page.hasMore);
    } catch (err) {
      console.error('Failed to load audit events:', err);
      setEvents([]);
      setHasMore(false);
    } finally {
      setLoading(false);
    }
  }, [buildQuery]);

  const loadMore = async () => {
    setLoadingMore(true);
    try {
      const data = await ApiService.getAuditEvents(buildQuery(events.length));
      const page = parseAuditResponse(data);
      setEvents((prev) => {
        const seen = new Set(prev.map((e) => e.id));
        return [...prev, ...page.items.filter((e) => !seen.has(e.id))];
      });
      setHasMore(page.hasMore);
    } catch (err) {
      console.error('Failed to load more audit events:', err);
      setHasMore(false);
    } finally {
      setLoadingMore(false);
    }
  };

  useEffect(() => {
    fetchNodes();
  }, []);

  useEffect(() => {
    fetchEvents();
  }, [fetchEvents]);

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    return events.filter((e) => {
      if (outcomeFilter !== 'All' && e.outcome.toLowerCase() !== outcomeFilter.toLowerCase()) {
        return false;
      }
      if (!q) return true;
      const haystack = [
        e.action,
        e.actorUsername,
        e.actorRole,
        e.resourceType,
        e.resourceId,
        e.detail,
        e.clientIp,
        e.nodeName,
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return haystack.includes(q);
    });
  }, [events, outcomeFilter, search]);

  const headerStats = [
    { label: 'SHOWN', value: String(filtered.length) },
    { label: 'WINDOW', value: timeRange === 'all' ? 'ALL' : timeRange.toUpperCase() },
    { label: 'SOURCE', value: isHub ? (selectedNode === 'all' ? 'FLEET' : selectedNode === 'local' ? 'HUB' : 'NODE') : 'LOCAL' },
  ];

  const rangeLabel = TIME_RANGE_OPTIONS.find((o) => o.value === timeRange)?.label ?? 'Last 24 hours';

  return (
    <PageShell isMobile={isMobile}>
      <PageHeader
        title="AUDIT LOG"
        subtitle="WHO DID WHAT ON THIS APPLIANCE"
        stats={headerStats}
      />

      <Paper
        variant="outlined"
        sx={{
          borderRadius: 3,
          overflow: 'hidden',
          borderColor: alpha(theme.palette.divider, 0.12),
        }}
      >
        <Box
          sx={{
            p: 2,
            borderBottom: '1px solid',
            borderColor: alpha(theme.palette.divider, 0.08),
            bgcolor: alpha(theme.palette.primary.main, 0.02),
          }}
        >
          <Stack
            direction={isMobile ? 'column' : 'row'}
            spacing={1.5}
            alignItems={isMobile ? 'stretch' : 'center'}
            justifyContent="space-between"
          >
            <Stack direction="row" spacing={1} alignItems="center">
              <FactCheckIcon color="primary" sx={{ fontSize: 20 }} />
              <Typography variant="caption" fontWeight={900} letterSpacing={1} color="text.secondary">
                HUMAN ACTIONS ONLY, NOT ENGINE LOGS
              </Typography>
            </Stack>
            <Stack direction={isMobile ? 'column' : 'row'} spacing={1} alignItems="center">
              <Button
                size="small"
                variant="outlined"
                startIcon={<TerminalIcon />}
                onClick={() => navigate('/logs')}
                sx={{ fontWeight: 800, whiteSpace: 'nowrap' }}
              >
                Engine logs
              </Button>
              <Tooltip title="Refresh">
                <IconButton size="small" onClick={fetchEvents} disabled={loading}>
                  <RefreshIcon fontSize="small" />
                </IconButton>
              </Tooltip>
            </Stack>
          </Stack>

          <Stack
            direction={isMobile ? 'column' : 'row'}
            spacing={1.5}
            sx={{ mt: 2 }}
            flexWrap="wrap"
            useFlexGap
          >
            <Box
              sx={{
                display: 'flex',
                alignItems: 'center',
                flex: isMobile ? 1 : '1 1 220px',
                minWidth: 200,
                px: 1.5,
                py: 0.5,
                borderRadius: 2,
                border: '1px solid',
                borderColor: alpha(theme.palette.divider, 0.2),
                bgcolor: 'background.paper',
              }}
            >
              <SearchIcon sx={{ fontSize: 18, color: 'text.secondary', mr: 1 }} />
              <InputBase
                placeholder="Search actor, action, IP, detail..."
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                sx={{ flex: 1, fontSize: '0.85rem' }}
                fullWidth
              />
            </Box>

            <FormControl size="small" sx={{ minWidth: 150 }}>
              <Select
                value={timeRange}
                onChange={(e) => setTimeRange(e.target.value as TimeRange)}
              >
                {TIME_RANGE_OPTIONS.map((o) => (
                  <MenuItem key={o.value} value={o.value}>
                    {o.label}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size="small" sx={{ minWidth: 160 }}>
              <Select
                value={actionFilter}
                onChange={(e) => setActionFilter(e.target.value as AuditActionFilter)}
                displayEmpty
              >
                {AUDIT_ACTION_FILTERS.map((f) => (
                  <MenuItem key={f.value} value={f.value}>
                    {f.label}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            <FormControl size="small" sx={{ minWidth: 130 }}>
              <Select
                value={outcomeFilter}
                onChange={(e) => setOutcomeFilter(e.target.value as AuditOutcomeFilter)}
              >
                {(['All', 'Success', 'Failed', 'Denied'] as const).map((o) => (
                  <MenuItem key={o} value={o}>
                    {o === 'All' ? 'All outcomes' : o}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            {isHub && (
              <FormControl size="small" sx={{ minWidth: 180 }}>
                <Select
                  value={selectedNode}
                  onChange={(e) => setSelectedNode(e.target.value)}
                >
                  <MenuItem value="all">All sites</MenuItem>
                  <MenuItem value="local">Central hub only</MenuItem>
                  {nodes.map((n) => (
                    <MenuItem key={n.id} value={n.id}>
                      {n.name}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            )}
          </Stack>
        </Box>

        {loading ? (
          <Box sx={{ py: 10, textAlign: 'center' }}>
            <CircularProgress size={32} />
            <Typography sx={{ mt: 2, color: 'text.secondary', fontWeight: 700 }}>
              LOADING AUDIT LOG
            </Typography>
          </Box>
        ) : isMobile ? (
          <Stack spacing={1.5} sx={{ p: 2 }}>
            {filtered.length === 0 ? (
              <Typography align="center" color="text.secondary" sx={{ py: 6, fontWeight: 700 }}>
                NO AUDIT RECORDS IN {rangeLabel.toUpperCase()}
              </Typography>
            ) : (
              filtered.map((event) => <AuditEventCard key={event.id} event={event} />)
            )}
          </Stack>
        ) : (
          <TableContainer>
            <Table size="small">
              <TableHead sx={{ bgcolor: alpha(theme.palette.primary.main, 0.05) }}>
                <TableRow>
                  {['TIME', 'ACTOR', 'ACTION', 'RESOURCE', 'OUTCOME', 'DETAIL', 'IP'].map((col) => (
                    <TableCell key={col} sx={{ fontWeight: 900, fontSize: '0.7rem', letterSpacing: 0.5 }}>
                      {col}
                    </TableCell>
                  ))}
                </TableRow>
              </TableHead>
              <TableBody>
                {filtered.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={7} align="center" sx={{ py: 8, color: 'text.secondary', fontWeight: 700 }}>
                      NO AUDIT RECORDS IN {rangeLabel.toUpperCase()}
                    </TableCell>
                  </TableRow>
                ) : (
                  filtered.map((event) => (
                    <TableRow key={event.id} hover>
                      <TableCell sx={{ whiteSpace: 'nowrap', fontSize: '0.8rem' }}>
                        {formatTimestamp(event.timestampUtc)}
                      </TableCell>
                      <TableCell>
                        <Typography variant="body2" fontWeight={700}>
                          {event.actorUsername}
                        </Typography>
                        <Typography variant="caption" color="text.secondary">
                          {event.actorRole || '-'}
                        </Typography>
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.8rem', maxWidth: 200 }}>
                        <Tooltip title={event.action}>
                          <span>{formatAuditAction(event.action)}</span>
                        </Tooltip>
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.8rem' }}>
                        {event.resourceType}
                        {event.resourceId ? (
                          <Typography variant="caption" display="block" color="text.secondary" noWrap>
                            {event.resourceId}
                          </Typography>
                        ) : null}
                      </TableCell>
                      <TableCell>
                        <Chip
                          size="small"
                          label={event.outcome}
                          color={outcomeColor(event.outcome)}
                          sx={{ fontWeight: 800, fontSize: '0.65rem' }}
                        />
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.8rem', maxWidth: 280, wordBreak: 'break-word' }}>
                        {event.detail || '-'}
                        {event.nodeName && (
                          <Typography variant="caption" display="block" color="text.secondary">
                            {event.nodeName}
                          </Typography>
                        )}
                      </TableCell>
                      <TableCell sx={{ fontSize: '0.8rem', whiteSpace: 'nowrap' }}>
                        {event.clientIp ? normalizeIpAddress(event.clientIp) : '-'}
                      </TableCell>
                    </TableRow>
                  ))
                )}
              </TableBody>
            </Table>
          </TableContainer>
        )}

        {!loading && (hasMore || events.length >= PAGE_SIZE) && (
          <Box sx={{ p: 2, borderTop: '1px solid', borderColor: alpha(theme.palette.divider, 0.08), textAlign: 'center' }}>
            <Button
              variant="outlined"
              onClick={loadMore}
              disabled={loadingMore || !hasMore}
              sx={{ fontWeight: 800 }}
            >
              {loadingMore ? 'Loading...' : hasMore ? 'Load older events' : 'End of results'}
            </Button>
            <Typography variant="caption" display="block" color="text.secondary" sx={{ mt: 1 }}>
              Showing {filtered.length} in view ({rangeLabel.toLowerCase()}). Older rows stay in the database.
            </Typography>
          </Box>
        )}
      </Paper>

      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 2, px: 0.5 }}>
        Default view is the last 24 hours; switch the window or use Load older for more. Events are retained with the
        appliance database (no automatic purge or rotation). Federated nodes push rows to the Hub. When syslog/SIEM is
        enabled, rows are also forwarded. For engine diagnostics, use{' '}
        <Box component="span" sx={{ fontWeight: 700 }}>Logs</Box>. Alert dispatch history is under{' '}
        <Box component="span" sx={{ fontWeight: 700 }}>Alerts → Audit / Replay</Box>.
      </Typography>
    </PageShell>
  );
}
