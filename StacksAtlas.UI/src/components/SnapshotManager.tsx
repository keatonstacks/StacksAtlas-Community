import React, { useEffect, useState } from 'react';
import {
    Box, Typography, Paper, Button, Stack, List, ListItem, ListItemText,
    IconButton, Divider, TextField, CircularProgress, alpha, useTheme, Alert, AlertTitle, Grid,
    FormControl, FormLabel, RadioGroup, FormControlLabel, Radio, Chip
} from '@mui/material';
import {
    Storage as StorageIcon,
    Add as AddIcon,
    CloudDownload as DownloadIcon,
    Restore as RestoreIcon,
    Delete as DeleteIcon,
    History as HistoryIcon,
    Warning as WarningIcon,
    PowerSettingsNew as RestartIcon
} from '@mui/icons-material';
import { ApiService, type FreshStartScope, type SnapshotStoreKind } from '../services/apiService';
import { useGlobalStats } from '../context/GlobalStatsContext';
import { useConfirm } from '../context/ConfirmContext';

interface Snapshot {
    fileName: string;
    sizeBytes: number;
    createdAt: string;
    store: SnapshotStoreKind;
}

const NODE_FRESH_START_OPTIONS: { value: FreshStartScope; label: string; description: string }[] = [
    {
        value: 'ApplianceOnly',
        label: 'Reset this site',
        description: 'Wipes local StacksAtlas.db (devices, users, history on this Node). Does not affect the Hub or other sites. Re-enrollment required after restart.',
    },
    {
        value: 'FactoryReset',
        label: 'Factory reset',
        description: 'Wipes local database and JSON settings on this Node. Preserves license.key. Re-enroll with the Hub after restart.',
    },
];

const HUB_FRESH_START_OPTIONS: { value: FreshStartScope; label: string; description: string }[] = [
    {
        value: 'ApplianceOnly',
        label: 'Reset appliance only',
        description: 'Wipes StacksAtlas.db (Hub users, local config). Preserves fleet inventory (StacksAtlas.Hub.db).',
    },
    {
        value: 'FleetInventoryOnly',
        label: 'Reset fleet inventory only',
        description: 'Wipes StacksAtlas.Hub.db (enrolled nodes, fleet devices, federated logs). Keeps Hub appliance data and settings.',
    },
    {
        value: 'ApplianceAndFleet',
        label: 'Reset appliance + fleet',
        description: 'Recommended for Hub re-onboarding. Wipes both databases. License and JSON settings preserved.',
    },
    {
        value: 'FactoryReset',
        label: 'Factory reset',
        description: 'Wipes both databases and resets JSON settings. Preserves license.key only.',
    },
];

