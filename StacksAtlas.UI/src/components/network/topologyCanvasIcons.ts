import { getDeviceIconKind, type DeviceIconKind } from '../../utils/deviceUtils';

/** Canvas-only kinds (not in deviceUtils). */
export type TopologyDrawKind = DeviceIconKind | 'switch' | 'laptop' | 'accesspoint';

function screenGlare(color: string): string {
    void color;
    return 'rgba(255,255,255,0.22)';
}

export function resolveTopologyIconKind(
    node: { group?: string | null; name?: string | null; isInfrastructure?: boolean },
    isRoot: boolean,
): TopologyDrawKind {
    if (isRoot) return 'network';

    const group = (node.group || '').toLowerCase();
    const name = (node.name || '').toLowerCase();

    if (node.isInfrastructure) {
        if (group.includes('access') || group.includes('ap') || name.includes(' ap')) return 'accesspoint';
        return 'switch';
    }

    if (group.includes('laptop') || name.includes('laptop') || name.includes('macbook')) return 'laptop';

    return getDeviceIconKind(node.group, undefined, node.name);
}

/** Draw a small white pictogram centered at (x, y). */
export function drawTopologyIcon(
    ctx: CanvasRenderingContext2D,
    kind: TopologyDrawKind,
    x: number,
    y: number,
    size: number,
    color: string,
): void {
    ctx.save();
    ctx.strokeStyle = color;
    ctx.fillStyle = color;
    ctx.lineWidth = Math.max(1.6, size * 0.155);
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';

    const s = size * 0.5;

    switch (kind) {
        case 'network': {
            ctx.strokeRect(x - s * 0.5, y - s * 0.12, s, s * 0.42);
            ctx.beginPath();
            ctx.moveTo(x, y - s * 0.12);
            ctx.lineTo(x, y - s * 0.55);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x, y - s * 0.62, s * 0.18, Math.PI * 1.15, Math.PI * 1.85);
            ctx.stroke();
            break;
        }
        case 'switch': {
            ctx.strokeRect(x - s * 0.52, y - s * 0.28, s * 1.04, s * 0.56);
            const ports = 5;
            for (let i = 0; i < ports; i += 1) {
                const px = x - s * 0.38 + (i * s * 0.76) / (ports - 1);
                ctx.beginPath();
                ctx.arc(px, y + s * 0.14, s * 0.055, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.beginPath();
            ctx.moveTo(x - s * 0.2, y - s * 0.12);
            ctx.lineTo(x + s * 0.2, y - s * 0.12);
            ctx.stroke();
            break;
        }
        case 'accesspoint': {
            ctx.beginPath();
            ctx.ellipse(x, y + s * 0.08, s * 0.38, s * 0.14, 0, 0, Math.PI * 2);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x, y - s * 0.18, s * 0.14, 0, Math.PI * 2);
            ctx.stroke();
            for (let i = 1; i <= 2; i += 1) {
                ctx.beginPath();
                ctx.arc(x, y - s * 0.18, s * (0.14 + i * 0.16), Math.PI * 1.2, Math.PI * 1.8);
                ctx.stroke();
            }
            break;
        }
        case 'hub': {
            drawTopologyIcon(ctx, 'switch', x, y, size, color);
            break;
        }
        case 'tv': {
            ctx.strokeRect(x - s * 0.58, y - s * 0.36, s * 1.16, s * 0.68);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.16, y + s * 0.32);
            ctx.lineTo(x + s * 0.16, y + s * 0.32);
            ctx.lineTo(x + s * 0.1, y + s * 0.44);
            ctx.lineTo(x - s * 0.1, y + s * 0.44);
            ctx.closePath();
            ctx.stroke();
            break;
        }
        case 'camera': {
            ctx.strokeRect(x - s * 0.4, y - s * 0.26, s * 0.72, s * 0.52);
            ctx.beginPath();
            ctx.moveTo(x + s * 0.32, y - s * 0.06);
            ctx.lineTo(x + s * 0.62, y - s * 0.2);
            ctx.lineTo(x + s * 0.62, y + s * 0.2);
            ctx.lineTo(x + s * 0.32, y + s * 0.06);
            ctx.closePath();
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x - s * 0.08, y, s * 0.12, 0, Math.PI * 2);
            ctx.stroke();
            break;
        }
        case 'audio': {
            ctx.strokeRect(x - s * 0.42, y - s * 0.42, s * 0.84, s * 0.84);
            ctx.beginPath();
            ctx.arc(x, y + s * 0.08, s * 0.22, 0, Math.PI * 2);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x, y + s * 0.08, s * 0.1, 0, Math.PI * 2);
            ctx.fill();
            ctx.beginPath();
            ctx.arc(x, y - s * 0.22, s * 0.08, 0, Math.PI * 2);
            ctx.stroke();
            break;
        }
        case 'amp': {
            ctx.strokeRect(x - s * 0.5, y - s * 0.3, s, s * 0.6);
            for (let i = 0; i < 3; i += 1) {
                ctx.beginPath();
                ctx.arc(x - s * 0.28 + i * s * 0.28, y - s * 0.12, s * 0.07, 0, Math.PI * 2);
                ctx.stroke();
            }
            ctx.beginPath();
            ctx.moveTo(x - s * 0.38, y + s * 0.14);
            ctx.lineTo(x + s * 0.38, y + s * 0.14);
            ctx.stroke();
            break;
        }
        case 'mixer': {
            ctx.strokeRect(x - s * 0.46, y - s * 0.38, s * 0.92, s * 0.76);
            for (let i = 0; i < 4; i += 1) {
                const fx = x - s * 0.28 + i * s * 0.18;
                ctx.beginPath();
                ctx.moveTo(fx, y + s * 0.28);
                ctx.lineTo(fx, y - s * 0.18);
                ctx.stroke();
                ctx.beginPath();
                ctx.arc(fx, y - s * 0.05 + (i % 2) * s * 0.08, s * 0.05, 0, Math.PI * 2);
                ctx.fill();
            }
            break;
        }
        case 'mic': {
            ctx.beginPath();
            ctx.arc(x, y - s * 0.14, s * 0.2, Math.PI, 0);
            ctx.stroke();
            ctx.strokeRect(x - s * 0.08, y - s * 0.14, s * 0.16, s * 0.38);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.16, y + s * 0.24);
            ctx.quadraticCurveTo(x, y + s * 0.34, x + s * 0.16, y + s * 0.24);
            ctx.stroke();
            ctx.beginPath();
            ctx.moveTo(x, y + s * 0.34);
            ctx.lineTo(x, y + s * 0.48);
            ctx.stroke();
            break;
        }
        case 'control': {
            ctx.strokeRect(x - s * 0.4, y - s * 0.38, s * 0.8, s * 0.76);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.16, y - s * 0.05);
            ctx.lineTo(x + s * 0.16, y - s * 0.05);
            ctx.moveTo(x, y - s * 0.22);
            ctx.lineTo(x, y + s * 0.12);
            ctx.stroke();
            ctx.fillRect(x - s * 0.08, y + s * 0.18, s * 0.16, s * 0.08);
            break;
        }
        case 'conference': {
            ctx.beginPath();
            ctx.arc(x - s * 0.22, y - s * 0.1, s * 0.22, 0, Math.PI * 2);
            ctx.arc(x + s * 0.22, y - s * 0.1, s * 0.22, 0, Math.PI * 2);
            ctx.moveTo(x - s * 0.52, y + s * 0.32);
            ctx.quadraticCurveTo(x, y + s * 0.04, x + s * 0.52, y + s * 0.32);
            ctx.stroke();
            break;
        }
        case 'mobile': {
            ctx.fillRect(x - s * 0.28, y - s * 0.5, s * 0.56, s * 1);
            ctx.strokeRect(x - s * 0.28, y - s * 0.5, s * 0.56, s * 1);
            ctx.fillStyle = screenGlare(color);
            ctx.beginPath();
            ctx.arc(x, y + s * 0.36, s * 0.05, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = color;
            ctx.fillRect(x - s * 0.16, y - s * 0.38, s * 0.32, s * 0.54);
            break;
        }
        case 'tablet': {
            ctx.fillRect(x - s * 0.38, y - s * 0.48, s * 0.76, s * 0.96);
            ctx.strokeRect(x - s * 0.38, y - s * 0.48, s * 0.76, s * 0.96);
            ctx.fillStyle = screenGlare(color);
            ctx.beginPath();
            ctx.arc(x, y + s * 0.36, s * 0.045, 0, Math.PI * 2);
            ctx.fill();
            ctx.fillStyle = color;
            ctx.fillRect(x - s * 0.28, y - s * 0.38, s * 0.56, s * 0.68);
            break;
        }
        case 'laptop': {
            ctx.fillRect(x - s * 0.36, y - s * 0.4, s * 0.72, s * 0.46);
            ctx.strokeRect(x - s * 0.36, y - s * 0.4, s * 0.72, s * 0.46);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.5, y + s * 0.08);
            ctx.lineTo(x + s * 0.5, y + s * 0.08);
            ctx.lineTo(x + s * 0.54, y + s * 0.2);
            ctx.lineTo(x - s * 0.54, y + s * 0.2);
            ctx.closePath();
            ctx.fill();
            ctx.stroke();
            break;
        }
        case 'computer': {
            ctx.fillRect(x - s * 0.4, y - s * 0.42, s * 0.8, s * 0.52);
            ctx.strokeRect(x - s * 0.4, y - s * 0.42, s * 0.8, s * 0.52);
            ctx.fillStyle = screenGlare(color);
            ctx.fillRect(x - s * 0.32, y - s * 0.34, s * 0.64, s * 0.36);
            ctx.fillStyle = color;
            ctx.fillRect(x - s * 0.14, y + s * 0.12, s * 0.28, s * 0.18);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.24, y + s * 0.3);
            ctx.lineTo(x + s * 0.24, y + s * 0.3);
            ctx.stroke();
            break;
        }
        case 'storage': {
            ctx.strokeRect(x - s * 0.44, y - s * 0.4, s * 0.88, s * 0.8);
            for (let row = 0; row < 3; row += 1) {
                const ry = y - s * 0.26 + row * s * 0.26;
                ctx.beginPath();
                ctx.moveTo(x - s * 0.36, ry);
                ctx.lineTo(x + s * 0.2, ry);
                ctx.stroke();
                ctx.beginPath();
                ctx.arc(x + s * 0.3, ry, s * 0.04, 0, Math.PI * 2);
                ctx.fill();
            }
            break;
        }
        case 'printer': {
            ctx.strokeRect(x - s * 0.44, y - s * 0.06, s * 0.88, s * 0.48);
            ctx.strokeRect(x - s * 0.3, y - s * 0.4, s * 0.6, s * 0.34);
            ctx.beginPath();
            ctx.moveTo(x - s * 0.18, y + s * 0.42);
            ctx.lineTo(x + s * 0.18, y + s * 0.42);
            ctx.stroke();
            break;
        }
        case 'security': {
            ctx.beginPath();
            ctx.moveTo(x, y - s * 0.5);
            ctx.lineTo(x + s * 0.4, y - s * 0.26);
            ctx.lineTo(x + s * 0.4, y + s * 0.1);
            ctx.quadraticCurveTo(x, y + s * 0.5, x, y + s * 0.5);
            ctx.quadraticCurveTo(x - s * 0.4, y + s * 0.1, x - s * 0.4, y - s * 0.26);
            ctx.closePath();
            ctx.stroke();
            break;
        }
        case 'gaming': {
            ctx.strokeRect(x - s * 0.5, y - s * 0.28, s, s * 0.56);
            ctx.beginPath();
            ctx.arc(x - s * 0.2, y + s * 0.02, s * 0.09, 0, Math.PI * 2);
            ctx.arc(x + s * 0.2, y + s * 0.02, s * 0.09, 0, Math.PI * 2);
            ctx.stroke();
            break;
        }
        case 'iot':
        case 'generic':
        default: {
            ctx.beginPath();
            ctx.arc(x, y, s * 0.24, 0, Math.PI * 2);
            ctx.stroke();
            ctx.beginPath();
            ctx.arc(x, y, s * 0.08, 0, Math.PI * 2);
            ctx.fill();
            break;
        }
    }

    ctx.restore();
}

/** Build a data URL for legend chips and node markers. */
export function createTopologyIconDataUrl(
    kind: TopologyDrawKind,
    size: number,
    bgColor: string,
    fgColor = '#ffffff',
): string {
    const canvas = document.createElement('canvas');
    const scale = 2;
    canvas.width = size * scale;
    canvas.height = size * scale;
    const ctx = canvas.getContext('2d');
    if (!ctx) return '';

    ctx.scale(scale, scale);
    const cx = size / 2;
    const cy = size / 2;
    const radius = size / 2 - 1.5;

    ctx.beginPath();
    ctx.arc(cx, cy, radius + 1.2, 0, Math.PI * 2);
    ctx.fillStyle = 'rgba(0,0,0,0.28)';
    ctx.fill();

    ctx.beginPath();
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.fillStyle = bgColor;
    ctx.fill();
    ctx.strokeStyle = 'rgba(255,255,255,0.28)';
    ctx.lineWidth = 1.1;
    ctx.stroke();

    drawTopologyIcon(ctx, kind, cx, cy, size * 0.9, fgColor);
    return canvas.toDataURL();
}
