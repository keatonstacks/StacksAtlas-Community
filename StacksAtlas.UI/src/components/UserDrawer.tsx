import { 
    Box, Typography, Drawer, Divider, IconButton, Stack, TextField, 
    FormControl, InputLabel, Select, MenuItem, 
    Chip, Button, alpha, useTheme, CircularProgress, Avatar, Grid, List, ListItem, ListItemIcon, ListItemText, Paper
} from "@mui/material";


import {
    Close as CloseIcon,
    Mail as MailIcon,
    CheckCircle as CheckCircleIcon,
    Warning as WarningIcon,
    Settings as SettingsIcon,
    Person as PersonIcon,
    Save as SaveIcon,
    Hub as WebhookIcon,
    CalendarToday as CalendarIcon,
    History as HistoryIcon,
    Shield as ShieldIcon
} from "@mui/icons-material";
import type { User } from "../models/User";

import { ApiService } from "../services/apiService";
import { useState, useEffect } from "react";
import { Link } from "react-router-dom";

interface UserDrawerProps {
    open: boolean;
    onClose: () => void;
    user: User | null;
    onUpdate: () => void;
    isSelf?: boolean;
    isNode?: boolean;
}

export default function UserDrawer({ open, onClose, user, onUpdate, isSelf = false, isNode = false }: UserDrawerProps) {
    const theme = useTheme();
    const [localUser, setLocalUser] = useState<User | null>(null);
    const [smtpEnabled, setSmtpEnabled] = useState<boolean | null>(null);
    const [saving, setSaving] = useState(false);
    const [webhooks, setWebhooks] = useState<any[]>([]);

    useEffect(() => {
        setLocalUser(user);
        if (open) {
            ApiService.getEmailSettings().then(settings => {
                setSmtpEnabled(settings.enabled);
            }).catch(() => setSmtpEnabled(null));
            
            ApiService.getWebhooks().then(data => {
                setWebhooks(data);
                // Clean up stale webhook IDs if the user has them selected
                if (localUser && localUser.preferredWebhookIds) {
                    const validIds = localUser.preferredWebhookIds.filter(id => 
                        data.some((w: any) => w.id === id)
                    );
                    if (validIds.length !== localUser.preferredWebhookIds.length) {
                        setLocalUser({ ...localUser, preferredWebhookIds: validIds });
                    }
                }
            }).catch(() => console.error("Failed to load webhooks"));
        }
    }, [user, open, localUser?.id]);

    if (!localUser) return null;

    const handleUpdate = async () => {
        setSaving(true);
        try {
            // Update basic info
            await ApiService.updateUser(localUser.id, {
                email: localUser.email,
                role: localUser.role
            });
            // Update alert preferences
            await ApiService.updateAlertPreferences(localUser.id, {
                alertEmail: localUser.alertEmail || "",
                alertsEnabled: localUser.alertsEnabled,
                webhookEnabled: localUser.webhookEnabled,
                preferredWebhookId: localUser.preferredWebhookId,
                preferredWebhookIds: localUser.preferredWebhookIds || [],
                alertOnDeviceDown: localUser.alertOnDeviceDown,
                alertOnDeviceUp: localUser.alertOnDeviceUp,
                alertOnNewDevice: localUser.alertOnNewDevice,
                alertSeverity: localUser.alertSeverity || "All"
            });
            onUpdate();
            onClose();
        } catch (error) {
            console.error("Failed to update user", error);
        } finally {
            setSaving(false);
        }
    };

    const hasWebhookConfigured = (localUser.preferredWebhookIds && localUser.preferredWebhookIds.length > 0) || !!localUser.preferredWebhookId;
    const isEmailReady = smtpEnabled && localUser.alertsEnabled;
    const isWebhookReady = localUser.webhookEnabled && hasWebhookConfigured;

    return (
        <Drawer
            anchor="right"
            open={open}
            onClose={onClose}
            PaperProps={{ sx: { width: { xs: '100%', sm: 500 }, backgroundImage: 'none' } }}
        >
            <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', bgcolor: 'background.default' }}>
                <Box sx={{ p: 3, display: 'flex', alignItems: 'center', justifyContent: 'space-between', bgcolor: alpha(theme.palette.primary.main, 0.05) }}>
                    <Box sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
                        <Avatar sx={{ bgcolor: theme.palette.primary.main, width: 32, height: 32 }}>
                            <PersonIcon sx={{ fontSize: 20 }} />
                        </Avatar>
                        <Typography variant="h6" fontWeight={700}>{isSelf ? "My Profile" : "Edit User"}</Typography>
                    </Box>
                    <IconButton onClick={onClose} size="small">
                        <CloseIcon />
                    </IconButton>
                </Box>

                <Divider />

                <Box sx={{ flexGrow: 1, overflowY: 'auto', p: 3 }}>
                    {isNode && (
                        <Box 
                            sx={{ 
                                p: 2, 
                                mb: 3, 
                                borderRadius: 2.5, 
                                bgcolor: alpha(theme.palette.primary.main, 0.04),
                                border: `1px solid ${alpha(theme.palette.primary.main, 0.15)}`,
                                display: 'flex', 
                                alignItems: 'center', 
                                gap: 1.5
                            }}
                        >
                            <ShieldIcon color="primary" sx={{ fontSize: 20 }} />
                            <Box>
                                <Typography variant="caption" sx={{ fontWeight: 800, display: 'block', letterSpacing: 0.5, lineHeight: 1 }}>
                                    GLOBAL GOVERNANCE ACTIVE
                                </Typography>
                                <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5, fontSize: '0.65rem' }}>
                                    Local account credentials, role levels, and notification triggers are locked in Node mode.
                                </Typography>
                            </Box>
                        </Box>
                    )}

                    {/* Channel Health Section */}
                    <Typography variant="overline" color="text.secondary" fontWeight={800} display="block" mb={2}>CHANNEL DELIVERY STATUS</Typography>
                    <Stack spacing={1.5} sx={{ mb: 4 }}>
                        {/* Email Health */}
                        <Paper variant="outlined" sx={{ p: 2, borderRadius: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderColor: isEmailReady ? alpha(theme.palette.success.main, 0.3) : 'divider', bgcolor: isEmailReady ? alpha(theme.palette.success.main, 0.02) : 'transparent' }}>
                            <Stack direction="row" spacing={2} alignItems="center">
                                {isEmailReady ? <CheckCircleIcon color="success" fontSize="small" /> : <WarningIcon color="warning" fontSize="small" />}
                                <Box>
                                    <Typography variant="subtitle2" fontWeight={700}>Email Notifications</Typography>
                                    <Typography variant="caption" color="text.secondary">
                                        {!smtpEnabled ? "System SMTP is disabled" : !localUser.alertsEnabled ? "Opted-out of emails" : "Configured and active"}
                                    </Typography>
                                </Box>
                            </Stack>
                            {!smtpEnabled && smtpEnabled !== null && (
                                <Button size="small" component={Link} to="/settings" startIcon={<SettingsIcon />} sx={{ fontWeight: 800 }}>FIX</Button>
                            )}
                        </Paper>

                        {/* Webhook Health */}
                        <Paper variant="outlined" sx={{ p: 2, borderRadius: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between', borderColor: isWebhookReady ? alpha(theme.palette.secondary.main, 0.3) : 'divider', bgcolor: isWebhookReady ? alpha(theme.palette.secondary.main, 0.02) : 'transparent' }}>
                            <Stack direction="row" spacing={2} alignItems="center">
                                {isWebhookReady ? <CheckCircleIcon color="secondary" fontSize="small" /> : <WarningIcon color="warning" fontSize="small" />}
                                <Box>
                                    <Typography variant="subtitle2" fontWeight={700}>Webhook Notifications</Typography>
                                    <Typography variant="caption" color="text.secondary">
                                        {!localUser.webhookEnabled ? "Opted-out of webhooks" : !hasWebhookConfigured ? "No destination selected" : "Configured and active"}
                                    </Typography>
                                </Box>
                            </Stack>
                        </Paper>
                    </Stack>

                    <Typography variant="overline" color="text.secondary" fontWeight={800} display="block" mb={2}>CORE IDENTITY</Typography>
                    <Stack spacing={2.5} sx={{ mb: 4 }}>
                        <TextField 
                            label="Username" 
                            fullWidth 
                            size="small" 
                            value={localUser.username} 
                            disabled 
                        />
                        <TextField 
                            label="Email Address" 
                            fullWidth 
                            size="small" 
                            value={localUser.email || ""} 
                            onChange={(e) => setLocalUser({ ...localUser, email: e.target.value })}
                            disabled={isNode}
                        />
                        {!isSelf && (
                            <FormControl fullWidth size="small" disabled={isNode}>
                                <InputLabel>Account Role</InputLabel>
                                <Select
                                    value={localUser.role}
                                    label="Account Role"
                                    onChange={(e) => setLocalUser({ ...localUser, role: e.target.value })}
                                >
                                    <MenuItem value="Admin">Administrator</MenuItem>
                                    <MenuItem value="Standard">Standard User</MenuItem>
                                    <MenuItem value="Viewer">Read-Only Viewer</MenuItem>
                                    <MenuItem value="AlertOnly">Notification Only</MenuItem>
                                </Select>
                            </FormControl>
                        )}
                    </Stack>

                    <Typography variant="overline" color="text.secondary" fontWeight={800} display="block" mb={2}>DELIVERY CHANNELS</Typography>
                    <Grid container spacing={2} sx={{ mb: 4 }}>
                        <Grid item xs={6}>
                            <Paper 
                                variant="outlined" 
                                onClick={() => !isNode && setLocalUser({ ...localUser, alertsEnabled: !localUser.alertsEnabled })}
                                sx={{ 
                                    p: 2, 
                                    textAlign: 'center', 
                                    cursor: isNode ? 'default' : 'pointer',
                                    borderRadius: 3,
                                    borderColor: localUser.alertsEnabled ? 'primary.main' : 'divider',
                                    bgcolor: localUser.alertsEnabled ? alpha(theme.palette.primary.main, 0.05) : 'transparent',
                                    transition: 'all 0.2s',
                                    opacity: isNode ? 0.7 : 1
                                }}
                            >
                                <MailIcon color={localUser.alertsEnabled ? "primary" : "disabled"} />
                                <Typography variant="caption" display="block" mt={1} fontWeight={700}>EMAIL</Typography>
                            </Paper>
                        </Grid>
                        <Grid item xs={6}>
                            <Paper 
                                variant="outlined" 
                                onClick={() => !isNode && setLocalUser({ ...localUser, webhookEnabled: !localUser.webhookEnabled })}
                                sx={{ 
                                    p: 2, 
                                    textAlign: 'center', 
                                    cursor: isNode ? 'default' : 'pointer',
                                    borderRadius: 3,
                                    borderColor: localUser.webhookEnabled ? 'secondary.main' : 'divider',
                                    bgcolor: localUser.webhookEnabled ? alpha(theme.palette.secondary.main, 0.05) : 'transparent',
                                    transition: 'all 0.2s',
                                    opacity: isNode ? 0.7 : 1
                                }}
                            >
                                <WebhookIcon color={localUser.webhookEnabled ? "secondary" : "disabled"} />
                                <Typography variant="caption" display="block" mt={1} fontWeight={700}>WEBHOOK</Typography>
                            </Paper>
                        </Grid>
                    </Grid>

                    {localUser.alertsEnabled && (
                        <Box sx={{ mb: 4 }}>
                            <Typography variant="caption" color="text.secondary" fontWeight={700} display="block" mb={1}>EMAIL PREFERENCES</Typography>
                            <TextField 
                                label="Alert Email" 
                                placeholder="Uses primary email if blank"
                                fullWidth 
                                size="small" 
                                value={localUser.alertEmail || ""} 
                                onChange={(e) => setLocalUser({ ...localUser, alertEmail: e.target.value })}
                                disabled={isNode}
                            />
                        </Box>
                    )}

                    {localUser.webhookEnabled && (
                        <Box sx={{ mb: 4 }}>
                            <Typography variant="caption" color="text.secondary" fontWeight={700} display="block" mb={1}>WEBHOOK PREFERENCES</Typography>
                            <FormControl fullWidth size="small" disabled={isNode}>
                                <InputLabel>Selected Webhooks</InputLabel>
                                <Select
                                    multiple
                                    value={localUser.preferredWebhookIds || []}
                                    label="Selected Webhooks"
                                    onChange={(e) => setLocalUser({ ...localUser, preferredWebhookIds: typeof e.target.value === 'string' ? e.target.value.split(',') : e.target.value })}
                                    renderValue={(selected) => (
                                        <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.5 }}>
                                            {selected.map((value: any) => {
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
                        </Box>
                    )}

                    <Typography variant="overline" color="text.secondary" fontWeight={800} display="block" mb={2}>ALERT SCOPE & TRIGGERS</Typography>
                    <Stack spacing={3}>
                        <FormControl fullWidth size="small" disabled={isNode}>
                            <InputLabel>Alert Scope</InputLabel>
                            <Select
                                value={localUser.alertSeverity || "All"}
                                label="Alert Scope"
                                onChange={(e) => setLocalUser({ ...localUser, alertSeverity: e.target.value })}
                            >
                                <MenuItem value="All">All Network Events</MenuItem>
                                <MenuItem value="AssignedOnly">Only Devices I Manage</MenuItem>
                            </Select>
                        </FormControl>

                        <Box>
                            <Typography variant="caption" color="text.secondary" fontWeight={700} display="block" mb={1}>ACTIVE TRIGGERS</Typography>
                            <Box sx={{ display: 'flex', gap: 1, flexWrap: 'wrap' }}>
                                <Chip 
                                    label="DEVICE DOWN" 
                                    size="small"
                                    color={localUser.alertOnDeviceDown ? "error" : "default"}
                                    onClick={isNode ? undefined : () => setLocalUser({ ...localUser, alertOnDeviceDown: !localUser.alertOnDeviceDown })}
                                    sx={{ cursor: isNode ? 'default' : 'pointer' }}
                                />
                                <Chip 
                                    label="DEVICE UP" 
                                    size="small"
                                    color={localUser.alertOnDeviceUp ? "success" : "default"}
                                    onClick={isNode ? undefined : () => setLocalUser({ ...localUser, alertOnDeviceUp: !localUser.alertOnDeviceUp })}
                                    sx={{ cursor: isNode ? 'default' : 'pointer' }}
                                />
                                <Chip 
                                    label="NEW DISCOVERY" 
                                    size="small"
                                    color={localUser.alertOnNewDevice ? "info" : "default"}
                                    onClick={isNode ? undefined : () => setLocalUser({ ...localUser, alertOnNewDevice: !localUser.alertOnNewDevice })}
                                    sx={{ cursor: isNode ? 'default' : 'pointer' }}
                                />
                            </Box>
                        </Box>
                    </Stack>

                    <Typography variant="overline" color="text.secondary" fontWeight={800} display="block" mb={2} mt={4}>ACCOUNT METADATA</Typography>
                    <List>
                        <ListItem sx={{ px: 0 }}>
                            <ListItemIcon sx={{ minWidth: 40 }}><CalendarIcon fontSize="small" /></ListItemIcon>
                            <ListItemText primary="Created On" secondary={new Date(localUser.createdAt).toLocaleString()} primaryTypographyProps={{ variant: 'caption', fontWeight: 700 }} />
                        </ListItem>
                        <ListItem sx={{ px: 0 }}>
                            <ListItemIcon sx={{ minWidth: 40 }}><HistoryIcon fontSize="small" /></ListItemIcon>
                            <ListItemText primary="Last Activity" secondary={localUser.lastLoginAt ? new Date(localUser.lastLoginAt).toLocaleString() : "Never"} primaryTypographyProps={{ variant: 'caption', fontWeight: 700 }} />
                        </ListItem>
                    </List>
                </Box>

                <Box sx={{ p: 2, bgcolor: 'background.paper', borderTop: '1px solid', borderColor: 'divider' }}>
                    <Button 
                        variant="contained" 
                        fullWidth 
                        startIcon={saving ? <CircularProgress size={20} color="inherit" /> : (isNode ? <ShieldIcon /> : <SaveIcon />)} 
                        onClick={handleUpdate}
                        disabled={saving || isNode}
                        sx={{ py: 1.5, borderRadius: 2, fontWeight: 800 }}
                    >
                        {saving ? "SAVING..." : (isNode ? "GOVERNED BY HUB" : "SAVE CHANGES")}
                    </Button>
                </Box>
            </Box>
        </Drawer>
    );
}

