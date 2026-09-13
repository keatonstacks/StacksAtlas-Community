
import React, { useState } from 'react';
import {
    Box, Typography, Button, Menu, MenuItem, ListItemIcon,
    ListItemText, Tooltip, alpha, useTheme, CircularProgress, Divider, Stack
} from '@mui/material';
import {
    Lan as LanIcon,
    Check as CheckIcon,
    Dns as DnsIcon,
    SettingsEthernet as EthernetIcon,
    Wifi as WifiIcon,
    SwapHoriz as SwapIcon
} from '@mui/icons-material';
import { ApiService } from '../services/apiService';
import { useGlobalStats } from '../context/GlobalStatsContext';

export const InterfaceSelector: React.FC = () => {
    const theme = useTheme();
    const { scanningRange, activeInterface, refresh } = useGlobalStats();
    const [anchorEl, setAnchorEl] = useState<null | HTMLElement>(null);
    const [interfaces, setInterfaces] = useState<any[]>([]);
    const [loading, setLoading] = useState(false);
    const [changing, setChanging] = useState(false);
    const [scannedSubnets, setScannedSubnets] = useState<any[]>([]);

    // Load subnets for tooltip (Poll to keep fresh)
    React.useEffect(() => {
        const load = () => ApiService.getNetworkSettings().then(data => setScannedSubnets(data?.subnets || [])).catch(() => { });
        load();
        const interval = setInterval(load, 5000);
        return () => clearInterval(interval);
    }, []);

    const open = Boolean(anchorEl);

    const handleClick = async (event: React.MouseEvent<HTMLButtonElement>) => {
        setAnchorEl(event.currentTarget);
        setLoading(true);
        try {
            const data = await ApiService.getInterfaces();
            setInterfaces(data || []);
        } catch (err) {
            console.error("Failed to load interfaces:", err);
        } finally {
            setLoading(false);
        }
    };

    const handleClose = () => {
        setAnchorEl(null);
    };

    const handleSelect = async (id: string | null) => {
        setChanging(true);
        try {
            await ApiService.setScanningInterface(id);
            await refresh();
            handleClose();
        } catch (err) {
            console.error("Failed to change interface:", err);
        } finally {
            setChanging(false);
        }
    };

    const getIcon = (type: string) => {
        if (type.toLowerCase().includes('wifi')) return <WifiIcon fontSize="small" />;
        if (type.toLowerCase().includes('ethernet')) return <EthernetIcon fontSize="small" />;
        return <LanIcon fontSize="small" />;
    };

    return (
        <Box>
            <Tooltip
                arrow
                title={
                    <Box sx={{ p: 1 }}>
                        <Typography variant="caption" fontWeight={900} color="primary.light">TARGET SUBNETS:</Typography>
                        {scannedSubnets.length > 0 ? (
                            scannedSubnets.map((s, idx) => {
                                const display = typeof s === 'string' ? s : s?.cidr || '';
                                return (
                                    <Typography key={display || idx} variant="body2" sx={{ fontFamily: 'monospace' }}>
                                        • {display}
                                    </Typography>
                                );
                            })
                        ) : (
                            <Typography variant="body2" color="text.secondary">Auto-Detect (Local Subnet)</Typography>
                        )}
                        <Divider sx={{ my: 1, borderColor: 'rgba(255,255,255,0.1)' }} />
                        <Typography variant="caption" sx={{ opacity: 0.7 }}>Click to change physical interface</Typography>
                    </Box>
                }
            >
                <Button
                    onClick={handleClick}
                    size="small"
                    startIcon={<LanIcon sx={{ fontSize: '1rem !important' }} />}
                    endIcon={changing ? <CircularProgress size={12} /> : <SwapIcon sx={{ fontSize: '0.8rem !important', opacity: 0.5 }} />}
                    sx={{
                        bgcolor: alpha(theme.palette.primary.main, 0.05),
                        border: '1px solid',
                        borderColor: alpha(theme.palette.primary.main, 0.1),
                        borderRadius: 2,
                        px: 1.5,
                        py: 0.5,
                        textTransform: 'none',
                        color: 'text.primary',
                        '&:hover': {
                            bgcolor: alpha(theme.palette.primary.main, 0.1),
                            borderColor: alpha(theme.palette.primary.main, 0.2),
                        }
                    }}
                >
                    <Box sx={{ textAlign: 'left' }}>
                        <Typography variant="caption" sx={{ display: 'block', fontSize: '0.65rem', fontWeight: 900, color: 'primary.main', lineHeight: 1 }}>
                            ACTIVE ADAPTER
                        </Typography>
                        <Typography variant="body2" sx={{ fontWeight: 800, fontFamily: 'monospace', lineHeight: 1.2 }}>
                            {scanningRange || "Scanning..."}
                        </Typography>
                    </Box>
                </Button>
            </Tooltip>

            <Menu
                anchorEl={anchorEl}
                open={open}
                onClose={handleClose}
                PaperProps={{
                    sx: {
                        mt: 1,
                        minWidth: 280,
                        borderRadius: 3,
                        boxShadow: `0 4px 20px ${alpha(theme.palette.common.black, 0.15)}`,
                        border: '1px solid',
                        borderColor: 'divider',
                    }
                }}
            >
                <Box px={2} py={1.5}>
                    <Typography variant="overline" fontWeight={900} sx={{ letterSpacing: 1 }}>
                        SELECT SCANNING INTERFACE
                    </Typography>
                </Box>

                <Divider />

                <MenuItem onClick={() => handleSelect(null)} selected={!activeInterface?.includes('(')}>
                    <ListItemIcon><DnsIcon fontSize="small" /></ListItemIcon>
                    <ListItemText
                        primary={<Typography variant="body2" fontWeight={800}>Auto-Detect (Recommended)</Typography>}
                        secondary="OS determines best path to 8.8.8.8"
                    />
                    {!activeInterface?.includes('(') && <CheckIcon fontSize="small" color="primary" />}
                </MenuItem>

                <Divider sx={{ my: 1, opacity: 0.5 }} />

                {loading ? (
                    <Box p={3} textAlign="center"><CircularProgress size={20} /></Box>
                ) : interfaces.map((i) => {
                    const isActive = activeInterface?.includes(i.name);
                    const isDown = i.status !== "Up";
                    return (
                        <MenuItem key={i.id} onClick={() => handleSelect(i.id)} selected={isActive} disabled={isDown && !isActive}>
                            <ListItemIcon sx={{ color: isDown ? 'text.disabled' : 'inherit' }}>{getIcon(i.type)}</ListItemIcon>
                            <ListItemText
                                primary={
                                    <Stack direction="row" alignItems="center" spacing={1}>
                                        <Typography variant="body2" fontWeight={800} sx={{ color: isDown ? 'text.secondary' : 'inherit' }}>{i.name}</Typography>
                                        {isDown && <Typography variant="caption" sx={{ bgcolor: alpha(theme.palette.error.main, 0.1), color: 'error.main', px: 0.5, borderRadius: 0.5, fontWeight: 900 }}>DOWN</Typography>}
                                    </Stack>
                                }
                                secondary={`${i.ipAddress} | ${i.description}`}
                            />
                            {isActive && <CheckIcon fontSize="small" color="primary" />}
                        </MenuItem>
                    );
                })}
            </Menu>
        </Box>
    );
};
