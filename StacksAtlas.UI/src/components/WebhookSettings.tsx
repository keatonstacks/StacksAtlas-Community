import React, { useState, useEffect } from 'react';
import {
  Box, Typography, Paper, Stack, Divider, Button, IconButton, Grid,
  alpha, useTheme, Chip, CircularProgress, Tooltip, Alert
} from '@mui/material';
import {
  Add as AddIcon,
  Delete as DeleteIcon,
  Edit as EditIcon,
  PlayArrow as TestIcon,
  Warning as WarningIcon,
  Lan as GenericIcon,
  Chat as SlackIcon,
  Groups as TeamsIcon,
  Forum as DiscordIcon,
  NotificationsActive as WebhookIcon
} from '@mui/icons-material';
import { ApiService } from '../services/apiService';
import { useConfirm } from '../context/ConfirmContext';
import { WebhookDrawer } from './WebhookDrawer';

interface WebhookSettingsProps {
  isGoverned?: boolean;
}

export const WebhookSettings: React.FC<WebhookSettingsProps> = ({ isGoverned = false }) => {
  const theme = useTheme();
  const { confirm } = useConfirm();
  const [webhooks, setWebhooks] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [testingId, setTestingId] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<{ id: string, success: boolean, message: string } | null>(null);
  
  const [drawerOpen, setDrawerOpen] = useState(false);
  const [selectedWebhook, setSelectedWebhook] = useState<any | null>(null);

  const fetchWebhooks = async () => {
    setLoading(true);
    try {
      const data = await ApiService.getWebhooks();
      setWebhooks(data);
    } catch (err) {
      console.error("Failed to fetch webhooks:", err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    fetchWebhooks();
  }, []);

  const handleAdd = () => {
    setSelectedWebhook(null);
    setDrawerOpen(true);
  };

  const handleEdit = (webhook: any) => {
    setSelectedWebhook(webhook);
    setDrawerOpen(true);
  };

  const handleDelete = async (id: string) => {
    const ok = await confirm({
      title: 'Delete webhook',
      message: 'Are you sure you want to delete this webhook?',
      confirmLabel: 'Delete',
      confirmColor: 'error',
    });
    if (!ok) return;
    try {
      await ApiService.deleteWebhook(id);
      fetchWebhooks();
    } catch (err) {
      console.error("Delete failed:", err);
    }
  };

  const handleTest = async (id: string) => {
    setTestingId(id);
    setTestResult(null);
    try {
      await ApiService.testWebhook(id);
      setTestResult({ id, success: true, message: "Test payload dispatched!" });
    } catch (err: any) {
      setTestResult({ id, success: false, message: err.message });
    } finally {
      setTestingId(null);
    }
  };

  const getProviderIcon = (provider: number) => {
    switch (provider) {
      case 1: return <SlackIcon sx={{ color: '#E01E5A' }} />;
      case 2: return <TeamsIcon sx={{ color: '#4B53BC' }} />;
      case 3: return <DiscordIcon sx={{ color: '#5865F2' }} />;
      default: return <GenericIcon />;
    }
  };

  const getProviderName = (provider: number) => {
    switch (provider) {
      case 1: return "Slack";
      case 2: return "Microsoft Teams";
      case 3: return "Discord";
      default: return "Generic JSON";
    }
  };

  if (loading && webhooks.length === 0) {
    return (
      <Box sx={{ p: 4, textAlign: 'center' }}>
        <CircularProgress size={24} />
      </Box>
    );
  }

  return (
    <Box>
      <Stack direction="row" alignItems="center" justifyContent="space-between" mb={2}>
        <Stack direction="row" alignItems="center" spacing={1.5}>
          <WebhookIcon color="primary" />
          <Typography variant="h6" fontWeight={900}>OUTBOUND WEBHOOKS</Typography>
        </Stack>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          size="small"
          onClick={handleAdd}
          disabled={isGoverned}
          sx={{ fontWeight: 800 }}
        >
          ADD WEBHOOK
        </Button>
      </Stack>
      <Divider sx={{ mb: 3, opacity: 0.5 }} />

      {webhooks.length === 0 ? (
        <Paper variant="outlined" sx={{ p: 4, textAlign: 'center', bgcolor: alpha(theme.palette.divider, 0.02), borderStyle: 'dashed' }}>
          <Typography variant="body2" color="text.secondary">
            No webhooks configured. Push real-time alerts to Slack, Discord, or Teams.
          </Typography>
        </Paper>
      ) : (
        <Stack spacing={2}>
          {webhooks.map((webhook) => (
            <Paper
              key={webhook.id}
              variant="outlined"
              sx={{
                p: 2,
                borderRadius: 2,
                transition: 'all 0.2s',
                '&:hover': { bgcolor: alpha(theme.palette.primary.main, 0.02), borderColor: alpha(theme.palette.primary.main, 0.3) }
              }}
            >
              <Grid container alignItems="center" spacing={2}>
                <Grid item xs={12} sm={4}>
                  <Stack direction="row" spacing={1.5} alignItems="center">
                    <Box sx={{ display: 'flex', color: 'text.secondary' }}>
                      {getProviderIcon(webhook.provider)}
                    </Box>
                    <Box>
                      <Typography variant="subtitle2" fontWeight={800}>{webhook.name}</Typography>
                      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                        {getProviderName(webhook.provider)}
                      </Typography>
                    </Box>
                  </Stack>
                </Grid>

                <Grid item xs={12} sm={4}>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                    {!webhook.enabled && <Chip label="Disabled" size="small" variant="outlined" />}
                    {webhook.status === 1 && ( // CircuitOpen
                      <Tooltip title={`Paused due to repeated failures: ${webhook.lastErrorMessage}`}>
                        <Chip
                          icon={<WarningIcon style={{ fontSize: 14 }} />}
                          label="Circuit Open"
                          size="small"
                          color="warning"
                        />
                      </Tooltip>
                    )}
                    {webhook.enabled && webhook.status === 0 && <Chip label="Active" size="small" color="success" variant="outlined" sx={{ fontWeight: 800 }} />}
                    
                    <Typography variant="caption" color="text.secondary" sx={{ width: '100%', mt: 0.5 }}>
                      {webhook.url}
                    </Typography>
                  </Stack>
                </Grid>

                <Grid item xs={12} sm={4}>
                  <Stack direction="row" spacing={1} justifyContent="flex-end">
                    <Tooltip title="Send Test Payload">
                      <IconButton 
                        size="small" 
                        onClick={() => handleTest(webhook.id)}
                        disabled={testingId === webhook.id || isGoverned}
                      >
                        {testingId === webhook.id ? <CircularProgress size={16} /> : <TestIcon fontSize="small" />}
                      </IconButton>
                    </Tooltip>
                    <IconButton size="small" disabled={isGoverned} onClick={() => handleEdit(webhook)}><EditIcon fontSize="small" /></IconButton>
                    <IconButton size="small" color="error" disabled={isGoverned} onClick={() => handleDelete(webhook.id)}><DeleteIcon fontSize="small" /></IconButton>
                  </Stack>
                </Grid>
                
                {testResult && testResult.id === webhook.id && (
                  <Grid item xs={12}>
                    <Alert 
                      severity={testResult.success ? "success" : "error"} 
                      sx={{ py: 0, mt: 1, borderRadius: 2 }}
                      onClose={() => setTestResult(null)}
                    >
                      {testResult.message}
                    </Alert>
                  </Grid>
                )}
              </Grid>
            </Paper>
          ))}
        </Stack>
      )}

      <WebhookDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        webhook={selectedWebhook}
        onSaved={fetchWebhooks}
      />
    </Box>
  );
};
