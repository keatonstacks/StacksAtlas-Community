import { useEffect, useState } from "react";
import {
    Box, Typography, Paper, Divider, TextField, Button,
    Stack, alpha, useTheme, Chip, InputAdornment, 
    CircularProgress, Snackbar, Alert, FormControl, InputLabel, Select, MenuItem, Grid
} from "@mui/material";


import {
    Mail as MailIcon,
    Save as SaveIcon
} from "@mui/icons-material";

import { ApiService } from "../services/apiService";
import { PageHeader } from "../components/pageheader";
import { useAuth } from "../context/AuthContext";
import { useIsMobileLayout } from "../hooks/useIsMobileLayout";
import { PageShell } from "../components/mobile/PageShell";

export default function ProfilePage() {
    const theme = useTheme();
    const { userId, isExternal } = useAuth();
    const isMobile = useIsMobileLayout();

    const [userData, setUserData] = useState<any>(null);
    const [loading, setLoading] = useState(true);
    const [saving, setSaving] = useState(false);
    
    // Form states
    const [email, setEmail] = useState("");
    const [alertEmail, setAlertEmail] = useState("");
    const [alertsEnabled, setAlertsEnabled] = useState(false);
    const [webhookEnabled, setWebhookEnabled] = useState(false);
    const [preferredWebhookId, setPreferredWebhookId] = useState<string | null>(null);
    const [preferredWebhookIds, setPreferredWebhookIds] = useState<string[]>([]);
    const [webhooks, setWebhooks] = useState<any[]>([]);

    const [alertOnDeviceDown, setAlertOnDeviceDown] = useState(true);
    const [alertOnDeviceUp, setAlertOnDeviceUp] = useState(true);
    const [alertOnNewDevice, setAlertOnNewDevice] = useState(true);
    const [alertSeverity, setAlertSeverity] = useState("All");

    // Feedback
    const [snackbar, setSnackbar] = useState<{ open: boolean, message: string, severity: 'success' | 'error' }>({
        open: false, message: "", severity: 'success'
    });

    useEffect(() => {
        if (userId) {
            loadProfile();
            loadWebhooks();
        }
    }, [userId]);

    const loadWebhooks = async () => {
        try {
            const data = await ApiService.getWebhooks();
            setWebhooks(data);
            
            // Clean up stale IDs from the current selection
            if (preferredWebhookIds.length > 0) {
                const validIds = preferredWebhookIds.filter(id => data.some((w: any) => w.id === id));
                if (validIds.length !== preferredWebhookIds.length) {
                    setPreferredWebhookIds(validIds);
                }
            }
        } catch (error) {
            console.error("Failed to load webhooks", error);
        }
    }

    const loadProfile = async () => {
        try {
            setLoading(true);
            const users = await ApiService.getUsers();
            const me = users.find((u: any) => u.id === userId);
            if (me) {
                setUserData(me);
                setEmail(me.email || "");
                setAlertEmail(me.alertEmail || "");
                setAlertsEnabled(me.alertsEnabled);
                setWebhookEnabled(me.webhookEnabled);
                setPreferredWebhookId(me.preferredWebhookId);
                setPreferredWebhookIds(me.preferredWebhookIds || []);
                setAlertOnDeviceDown(me.alertOnDeviceDown);
                setAlertOnDeviceUp(me.alertOnDeviceUp);
                setAlertOnNewDevice(me.alertOnNewDevice);
                setAlertSeverity(me.alertSeverity || "All");
            }
        } catch (error) {
            console.error("Failed to load profile", error);
        } finally {
            setLoading(false);
        }
    };

    const handleSave = async () => {
        if (!userId) return;
        setSaving(true);
        try {
            // Update basic info
            await ApiService.updateUser(userId, { email });
            
            // Update alert preferences
            await ApiService.updateAlertPreferences(userId, {
                alertEmail,
                alertsEnabled,
                webhookEnabled,
                preferredWebhookId,
                preferredWebhookIds,
                alertOnDeviceDown,
                alertOnDeviceUp,
                alertOnNewDevice,
                alertSeverity
            });
            
            setSnackbar({ open: true, message: "Profile updated successfully", severity: 'success' });
            loadProfile();
        } catch {
            setSnackbar({ open: true, message: "Failed to update profile", severity: 'error' });
        } finally {
            setSaving(false);
        }
    };

    if (loading) {
        return (
            <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '60vh' }}>
                <CircularProgress />
            </Box>
        );
    }

    return (
        <PageShell isMobile={isMobile} sx={{ maxWidth: 800, mx: 'auto' }}>
            <PageHeader 
                title="MY PROFILE" 
                subtitle="Manage your personal information and notification preferences."
                stats={[
                    { label: "Role", value: userData?.role?.toUpperCase() || "USER" },
                    { label: "Status", value: (alertsEnabled || webhookEnabled) ? "MONITORED" : "SILENT", color: (alertsEnabled || webhookEnabled) ? "success.main" : "text.secondary" }
                ]}
            />


            <Grid container spacing={3}>
                <Grid item xs={12} md={6}>
                    <Paper variant="outlined" sx={{ p: 3, borderRadius: 3, height: '100%' }}>
                        <Typography variant="overline" color="text.secondary" fontWeight={800}>ACCOUNT INFORMATION</Typography>
                        <Divider sx={{ mb: 3, mt: 1 }} />
                        
                        {isExternal && (
                            <Alert severity="info" sx={{ mb: 3, fontWeight: 700, fontSize: '0.75rem' }}>
                                Account managed by {userData?.provider}.
                            </Alert>
                        )}

                        <Stack spacing={3}>
                            <TextField 
                                label="Username" 
                                value={userData?.username || ""} 
                                disabled 
                                fullWidth 
                                variant="filled"
                                size="small"
                            />
                            <TextField 
                                label="Email Address" 
                                value={email} 
                                size="small"
                                onChange={(e) => setEmail(e.target.value)}
                                fullWidth
                                InputProps={{
                                    startAdornment: (
                                        <InputAdornment position="start">
                                            <MailIcon fontSize="small" />
                                        </InputAdornment>
                                    ),
                                }}
                            />
                            <Box>
                                <Typography variant="caption" color="text.secondary" display="block">Account Role</Typography>
                                <Chip 
                                    label={userData?.role?.toUpperCase()} 
                                    color="primary" 
                                    size="small" 
                                    sx={{ fontWeight: 900, mt: 1, borderRadius: 1 }} 
                                />
                            </Box>
                        </Stack>
                    </Paper>
                </Grid>

                <Grid item xs={12} md={6}>
                    <Paper 
                        variant="outlined" 
                        sx={{ 
                            p: 3, 
                            borderRadius: 3,
                            border: (alertsEnabled || webhookEnabled) ? `1px solid ${alpha(theme.palette.success.main, 0.4)}` : '1px solid divider',
                            bgcolor: (alertsEnabled || webhookEnabled) ? alpha(theme.palette.success.main, 0.02) : 'transparent'
                        }}
                    >
                        <Stack direction="row" justifyContent="space-between" alignItems="center">
                            <Typography variant="overline" color="text.secondary" fontWeight={800}>NOTIFICATION CHANNELS</Typography>
                            <Chip 
                                label={(alertsEnabled || webhookEnabled) ? "ACTIVE" : "DISABLED"} 
                                color={(alertsEnabled || webhookEnabled) ? "success" : "default"} 
                                size="small" 
                                sx={{ fontWeight: 900, fontSize: '0.6rem' }} 
                            />
                        </Stack>
                        <Divider sx={{ mb: 3, mt: 1 }} />

                        <Stack spacing={3}>
                            {/* Email Channel */}
                            <Box sx={{ p: 2, borderRadius: 2, border: '1px solid', borderColor: 'divider', bgcolor: alertsEnabled ? alpha(theme.palette.primary.main, 0.05) : 'transparent' }}>
                                <FormControl fullWidth size="small">
                                    <InputLabel>Email Alerts</InputLabel>
                                    <Select
                                        value={alertsEnabled ? "yes" : "no"}
                                        label="Email Alerts"
                                        onChange={(e) => setAlertsEnabled(e.target.value === "yes")}
                                    >
                                        <MenuItem value="yes">Enabled</MenuItem>
                                        <MenuItem value="no">Disabled</MenuItem>
                                    </Select>
                                </FormControl>

                                {alertsEnabled && (
                                    <TextField 
                                        label="Alternate Alert Email" 
                                        value={alertEmail} 
                                        size="small"
                                        onChange={(e) => setAlertEmail(e.target.value)}
                                        fullWidth
                                        sx={{ mt: 2 }}
                                        helperText="Leave blank to use account email"
                                    />
                                )}
                            </Box>

                            {/* Webhook Channel */}
                            <Box sx={{ p: 2, borderRadius: 2, border: '1px solid', borderColor: 'divider', bgcolor: webhookEnabled ? alpha(theme.palette.secondary.main, 0.05) : 'transparent' }}>
                                <FormControl fullWidth size="small">
                                    <InputLabel>Webhook Alerts</InputLabel>
                                    <Select
                                        value={webhookEnabled ? "yes" : "no"}
                                        label="Webhook Alerts"
                                        onChange={(e) => setWebhookEnabled(e.target.value === "yes")}
                                    >
                                        <MenuItem value="yes">Enabled</MenuItem>
                                        <MenuItem value="no">Disabled</MenuItem>
                                    </Select>
                                </FormControl>

                                {webhookEnabled && (
                                    <FormControl fullWidth size="small" sx={{ mt: 2 }}>
                                        <InputLabel>Select Webhooks</InputLabel>
                                        <Select
                                            multiple
                                            value={preferredWebhookIds || []}
                                            label="Select Webhooks"
                                            onChange={(e) => setPreferredWebhookIds(typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value)}
                                            renderValue={(selected) => (
                                                <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                                                    {selected.map((value: string) => {
                                                        const webhook = webhooks.find((w: any) => w.id === value);
                                                        if (!webhook) return null;
                                                        return <Chip key={value} label={webhook.name} size="small" variant="outlined" />;
                                                    })}
                                                </Box>
                                            )}
                                        >
                                            {webhooks.length === 0 && <MenuItem disabled>No webhooks configured</MenuItem>}
                                            {webhooks.map((w: any) => (
                                                <MenuItem key={w.id} value={w.id}>
                                                    {w.name} ({w.provider === 1 ? 'Slack' : w.provider === 2 ? 'Teams' : w.provider === 3 ? 'Discord' : 'Generic'})
                                                </MenuItem>
                                            ))}
                                        </Select>
                                    </FormControl>
                                )}
                            </Box>
                        </Stack>
                    </Paper>
                </Grid>

                <Grid item xs={12}>
                    <Paper variant="outlined" sx={{ p: 3, borderRadius: 3 }}>
                        <Typography variant="overline" color="text.secondary" fontWeight={800}>ALERT RULES (APPLIES TO ALL CHANNELS)</Typography>
                        <Divider sx={{ mb: 3, mt: 1 }} />
                        
                        <Grid container spacing={3}>
                            <Grid item xs={12} md={6}>
                                <FormControl fullWidth size="small">
                                    <InputLabel>Alert Scope</InputLabel>
                                    <Select
                                        value={alertSeverity}
                                        label="Alert Scope"
                                        onChange={(e) => setAlertSeverity(e.target.value)}
                                    >
                                        <MenuItem value="All">All Network Events</MenuItem>
                                        <MenuItem value="AssignedOnly">Only Devices I Manage</MenuItem>
                                    </Select>
                                </FormControl>
                            </Grid>
                            <Grid item xs={12} md={6}>
                                <Box>
                                    <Typography variant="caption" fontWeight={800} color="text.secondary" display="block" mb={1.5}>EVENT TRIGGERS</Typography>
                                    <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                                        <Chip 
                                            label="DEVICE DOWN" 
                                            color={alertOnDeviceDown ? "error" : "default"}
                                            variant={alertOnDeviceDown ? "filled" : "outlined"}
                                            onClick={() => setAlertOnDeviceDown(!alertOnDeviceDown)}
                                            sx={{ fontWeight: 700 }}
                                        />
                                        <Chip 
                                            label="DEVICE UP" 
                                            color={alertOnDeviceUp ? "success" : "default"}
                                            variant={alertOnDeviceUp ? "filled" : "outlined"}
                                            onClick={() => setAlertOnDeviceUp(!alertOnDeviceUp)}
                                            sx={{ fontWeight: 700 }}
                                        />
                                        <Chip 
                                            label="NEW DISCOVERY" 
                                            color={alertOnNewDevice ? "info" : "default"}
                                            variant={alertOnNewDevice ? "filled" : "outlined"}
                                            onClick={() => setAlertOnNewDevice(!alertOnNewDevice)}
                                            sx={{ fontWeight: 700 }}
                                        />
                                    </Box>
                                </Box>
                            </Grid>
                        </Grid>
                    </Paper>
                </Grid>

                <Grid item xs={12}>
                    <Box sx={{ display: 'flex', justifyContent: 'flex-end', mt: 2 }}>
                        <Button 
                            variant="contained" 
                            size="large" 
                            startIcon={saving ? <CircularProgress size={20} color="inherit" /> : <SaveIcon />}
                            disabled={saving}
                            onClick={handleSave}
                            sx={{ px: 6, py: 1.5, borderRadius: 2, fontWeight: 900 }}
                        >
                            {saving ? "SAVING..." : "UPDATE PROFILE"}
                        </Button>
                    </Box>
                </Grid>
            </Grid>

            <Snackbar
                open={snackbar.open}
                autoHideDuration={6000}
                onClose={() => setSnackbar({ ...snackbar, open: false })}
                anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
            >
                <Alert severity={snackbar.severity} sx={{ width: '100%', fontWeight: 700 }}>
                    {snackbar.message}
                </Alert>
            </Snackbar>
        </PageShell>
    );
}


