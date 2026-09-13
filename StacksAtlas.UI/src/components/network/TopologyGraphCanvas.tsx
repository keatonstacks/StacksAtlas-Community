import { useEffect, useState, useRef, useCallback, useMemo, useLayoutEffect } from 'react';
import {
    Box,
    Button,
    IconButton,
    InputAdornment,
    Menu,
    MenuItem,
    Stack,
    TextField,
    Tooltip,
    Typography,
    alpha,
    useTheme,
    CircularProgress,
} from '@mui/material';
import UnfoldMoreIcon from '@mui/icons-material/UnfoldMore';
import AccountTreeOutlinedIcon from '@mui/icons-material/AccountTreeOutlined';
import FitScreenIcon from '@mui/icons-material/FitScreen';
import OpenInFullIcon from '@mui/icons-material/OpenInFull';
import DownloadIcon from '@mui/icons-material/Download';
import SearchIcon from '@mui/icons-material/Search';
import ClearIcon from '@mui/icons-material/Clear';
import { ApiService } from '../../services/apiService';
import { TopologyLegend } from './TopologyLegend';
import { createTopologyIconDataUrl, resolveTopologyIconKind } from './topologyCanvasIcons';
import {
    NODE_R,
    ancestorIds,
    buildBusLinkGroups,
    buildParentMap,
    computeGraphBounds,
    computeSignalPath,
    fitGraphTransform,
    isNewlyDiscovered,
    linkEndpointId,
    nodeMatchesSearch,
    zoomAtPoint,
    type GraphTransform,
    type TopologyLink,
    type TopologyNode,
} from './topologyGraphUtils';
import {
    TOPOLOGY_FONT,
    buildNodeLabelLines,
    createTopologyExportSvg,
    downloadTopologyPngFromSvg,
    downloadTopologySvgFromElement,
    nodeLabelLineStyle,
} from './topologyNodeLabel';
import {
    linkStrokeColor,
    linkStrokeDash,
    linkStrokeWidth,
    nodeIconFill,
    pathDimOpacity,
    pathFocusStroke,
    resolveLinkVisualKind,
    securityRingColor,
    shouldShowSecurityRing,
    trunkStrokeColor,
    trunkStrokeDash,
    trunkStrokeGlowColor,
    trunkStrokeOpacity,
    trunkStrokeWidth,
} from './topologyVisualTheme';

interface GraphData {
    nodes: TopologyNode[];
    links: TopologyLink[];
    viewerNodeId?: string | null;
}

const HORIZ_MIN = 136;
const VERT_GAP = 128;
const TOP_PAD = 52;
const LABEL_OFFSET = NODE_R + 8;

function buildChildMap(links: TopologyLink[]): Map<string, string[]> {
    const children = new Map<string, string[]>();
    for (const link of links) {
        const source = linkEndpointId(link.source);
        const target = linkEndpointId(link.target);
        const list = children.get(source);
        if (list) list.push(target);
        else children.set(source, [target]);
    }
    return children;
}

function collectDescendants(rootId: string, children: Map<string, string[]>): Set<string> {
    const result = new Set<string>();
    const stack = [...(children.get(rootId) ?? [])];
    while (stack.length > 0) {
        const id = stack.pop()!;
        if (result.has(id)) continue;
        result.add(id);
        for (const child of children.get(id) ?? []) stack.push(child);
    }
    return result;
}

function findRoots(nodes: TopologyNode[], links: TopologyLink[]): string[] {
    const hasParent = new Set<string>();
    for (const link of links) hasParent.add(linkEndpointId(link.target));
    const roots = nodes.filter((n) => !hasParent.has(n.id)).map((n) => n.id);
    if (roots.length > 0) return roots;
    const gateway = nodes.find((n) => n.id === 'root_gateway');
    if (gateway) return [gateway.id];
    return nodes.length > 0 ? [nodes[0].id] : [];
}

function measureSubtreeWidth(nodeId: string, children: Map<string, string[]>, cache: Map<string, number>): number {
    if (cache.has(nodeId)) return cache.get(nodeId)!;
    const kids = children.get(nodeId) ?? [];
    if (kids.length === 0) {
        cache.set(nodeId, HORIZ_MIN);
        return HORIZ_MIN;
    }
    const width = kids.reduce((sum, kid) => sum + measureSubtreeWidth(kid, children, cache), 0);
    const result = Math.max(HORIZ_MIN, width);
    cache.set(nodeId, result);
    return result;
}

