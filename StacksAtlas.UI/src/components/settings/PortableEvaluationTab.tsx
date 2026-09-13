import React from 'react';
import {
  Box,
  Grid,
  Paper,
  Stack,
  Typography,
  Chip,
  alpha,
  useTheme,
  List,
  ListItem,
  ListItemIcon,
  ListItemText,
  Button,
} from '@mui/material';
import {
  WorkspacePremium as PremiumIcon,
  CheckCircle as CheckIcon,
  Lock as LockIcon,
  InstallDesktop as InstallIcon,
  Launch as LaunchIcon,
} from '@mui/icons-material';
import { LICENSING_LINKS, TIER_2_COPY } from '../../config/licensingLinks';

interface PortableEvaluationTabProps {
  promoteHint?: string | null;
  dataDirectory?: string | null;
}

const PortableEvaluationTab: React.FC<PortableEvaluationTabProps> = ({
  promoteHint,
  dataDirectory,
}) => {
  const theme = useTheme();

  const included = [
    'Unlimited device discovery and monitoring',
    'Network interfaces, scanning, and alerts',
    'Local dashboard and on-demand diagnostics',
    'Portable data stored in your user folder',
  ];

  const fullInstall = [
    'Always-on background service (Business tier)',
    'Persistent database and scan history',
    'Federation, Hub fleet mode, and SSO',
    'Advanced velocity, credential broker, and compliance features',
  ];

  return (
    <Grid container spacing={3}>
      <Grid item xs={12} md={7}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: 'blur(20px)',
            border: `2px solid ${alpha(theme.palette.info.main, 0.35)}`,
            boxShadow: `0 8px 16px ${alpha('#000', 0.1)}`,
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={3}>
            <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.info.main, 0.12) }}>
              <PremiumIcon color="info" sx={{ fontSize: 24 }} />
            </Box>
            <Box>
              <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>
                PORTABLE MODE
              </Typography>
              <Typography variant="caption" color="text.secondary">
                FREE TIER  -  PORTABLE SESSION, NO LICENSE KEY
              </Typography>
            </Box>
            <Chip label="ACTIVE" color="info" size="small" sx={{ ml: 'auto', fontWeight: 800 }} />
          </Stack>

          <Typography variant="body2" color="text.secondary" sx={{ mb: 3, lineHeight: 1.7 }}>
            {TIER_2_COPY.free.body} Upgrade to {TIER_2_COPY.business.title} ({TIER_2_COPY.business.price}) for an
            always-on appliance install when you are ready for production.
          </Typography>

          <Button
            fullWidth
            variant="contained"
            color="info"
            href={LICENSING_LINKS.BUSINESS_CHECKOUT}
            target="_blank"
            rel="noopener noreferrer"
            component="a"
            endIcon={<LaunchIcon />}
            sx={{ mb: 3, fontWeight: 900, borderRadius: 3, py: 1.25 }}
          >
            Get Business  -  {TIER_2_COPY.business.price}
          </Button>

          {dataDirectory && (
            <Box
              sx={{
                p: 2,
                mb: 3,
                borderRadius: 2,
                bgcolor: alpha(theme.palette.background.default, 0.5),
                border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
              }}
            >
              <Typography variant="caption" fontWeight={800} color="primary" sx={{ display: 'block', mb: 0.5 }}>
                TRIAL DATA FOLDER
              </Typography>
              <Typography variant="caption" sx={{ fontFamily: 'monospace', wordBreak: 'break-all' }}>
                {dataDirectory}
              </Typography>
            </Box>
          )}

          <Stack direction="row" alignItems="flex-start" spacing={1.5}>
            <InstallIcon color="primary" sx={{ mt: 0.25 }} />
            <Box>
              <Typography variant="subtitle2" sx={{ fontWeight: 700, mb: 0.5 }}>
                Move to full appliance install
              </Typography>
              <Typography variant="body2" color="text.secondary" sx={{ lineHeight: 1.7 }}>
                {promoteHint ??
                  'Re-run the StacksAtlas installer and choose the background service option for production mode.'}
              </Typography>
            </Box>
          </Stack>
        </Paper>
      </Grid>

      <Grid item xs={12} md={5}>
        <Stack spacing={2}>
          <Paper
            variant="outlined"
            sx={{
              p: 2.5,
              borderRadius: 4,
              bgcolor: alpha(theme.palette.success.main, 0.04),
              border: `1px solid ${alpha(theme.palette.success.main, 0.2)}`,
            }}
          >
            <Typography variant="overline" color="success.main" sx={{ fontWeight: 800, display: 'block', mb: 1 }}>
              Included in portable mode
            </Typography>
            <List dense disablePadding>
              {included.map((item) => (
                <ListItem key={item} disableGutters sx={{ py: 0.35 }}>
                  <ListItemIcon sx={{ minWidth: 28 }}>
                    <CheckIcon sx={{ fontSize: 16, color: 'success.main' }} />
                  </ListItemIcon>
                  <ListItemText
                    primary={item}
                    primaryTypographyProps={{ variant: 'caption', sx: { fontWeight: 600, lineHeight: 1.5 } }}
                  />
                </ListItem>
              ))}
            </List>
          </Paper>

          <Paper
            variant="outlined"
            sx={{
              p: 2.5,
              borderRadius: 4,
              bgcolor: alpha(theme.palette.text.primary, 0.02),
              border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
            }}
          >
            <Typography variant="overline" sx={{ fontWeight: 800, display: 'block', mb: 1, opacity: 0.6 }}>
              Full install unlocks
            </Typography>
            <List dense disablePadding>
              {fullInstall.map((item) => (
                <ListItem key={item} disableGutters sx={{ py: 0.35 }}>
                  <ListItemIcon sx={{ minWidth: 28 }}>
                    <LockIcon sx={{ fontSize: 16, opacity: 0.5 }} />
                  </ListItemIcon>
                  <ListItemText
                    primary={item}
                    primaryTypographyProps={{ variant: 'caption', sx: { fontWeight: 600, lineHeight: 1.5, opacity: 0.8 } }}
                  />
                </ListItem>
              ))}
            </List>
          </Paper>
        </Stack>
      </Grid>
    </Grid>
  );
};

export default PortableEvaluationTab;