export const SnapshotManager: React.FC = () => {
    const theme = useTheme();
    const { isHub } = useGlobalStats();
    const { confirm } = useConfirm();
    const [snapshots, setSnapshots] = useState<Snapshot[]>([]);
    const [hubDatabasePresent, setHubDatabasePresent] = useState(false);
    const [loading, setLoading] = useState(true);
    const [actionLoading, setActionLoading] = useState(false);
    const [label, setLabel] = useState("");
    const [freshStartScope, setFreshStartScope] = useState<FreshStartScope>('ApplianceOnly');
    const [message, setMessage] = useState<{ type: 'success' | 'info' | 'error' | 'warning', text: string } | null>(null);

    const loadSnapshots = async () => {
        try {
            setLoading(true);
            const data = await ApiService.getSnapshots();
            setSnapshots((data.snapshots || []).map((s: any) => ({
                ...s,
                store: s.store === 1 || s.store === 'Fleet' ? 'Fleet' : 'Appliance',
            })));
            setHubDatabasePresent(data.hubDatabasePresent ?? false);
        } catch (err: any) {
            setMessage({ type: 'error', text: `Failed to load snapshots: ${err.message}` });
        } finally {
            setLoading(false);
        }
    };

    useEffect(() => {
        loadSnapshots();
    }, []);

    const isHubBrain = isHub && hubDatabasePresent;

    useEffect(() => {
        if (!isHubBrain && (freshStartScope === 'FleetInventoryOnly' || freshStartScope === 'ApplianceAndFleet')) {
            setFreshStartScope('ApplianceOnly');
        }
    }, [isHubBrain, freshStartScope]);

    const handleCreate = async () => {
        try {
            setActionLoading(true);
            const result = await ApiService.createSnapshot(label || "Manual Backup");
            setLabel("");
            const fleetNote = result.fleetFileName
                ? ` Fleet backup: ${result.fleetFileName}.`
                : '';
            setMessage({
                type: 'success',
                text: `Appliance backup created: ${result.applianceFileName ?? result.fileName}.${fleetNote}`,
            });
            loadSnapshots();
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message });
        } finally {
            setActionLoading(false);
        }
    };

    const handleRestore = async (fileName: string, store: SnapshotStoreKind) => {
        const storeLabel = store === 'Fleet' ? 'Hub fleet (StacksAtlas.Hub.db)' : 'appliance (StacksAtlas.db)';
        const ok = await confirm({
            title: 'Restore snapshot',
            message: `Restore ${fileName}?\n\nThis will replace the ${storeLabel} on next restart. A restart is required after staging.`,
            confirmColor: 'warning',
        });
        if (!ok) return;

        try {
            setActionLoading(true);
            const result = await ApiService.restoreSnapshot(fileName);
            setMessage({ type: 'warning', text: result.message || "Restoration staged. Please restart the service." });
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message });
        } finally {
            setActionLoading(false);
        }
    };

    const handleDelete = async (fileName: string) => {
        const ok = await confirm({
            title: 'Delete snapshot',
            message: `Delete ${fileName}? This cannot be undone.`,
            confirmLabel: 'Delete',
            confirmColor: 'error',
        });
        if (!ok) return;

        try {
            setActionLoading(true);
            await ApiService.deleteSnapshot(fileName);
            loadSnapshots();
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message });
        } finally {
            setActionLoading(false);
        }
    };

    const describeFreshStartConfirm = (scope: FreshStartScope): string => {
        if (!isHubBrain) {
            switch (scope) {
                case 'FactoryReset':
                    return 'This backs up local data, then wipes this Node\'s database and JSON settings on next restart. The Hub and other sites are not affected. Re-enrollment required.';
                default:
                    return 'This backs up local data, then wipes this Node\'s StacksAtlas.db on next restart. The Hub fleet is not affected. Re-enrollment required.';
            }
        }

        switch (scope) {
            case 'ApplianceOnly':
                return 'This backs up both stores, then wipes StacksAtlas.db on next restart. Hub fleet inventory is preserved.';
            case 'FleetInventoryOnly':
                return 'This backs up both stores, then wipes StacksAtlas.Hub.db on next restart. Hub appliance data and settings are preserved. Nodes must re-enroll.';
            case 'ApplianceAndFleet':
                return 'This backs up both stores, then wipes StacksAtlas.db and StacksAtlas.Hub.db on next restart. License preserved; nodes must re-enroll.';
            case 'FactoryReset':
                return 'This backs up both stores, wipes all databases, and removes JSON settings on next restart. Only license.key is preserved.';
            default:
                return 'Proceed with Fresh Start?';
        }
    };

    const handleFreshStart = async () => {
        const ok = await confirm({
            title: 'Fresh start',
            message: `CRITICAL: ${describeFreshStartConfirm(freshStartScope)}\n\nProceed?`,
            confirmLabel: 'Proceed',
            confirmColor: 'error',
        });
        if (!ok) return;

        try {
            setActionLoading(true);
            const result = await ApiService.triggerFreshStart(freshStartScope);
            setMessage({ type: 'warning', text: result.message || "Fresh start staged. Service restart required." });
        } catch (err: any) {
            setMessage({ type: 'error', text: err.message });
        } finally {
            setActionLoading(false);
        }
    };

    const handleRestart = async () => {
        const ok = await confirm({
            title: 'Restart engine',
            message: 'Restart the StacksAtlas engine? This will disconnect all active web sessions for a few seconds.',
            confirmColor: 'warning',
        });
        if (!ok) return;
        try {
            setActionLoading(true);
            await ApiService.restartSystem();
            setMessage({ type: 'success', text: "Restart command sent. The page will reload automatically in 5 seconds." });
            setTimeout(() => window.location.reload(), 5000);
        } catch (err: any) {
            setMessage({ type: 'error', text: `Restart failed: ${err.message}` });
        } finally {
            setActionLoading(false);
        }
    };

    const formatSize = (bytes: number) => (bytes / (1024 * 1024)).toFixed(2) + " MB";

    const isRestartRequired = message?.type === 'warning' && message.text.toLowerCase().includes('restart');

    const visibleFreshStartOptions = isHubBrain ? HUB_FRESH_START_OPTIONS : NODE_FRESH_START_OPTIONS;

    const visibleSnapshots = snapshots.filter((s) => isHubBrain || s.store === 'Appliance');

    const storeChip = (store: SnapshotStoreKind) => (
        <Chip
            size="small"
            label={store === 'Fleet' ? 'FLEET' : 'APPLIANCE'}
            color={store === 'Fleet' ? 'secondary' : 'primary'}
            variant="outlined"
            sx={{ fontWeight: 800, fontSize: '0.65rem' }}
        />
    );

    return (
        <Box>
            <Stack direction="row" alignItems="center" spacing={1.5} mb={2}>
                <StorageIcon color="primary" />
                <Typography variant="h6" fontWeight={900}>DATA SOVEREIGNTY</Typography>
                <Box sx={{ flexGrow: 1 }} />
                <Button
                    variant={isRestartRequired ? "contained" : "outlined"}
                    color={isRestartRequired ? "warning" : "primary"}
                    startIcon={<RestartIcon />}
                    size="small"
                    onClick={handleRestart}
                    disabled={actionLoading}
                    sx={{
                        fontWeight: 900,
                        borderRadius: 2,
                        animation: isRestartRequired ? 'pulseWarning 2s infinite' : 'none',
                        '@keyframes pulseWarning': {
                            '0%': { transform: 'scale(1)', boxShadow: '0 0 0 0 rgba(237, 108, 2, 0.4)' },
                            '70%': { transform: 'scale(1.05)', boxShadow: '0 0 0 10px rgba(237, 108, 2, 0)' },
                            '100%': { transform: 'scale(1)', boxShadow: '0 0 0 0 rgba(237, 108, 2, 0)' }
                        }
                    }}
                >
                    RESTART SYSTEM
                </Button>
            </Stack>
            <Typography variant="caption" color="text.secondary" display="block" mb={3}>
                {isHubBrain ? (
                    <>
                        Snapshots back up <strong>StacksAtlas.db</strong> (appliance) and <strong>StacksAtlas.Hub.db</strong> (fleet).
                        Manual backups create both. Restoration is staged and applied on restart.
                    </>
                ) : (
                    <>
                        Snapshots back up <strong>StacksAtlas.db</strong> on this Node only.
                        Fleet inventory lives on the Hub and is not managed from a Node.
                    </>
                )}
            </Typography>

            {message && (
                <Alert
                    severity={message.type}
                    sx={{ mb: 3, borderLeft: '4px solid', borderColor: `${message.type}.main` }}
                    onClose={() => setMessage(null)}
                >
                    <AlertTitle sx={{ fontWeight: 900, fontSize: '0.75rem' }}>SYSTEM NOTIFICATION</AlertTitle>
                    <Typography variant="body2" fontWeight={700}>{message.text}</Typography>
                </Alert>
            )}

            <Grid container spacing={3}>
                <Grid item xs={12} md={5}>
                    <Paper variant="outlined" sx={{ p: 3, borderRadius: 3, bgcolor: alpha(theme.palette.background.paper, 0.5) }}>
                        <Typography variant="subtitle2" fontWeight={900} mb={2}>TAKE SNAPSHOT</Typography>
                        <Stack spacing={2}>
                            <TextField
                                fullWidth
                                size="small"
                                label="Snapshot Label"
                                placeholder="e.g. Pre-Maintenance"
                                value={label}
                                onChange={(e) => setLabel(e.target.value)}
                            />
                            <Button
                                variant="contained"
                                startIcon={actionLoading ? <CircularProgress size={20} color="inherit" /> : <AddIcon />}
                                onClick={handleCreate}
                                disabled={actionLoading}
                                sx={{ fontWeight: 900 }}
                            >
                                CREATE MANUAL BACKUP
                            </Button>
                            {isHubBrain && (
                                <Typography variant="caption" color="text.secondary" fontWeight={700}>
                                    Creates appliance + fleet snapshots.
                                </Typography>
                            )}
                        </Stack>

                        <Divider sx={{ my: 3 }} />

                        <Typography variant="subtitle2" fontWeight={900} color="error" mb={1}>DANGER ZONE</Typography>
                        <Typography variant="caption" color="text.secondary" display="block" mb={2}>
                            {isHubBrain
                                ? 'All scopes auto-backup both databases before any wipe. Restart required to apply.'
                                : 'Local site reset only  -  the Hub fleet is never modified from a Node. Restart required to apply.'}
                        </Typography>

                        <FormControl component="fieldset" fullWidth sx={{ mb: 2 }}>
                            <FormLabel component="legend" sx={{ fontWeight: 800, fontSize: '0.75rem', mb: 1 }}>
                                FRESH START SCOPE
                            </FormLabel>
                            <RadioGroup
                                value={freshStartScope}
                                onChange={(e) => setFreshStartScope(e.target.value as FreshStartScope)}
                            >
                                {visibleFreshStartOptions.map((opt) => (
                                    <FormControlLabel
                                        key={opt.value}
                                        value={opt.value}
                                        sx={{ alignItems: 'flex-start', mb: 1 }}
                                        control={<Radio size="small" sx={{ mt: 0.25 }} />}
                                        label={
                                            <Box>
                                                <Typography variant="caption" fontWeight={800} display="block">
                                                    {opt.label}
                                                </Typography>
                                                <Typography variant="caption" color="text.secondary" display="block">
                                                    {opt.description}
                                                </Typography>
                                            </Box>
                                        }
                                    />
                                ))}
                            </RadioGroup>
                        </FormControl>

                        <Button
                            variant="outlined"
                            color="error"
                            fullWidth
                            startIcon={<WarningIcon />}
                            onClick={handleFreshStart}
                            disabled={actionLoading}
                            sx={{ fontWeight: 900, borderStyle: 'dashed', borderWidth: 2, '&:hover': { borderWidth: 2 } }}
                        >
                            TRIGGER FRESH START
                        </Button>
                    </Paper>
                </Grid>

                <Grid item xs={12} md={7}>
                    <Paper variant="outlined" sx={{ p: 0, borderRadius: 3, overflow: 'hidden' }}>
                        <Box px={3} py={2} bgcolor={alpha(theme.palette.primary.main, 0.05)} borderBottom="1px solid" borderColor="divider">
                            <Stack direction="row" alignItems="center" spacing={1}>
                                <HistoryIcon fontSize="small" color="primary" />
                                <Typography variant="subtitle2" fontWeight={900}>SNAPSHOT HISTORY</Typography>
                            </Stack>
                        </Box>

                        {loading ? (
                            <Box p={4} textAlign="center"><CircularProgress size={30} /></Box>
                        ) : visibleSnapshots.length === 0 ? (
                            <Box p={4} textAlign="center">
                                <Typography variant="caption" color="text.secondary" fontWeight={700}>NO SNAPSHOTS FOUND</Typography>
                            </Box>
                        ) : (
                            <List sx={{ p: 0 }}>
                                {visibleSnapshots.map((s, i) => (
                                    <ListItem
                                        key={s.fileName}
                                        divider={i < visibleSnapshots.length - 1}
                                        alignItems="flex-start"
                                        sx={{
                                            pr: 16,
                                            '&:hover': { bgcolor: alpha(theme.palette.action.hover, 0.04) },
                                            transition: 'background 0.2s',
                                        }}
                                        secondaryAction={
                                            <Stack direction="row" spacing={0.5} sx={{ mt: 0.25 }}>
                                                <IconButton size="small" color="primary" title="Download" onClick={() => ApiService.downloadSnapshot(s.fileName)}>
                                                    <DownloadIcon fontSize="small" />
                                                </IconButton>
                                                <IconButton size="small" color="info" title="Restore" onClick={() => handleRestore(s.fileName, s.store)}>
                                                    <RestoreIcon fontSize="small" />
                                                </IconButton>
                                                <IconButton size="small" color="error" title="Delete" onClick={() => handleDelete(s.fileName)}>
                                                    <DeleteIcon fontSize="small" />
                                                </IconButton>
                                            </Stack>
                                        }
                                    >
                                        <ListItemText
                                            sx={{ minWidth: 0, mr: 1 }}
                                            primary={
                                                <Typography
                                                    variant="body2"
                                                    fontWeight={800}
                                                    title={s.fileName}
                                                    sx={{
                                                        fontFamily: 'monospace',
                                                        overflow: 'hidden',
                                                        textOverflow: 'ellipsis',
                                                        whiteSpace: 'nowrap',
                                                        display: 'block',
                                                    }}
                                                >
                                                    {s.fileName}
                                                </Typography>
                                            }
                                            secondary={
                                                <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" mt={0.5}>
                                                    {storeChip(s.store)}
                                                    <Typography variant="caption" fontWeight={700}>{formatSize(s.sizeBytes)}</Typography>
                                                    <Typography variant="caption" sx={{ opacity: 0.5 }}>|</Typography>
                                                    <Typography variant="caption" fontWeight={700}>{new Date(s.createdAt).toLocaleString()}</Typography>
                                                </Stack>
                                            }
                                        />
                                    </ListItem>
                                ))}
                            </List>
                        )}
                    </Paper>
                </Grid>
            </Grid>
        </Box>
    );
};