function placeSubtree(
    nodeId: string,
    depth: number,
    left: number,
    children: Map<string, string[]>,
    widthCache: Map<string, number>,
    positions: Map<string, { x: number; y: number }>,
): void {
    const kids = children.get(nodeId) ?? [];
    const subtreeW = widthCache.get(nodeId) ?? HORIZ_MIN;
    if (kids.length === 0) {
        positions.set(nodeId, { x: left + subtreeW / 2, y: TOP_PAD + depth * VERT_GAP });
        return;
    }
    let cursor = left;
    for (const kid of kids) {
        const kidW = widthCache.get(kid) ?? HORIZ_MIN;
        placeSubtree(kid, depth + 1, cursor, children, widthCache, positions);
        cursor += kidW;
    }
    positions.set(nodeId, { x: left + subtreeW / 2, y: TOP_PAD + depth * VERT_GAP });
}

function layoutHierarchy(nodes: TopologyNode[], links: TopologyLink[]): Map<string, { x: number; y: number }> {
    const children = buildChildMap(links);
    const roots = findRoots(nodes, links);
    const widthCache = new Map<string, number>();
    for (const node of nodes) measureSubtreeWidth(node.id, children, widthCache);
    const positions = new Map<string, { x: number; y: number }>();
    let cursor = 0;
    for (const root of roots) {
        placeSubtree(root, 0, cursor, children, widthCache, positions);
        cursor += widthCache.get(root) ?? HORIZ_MIN;
    }
    return positions;
}

