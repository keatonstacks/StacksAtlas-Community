import type { Theme } from '@mui/material';
import { alpha } from '@mui/material';
import { normalizeSecurityGrade } from '../device-drawer/deviceDrawerUtils';
import type { TopologyLink, TopologyNode } from './topologyGraphUtils';

export type LinkVisualKind = 'lan' | 'wifi' | 'gateway' | 'offline';

export function resolveLinkVisualKind(link: TopologyLink, target?: TopologyNode): LinkVisualKind {
    const offline = (target?.status || 'online') !== 'online';
    if (offline) return 'offline';

    const kind = (link.kind || target?.attachmentKind || 'gateway').toLowerCase();
    if (kind === 'wifi' || kind === 'wi-fi' || kind === 'wireless') return 'wifi';
    if (kind === 'ethernet' || kind === 'fiber' || kind === 'fibre' || kind === 'sfp') return 'lan';
    return 'gateway';
}

export function linkStrokeColor(visual: LinkVisualKind, theme: Theme): string {
    switch (visual) {
        case 'lan':
            return alpha(theme.palette.success.main, 0.88);
        case 'wifi':
            return alpha(theme.palette.info.main, 0.9);
        case 'offline':
            return alpha(theme.palette.error.main, 0.78);
        default:
            return alpha(theme.palette.warning.light, 0.65);
    }
}

export function linkStrokeWidth(visual: LinkVisualKind): number {
    if (visual === 'lan') return 2.2;
    if (visual === 'wifi') return 1.9;
    if (visual === 'offline') return 2;
    return 1.5;
}

export function linkStrokeDash(visual: LinkVisualKind): string | undefined {
    if (visual === 'wifi') return '5 3';
    if (visual === 'offline') return '5 4';
    if (visual === 'gateway') return '2 3';
    return undefined;
}

/** Shared backbone from parent to bus (distinct from gateway drop legs). */
export function trunkStrokeColor(theme: Theme): string {
    return alpha(theme.palette.grey[400], 0.72);
}

export function trunkStrokeGlowColor(theme: Theme): string {
    return alpha(theme.palette.primary.light, 0.14);
}

export function trunkStrokeWidth(): number {
    return 2.2;
}

/** Subtle dash so backbone reads as infrastructure, not a device link. */
export function trunkStrokeDash(): string {
    return '8 5';
}

export function trunkStrokeOpacity(pathFocusActive: boolean, onPath: boolean): number {
    if (pathFocusActive && onPath) return 1;
    if (pathFocusActive) return 0.28;
    return 0.88;
}

export function pathFocusStroke(theme: Theme): string {
    return theme.palette.warning.main;
}

export function pathDimOpacity(active: boolean, inPath: boolean, base = 1): number {
    if (!active) return base;
    return inPath ? base : 0.2;
}

export function shouldShowSecurityRing(grade: number | string | null | undefined): boolean {
    return normalizeSecurityGrade(grade) >= 1;
}

export function securityRingColor(grade: number | string | null | undefined, theme: Theme): string {
    const g = normalizeSecurityGrade(grade);
    if (g === 2) return theme.palette.error.main;
    return theme.palette.warning.main;
}

export function formatConnectionBadge(kind?: string | null): 'LAN' | 'Wi-Fi' | null {
    const k = (kind || '').toLowerCase();
    if (k === 'wifi' || k === 'wi-fi' || k === 'wireless') return 'Wi-Fi';
    if (k === 'ethernet' || k === 'fiber' || k === 'fibre' || k === 'sfp') return 'LAN';
    return null;
}

export function nodeIconFill(
    theme: Theme,
    opts: { isRoot: boolean; isInfra: boolean; isOnline: boolean },
): string {
    const { isRoot, isInfra, isOnline } = opts;
    if (isRoot) return theme.palette.info.main;
    if (!isOnline) return theme.palette.grey[600];
    if (isInfra) return theme.palette.primary.main;
    return theme.palette.success.main;
}
