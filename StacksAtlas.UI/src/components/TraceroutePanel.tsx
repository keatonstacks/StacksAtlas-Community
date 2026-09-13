import { useState } from 'react';
import {
    Box,
    Typography,
    Button,
    Stepper,
    Step,
    StepLabel,
    StepContent,
    CircularProgress,
    useTheme,
    alpha
} from '@mui/material';
import {
    Router as RouterIcon,
    PlayArrow as PlayIcon
} from '@mui/icons-material';
import { ApiService } from '../services/apiService';
import { useAuth } from '../context/AuthContext';

interface TracerouteHop {
    hopNumber: number;
    ipAddress: string;
    hostname: string;
    roundTripTime: number;
    status: number; // 0=Success, 11003=DestUnreachable, 11010=TimeExceeded, 11013=TtlExpired
    isTarget: boolean;
}

interface TraceroutePanelProps {
    targetIp: string;
    deviceId?: string;
    /** Federated device  -  trace runs from the enrolled node's LAN via hub relay. */
    hubRemoteDevice?: boolean;
}

export function TraceroutePanel({ targetIp, deviceId, hubRemoteDevice }: TraceroutePanelProps) {
    const theme = useTheme();
    const { role } = useAuth();
    const [loading, setLoading] = useState(false);
    const [hops, setHops] = useState<TracerouteHop[]>([]);
    const [error, setError] = useState<string | null>(null);

    const runTraceroute = async () => {
        setLoading(true);
        setHops([]);
        setError(null);
        try {
            const result = await ApiService.runTraceroute(targetIp, deviceId);
            if (Array.isArray(result)) {
                if (result.length === 0) {
                    setError('No hops returned. The target may be unreachable or ICMP may be blocked on this network path.');
                } else {
                    setHops(result);
                }
            } else {
                setError('Invalid response from server.');
            }
        } catch (err) {
            console.error(err);
            const message = err instanceof Error ? err.message : 'Traceroute failed to execute.';
            setError(message);
        } finally {
            setLoading(false);
        }
    };

    const getStatusColor = (hop: TracerouteHop) => {
        if (hop.ipAddress === "*" || hop.status === 11010) return "warning.main";
        if (hop.roundTripTime > 100) return "warning.main";
        if (hop.roundTripTime > 200) return "error.main";
        return "success.main";
    };

    return (
        <Box>
            {hubRemoteDevice && (
                <Box sx={{ mb: 2, p: 1.5, bgcolor: alpha(theme.palette.info.main, 0.06), borderRadius: 2, border: '1px solid', borderColor: alpha(theme.palette.info.main, 0.2) }}>
                    <Typography variant="caption" color="text.secondary">
                        Path diagnostics run from the enrolled node&apos;s network via the hub relay.
                    </Typography>
                </Box>
            )}
            <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', mb: 2 }}>
                <Typography variant="body2" color="text.secondary">
                    Visualizing Layer 3 path to <strong>{targetIp}</strong>
                </Typography>
                <Button
                    startIcon={loading ? <CircularProgress size={16} /> : <PlayIcon />}
                    variant="contained"
                    size="small"
                    onClick={runTraceroute}
                    disabled={loading || role?.toLowerCase() === 'viewer'}
                    sx={{ textTransform: 'none', borderRadius: 2 }}
                >
                    {loading ? "Tracing..." : "Run Trace"}
                </Button>
            </Box>

            {error && (
                <Typography color="error" variant="body2" sx={{ mb: 2 }}>
                    {error}
                </Typography>
            )}

            {hops.length === 0 && !loading && !error && (
                <Box sx={{ textAlign: 'center', py: 4, bgcolor: 'action.hover', borderRadius: 2, border: '1px dashed', borderColor: 'divider' }}>
                    <RouterIcon sx={{ fontSize: 40, color: 'text.disabled', mb: 1 }} />
                    <Typography variant="body2" color="text.secondary">
                        Click "Run Trace" to map the network path.
                    </Typography>
                </Box>
            )}

            {hops.length > 0 && (
                <Stepper orientation="vertical" activeStep={hops.length} sx={{ '& .MuiStepConnector-line': { minHeight: 20 } }}>
                    {hops.map((hop) => {
                        const color = getStatusColor(hop);
                        const isTimeout = hop.ipAddress === "*";

                        return (
                            <Step key={hop.hopNumber} active={true} expanded={true}>
                                <StepLabel
                                    icon={
                                        <Box
                                            sx={{
                                                bgcolor: isTimeout ? 'warning.main' : 'primary.main',
                                                color: 'white',
                                                borderRadius: '50%',
                                                width: 24,
                                                height: 24,
                                                display: 'flex',
                                                alignItems: 'center',
                                                justifyContent: 'center',
                                                fontSize: 12,
                                                fontWeight: 800
                                            }}
                                        >
                                            {hop.hopNumber}
                                        </Box>
                                    }
                                >
                                    <Box sx={{ display: 'flex', alignItems: 'baseline', gap: 1 }}>
                                        <Typography variant="body2" fontWeight={700} color={isTimeout ? 'text.secondary' : 'text.primary'}>
                                            {isTimeout ? "Request Timed Out" : hop.ipAddress}
                                        </Typography>
                                        {!isTimeout && (
                                            <Typography variant="caption" color="text.secondary" fontFamily="monospace">
                                                ({hop.hostname})
                                            </Typography>
                                        )}
                                        {!isTimeout && (
                                            <Box
                                                sx={{
                                                    ml: 'auto',
                                                    px: 1,
                                                    py: 0.25,
                                                    borderRadius: 1,
                                                    bgcolor: alpha(theme.palette.success.main, 0.1),
                                                    color: color,
                                                    fontWeight: 700,
                                                    fontSize: '0.7rem',
                                                    fontFamily: "monospace"
                                                }}
                                            >
                                                {hop.roundTripTime}ms
                                            </Box>
                                        )}
                                    </Box>
                                </StepLabel>
                                <StepContent>
                                    {/* Optional details per hop */}
                                </StepContent>
                            </Step>
                        );
                    })}
                </Stepper>
            )}
        </Box>
    );
}
