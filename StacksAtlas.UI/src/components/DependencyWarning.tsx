import { useEffect, useState } from 'react';
import {
    Box,
    Alert,
    AlertTitle,
    Button,
    Typography,
    Collapse,
    Stack,
} from '@mui/material';
import { Warning as WarningIcon, Download as DownloadIcon } from '@mui/icons-material';
import { useGlobalStats } from '../context/GlobalStatsContext';

const DISMISS_KEY = 'stacksatlas_ack_optional_deep_scan_v1';

export default function DependencyWarning() {
    const { missingDependencies, isHub } = useGlobalStats();
    const [dismissed, setDismissed] = useState(false);

    useEffect(() => {
        try {
            if (localStorage.getItem(DISMISS_KEY) === '1') {
                setDismissed(true);
            }
        } catch {
            /* ignore */
        }
    }, []);

    const handleDismiss = () => {
        try {
            localStorage.setItem(DISMISS_KEY, '1');
        } catch {
            /* ignore */
        }
        setDismissed(true);
    };

    if (isHub) return null;

    if (!missingDependencies || missingDependencies.length === 0 || dismissed) return null;

    const needsNpcap = missingDependencies.some(d => d.includes('Npcap Driver'));
    const needsNmap = missingDependencies.some(d => d.includes('Nmap Binary'));
    const isUnixNmapOnly = needsNmap && !needsNpcap;

    return (
        <Box sx={{ mb: 3 }}>
            <Collapse in={true}>
                <Alert
                    severity="info"
                    icon={<WarningIcon fontSize="inherit" />}
                    sx={{
                        bgcolor: 'rgba(237, 108, 2, 0.05)',
                        border: '1px solid rgba(237, 108, 2, 0.2)',
                        '& .MuiAlert-icon': { color: '#ed6c02' }
                    }}
                    action={
                        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                            <Button
                                variant="outlined"
                                color="inherit"
                                size="small"
                                onClick={handleDismiss}
                                sx={{ fontWeight: 600 }}
                            >
                                Discovery is enough
                            </Button>
                            {isUnixNmapOnly ? (
                                <Button
                                    variant="contained"
                                    color="warning"
                                    size="small"
                                    href="https://stacksatlas.com/docs/installation#optional-deep-scan"
                                    target="_blank"
                                    rel="noopener noreferrer"
                                    sx={{ fontWeight: 700, boxShadow: 'none' }}
                                >
                                    Docker Deep Scan guide
                                </Button>
                            ) : (needsNpcap && needsNmap) ? (
                                <Button
                                    variant="contained"
                                    color="warning"
                                    size="small"
                                    startIcon={<DownloadIcon />}
                                    href="https://nmap.org/dist/nmap-7.95-setup.exe"
                                    target="_blank"
                                    sx={{ fontWeight: 700, boxShadow: 'none' }}
                                >
                                    Nmap + Npcap (Windows)
                                </Button>
                            ) : (
                                <>
                                    {needsNpcap && (
                                        <Button
                                            color="warning"
                                            size="small"
                                            startIcon={<DownloadIcon />}
                                            href="https://npcap.com/#download"
                                            target="_blank"
                                            sx={{ fontWeight: 600 }}
                                        >
                                            Npcap
                                        </Button>
                                    )}
                                    {needsNmap && (
                                        <Button
                                            color="warning"
                                            size="small"
                                            startIcon={<DownloadIcon />}
                                            href="https://nmap.org/download.html"
                                            target="_blank"
                                            sx={{ fontWeight: 600 }}
                                        >
                                            Nmap
                                        </Button>
                                    )}
                                </>
                            )}
                        </Stack>
                    }
                >
                    <AlertTitle sx={{ fontWeight: 700 }}>Optional: Deep Scan (Nmap not installed)</AlertTitle>
                    <Typography variant="body2" sx={{ color: 'text.secondary' }}>
                        <strong>Your network scan already works.</strong> Deep Scan (extra port/OS detail) is optional and needs Nmap.
                        {needsNpcap && !isUnixNmapOnly && (
                            <> On Windows, install Nmap from nmap.org (includes Npcap; enable WinPcap-compatible mode).</>
                        )}
                        {isUnixNmapOnly && (
                            <> On Docker, see the step-by-step guide at stacksatlas.com/docs/installation#optional-deep-scan  -  everything is copy/paste, no GitHub repo needed.</>
                        )}
                    </Typography>
                </Alert>
            </Collapse>
        </Box>
    );
}
