import { useState, useEffect, useRef, useMemo, useCallback, startTransition } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  Box, Typography, Paper, IconButton, Tooltip,
  Stack, Switch, FormControlLabel, alpha, useTheme
} from '@mui/material';
import {
  Refresh as RefreshIcon,
  Terminal as TerminalIcon,
  Download as DownloadIcon,
  Search as SearchIcon,
  DeleteSweep as ClearIcon,
  Fullscreen as FullscreenIcon,
  FullscreenExit as FullscreenExitIcon,
  FactCheck as FactCheckIcon,
} from '@mui/icons-material';
import { InputBase, Select, MenuItem, FormControl, Button } from '@mui/material';

import { ApiService } from '../services/apiService';
import { PageHeader } from '../components/pageheader';
import { PageShell } from '../components/mobile/PageShell';
import { APP_VERSION } from '../constants/version';
import { VirtualLogList } from '../components/logs/VirtualLogList';
import { useIsMobileLayout } from '../hooks/useIsMobileLayout';

export default function LogsPage() {
  const theme = useTheme();
  const navigate = useNavigate();
  const isMobile = useIsMobileLayout();
  const [logs, setLogs] = useState<string[]>([]);
  const [filterText, setFilterText] = useState("");
  const [loading, setLoading] = useState(true);
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [debugMode, setDebugMode] = useState(false);
  const [tailCount, setTailCount] = useState(500);
  const [isFullScreen, setIsFullScreen] = useState(false);
  const [nodes, setNodes] = useState<any[]>([]);
  const [selectedNode, setSelectedNode] = useState<string>(() => {
    const params = new URLSearchParams(window.location.search);
    return params.get('nodeId') || "local";
  });
  const [isHub, setIsHub] = useState(false);
  const logContainerRef = useRef<HTMLDivElement>(null);
  const stickToBottomRef = useRef(true);
  const clearedLogLineRef = useRef<string | null>(null);
  
  const handleDownloadLogs = () => {
    if (logs.length === 0) return;
    const blob = new Blob([logs.join('\n')], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    const nodeLabel = selectedNode === "local" ? "hub" : `node_${selectedNode}`;
    link.download = `stacksatlas_engine_${nodeLabel}_${new Date().toISOString().replace(/[:.]/g, '-')}.log`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const readNodeDebugMode = (nodeList: any[], nodeId: string) => {
    const node = nodeList.find((n) => n.id === nodeId || n.Id === nodeId);
    if (!node) return false;
    if (typeof node.isDebugLoggingEnabled === 'boolean') return node.isDebugLoggingEnabled;
    if (typeof node.IsDebugLoggingEnabled === 'boolean') return node.IsDebugLoggingEnabled;
    return false;
  };

  const fetchConfig = async () => {
    try {
      const config = await ApiService.getSystemConfig();
      setDebugMode(Boolean(config.isDebugLoggingEnabled));
    } catch (err) {
      console.error('Failed to fetch system config:', err);
    }
  };

  const fetchNodes = async () => {
    try {
      const data = await ApiService.getFederationNodes();
      if (Array.isArray(data)) {
        setNodes(data);
        setIsHub(true);
        if (selectedNode !== 'local') {
          setDebugMode(readNodeDebugMode(data, selectedNode));
        }
      }
    } catch (err) {
      // Standalone/Node mode: federated log stream not available
      setIsHub(false);
    }
  };

  const fetchLogs = async () => {
    try {
      let data;
      if (selectedNode === "local") {
        data = await ApiService.getSystemLogs(tailCount);
      } else {
        data = await ApiService.getRemoteSystemLogs(selectedNode, tailCount);
      }
      if (Array.isArray(data)) {
        if (clearedLogLineRef.current) {
          const idx = data.lastIndexOf(clearedLogLineRef.current);
          if (idx !== -1) {
            startTransition(() => setLogs(data.slice(idx + 1)));
            return;
          } else {
            // The cleared line fell out of the buffer, reset it
            clearedLogLineRef.current = null;
          }
        }
        startTransition(() => setLogs(data));
      }
    } catch (err) {
      console.error('Failed to fetch logs:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleClearLogs = () => {
    if (logs.length > 0) {
      clearedLogLineRef.current = logs[logs.length - 1];
    }
    setLogs([]);
  };

  const handleManualRefresh = () => {
    setLoading(true);
    clearedLogLineRef.current = null;
    fetchLogs();
  };

  useEffect(() => {
    if (selectedNode === 'local') {
      fetchConfig();
      return;
    }
    // Prefer live node telemetry; fall back to last known list entry.
    setDebugMode(readNodeDebugMode(nodes, selectedNode));
    void fetchNodes();
  }, [selectedNode]);

  useEffect(() => {
    fetchNodes();
  }, []);

  useEffect(() => {
    setLoading(true);
    clearedLogLineRef.current = null; // Reset clear filter when switching stream context
    fetchLogs();
  }, [tailCount, selectedNode]);

  useEffect(() => {
    if (!autoRefresh) return;
    const interval = setInterval(fetchLogs, 5000);
    return () => clearInterval(interval);
  }, [autoRefresh, selectedNode, tailCount]);

  const handleToggleDebug = async (enabled: boolean) => {
    try {
      const nodeId = selectedNode !== 'local' ? selectedNode : undefined;
      await ApiService.toggleDebugLogging(enabled, nodeId);
      setDebugMode(enabled);
      if (nodeId) {
        setNodes((prev) =>
          prev.map((n) =>
            n.id === nodeId || n.Id === nodeId
              ? { ...n, isDebugLoggingEnabled: enabled, IsDebugLoggingEnabled: enabled }
              : n,
          ),
        );
      }
      setTimeout(fetchLogs, 1000);
    } catch (err) {
      console.error('Failed to toggle debug logging:', err);
      // Re-sync UI from source of truth after a failed toggle.
      if (selectedNode === 'local') fetchConfig();
      else void fetchNodes();
    }
  };

  const filteredLogs = useMemo(() => {
    const needle = filterText.trim().toLowerCase();
    if (!needle) return logs;
    return logs.filter((line) => line.toLowerCase().includes(needle));
  }, [logs, filterText]);

  const handleLogScroll = useCallback(() => {
    const el = logContainerRef.current;
    if (!el) return;
    stickToBottomRef.current = el.scrollHeight - el.scrollTop - el.clientHeight < 48;
  }, []);

  useEffect(() => {
    if (!stickToBottomRef.current) return;
    const el = logContainerRef.current;
    if (!el) return;
    el.scrollTop = el.scrollHeight;
  }, [filteredLogs]);

  const getLogColor = (line: string) => {
    // Prefer bracketed Serilog tokens so message text like "Error" does not false-color.
    if (/\[[^\]]*ERR[^\]]*\]/.test(line) || /\[[^\]]*FTL[^\]]*\]/.test(line)) return '#ff5252';
    if (/\[[^\]]*WRN[^\]]*\]/.test(line) || /\[[^\]]*WAR[^\]]*\]/.test(line)) return '#ffb142';
    if (/\[[^\]]*DBG[^\]]*\]/.test(line)) return '#d1ccc0';
    if (/\[[^\]]*INF[^\]]*\]/.test(line)) return '#34ace0';
    if (line.includes('STACKSATLAS')) return '#33d9b2';
    return 'inherit';
  };

  const renderLogLine = useCallback((line: string) => {
    const match = line.match(/^\[(\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?: ?[+-]\d{2}:?\d{2}| ?Z)?)\](.*)/);
    if (match) {
      const rawTimestamp = match[1];
      const restOfLine = match[2];
      
      const isoStr = rawTimestamp.trim().replace(/\s+/, 'T').replace(/\s+/, '');
      const date = new Date(isoStr);
      
      if (!isNaN(date.getTime())) {
        const pad = (num: number, size: number = 2) => {
          let s = num.toString();
          while (s.length < size) s = "0" + s;
          return s;
        };

        const YYYY = date.getFullYear();
        const MM = pad(date.getMonth() + 1);
        const DD = pad(date.getDate());
        const hh = pad(date.getHours());
        const mm = pad(date.getMinutes());
        const ss = pad(date.getSeconds());
        const ms = pad(date.getMilliseconds(), 3);

        const offsetMin = -date.getTimezoneOffset();
        const sign = offsetMin >= 0 ? "+" : "-";
        const absOffset = Math.abs(offsetMin);
        const offsetH = pad(Math.floor(absOffset / 60));
        const offsetM = pad(absOffset % 60);
        const offsetStr = `${sign}${offsetH}:${offsetM}`;

        return `[${YYYY}-${MM}-${DD} ${hh}:${mm}:${ss}.${ms} ${offsetStr}]${restOfLine}`;
      }
    }
    return line;
  }, []);

  const renderLogRow = useCallback((line: string) => (
    <Box sx={{
      whiteSpace: 'pre-wrap',
      wordBreak: 'break-all',
      color: getLogColor(line),
      '&:hover': { bgcolor: 'rgba(255,255,255,0.03)' }
    }}>
      {renderLogLine(line)}
    </Box>
  ), [renderLogLine]);

  const headerStats = isMobile
    ? [
        { label: 'STREAM', value: autoRefresh ? 'LIVE' : 'PAUSED', color: autoRefresh ? 'success.main' : 'warning.main' },
        { label: 'TAIL', value: tailCount.toString() },
      ]
    : [
        { label: 'LOG RETENTION', value: '30 DAYS', color: 'text.secondary' },
        { label: 'ACTIVE STREAM', value: autoRefresh ? 'LIVE' : 'PAUSED', color: autoRefresh ? 'success.main' : 'warning.main' },
        { label: 'SYSTEM VER', value: `v${APP_VERSION}` },
      ];

  const iconActions = (
    <>
      <Tooltip title="Clear Terminal">
        <IconButton size="small" onClick={handleClearLogs} sx={{ color: '#888' }}>
          <ClearIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Tooltip title="Export to File">
        <IconButton size="small" onClick={handleDownloadLogs} disabled={logs.length === 0} sx={{ color: '#888' }}>
          <DownloadIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Tooltip title="Manual Refresh">
        <IconButton size="small" onClick={handleManualRefresh} disabled={loading} sx={{ color: '#888' }}>
          <RefreshIcon fontSize="small" />
        </IconButton>
      </Tooltip>
      <Tooltip title={isFullScreen ? 'Exit Fullscreen' : 'Fullscreen Mode'}>
        <IconButton size="small" onClick={() => setIsFullScreen(!isFullScreen)} sx={{ color: isFullScreen ? 'primary.main' : '#888' }}>
          {isFullScreen ? <FullscreenExitIcon fontSize="small" /> : <FullscreenIcon fontSize="small" />}
        </IconButton>
      </Tooltip>
    </>
  );

  return (
    <PageShell isMobile={isMobile}>
      <PageHeader
        title="SYSTEM TERMINAL"
        subtitle="REAL-TIME ENGINE TELEMETRY & DIAGNOSTICS"
        stats={headerStats}
      />

      <Paper
        variant="outlined"
        sx={{
          bgcolor: '#0c0c0c',
          color: '#d1d1d1',
          borderRadius: isFullScreen ? 0 : 3,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
          height: isFullScreen ? '100vh' : isMobile ? 'calc(100dvh - 200px)' : '70vh',
          border: isFullScreen ? 'none' : '1px solid',
          borderColor: alpha(theme.palette.divider, 0.1),
          boxShadow: '0 20px 40px rgba(0,0,0,0.4)',
          position: isFullScreen ? 'fixed' : 'relative',
          top: isFullScreen ? 0 : 'auto',
          left: isFullScreen ? 0 : 'auto',
          right: isFullScreen ? 0 : 'auto',
          bottom: isFullScreen ? 0 : 'auto',
          zIndex: isFullScreen ? 1300 : 1,
          transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)'
        }}
      >
        {/* Toolbar */}
        <Box sx={{
          p: 1.5,
          bgcolor: '#1a1a1a',
          borderBottom: '1px solid #2a2a2a',
          display: 'flex',
          flexDirection: isMobile ? 'column' : 'row',
          justifyContent: 'space-between',
          alignItems: isMobile ? 'stretch' : 'center',
          gap: isMobile ? 1.5 : 0,
        }}>
          <Stack direction="row" spacing={1.5} alignItems="center" flexWrap="wrap" useFlexGap>
            <TerminalIcon sx={{ fontSize: 18, color: '#33d9b2', mr: 0.5 }} />
            <Typography variant="caption" fontWeight={800} sx={{ letterSpacing: 1.5, color: 'text.secondary', textTransform: 'uppercase' }}>
              stacksatlas_engine.log
            </Typography>
            <Button
              size="small"
              variant="outlined"
              startIcon={<FactCheckIcon sx={{ fontSize: 14 }} />}
              onClick={() => navigate('/audit')}
              sx={{
                ml: 0.5,
                minHeight: 28,
                py: 0.25,
                px: 1.25,
                fontSize: '0.65rem',
                fontWeight: 700,
                letterSpacing: 1.5,
                color: 'text.secondary',
                borderColor: alpha(theme.palette.divider, 0.35),
                '&:hover': { borderColor: alpha(theme.palette.primary.main, 0.4), color: 'primary.main' },
              }}
            >
              Audit log
            </Button>
            {isHub && (
              <>
                {!isMobile && <Box sx={{ width: '1px', height: 14, bgcolor: 'rgba(255, 255, 255, 0.12)', mx: 1 }} />}
                <FormControl variant="standard" sx={{ minWidth: isMobile ? '100%' : 150, flex: isMobile ? 1 : undefined }}>
                  <Select
                    value={selectedNode}
                    onChange={(e) => setSelectedNode(e.target.value as string)}
                    disableUnderline
                    sx={{
                      color: 'text.primary',
                      fontSize: '0.65rem',
                      fontWeight: 700,
                      border: `1px solid ${alpha(theme.palette.divider, 0.35)}`,
                      borderRadius: 1.5,
                      px: 1.5,
                      bgcolor: alpha(theme.palette.action.hover, 0.04),
                      letterSpacing: 1.5,
                      textTransform: 'uppercase',
                      minHeight: 28,
                      height: 28,
                      '& .MuiSelect-select': { 
                        py: 0.5, 
                        display: 'flex', 
                        alignItems: 'center',
                        pr: '28px !important'
                      },
                      '& .MuiSvgIcon-root': { 
                        color: 'text.secondary', 
                        fontSize: '1rem',
                        pointerEvents: 'none'
                      },
                      '&:hover': {
                        color: 'primary.main',
                        borderColor: alpha(theme.palette.primary.main, 0.35),
                        bgcolor: alpha(theme.palette.primary.main, 0.04),
                        '& .MuiSvgIcon-root': { color: 'primary.main' }
                      }
                    }}
                    MenuProps={{
                      PaperProps: {
                        sx: {
                          bgcolor: '#141414',
                          border: '1px solid #2a2a2a',
                          color: '#d1d1d1',
                          backgroundImage: 'none',
                          boxShadow: '0 10px 30px rgba(0,0,0,0.5)',
                          '& .MuiMenuItem-root': {
                            fontSize: '0.7rem',
                            fontWeight: 800,
                            py: 0.75,
                            '&:hover': { bgcolor: 'rgba(51, 217, 178, 0.1)' },
                            '&.Mui-selected': { bgcolor: 'rgba(51, 217, 178, 0.15)', color: '#33d9b2' }
                          }
                        }
                      }
                    }}
                  >
                    <MenuItem value="local">CENTRAL HUB</MenuItem>
                    {nodes.map((n) => (
                      <MenuItem key={n.id} value={n.id}>
                        NODE: {n.name.toUpperCase()} ({n.id.toUpperCase()})
                      </MenuItem>
                    ))}
                  </Select>
                </FormControl>
              </>
            )}
          </Stack>

          <Stack
            direction={isMobile ? 'column' : 'row'}
            spacing={isMobile ? 1.25 : 2}
            alignItems={isMobile ? 'stretch' : 'center'}
            sx={{ width: isMobile ? '100%' : 'auto' }}
          >
            <Box sx={{
              display: 'flex',
              alignItems: 'center',
              bgcolor: 'rgba(0,0,0,0.3)',
              px: 1.5,
              py: 0.5,
              borderRadius: 2,
              border: '1px solid #333',
              width: isMobile ? '100%' : 'auto',
            }}>
              <SearchIcon sx={{ fontSize: 14, color: '#666', mr: 1 }} />
              <InputBase
                placeholder="Pattern filter..."
                value={filterText}
                onChange={(e) => setFilterText(e.target.value)}
                sx={{
                  color: '#d1d1d1',
                  fontSize: '0.7rem',
                  width: isMobile ? '100%' : { xs: 80, md: 150 },
                  fontWeight: 600,
                  '& input::placeholder': { color: '#666', opacity: 1 },
                }}
              />
            </Box>

            <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap justifyContent={isMobile ? 'space-between' : 'flex-start'}>
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={autoRefresh}
                    onChange={(e) => setAutoRefresh(e.target.checked)}
                    color="primary"
                  />
                }
                label={<Typography variant="caption" fontWeight={700} sx={{ letterSpacing: 1.5, color: 'text.secondary', textTransform: 'uppercase' }}>Live stream</Typography>}
              />

              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={debugMode}
                    onChange={(e) => handleToggleDebug(e.target.checked)}
                    color="warning"
                  />
                }
                label={<Typography variant="caption" fontWeight={700} sx={{ letterSpacing: 1.5, color: debugMode ? 'warning.main' : 'text.secondary', textTransform: 'uppercase' }}>
                  {selectedNode === 'local' ? 'Debug mode' : 'Node debug'}
                </Typography>}
              />

              <FormControl variant="standard" sx={{ minWidth: 80 }}>
                <Select
                  value={tailCount}
                  onChange={(e) => setTailCount(Number(e.target.value))}
                  sx={{
                    color: 'text.secondary',
                    fontSize: '0.65rem',
                    fontWeight: 700,
                    letterSpacing: 1.5,
                    textTransform: 'uppercase',
                    '&:before, &:after': { display: 'none' },
                    '& .MuiSelect-select': { py: 0.5 },
                  }}
                >
                  <MenuItem value={100}>TAIL 100</MenuItem>
                  <MenuItem value={500}>TAIL 500</MenuItem>
                  <MenuItem value={1000}>TAIL 1k</MenuItem>
                  <MenuItem value={2000}>TAIL 2k</MenuItem>
                </Select>
              </FormControl>

              <Stack direction="row" spacing={0.5} alignItems="center">
                {iconActions}
              </Stack>
            </Stack>
          </Stack>
        </Box>

        {/* Log Content */}
        <Box
          ref={logContainerRef}
          onScroll={handleLogScroll}
          sx={{
            flexGrow: 1,
            overflowY: 'auto',
            p: 2,
            fontFamily: '"Fira Code", "Roboto Mono", monospace',
            fontSize: isMobile ? '0.75rem' : '0.85rem',
            lineHeight: 1.6,
            '&::-webkit-scrollbar': { width: 8 },
            '&::-webkit-scrollbar-thumb': { bgcolor: '#333', borderRadius: 4 },
            '&::-webkit-scrollbar-track': { bgcolor: 'transparent' }
          }}
        >
          {filteredLogs.length === 0 && !loading && (
            <Typography variant="body2" sx={{ opacity: 0.5, fontStyle: 'italic', textAlign: 'center', mt: 4 }}>
              {filterText ? `No logs matching pattern: "${filterText}"` : "No log entries found for the current period."}
            </Typography>
          )}

          <VirtualLogList
            lines={filteredLogs}
            containerRef={logContainerRef}
            renderLine={(line: string) => renderLogRow(line)}
          />
        </Box>
      </Paper>

      <Box sx={{ mt: 3, textAlign: 'center', opacity: 0.5 }}>
        <Typography variant="caption">
          💡 System logs rotate daily. Historical logs are retained in the /data/logs directory.
        </Typography>
      </Box>
    </PageShell>
  );
}
