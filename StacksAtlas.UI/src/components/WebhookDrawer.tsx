import React, { useState, useEffect } from 'react';
import {
  Drawer, Box, Typography, TextField, Button, Stack, Divider,
  FormControl, Select, MenuItem, Checkbox, FormControlLabel,
  FormGroup, IconButton, alpha, useTheme, Alert
} from '@mui/material';
import {
  Close as CloseIcon,
  Save as SaveIcon,
  NotificationsActive as WebhookIcon,
  Webhook as GenericIcon,
  Chat as SlackIcon,
  Groups as TeamsIcon,
  Forum as DiscordIcon
} from '@mui/icons-material';
import { ApiService } from '../services/apiService';

interface WebhookDrawerProps {
  open: boolean;
  onClose: () => void;
  webhook: any | null;
  onSaved: () => void;
}

export const WebhookDrawer: React.FC<WebhookDrawerProps> = ({ open, onClose, webhook, onSaved }) => {
  const theme = useTheme();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [users, setUsers] = useState<any[]>([]);

  const [formData, setFormData] = useState<any>({
    name: '',
    url: '',
    provider: 0, // Generic
    enabled: true,
    signingSecret: '',
    triggerEvents: [1, 2], // DeviceDown, DeviceUp
    alertSeverity: 'All',
    assignedToUserId: null
  });

  useEffect(() => {
    const fetchUsers = async () => {
      try {
        const data = await ApiService.getUsers();
        setUsers(data);
      } catch (err) {
        console.error("Failed to fetch users for scoping", err);
      }
    };
    fetchUsers();
  }, []);

  useEffect(() => {
    if (webhook) {
      setFormData({
        ...webhook,
        url: webhook.url || '',
        signingSecret: webhook.signingSecret || '',
        alertSeverity: webhook.alertSeverity || 'All',
        assignedToUserId: webhook.assignedToUserId || null
      });
    } else {
      setFormData({
        name: '',
        url: '',
        provider: 0,
        enabled: true,
        signingSecret: '',
        triggerEvents: [1, 2],
        alertSeverity: 'All',
        assignedToUserId: null
      });
    }
    setError(null);
  }, [webhook, open]);

  const handleSave = async () => {
    if (!formData.name || !formData.url) {
      setError("Name and URL are required.");
      return;
    }

    setLoading(true);
    setError(null);
    try {
      if (webhook?.id) {
        await ApiService.updateWebhook(webhook.id, formData);
      } else {
        await ApiService.createWebhook(formData);
      }
      onSaved();
      onClose();
    } catch (err: any) {
      setError(err.message || "Failed to save webhook.");
    } finally {
      setLoading(false);
    }
  };

  const toggleEvent = (eventId: number) => {
    const current = [...formData.triggerEvents];
    if (current.includes(eventId)) {
      setFormData({ ...formData, triggerEvents: current.filter(id => id !== eventId) });
    } else {
      setFormData({ ...formData, triggerEvents: [...current, eventId] });
    }
  };

  return (
    <Drawer anchor="right" open={open} onClose={onClose} PaperProps={{ sx: { width: { xs: '100%', sm: 450 }, bgcolor: 'background.paper' } }}>
      <Box sx={{ p: 3, height: '100%', display: 'flex', flexDirection: 'column' }}>
        <Stack direction="row" alignItems="center" justifyContent="space-between" mb={3}>
          <Stack direction="row" alignItems="center" spacing={1.5}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1), color: 'primary.main', display: 'flex' }}>
              <WebhookIcon />
            </Box>
            <Typography variant="h6" fontWeight={800}>
              {webhook ? 'EDIT WEBHOOK' : 'NEW WEBHOOK'}
            </Typography>
          </Stack>
          <IconButton onClick={onClose} size="small"><CloseIcon /></IconButton>
        </Stack>

        <Divider sx={{ mb: 3 }} />

        <Box sx={{ flexGrow: 1, overflowY: 'auto' }}>
          <Stack spacing={2.5}>
            {error && <Alert severity="error" sx={{ borderRadius: 2 }}>{error}</Alert>}


            <Box>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 0.5, display: 'block', letterSpacing: 0.5 }}>
                FRIENDLY NAME
              </Typography>
              <TextField
                fullWidth
                placeholder="e.g., Discord (IT Alerts)"
                value={formData.name}
                onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                size="small"
                required
                variant="outlined"
              />
            </Box>

            <Box>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 0.5, display: 'block', letterSpacing: 0.5 }}>
                PROVIDER TYPE
              </Typography>
              <FormControl fullWidth size="small">
                <Select
                  value={formData.provider}
                  onChange={(e) => setFormData({ ...formData, provider: e.target.value as number })}
                >
                  <MenuItem value={0}><Stack direction="row" spacing={1} alignItems="center"><GenericIcon fontSize="small" /> <Typography variant="body2" fontWeight={600}>Generic JSON</Typography></Stack></MenuItem>
                  <MenuItem value={1}><Stack direction="row" spacing={1} alignItems="center"><SlackIcon fontSize="small" sx={{ color: '#E01E5A' }} /> <Typography variant="body2" fontWeight={600}>Slack</Typography></Stack></MenuItem>
                  <MenuItem value={2}><Stack direction="row" spacing={1} alignItems="center"><TeamsIcon fontSize="small" sx={{ color: '#4B53BC' }} /> <Typography variant="body2" fontWeight={600}>Microsoft Teams</Typography></Stack></MenuItem>
                  <MenuItem value={3}><Stack direction="row" spacing={1} alignItems="center"><DiscordIcon fontSize="small" sx={{ color: '#5865F2' }} /> <Typography variant="body2" fontWeight={600}>Discord</Typography></Stack></MenuItem>
                </Select>
              </FormControl>
            </Box>

            <Box>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 0.5, display: 'block', letterSpacing: 0.5 }}>
                WEBHOOK URL
              </Typography>
              <TextField
                fullWidth
                placeholder="https://hooks.slack.com/services/..."
                value={formData.url}
                onChange={(e) => setFormData({ ...formData, url: e.target.value })}
                size="small"
                required
                multiline
                rows={2}
              />
            </Box>

            <Box>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 0.5, display: 'block', letterSpacing: 0.5 }}>
                SIGNING SECRET (OPTIONAL)
              </Typography>
              <TextField
                fullWidth
                placeholder="For X-StacksAtlas-Signature header"
                value={formData.signingSecret}
                onChange={(e) => setFormData({ ...formData, signingSecret: e.target.value })}
                size="small"
                type="password"
              />
            </Box>

            <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.divider, 0.05), border: '1px solid', borderColor: 'divider' }}>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 1.5, display: 'block', letterSpacing: 1 }}>
                ALERT SCOPE
              </Typography>
              <Stack spacing={2}>
                <FormControl fullWidth size="small">
                  <Select
                    value={formData.alertSeverity}
                    onChange={(e) => setFormData({ ...formData, alertSeverity: e.target.value })}
                  >
                    <MenuItem value="All"><Typography variant="body2" fontWeight={600}>Global (All Devices)</Typography></MenuItem>
                    <MenuItem value="AssignedOnly"><Typography variant="body2" fontWeight={600}>Targeted (Assigned to User)</Typography></MenuItem>
                  </Select>
                </FormControl>

                {formData.alertSeverity === 'AssignedOnly' && (
                  <FormControl fullWidth size="small">
                    <Typography variant="caption" color="text.secondary" sx={{ mb: 0.5, fontWeight: 700 }}>
                      SEND ONLY FOR DEVICES ASSIGNED TO:
                    </Typography>
                    <Select
                      value={formData.assignedToUserId || ''}
                      onChange={(e) => setFormData({ ...formData, assignedToUserId: e.target.value })}
                      displayEmpty
                    >
                      <MenuItem value="" disabled>Select User...</MenuItem>
                      {users.map(u => (
                        <MenuItem key={u.id} value={u.id}>{u.username}</MenuItem>
                      ))}
                    </Select>
                  </FormControl>
                )}
              </Stack>
            </Box>

            <Box sx={{ p: 2, borderRadius: 2, bgcolor: alpha(theme.palette.divider, 0.05), border: '1px solid', borderColor: 'divider' }}>
              <Typography variant="caption" fontWeight={900} color="text.secondary" sx={{ mb: 1.5, display: 'block', letterSpacing: 1 }}>
                TRIGGER EVENTS
              </Typography>
              <FormGroup>
                <FormControlLabel
                  control={<Checkbox size="small" checked={formData.triggerEvents.includes(1)} onChange={() => toggleEvent(1)} />}
                  label={<Typography variant="body2" fontWeight={600}>Device Offline</Typography>}
                />
                <FormControlLabel
                  control={<Checkbox size="small" checked={formData.triggerEvents.includes(2)} onChange={() => toggleEvent(2)} />}
                  label={<Typography variant="body2" fontWeight={600}>Device Online</Typography>}
                />
                <FormControlLabel
                  control={<Checkbox size="small" checked={formData.triggerEvents.includes(3)} onChange={() => toggleEvent(3)} />}
                  label={<Typography variant="body2" fontWeight={600}>New Discovery</Typography>}
                />
              </FormGroup>
            </Box>

            <FormControlLabel
              control={<Checkbox size="small" checked={formData.enabled} onChange={(e) => setFormData({ ...formData, enabled: e.target.checked })} />}
              label={<Typography variant="body2" fontWeight={800}>ENABLED</Typography>}
              sx={{ ml: 0.5 }}
            />
          </Stack>
        </Box>

        <Box sx={{ mt: 3, pt: 3, borderTop: '1px solid', borderColor: 'divider' }}>
          <Stack direction="row" spacing={2}>
            <Button variant="outlined" fullWidth onClick={onClose} disabled={loading}>CANCEL</Button>
            <Button
              variant="contained"
              fullWidth
              startIcon={<SaveIcon />}
              onClick={handleSave}
              disabled={loading}
              sx={{ fontWeight: 800 }}
            >
              {loading ? 'SAVING...' : 'SAVE WEBHOOK'}
            </Button>
          </Stack>
        </Box>
      </Box>
    </Drawer>
  );
};
