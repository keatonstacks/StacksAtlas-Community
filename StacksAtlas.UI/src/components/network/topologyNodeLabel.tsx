import type { Theme } from '@mui/material';
import { alpha } from '@mui/material';
import {
    truncateNodeLabel,
    type GraphBounds,
    type TopologyNode,
} from './topologyGraphUtils';
import { formatConnectionBadge } from './topologyVisualTheme';

export const TOPOLOGY_FONT = 'Inter, system-ui, -apple-system, "Segoe UI", Roboto, Helvetica, Arial, sans-serif';

export type NodeLabelRole = 'name' | 'ip' | 'port' | 'meta' | 'lan' | 'wifi';

export interface NodeLabelLine {
    text: string;
    role: NodeLabelRole;
}

export function buildNodeLabelLines(node: TopologyNode, maxNameLen = 22): NodeLabelLine[] {
    const rawName = (node.name || node.ip || node.group || 'Device').trim();
    const name = truncateNodeLabel(rawName, maxNameLen);
    const ip = (node.ip || '').trim();
    const conn = formatConnectionBadge(node.attachmentKind);
    const isOnline = (node.status || 'online') === 'online';

    const lines: NodeLabelLine[] = [{ text: name, role: 'name' }];

    if (ip && ip !== rawName) {
        if (conn && isOnline) {
            lines.push({
                text: `${ip} · ${conn}`,
                role: conn === 'Wi-Fi' ? 'wifi' : 'lan',
            });
        } else {
            lines.push({ text: ip, role: 'ip' });
        }
    } else if (conn && isOnline) {
        lines.push({ text: conn, role: conn === 'Wi-Fi' ? 'wifi' : 'lan' });
    }

    return lines;
}

interface LabelStyle {
    fill: string;
    fontSize: number;
    fontWeight: number;
}

export function nodeLabelLineStyle(
    role: NodeLabelRole,
    theme: Theme,
    opts: { isOnline: boolean; isSelected: boolean; isMatch: boolean },
): LabelStyle {
    const { isOnline, isSelected, isMatch } = opts;

    if (role === 'name') {
        if (isSelected) {
            return { fill: theme.palette.warning.main, fontSize: 11.5, fontWeight: 700 };
        }
        if (isMatch) {
            return { fill: theme.palette.primary.main, fontSize: 11.5, fontWeight: 700 };
        }
        if (!isOnline) {
            return { fill: alpha(theme.palette.text.primary, 0.45), fontSize: 11, fontWeight: 500 };
        }
        return { fill: theme.palette.text.primary, fontSize: 11, fontWeight: 600 };
    }

    if (role === 'port') {
        return { fill: theme.palette.text.primary, fontSize: 9.5, fontWeight: 700 };
    }

    if (role === 'lan') {
        return { fill: alpha(theme.palette.success.main, 0.9), fontSize: 9, fontWeight: 600 };
    }

    if (role === 'wifi') {
        return { fill: alpha(theme.palette.info.main, 0.95), fontSize: 9, fontWeight: 600 };
    }

    if (role === 'ip') {
        return {
            fill: isOnline ? alpha(theme.palette.text.secondary, 0.9) : alpha(theme.palette.text.disabled, 0.85),
            fontSize: 9.5,
            fontWeight: 500,
        };
    }

    return {
        fill: alpha(theme.palette.text.disabled, 0.85),
        fontSize: 9,
        fontWeight: 500,
    };
}

export function createTopologyExportSvg(
    graphGroup: SVGGElement,
    bounds: GraphBounds,
    bgColor: string,
    footerText?: string,
): SVGSVGElement {
    const vbX = bounds.minX;
    const vbY = bounds.minY;
    const vbW = Math.max(bounds.maxX - bounds.minX, 120);
    const vbH = Math.max(bounds.maxY - bounds.minY, 120);

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('xmlns', 'http://www.w3.org/2000/svg');
    svg.setAttribute('xmlns:xlink', 'http://www.w3.org/1999/xlink');
    svg.setAttribute('viewBox', `${vbX} ${vbY} ${vbW} ${vbH}`);
    svg.setAttribute('width', String(Math.ceil(vbW)));
    svg.setAttribute('height', String(Math.ceil(vbH)));

    const bg = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
    bg.setAttribute('x', String(vbX));
    bg.setAttribute('y', String(vbY));
    bg.setAttribute('width', String(vbW));
    bg.setAttribute('height', String(vbH));
    bg.setAttribute('fill', bgColor);
    svg.appendChild(bg);

    const clone = graphGroup.cloneNode(true) as SVGGElement;
    clone.removeAttribute('transform');
    clone.querySelectorAll('animate').forEach((n) => n.remove());
    clone.querySelectorAll('[data-topology-hit]').forEach((n) => n.remove());
    clone.querySelectorAll('image').forEach((img) => {
        const href = img.getAttribute('href') || img.getAttributeNS('http://www.w3.org/1999/xlink', 'href');
        if (href) {
            img.setAttributeNS('http://www.w3.org/1999/xlink', 'href', href);
        }
    });
    svg.appendChild(clone);

    if (footerText) {
        const footer = document.createElementNS('http://www.w3.org/2000/svg', 'text');
        footer.setAttribute('x', String(vbX + 8));
        footer.setAttribute('y', String(vbY + vbH - 8));
        footer.setAttribute('fill', '#888888');
        footer.setAttribute('font-size', '9');
        footer.setAttribute('font-family', TOPOLOGY_FONT);
        footer.textContent = footerText;
        svg.appendChild(footer);
    }

    return svg;
}

export async function downloadTopologyPngFromSvg(exportSvg: SVGSVGElement, filename: string): Promise<void> {
    const svgText = new XMLSerializer().serializeToString(exportSvg);
    const blob = new Blob([svgText], { type: 'image/svg+xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const img = new Image();

    await new Promise<void>((resolve, reject) => {
        img.onload = () => resolve();
        img.onerror = () => reject(new Error('PNG export failed'));
        img.src = url;
    });

    const w = Number(exportSvg.getAttribute('width')) || 800;
    const h = Number(exportSvg.getAttribute('height')) || 600;
    const scale = 2;

    const canvas = document.createElement('canvas');
    canvas.width = Math.ceil(w * scale);
    canvas.height = Math.ceil(h * scale);
    const ctx = canvas.getContext('2d');
    if (!ctx) throw new Error('Canvas unavailable');
    ctx.scale(scale, scale);
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

export function downloadTopologySvgFromElement(exportSvg: SVGSVGElement, filename: string): void {
    const svgText = new XMLSerializer().serializeToString(exportSvg);
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
