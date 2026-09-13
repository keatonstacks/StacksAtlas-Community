import type { Theme } from '@mui/material';
import { alpha } from '@mui/material';
import { normalizeSecurityGrade } from '../device-drawer/deviceDrawerUtils';

export interface TopologyNode {
    id: string;
    name: string;
    group: string;
    val: number;
    status: string;
    ip: string;
    attachmentKind?: string | null;
    attachmentPort?: string | null;
    isInfrastructure?: boolean;
    childCount?: number;
    securityGrade?: number | string | null;
    firstDiscoveredUtc?: string | null;
    vendor?: string | null;
    x?: number;
    y?: number;
}

export interface TopologyLink {
    source: string | TopologyNode;
    target: string | TopologyNode;
    kind?: string;
    port?: string | null;
}

export interface GraphTransform {
    x: number;
    y: number;
    k: number;
}

export interface GraphBounds {
    minX: number;
    minY: number;
    maxX: number;
    maxY: number;
}

export const NODE_R = 18;
export const NEW_DEVICE_PULSE_MS = 60_000;

export function linkEndpointId(end: string | TopologyNode): string {
    return typeof end === 'object' && end != null ? end.id : String(end);
}

export function buildParentMap(links: TopologyLink[]): Map<string, string> {
    const parent = new Map<string, string>();
    for (const link of links) {
        parent.set(linkEndpointId(link.target), linkEndpointId(link.source));
    }
    return parent;
}

export function ancestorIds(nodeId: string, parentMap: Map<string, string>): string[] {
    const result: string[] = [];
    let cur = parentMap.get(nodeId);
    while (cur) {
        result.push(cur);
        cur = parentMap.get(cur);
    }
    return result;
}

export interface SignalPathHighlight {
    targetId: string;
    nodeIds: Set<string>;
    segmentKeys: Set<string>;
    busSourceIds: Set<string>;
}

/** Nodes and link segments from a device up to the gateway/root. */
export function computeSignalPath(
    targetId: string | null,
    links: TopologyLink[],
    busGroups: TopologyBusGroup[],
): SignalPathHighlight | null {
    if (!targetId) return null;

    const parentMap = buildParentMap(links);
    const nodeIds = new Set<string>([targetId, ...ancestorIds(targetId, parentMap)]);

    const segmentKeys = new Set<string>();
    const busSourceIds = new Set<string>();

    for (const group of busGroups) {
        let groupOnPath = false;
        for (const seg of group.segments) {
            if (nodeIds.has(seg.target.id)) {
                segmentKeys.add(seg.key);
                groupOnPath = true;
            }
        }
        if (groupOnPath) busSourceIds.add(group.sourceId);
    }

    return { targetId, nodeIds, segmentKeys, busSourceIds };
}

export function buildOrthogonalPath(
    source: { x: number; y: number },
    target: { x: number; y: number },
    nodeR = NODE_R,
): string {
    const y1 = source.y + nodeR;
    const y2 = target.y - nodeR;
    const midY = (y1 + y2) / 2;
    return `M ${source.x} ${y1} L ${source.x} ${midY} L ${target.x} ${midY} L ${target.x} ${y2}`;
}

export interface TopologyBusSegment {
    key: string;
    dropPath: string;
    labelPos: { x: number; y: number };
    portLabel: string;
    link: TopologyLink;
    target: TopologyNode;
}

export interface TopologyBusGroup {
    sourceId: string;
    trunkPath: string;
    busPath: string;
    segments: TopologyBusSegment[];
}

/** Format port for display (normalize Port1 → Port 1). */
export function formatLinkPortLabel(port?: string | null): string {
    const raw = (port || '').trim();
    if (!raw) return '';
    if (/^\d+$/.test(raw)) return `Port ${raw}`;

    const compactPort = raw.match(/^port\s*(\d+)$/i);
    if (compactPort) return `Port ${compactPort[1]}`;

    if (/^port\b/i.test(raw)) {
        const rest = raw.replace(/^port\s*/i, '').trim();
        if (!rest) return 'Port';
        if (/^\d+$/.test(rest)) return `Port ${rest}`;
        return `Port ${rest}`;
    }

    return raw.length > 18 ? `${raw.slice(0, 16)}...` : raw;
}

