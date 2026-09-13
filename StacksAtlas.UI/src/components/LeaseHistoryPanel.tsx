import { useEffect, useState } from 'react';
import {
    Box,
    Typography,
    Table,
    TableBody,
    TableCell,
    TableContainer,
    TableHead,
    TableRow,
    Paper,
    CircularProgress,
    alpha,
    useTheme
} from '@mui/material';

import { ApiService } from '../services/apiService';

interface LeaseHistoryEntry {
    ipAddress: string;
    macAddress: string;
    hostname: string | null;
    vendor: string | null;
    firstSeen: string;
    lastSeen: string;
}

interface LeaseHistoryPanelProps {
    macAddress?: string;
    currentIp: string;
}

export function LeaseHistoryPanel({ macAddress, currentIp }: LeaseHistoryPanelProps) {
    const theme = useTheme();
    const [history, setHistory] = useState<LeaseHistoryEntry[]>([]);
    const [loading, setLoading] = useState(false);
    const [error, setError] = useState<string | null>(null);

    useEffect(() => {
        const fetchHistory = async () => {
            setLoading(true);
            setError(null);
            try {
                let data: LeaseHistoryEntry[] = [];

                // Prefer MAC-based history as it tracks the device across IPs
                if (macAddress && macAddress.length > 5) {
                    data = await ApiService.getLeaseHistoryByMac(macAddress);
                }
                // Fallback to IP-based history (shows what devices held this IP)
                // Only if MAC is missing (which shouldn't happen often for registered devices)
                else {
                    data = await ApiService.getLeaseHistoryByIp(currentIp);
                }

                setHistory(data);
            } catch (err) {
                console.error("Failed to fetch lease history:", err);
                setError("Unable to load history");
            } finally {
                setLoading(false);
            }
        };

        if (macAddress || currentIp) {
            fetchHistory();
        }
    }, [macAddress, currentIp]);

    if (loading) {
        return (
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, p: 2 }}>
                <CircularProgress size={16} />
                <Typography variant="caption" color="text.secondary">Loading lease history...</Typography>
            </Box>
        );
    }

    if (error) {
        return (
            <Box sx={{ p: 2, bgcolor: alpha(theme.palette.error.main, 0.05), borderRadius: 1 }}>
                <Typography variant="caption" color="error">{error}</Typography>
            </Box>
        );
    }

    if (!history || history.length === 0) {
        return (
            <Box sx={{ p: 2, textAlign: 'center', bgcolor: alpha(theme.palette.action.disabledBackground, 0.3), borderRadius: 2 }}>
                <Typography variant="caption" color="text.secondary" sx={{ fontStyle: 'italic' }}>
                    No lease history recorded for this device.
                </Typography>
            </Box>
        );
    }

    return (
        <TableContainer
            component={Paper}
            variant="outlined"
            sx={{
                maxHeight: 300,
                borderRadius: 3,
                bgcolor: alpha(theme.palette.background.paper, 0.5),
                overflowX: 'auto', // Allow horizontal scroll if content is too wide
                // Custom Scrollbar Styling
                '&::-webkit-scrollbar': {
                    width: '6px',
                    height: '6px'
                },
                '&::-webkit-scrollbar-track': {
                    background: 'transparent'
                },
                '&::-webkit-scrollbar-thumb': {
                    background: alpha(theme.palette.text.secondary, 0.2),
                    borderRadius: '10px'
                },
                '&::-webkit-scrollbar-thumb:hover': {
                    background: alpha(theme.palette.text.secondary, 0.4)
                }
            }}
        >
            <Table size="small" stickyHeader sx={{ minWidth: 500 }}>
                <TableHead>
                    <TableRow>
                        <TableCell sx={{ minWidth: 150, fontWeight: 800, fontSize: '0.65rem', letterSpacing: 0.5, color: 'text.secondary', bgcolor: theme.palette.background.paper, py: 1.5, whiteSpace: 'nowrap' }}>IP ADDRESS</TableCell>
                        <TableCell sx={{ minWidth: 120, fontWeight: 800, fontSize: '0.65rem', letterSpacing: 0.5, color: 'text.secondary', bgcolor: theme.palette.background.paper, py: 1.5, whiteSpace: 'nowrap' }}>VENDOR</TableCell>
                        <TableCell sx={{ minWidth: 100, fontWeight: 800, fontSize: '0.65rem', letterSpacing: 0.5, color: 'text.secondary', bgcolor: theme.palette.background.paper, py: 1.5, whiteSpace: 'nowrap' }}>HOSTNAME</TableCell>
                        <TableCell align="right" sx={{ minWidth: 110, fontWeight: 800, fontSize: '0.65rem', letterSpacing: 0.5, color: 'text.secondary', bgcolor: theme.palette.background.paper, py: 1.5, whiteSpace: 'nowrap' }}>SEEN</TableCell>
                    </TableRow>
                </TableHead>
                <TableBody>
                    {history.map((entry, idx) => {
                        const isCurrent = entry.ipAddress === currentIp;
                        return (
                            <TableRow
                                key={idx}
                                hover
                                sx={{
                                    bgcolor: isCurrent ? alpha(theme.palette.primary.main, 0.04) : 'inherit',
                                    '&:last-child td, &:last-child th': { border: 0 }
                                }}
                            >
                                <TableCell sx={{ fontFamily: 'monospace', fontSize: '0.75rem', fontWeight: isCurrent ? 700 : 500, color: isCurrent ? 'primary.main' : 'text.primary' }}>
                                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
                                        {entry.ipAddress}
                                        {isCurrent && (
                                            <Box
                                                sx={{
                                                    fontSize: '0.6rem',
                                                    bgcolor: 'primary.main',
                                                    color: 'white',
                                                    px: 0.8,
                                                    py: 0.2,
                                                    borderRadius: 1,
                                                    fontWeight: 700,
                                                    letterSpacing: 0.5,
                                                    lineHeight: 1
                                                }}
                                            >
                                                NOW
                                            </Box>
                                        )}
                                    </Box>
                                </TableCell>
                                <TableCell sx={{ fontSize: '0.7rem', fontWeight: 600, color: entry.vendor ? 'text.primary' : 'text.disabled' }} title={entry.vendor || ''}>
                                    {entry.vendor || 'Unknown'}
                                </TableCell>
                                <TableCell sx={{ fontSize: '0.7rem', fontWeight: 600, color: entry.hostname ? 'text.primary' : 'text.disabled' }} title={entry.hostname || ''}>
                                    {entry.hostname || ' - '}
                                </TableCell>
                                <TableCell align="right" sx={{ fontSize: '0.7rem', color: 'text.secondary', fontFamily: 'monospace', lineHeight: 1.2 }}>
                                    <Box component="span" display="block">{new Date(entry.lastSeen).toLocaleDateString()}</Box>
                                    <Box component="span" display="block" color="text.disabled" fontSize="0.65rem">{new Date(entry.lastSeen).toLocaleTimeString()}</Box>
                                </TableCell>
                            </TableRow>
                        );
                    })}
                </TableBody>
            </Table>
        </TableContainer>
    );
}