function formatRefreshedAt(date: Date): string {
    return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

export interface TopologyGraphCanvasProps {
    onNodeClick?: (nodeId: string) => void;
    nodeId?: string;
    selectedNodeId?: string | null;
    showFullscreenButton?: boolean;
    onOpenFullscreen?: () => void;
}

export function TopologyGraphCanvas({
    onNodeClick,
    nodeId,
    selectedNodeId,
    showFullscreenButton = false,
    onOpenFullscreen,
}: TopologyGraphCanvasProps) {
    const theme = useTheme();
    const [graphData, setGraphData] = useState<GraphData | null>(null);
    const [loading, setLoading] = useState(true);
    const [collapsedIds, setCollapsedIds] = useState<Set<string>>(() => new Set());
    const [hoverNodeId, setHoverNodeId] = useState<string | null>(null);
    const [hoverLinkKey, setHoverLinkKey] = useState<string | null>(null);
    const [searchQuery, setSearchQuery] = useState('');
    const [dimensions, setDimensions] = useState({ w: 800, h: 480 });
    const [transform, setTransform] = useState<GraphTransform>({ x: 0, y: 0, k: 1 });
    const [lastRefreshedAt, setLastRefreshedAt] = useState<Date | null>(null);
    const [exportAnchor, setExportAnchor] = useState<null | HTMLElement>(null);
    const [pathFocusTargetId, setPathFocusTargetId] = useState<string | null>(null);
    const [pulseTick, setPulseTick] = useState(0);
    const [isPanning, setIsPanning] = useState(false);

    const containerRef = useRef<HTMLDivElement>(null);
    const graphAreaRef = useRef<HTMLDivElement>(null);
    const svgRef = useRef<SVGSVGElement>(null);
    const graphGroupRef = useRef<SVGGElement>(null);
    const panStartRef = useRef<{ x: number; y: number; tx: number; ty: number } | null>(null);
    const fitKeyRef = useRef('');

    const measureContainer = useCallback(() => {
        const el = graphAreaRef.current ?? containerRef.current;
        if (!el) return;
        const w = Math.floor(el.clientWidth);
        const h = Math.floor(el.clientHeight);
        if (w < 80 || h < 80) return;
        setDimensions((prev) => (prev.w === w && prev.h === h ? prev : { w, h }));
    }, []);

    useLayoutEffect(() => {
        measureContainer();
        const el = containerRef.current;
        if (!el) return;
        const ro = new ResizeObserver(() => measureContainer());
        ro.observe(el);
        window.addEventListener('resize', measureContainer);
        return () => {
            ro.disconnect();
            window.removeEventListener('resize', measureContainer);
        };
    }, [measureContainer, loading]);

    const loadData = useCallback(async () => {
        try {
            const data = await ApiService.getTopology(nodeId);
            setGraphData(data);
            setLastRefreshedAt(new Date());
        } catch (err) {
            console.error('Failed to load topology:', err);
        } finally {
            setLoading(false);
        }
    }, [nodeId]);

    useEffect(() => {
        setLoading(true);
        setCollapsedIds(new Set());
        void loadData();
        const interval = setInterval(() => void loadData(), 20000);
        return () => clearInterval(interval);
    }, [nodeId]); // eslint-disable-line react-hooks/exhaustive-deps

    useEffect(() => {
        const t = window.setInterval(() => setPulseTick((v) => v + 1), 2000);
        return () => window.clearInterval(t);
    }, []);

    const viewerNodeId = graphData?.viewerNodeId ?? null;

    const visibleGraph = useMemo(() => {
        if (!graphData) return null;
        const children = buildChildMap(graphData.links);
        const hidden = new Set<string>();
        for (const id of collapsedIds) {
            for (const desc of collectDescendants(id, children)) hidden.add(desc);
        }
        const nodes = graphData.nodes
            .filter((n) => !hidden.has(n.id))
            .map((n) => {
                if (!collapsedIds.has(n.id)) return n;
                const directKids = (children.get(n.id) ?? []).length;
                return { ...n, name: directKids > 0 ? `${n.name} (+${directKids})` : n.name };
            });
        const visibleIds = new Set(nodes.map((n) => n.id));
        const links = graphData.links.filter((link) => {
            const source = linkEndpointId(link.source);
            const target = linkEndpointId(link.target);
            return visibleIds.has(source) && visibleIds.has(target);
        });
        const positions = layoutHierarchy(nodes, links);
        const laidOut = nodes.map((n) => {
            const pos = positions.get(n.id);
            return pos ? { ...n, x: pos.x, y: pos.y } : n;
        });
        return { nodes: laidOut, links };
    }, [graphData, collapsedIds]);

    const graphBounds = useMemo(() => computeGraphBounds(visibleGraph?.nodes ?? []), [visibleGraph]);
    const nodeCount = visibleGraph?.nodes.length ?? 0;

    const fitToView = useCallback(() => {
        setTransform(fitGraphTransform(graphBounds, dimensions.w, dimensions.h, 28, nodeCount));
    }, [graphBounds, dimensions.w, dimensions.h, nodeCount]);

    useEffect(() => {
        const key = `${dimensions.w}x${dimensions.h}:${visibleGraph?.nodes.length ?? 0}:${collapsedIds.size}`;
        if (key !== fitKeyRef.current && visibleGraph && visibleGraph.nodes.length > 0) {
            fitKeyRef.current = key;
            setTransform(fitGraphTransform(graphBounds, dimensions.w, dimensions.h, 28, nodeCount));
        }
    }, [graphBounds, dimensions.w, dimensions.h, visibleGraph, collapsedIds.size, nodeCount]);

    const nodeMap = useMemo(() => {
        const map = new Map<string, TopologyNode>();
        for (const n of visibleGraph?.nodes ?? []) map.set(n.id, n);
        return map;
    }, [visibleGraph]);

    const busGroups = useMemo(
        () => buildBusLinkGroups(visibleGraph?.links ?? [], nodeMap),
        [visibleGraph, nodeMap],
    );

    const signalPath = useMemo(
        () => computeSignalPath(pathFocusTargetId, visibleGraph?.links ?? [], busGroups),
        [pathFocusTargetId, visibleGraph, busGroups],
    );

    const pathFocusActive = signalPath != null;

    const pathFocusLabel = useMemo(() => {
        if (!pathFocusTargetId || !visibleGraph) return '';
        const node = visibleGraph.nodes.find((n) => n.id === pathFocusTargetId);
        const root = visibleGraph.nodes.find((n) => n.id === 'root_gateway');
        const from = (node?.name || node?.ip || 'Device').trim();
        const to = (root?.name || root?.ip || 'Gateway').trim();
        return `${from} → ${to}`;
    }, [pathFocusTargetId, visibleGraph]);

    const searchMatches = useMemo(() => {
        const q = searchQuery.trim();
        if (!q || !visibleGraph) return new Set<string>();
        return new Set(visibleGraph.nodes.filter((n) => nodeMatchesSearch(n, q)).map((n) => n.id));
    }, [searchQuery, visibleGraph]);

    useEffect(() => {
        const q = searchQuery.trim();
        if (!q || !graphData) return;
        const parentMap = buildParentMap(graphData.links);
        const matches = graphData.nodes.filter((n) => nodeMatchesSearch(n, q));
        if (matches.length === 0) return;
        const toExpand = new Set<string>();
        for (const match of matches) {
            for (const anc of ancestorIds(match.id, parentMap)) {
                if (graphData.nodes.some((n) => n.id === anc && n.isInfrastructure)) toExpand.add(anc);
            }
        }
        if (toExpand.size === 0) return;
        setCollapsedIds((prev) => {
            const next = new Set(prev);
            let changed = false;
            for (const id of toExpand) {
                if (next.has(id)) { next.delete(id); changed = true; }
            }
            return changed ? next : prev;
        });
    }, [searchQuery, graphData]);

    const collapsibleIds = useMemo(() => {
        if (!graphData) return new Set<string>();
        return new Set(
            graphData.nodes
                .filter((n) => n.isInfrastructure && (n.childCount ?? 0) > 0 && n.id !== 'root_gateway')
                .map((n) => n.id),
        );
    }, [graphData]);

    const toggleCollapse = useCallback((id: string) => {
        setCollapsedIds((prev) => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });
    }, []);

    const expandAll = useCallback(() => setCollapsedIds(new Set()), []);
    const collapseAllInfra = useCallback(() => setCollapsedIds(new Set(collapsibleIds)), [collapsibleIds]);

    const handleNodeClick = useCallback((id: string) => {
        if (id === 'root_gateway' || id === 'host_machine') return;
        onNodeClick?.(id);
    }, [onNodeClick]);

    const togglePathFocus = useCallback((targetId: string) => {
        setPathFocusTargetId((prev) => (prev === targetId ? null : targetId));
    }, []);

    const clearPathFocus = useCallback(() => setPathFocusTargetId(null), []);

    useEffect(() => {
        const onKeyDown = (e: KeyboardEvent) => {
            if (e.key === 'Escape') clearPathFocus();
        };
        window.addEventListener('keydown', onKeyDown);
        return () => window.removeEventListener('keydown', onKeyDown);
    }, [clearPathFocus]);

    const nodeFill = useCallback((isRoot: boolean, isInfra: boolean, isOnline: boolean) => {
        return nodeIconFill(theme, { isRoot, isInfra, isOnline });
    }, [theme]);

    const iconUrlByNodeId = useMemo(() => {
        const map = new Map<string, string>();
        for (const node of visibleGraph?.nodes ?? []) {
            const isRoot = node.id === 'root_gateway';
            const isInfra = !!node.isInfrastructure;
            const isOnline = node.status === 'online';
            const kind = resolveTopologyIconKind(node, isRoot);
            map.set(node.id, createTopologyIconDataUrl(kind, NODE_R * 2, nodeFill(isRoot, isInfra, isOnline)));
        }
        return map;
    }, [visibleGraph, nodeFill]);

    const linkVisual = useCallback((link: TopologyLink, target?: TopologyNode) => {
        return resolveLinkVisualKind(link, target);
    }, []);

    const clientToSvg = useCallback((clientX: number, clientY: number) => {
        const svg = svgRef.current;
        if (!svg) return { x: 0, y: 0 };
        const pt = svg.createSVGPoint();
        pt.x = clientX;
        pt.y = clientY;
        const ctm = svg.getScreenCTM();
        if (!ctm) return { x: 0, y: 0 };
        return pt.matrixTransform(ctm.inverse());
    }, []);

    const handleWheel = useCallback((e: React.WheelEvent<SVGSVGElement>) => {
        e.preventDefault();
        const pt = clientToSvg(e.clientX, e.clientY);
        setTransform((prev) => zoomAtPoint(prev, pt.x, pt.y, e.deltaY < 0 ? 1.1 : 0.9));
    }, [clientToSvg]);

    const handleSvgPointerDown = useCallback((e: React.PointerEvent<SVGSVGElement>) => {
        if (e.button !== 0) return;
        if ((e.target as Element).closest('[data-topology-node]')) return;
        if ((e.target as Element).closest('[data-topology-hit]')) return;
        panStartRef.current = { x: e.clientX, y: e.clientY, tx: transform.x, ty: transform.y };
        setIsPanning(true);
        e.currentTarget.setPointerCapture(e.pointerId);
    }, [transform.x, transform.y]);

    const handleSvgClick = useCallback((e: React.MouseEvent<SVGSVGElement>) => {
        if ((e.target as Element).closest('[data-topology-node]')) return;
        if ((e.target as Element).closest('[data-topology-hit]')) return;
        clearPathFocus();
    }, [clearPathFocus]);

    const handleSvgPointerMove = useCallback((e: React.PointerEvent<SVGSVGElement>) => {
        const start = panStartRef.current;
        if (!start) return;
        const ctm = svgRef.current?.getScreenCTM();
        if (!ctm) return;
        const scale = ctm.a || 1;
        setTransform((prev) => ({
            ...prev,
            x: start.tx + (e.clientX - start.x) / scale,
            y: start.ty + (e.clientY - start.y) / scale,
        }));
    }, []);

    const handleSvgPointerUp = useCallback((e: React.PointerEvent<SVGSVGElement>) => {
        panStartRef.current = null;
        setIsPanning(false);
        try { e.currentTarget.releasePointerCapture(e.pointerId); } catch { /* ignore */ }
    }, []);

    const handleExport = useCallback(async (format: 'png' | 'svg') => {
        setExportAnchor(null);
        const graphGroup = graphGroupRef.current;
        if (!graphGroup) return;
        const stamp = new Date().toISOString().slice(0, 19).replace(/[:T]/g, '-');
        const filename = `stacksatlas-topology-${stamp}.${format}`;
        const exportSvg = createTopologyExportSvg(
            graphGroup,
            graphBounds,
            theme.palette.background.paper,
            `StacksAtlas topology · ${new Date().toLocaleString()}`,
        );
        try {
            if (format === 'png') await downloadTopologyPngFromSvg(exportSvg, filename);
            else downloadTopologySvgFromElement(exportSvg, filename);
        } catch (err) {
            console.error('Topology export failed:', err);
        }
    }, [graphBounds, theme.palette.background.paper]);

    if (loading) {
        return (
            <Box display="flex" justifyContent="center" alignItems="center" height="100%" width="100%">
                <CircularProgress size={24} />
            </Box>
        );
    }

    if (!visibleGraph || visibleGraph.nodes.length === 0) {
        return (
            <Box display="flex" justifyContent="center" alignItems="center" height="100%" width="100%">
                <Typography color="text.secondary" variant="caption">No topology data available.</Typography>
            </Box>
        );
    }

    const nowMs = Date.now();

    return (
        <Box
            ref={containerRef}
            sx={{
                width: '100%',
                height: '100%',
                minHeight: 0,
                overflow: 'hidden',
                display: 'flex',
                flexDirection: 'column',
                bgcolor: 'background.paper',
            }}
        >
            <Stack
                direction="row"
                spacing={1}
                alignItems="center"
                sx={{
                    flexShrink: 0,
                    px: 1,
                    py: 0.6,
                    borderBottom: 1,
                    borderColor: 'divider',
                    bgcolor: alpha(theme.palette.background.paper, 0.98),
                }}
            >
                <TextField
                    size="small"
                    placeholder="Search..."
                    value={searchQuery}
                    onChange={(e) => setSearchQuery(e.target.value)}
                    sx={{
                        width: { xs: 140, sm: 180 },
                        flexShrink: 0,
                        '& .MuiInputBase-root': { height: 30, fontSize: '0.75rem' },
                    }}
                    InputProps={{
                        startAdornment: <InputAdornment position="start"><SearchIcon sx={{ fontSize: 18, opacity: 0.55 }} /></InputAdornment>,
                        endAdornment: searchQuery ? (
                            <InputAdornment position="end">
                                <IconButton size="small" onClick={() => setSearchQuery('')} sx={{ p: 0.25 }}><ClearIcon sx={{ fontSize: 15 }} /></IconButton>
                            </InputAdornment>
                        ) : undefined,
                    }}
                />
                <Box sx={{ flex: 1, minWidth: 8 }} />
                {lastRefreshedAt && (
                    <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.62rem', display: { xs: 'none', md: 'block' }, whiteSpace: 'nowrap', flexShrink: 0 }}>
                        Updated {formatRefreshedAt(lastRefreshedAt)}
                    </Typography>
                )}
                <Stack direction="row" spacing={0.25} alignItems="center" sx={{ flexShrink: 0 }}>
                    <Tooltip title="Fit to view"><IconButton size="small" onClick={fitToView}><FitScreenIcon fontSize="small" /></IconButton></Tooltip>
                    {showFullscreenButton && onOpenFullscreen && (
                        <Tooltip title="Open full screen"><IconButton size="small" onClick={onOpenFullscreen}><OpenInFullIcon fontSize="small" /></IconButton></Tooltip>
                    )}
                    <Tooltip title="Export diagram"><IconButton size="small" onClick={(e) => setExportAnchor(e.currentTarget)}><DownloadIcon fontSize="small" /></IconButton></Tooltip>
                    <Tooltip title="Expand all"><span><IconButton size="small" onClick={expandAll} disabled={collapsedIds.size === 0}><UnfoldMoreIcon fontSize="small" /></IconButton></span></Tooltip>
                    <Tooltip title="Collapse switches / APs"><span><IconButton size="small" onClick={collapseAllInfra} disabled={collapsibleIds.size === 0}><AccountTreeOutlinedIcon fontSize="small" /></IconButton></span></Tooltip>
                </Stack>
            </Stack>

            <Menu anchorEl={exportAnchor} open={Boolean(exportAnchor)} onClose={() => setExportAnchor(null)}>
                <MenuItem onClick={() => void handleExport('png')}>Download PNG</MenuItem>
                <MenuItem onClick={() => void handleExport('svg')}>Download SVG</MenuItem>
            </Menu>

            <Box ref={graphAreaRef} sx={{ flex: 1, minHeight: 0, position: 'relative', overflow: 'hidden' }}>
            <TopologyLegend />

            {pathFocusActive && (
                <Stack
                    direction="row"
                    spacing={0.5}
                    alignItems="center"
                    sx={{
                        position: 'absolute',
                        top: 8,
                        left: 8,
                        zIndex: 3,
                        px: 1,
                        py: 0.35,
                        borderRadius: 1,
                        bgcolor: alpha(theme.palette.warning.main, 0.12),
                        border: 1,
                        borderColor: alpha(theme.palette.warning.main, 0.45),
                        maxWidth: 'min(420px, calc(100% - 16px))',
                    }}
                >
                    <Typography variant="caption" sx={{ fontSize: '0.62rem', fontWeight: 600, color: 'warning.main', whiteSpace: 'nowrap' }}>
                        Signal path
                    </Typography>
                    <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.62rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {pathFocusLabel}
                    </Typography>
                    <IconButton size="small" onClick={clearPathFocus} sx={{ p: 0.2, ml: 0.25 }} aria-label="Clear signal path">
                        <ClearIcon sx={{ fontSize: 14 }} />
                    </IconButton>
                </Stack>
            )}

            {collapsedIds.size > 0 && (
                <Button size="small" variant="outlined" onClick={expandAll} sx={{
                    position: 'absolute', bottom: 10, left: 12, zIndex: 2, fontWeight: 800, fontSize: '0.7rem',
                    bgcolor: alpha(theme.palette.background.paper, 0.94),
                }}>
                    Expand all ({collapsedIds.size})
                </Button>
            )}

            <Box
                component="svg"
                ref={svgRef}
                width="100%"
                height="100%"
                viewBox={`0 0 ${dimensions.w} ${dimensions.h}`}
                preserveAspectRatio="xMidYMid meet"
                onWheel={handleWheel}
                onPointerDown={handleSvgPointerDown}
                onPointerMove={handleSvgPointerMove}
                onPointerUp={handleSvgPointerUp}
                onPointerLeave={handleSvgPointerUp}
                onClick={handleSvgClick}
                sx={{ display: 'block', cursor: isPanning ? 'grabbing' : 'grab', touchAction: 'none' }}
            >
                <defs>
                    <pattern id="topology-dot-grid" width="20" height="20" patternUnits="userSpaceOnUse">
                        <circle cx="1" cy="1" r="0.75" fill={alpha(theme.palette.text.primary, 0.08)} />
                    </pattern>
                </defs>
                <rect width={dimensions.w} height={dimensions.h} fill="url(#topology-dot-grid)" pointerEvents="none" />

                <g ref={graphGroupRef} data-topology-graph="1" transform={`translate(${transform.x},${transform.y}) scale(${transform.k})`}>
                    {/* Shared trunk + bus (neutral) */}
                    {busGroups.map((group) => {
                        const busOnPath = !pathFocusActive || signalPath!.busSourceIds.has(group.sourceId);
                        const trunkStroke = pathFocusActive && busOnPath ? pathFocusStroke(theme) : trunkStrokeColor(theme);
                        const trunkGlow = trunkStrokeGlowColor(theme);
                        const trunkOpacity = trunkStrokeOpacity(pathFocusActive, busOnPath);
                        const trunkW = pathFocusActive && busOnPath ? 2.8 : trunkStrokeWidth();
                        const renderBackbonePath = (d: string, key: string) => (
                            <g key={key} pointerEvents="none">
                                <path
                                    d={d}
                                    fill="none"
                                    stroke={trunkGlow}
                                    strokeWidth={trunkW + 4}
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeOpacity={trunkOpacity * 0.65}
                                />
                                <path
                                    d={d}
                                    fill="none"
                                    stroke={trunkStroke}
                                    strokeWidth={trunkW}
                                    strokeDasharray={pathFocusActive && busOnPath ? undefined : trunkStrokeDash()}
                                    strokeLinecap="round"
                                    strokeLinejoin="round"
                                    strokeOpacity={trunkOpacity}
                                />
                            </g>
                        );
                        return (
                        <g key={`bus-${group.sourceId}`} pointerEvents="none">
                            {group.trunkPath && renderBackbonePath(group.trunkPath, `trunk-${group.sourceId}`)}
                            {group.busPath && renderBackbonePath(group.busPath, `bus-${group.sourceId}`)}
                        </g>
                        );
                    })}

                    {/* Per-device drop legs (colored by status / link kind) */}
                    {busGroups.flatMap((group) =>
                        group.segments.map((seg) => {
                            const visual = linkVisual(seg.link, seg.target);
                            const isLinkHovered = hoverLinkKey === seg.key;
                            const segOnPath = !pathFocusActive || signalPath!.segmentKeys.has(seg.key);
                            const isPathFocused = pathFocusActive && pathFocusTargetId === seg.target.id;
                            const baseW = linkStrokeWidth(visual);
                            const stroke = pathFocusActive && segOnPath ? pathFocusStroke(theme) : linkStrokeColor(visual, theme);
                            const strokeW = pathFocusActive && segOnPath ? baseW + 1.5 : isLinkHovered ? baseW + 1 : baseW;
                            const strokeOpacity = pathDimOpacity(pathFocusActive, segOnPath, segOnPath && pathFocusActive ? 1 : visual === 'offline' ? 0.85 : 0.9);
                            return (
                                <g key={`vis-${seg.key}`}>
                                    <path
                                        d={seg.dropPath}
                                        fill="none"
                                        stroke={stroke}
                                        strokeWidth={strokeW}
                                        strokeDasharray={pathFocusActive && segOnPath ? undefined : linkStrokeDash(visual)}
                                        strokeOpacity={strokeOpacity}
                                        pointerEvents="none"
                                    />
                                    {seg.portLabel && (
                                        <g
                                            transform={`translate(${seg.labelPos.x}, ${seg.labelPos.y})`}
                                            opacity={pathDimOpacity(pathFocusActive, segOnPath)}
                                            style={{ cursor: 'pointer' }}
                                            onPointerDown={(e) => e.stopPropagation()}
                                            onClick={(e) => { e.stopPropagation(); togglePathFocus(seg.target.id); }}
                                        >
                                            <rect
                                                x={-Math.max(22, seg.portLabel.length * 2.6)}
                                                y={-7}
                                                width={Math.max(44, seg.portLabel.length * 5.2)}
                                                height={13}
                                                rx={3}
                                                fill={alpha(theme.palette.background.paper, isPathFocused ? 1 : 0.92)}
                                                stroke={isPathFocused || (pathFocusActive && segOnPath)
                                                    ? pathFocusStroke(theme)
                                                    : alpha(theme.palette.divider, 0.9)}
                                                strokeWidth={isPathFocused ? 1.8 : 1}
                                            />
                                            <text
                                                textAnchor="middle"
                                                dominantBaseline="middle"
                                                fill={isPathFocused ? pathFocusStroke(theme) : theme.palette.text.primary}
                                                fontSize={9}
                                                fontWeight={700}
                                                fontFamily={TOPOLOGY_FONT}
                                                pointerEvents="none"
                                            >
                                                {seg.portLabel}
                                            </text>
                                        </g>
                                    )}
                                </g>
                            );
                        }),
                    )}

                    {visibleGraph.nodes.map((node) => {
                        if (node.x == null || node.y == null) return null;
                        const isRoot = node.id === 'root_gateway';
                        const isOnline = node.status === 'online';
                        const isInfra = !!node.isInfrastructure;
                        const isCollapsed = collapsedIds.has(node.id);
                        const isHovered = hoverNodeId === node.id;
                        const isSelected = selectedNodeId === node.id;
                        const isMatch = searchMatches.has(node.id);
                        const isNew = !isRoot && isNewlyDiscovered(node.firstDiscoveredUtc, nowMs + pulseTick * 0);
                        const showSecurityRing = !isRoot && node.id !== 'host_machine' && shouldShowSecurityRing(node.securityGrade);
                        const fill = nodeFill(isRoot, isInfra, isOnline);
                        const iconHref = iconUrlByNodeId.get(node.id) || '';
                        const labelLines = buildNodeLabelLines(node);

                        const isPathNode = !pathFocusActive || signalPath!.nodeIds.has(node.id);
                        const isPathEndpoint = pathFocusActive && pathFocusTargetId === node.id;
                        const isViewerHost = viewerNodeId != null && node.id === viewerNodeId;

                        return (
                            <g
                                key={node.id}
                                data-topology-node="1"
                                transform={`translate(${node.x}, ${node.y})`}
                                opacity={pathDimOpacity(pathFocusActive, isPathNode)}
                                style={{ cursor: isRoot ? 'default' : 'pointer' }}
                                onMouseEnter={() => setHoverNodeId(node.id)}
                                onMouseLeave={() => setHoverNodeId((prev) => (prev === node.id ? null : prev))}
                                onClick={(e) => { e.stopPropagation(); handleNodeClick(node.id); }}
                                onContextMenu={(e) => { e.preventDefault(); if (isInfra && (node.childCount ?? 0) > 0) toggleCollapse(node.id); }}
                            >
                                {isNew && (
                                    <circle r={NODE_R + 4} fill="none" stroke={theme.palette.info.main} strokeWidth={2} opacity={0.5}>
                                        <animate attributeName="r" values={`${NODE_R + 4};${NODE_R + 14}`} dur="2s" repeatCount="indefinite" />
                                        <animate attributeName="opacity" values="0.55;0" dur="2s" repeatCount="indefinite" />
                                    </circle>
                                )}
                                {isViewerHost && (
                                    <>
                                        <circle r={NODE_R + 7} fill="none" stroke={theme.palette.info.main} strokeWidth={2.2} opacity={0.5}>
                                            <animate attributeName="r" values={`${NODE_R + 7};${NODE_R + 18}`} dur="2.4s" repeatCount="indefinite" />
                                            <animate attributeName="opacity" values="0.65;0.12;0.65" dur="2.4s" repeatCount="indefinite" />
                                        </circle>
                                        <circle r={NODE_R + 4} fill="none" stroke={theme.palette.info.light} strokeWidth={2.4} opacity={0.9} pointerEvents="none" />
                                    </>
                                )}
                                {isMatch && (
                                    <circle r={NODE_R + 5} fill="none" stroke={theme.palette.primary.main} strokeWidth={2.5} opacity={0.85}>
                                        <animate attributeName="opacity" values="0.85;0.35;0.85" dur="1.2s" repeatCount="indefinite" />
                                    </circle>
                                )}
                                {(isHovered || isSelected) && !isRoot && (
                                    <circle r={NODE_R + 6} fill="none" stroke={isSelected ? theme.palette.warning.main : alpha(theme.palette.primary.main, 0.35)} strokeWidth={1.5} opacity={0.9} pointerEvents="none" />
                                )}
                                {showSecurityRing && (
                                    <circle r={NODE_R + 4} fill="none" stroke={securityRingColor(node.securityGrade, theme)} strokeWidth={2} pointerEvents="none" />
                                )}
                                {pathFocusActive && isPathNode && (
                                    <circle
                                        r={NODE_R + 3}
                                        fill="none"
                                        stroke={pathFocusStroke(theme)}
                                        strokeWidth={isPathEndpoint ? 2.4 : 1.6}
                                        opacity={isPathEndpoint ? 0.95 : 0.55}
                                        pointerEvents="none"
                                    />
                                )}
                                <circle
                                    r={NODE_R}
                                    fill={fill}
                                    fillOpacity={isOnline ? 1 : 0.8}
                                    stroke={
                                        isViewerHost
                                            ? theme.palette.info.light
                                            : isSelected
                                            ? theme.palette.warning.main
                                            : isCollapsed
                                                ? theme.palette.warning.main
                                                : !isOnline && !isRoot
                                                    ? alpha(theme.palette.error.main, 0.65)
                                                    : alpha(theme.palette.common.white, 0.35)
                                    }
                                    strokeWidth={isViewerHost ? 2.6 : isSelected ? 2.8 : isCollapsed ? 2.4 : !isOnline && !isRoot ? 1.8 : 1.4}
                                />
                                {iconHref && (
                                    <image
                                        href={iconHref}
                                        x={-NODE_R}
                                        y={-NODE_R}
                                        width={NODE_R * 2}
                                        height={NODE_R * 2}
                                        opacity={isOnline ? 1 : 0.55}
                                        pointerEvents="none"
                                    />
                                )}
                                {isInfra && (node.childCount ?? 0) > 0 && (
                                    <g transform={`translate(${NODE_R * 0.75}, ${-NODE_R * 0.75})`}>
                                        <circle r={7} fill={isCollapsed ? theme.palette.warning.main : theme.palette.background.paper} stroke={theme.palette.warning.main} strokeWidth={1.2} />
                                        <text textAnchor="middle" dominantBaseline="middle" fill={isCollapsed ? theme.palette.warning.contrastText : theme.palette.warning.main} fontSize={9} fontWeight={700} fontFamily={TOPOLOGY_FONT}>
                                            {isCollapsed ? '+' : '-'}
                                        </text>
                                    </g>
                                )}
                                <g transform={`translate(0, ${LABEL_OFFSET})`} pointerEvents="none">
                                    {labelLines.map((line, idx) => {
                                        const style = nodeLabelLineStyle(line.role, theme, { isOnline, isSelected, isMatch });
                                        return (
                                            <text
                                                key={`${node.id}-lbl-${idx}`}
                                                y={idx * 13 + 10}
                                                textAnchor="middle"
                                                fill={style.fill}
                                                fontSize={style.fontSize}
                                                fontWeight={style.fontWeight}
                                                fontFamily={TOPOLOGY_FONT}
                                            >
                                                {line.text}
                                            </text>
                                        );
                                    })}
                                </g>
                            </g>
                        );
                    })}

                    {/* Link hit targets for hover emphasis on drop legs */}
                    {busGroups.flatMap((group) =>
                        group.segments.map((seg) => (
                            <path
                                key={`hit-${seg.key}`}
                                data-topology-hit="1"
                                d={seg.dropPath}
                                fill="none"
                                stroke="transparent"
                                strokeWidth={16}
                                style={{ cursor: 'pointer' }}
                                onPointerDown={(e) => e.stopPropagation()}
                                onMouseEnter={() => setHoverLinkKey(seg.key)}
                                onMouseLeave={() => setHoverLinkKey((prev) => (prev === seg.key ? null : prev))}
                                onClick={(e) => { e.stopPropagation(); togglePathFocus(seg.target.id); }}
                            />
                        )),
                    )}
                </g>
            </Box>
            </Box>
        </Box>
    );
}