export function resolveLinkPortLabel(link: TopologyLink, target: TopologyNode): string {
    return formatLinkPortLabel(link.port || target.attachmentPort);
}

/**
 * UniFi-style bus routing: one trunk from parent, shared horizontal bus, per-child colored drops.
 */
export function buildBusLinkGroups(
    links: TopologyLink[],
    nodeMap: Map<string, TopologyNode>,
    nodeR = NODE_R,
): TopologyBusGroup[] {
    const bySource = new Map<string, Array<{ link: TopologyLink; target: TopologyNode }>>();

    for (let i = 0; i < links.length; i += 1) {
        const link = links[i];
        const sourceId = linkEndpointId(link.source);
        const target = nodeMap.get(linkEndpointId(link.target));
        const source = nodeMap.get(sourceId);
        if (!source || !target || source.x == null || target.x == null || source.y == null || target.y == null) {
            continue;
        }
        const list = bySource.get(sourceId);
        const entry = { link, target };
        if (list) list.push(entry);
        else bySource.set(sourceId, [entry]);
    }

    const groups: TopologyBusGroup[] = [];

    for (const [sourceId, children] of bySource) {
        const source = nodeMap.get(sourceId)!;
        const px = source.x!;
        const py = source.y!;
        const yParentBottom = py + nodeR;

        if (children.length === 1) {
            const { link, target } = children[0];
            const tx = target.x!;
            const ty = target.y!;
            const yChildTop = ty - nodeR;
            const busY = (yParentBottom + yChildTop) / 2;
            const dropPath = `M ${tx} ${busY} L ${tx} ${yChildTop}`;
            const trunkPath = px === tx
                ? `M ${px} ${yParentBottom} L ${tx} ${yChildTop}`
                : `M ${px} ${yParentBottom} L ${px} ${busY}`;
            const busPath = px === tx ? '' : `M ${px} ${busY} L ${tx} ${busY}`;

            groups.push({
                sourceId,
                trunkPath,
                busPath,
                segments: [{
                    key: `drop-${sourceId}-${target.id}`,
                    dropPath: px === tx ? trunkPath : dropPath,
                    labelPos: { x: tx, y: busY + (yChildTop - busY) * 0.45 },
                    portLabel: resolveLinkPortLabel(link, target),
                    link,
                    target,
                }],
            });
            continue;
        }

        const childXs = children.map((c) => c.target.x!);
        const minChildTop = Math.min(...children.map((c) => c.target.y! - nodeR));
        const busY = yParentBottom + (minChildTop - yParentBottom) * 0.5;
        const minX = Math.min(px, ...childXs);
        const maxX = Math.max(px, ...childXs);

        const trunkPath = `M ${px} ${yParentBottom} L ${px} ${busY}`;
        const busPath = `M ${minX} ${busY} L ${maxX} ${busY}`;

        const segments: TopologyBusSegment[] = children.map(({ link, target }) => {
            const tx = target.x!;
            const yChildTop = target.y! - nodeR;
            const dropPath = `M ${tx} ${busY} L ${tx} ${yChildTop}`;
            return {
                key: `drop-${sourceId}-${target.id}`,
                dropPath,
                labelPos: { x: tx, y: busY + (yChildTop - busY) * 0.42 },
                portLabel: resolveLinkPortLabel(link, target),
                link,
                target,
            };
        });

        groups.push({ sourceId, trunkPath, busPath, segments });
    }

    return groups;
}

export function linkLabelPosition(
    source: { x: number; y: number },
    target: { x: number; y: number },
    nodeR = NODE_R,
): { x: number; y: number } {
    const y1 = source.y + nodeR;
    const y2 = target.y - nodeR;
    const midY = (y1 + y2) / 2;
    return { x: (source.x + target.x) / 2, y: midY };
}

export function computeGraphBounds(nodes: TopologyNode[], nodeR = NODE_R): GraphBounds {
    if (nodes.length === 0) {
        return { minX: 0, minY: 0, maxX: 800, maxY: 480 };
    }
    const xs = nodes.map((n) => n.x ?? 0);
    const ys = nodes.map((n) => n.y ?? 0);
    return {
        minX: Math.min(...xs) - nodeR - 48,
        minY: Math.min(...ys) - nodeR - 40,
        maxX: Math.max(...xs) + nodeR + 48,
        maxY: Math.max(...ys) + nodeR + 52,
    };
}

