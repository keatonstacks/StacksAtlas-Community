import { useState } from 'react';
import {
    Box,
    Chip,
    Collapse,
    IconButton,
    Stack,
    Typography,
    alpha,
    useTheme,
} from '@mui/material';
import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import {
    linkStrokeColor,
    trunkStrokeColor,
    trunkStrokeDash,
    trunkStrokeWidth,
} from './topologyVisualTheme';

function MiniLine({ color, dash, width = 1.6 }: { color: string; dash?: string; width?: number }) {
    return (
        <Box component="svg" width={22} height={8} sx={{ flexShrink: 0 }}>
            <line x1={0} y1={4} x2={22} y2={4} stroke={color} strokeWidth={width} strokeDasharray={dash} />
        </Box>
    );
}

export function TopologyLegend() {
    const theme = useTheme();
    const [open, setOpen] = useState(false);

    const lan = linkStrokeColor('lan', theme);
    const wifi = linkStrokeColor('wifi', theme);
    const gw = linkStrokeColor('gateway', theme);
    const off = linkStrokeColor('offline', theme);
    const trunk = trunkStrokeColor(theme);

    return (
        <Box
            sx={{
                position: 'absolute',
                bottom: 8,
                right: 8,
                zIndex: 4,
                maxWidth: open ? 172 : 'none',
                pointerEvents: 'auto',
            }}
        >
            {!open ? (
                <Chip
                    size="small"
                    label="Legend"
                    onClick={() => setOpen(true)}
                    icon={<ExpandLessIcon sx={{ fontSize: '14px !important' }} />}
                    sx={{
                        height: 24,
                        fontSize: '0.65rem',
                        fontWeight: 700,
                        bgcolor: alpha(theme.palette.background.paper, 0.94),
                        border: '1px solid',
                        borderColor: 'divider',
                    }}
                />
            ) : (
                <Box
                    sx={{
                        bgcolor: alpha(theme.palette.background.paper, 0.94),
                        border: '1px solid',
                        borderColor: 'divider',
                        borderRadius: 1.5,
                        px: 1,
                        py: 0.75,
                    }}
                >
                    <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={0.5}>
                        <Typography variant="caption" fontWeight={800} sx={{ fontSize: '0.6rem', letterSpacing: 0.5 }}>
                            LEGEND
                        </Typography>
                        <IconButton size="small" onClick={() => setOpen(false)} sx={{ p: 0.15 }} aria-label="Minimize legend">
                            <ExpandMoreIcon sx={{ fontSize: 14 }} />
                        </IconButton>
                    </Stack>

                    <Collapse in={open}>
                        <Stack spacing={0.45} sx={{ mt: 0.5 }}>
                            <Stack direction="row" spacing={0.75} alignItems="center">
                                <MiniLine color={lan} width={2.2} />
                                <Typography variant="caption" sx={{ fontSize: '0.6rem' }}>LAN (solid)</Typography>
                            </Stack>
                            <Stack direction="row" spacing={0.75} alignItems="center">
                                <MiniLine color={wifi} dash="5 3" width={1.9} />
                                <Typography variant="caption" sx={{ fontSize: '0.6rem' }}>Wi-Fi</Typography>
                            </Stack>
                            <Stack direction="row" spacing={0.75} alignItems="center">
                                <MiniLine color={trunk} dash={trunkStrokeDash()} width={trunkStrokeWidth()} />
                                <Typography variant="caption" sx={{ fontSize: '0.6rem' }}>Trunk</Typography>
                            </Stack>
                            <Stack direction="row" spacing={0.75} alignItems="center">
                                <MiniLine color={gw} dash="2 3" width={1.5} />
                                <Typography variant="caption" sx={{ fontSize: '0.6rem' }}>Gateway</Typography>
                            </Stack>
                            <Stack direction="row" spacing={0.75} alignItems="center">
                                <MiniLine color={off} dash="5 4" width={2} />
                                <Typography variant="caption" sx={{ fontSize: '0.6rem' }}>Offline</Typography>
                            </Stack>
                        </Stack>

                        <Stack direction="row" spacing={1} alignItems="center" sx={{ mt: 0.65, pt: 0.5, borderTop: 1, borderColor: 'divider' }}>
                            <Box component="svg" width={12} height={12}>
                                <circle cx={6} cy={6} r={4.5} fill="none" stroke={alpha(theme.palette.info.main, 0.85)} strokeWidth={1.4} />
                            </Box>
                            <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.58rem' }}>This device</Typography>
                            <Box component="svg" width={12} height={12}>
                                <circle cx={6} cy={6} r={4} fill="none" stroke={alpha(theme.palette.info.main, 0.7)} strokeWidth={1.2} />
                            </Box>
                            <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.58rem' }}>New device</Typography>
                            <Box component="svg" width={12} height={12}>
                                <circle cx={6} cy={6} r={4.5} fill={theme.palette.grey[600]} stroke={alpha(theme.palette.error.main, 0.65)} strokeWidth={1.2} />
                            </Box>
                            <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.58rem' }}>Offline</Typography>
                        </Stack>
                        <Typography variant="caption" color="text.secondary" sx={{ fontSize: '0.55rem', lineHeight: 1.3, mt: 0.5, display: 'block' }}>
                            Click a link or port badge to trace signal path to the router. Esc or click empty space to clear.
                        </Typography>
                    </Collapse>
                </Box>
            )}
        </Box>
    );
}