export function fitGraphTransform(
    bounds: GraphBounds,
    vw: number,
    vh: number,
    padding = 28,
    nodeCount = 1,
): GraphTransform {
    const gw = Math.max(bounds.maxX - bounds.minX, 120);
    const gh = Math.max(bounds.maxY - bounds.minY, 120);
    let k = Math.min((vw - padding * 2) / gw, (vh - padding * 2) / gh, 2.5);
    const minScale = nodeCount <= 14 ? 0.82 : nodeCount <= 28 ? 0.74 : nodeCount <= 45 ? 0.66 : 0.58;
    k = Math.max(k, minScale);
    return {
        k,
        x: (vw - gw * k) / 2 - bounds.minX * k,
        y: (vh - gh * k) / 2 - bounds.minY * k,
    };
}

export function clampTransformK(k: number): number {
    return Math.min(4, Math.max(0.12, k));
}

export function zoomAtPoint(
    transform: GraphTransform,
    pointerX: number,
    pointerY: number,
    factor: number,
): GraphTransform {
    const newK = clampTransformK(transform.k * factor);
    return {
        k: newK,
        x: pointerX - ((pointerX - transform.x) * newK) / transform.k,
        y: pointerY - ((pointerY - transform.y) * newK) / transform.k,
    };
}

export function securityGradeRingColor(grade: number | string | null | undefined, theme: Theme): string {
    const g = normalizeSecurityGrade(grade);
    if (g === 2) return theme.palette.error.main;
    if (g === 1) return theme.palette.warning.main;
    return alpha(theme.palette.success.main, 0.55);
}

export function isNewlyDiscovered(firstDiscoveredUtc?: string | null, nowMs = Date.now()): boolean {
    if (!firstDiscoveredUtc) return false;
    const t = Date.parse(firstDiscoveredUtc);
    if (Number.isNaN(t)) return false;
    return nowMs - t < NEW_DEVICE_PULSE_MS;
}

export function nodeMatchesSearch(node: TopologyNode, query: string): boolean {
    const tokens = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    if (tokens.length === 0) return false;
    const hay = [node.name, node.ip, node.group, node.vendor, node.attachmentPort]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
    return tokens.every((t) => hay.includes(t));
}

export function truncateNodeLabel(label: string, max = 22): string {
    const t = label.trim();
    if (t.length <= max) return t;
    return `${t.slice(0, max - 1)}...`;
}

/** @deprecated Use createTopologyExportSvg from topologyNodeLabel.tsx */
export async function downloadTopologyPng(svgEl: SVGSVGElement, filename: string, bgColor = '#ffffff'): Promise<void> {
    const clone = svgEl.cloneNode(true) as SVGSVGElement;
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    const bbox = svgEl.getBoundingClientRect();
    clone.setAttribute('width', String(Math.ceil(bbox.width)));
    clone.setAttribute('height', String(Math.ceil(bbox.height)));

    const svgText = new XMLSerializer().serializeToString(clone);
    const blob = new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const img = new Image();
    await new Promise<void>((resolve, reject) => {
        img.onload = () => resolve();
        img.onerror = () => reject(new Error('PNG export failed'));
        img.src = url;
    });

    const canvas = document.createElement('canvas');
    canvas.width = Math.ceil(bbox.width * 2);
    canvas.height = Math.ceil(bbox.height * 2);
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Canvas unavailable');
    ctx.scale(2, 2);
    ctx.fillStyle = bgColor;
    ctx.fillRect(0, 0, bbox.width, bbox.height);
    ctx.drawImage(img, 0, 0);
    URL.revokeObjectURL(url);

    const pngUrl = canvas.toDataURL('image/png');
    const a = document.createElement('a');
    a.href = pngUrl;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
}

export function downloadTopologySvg(svgEl: SVGSVGElement, filename: string): void {
    const clone = svgEl.cloneNode(true) as SVGSVGElement;
    clone.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    const svgText = new XMLSerializer().serializeToString(clone);
    const blob = new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
}
