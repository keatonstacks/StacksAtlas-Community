import React from 'react';
import {
  Box,
  Grid,
  Paper,
  Stack,
  Typography,
  TextField,
  Button,
  Chip,
  CircularProgress,
  IconButton as MuiIconButton,
  alpha,
  useTheme,
  Link,
} from '@mui/material';
import { licenseTierLabel, normalizeLicenseTier } from '../../utils/federationMode';
import { DOWNLOAD_LINKS, LICENSING_LINKS, TIER_2_COPY } from '../../config/licensingLinks';
import {
  VerifiedUser as VerifiedIcon,
  ContentCopy as CopyIcon,
  Refresh as ResetIcon,
  WorkspacePremium as PremiumIcon,
  Launch as LaunchIcon,
  Download as DownloadIcon,
  DesktopWindows as WindowsIcon,
  LaptopMac as MacIcon,
  InstallDesktop as MsiIcon,
  Storage as DockerIcon,
  CheckCircle as CheckIcon,
} from '@mui/icons-material';

interface LicensingTabProps {
  license: any;
  licenseLoading: boolean;
  refreshLicense: () => void;
  keyInput: string;
  setKeyInput: (key: string) => void;
  activateLicense: (key: string) => Promise<{ success: boolean; message?: string }>;
  onNotify?: (message: string, severity?: 'success' | 'error') => void;
}

const DOWNLOADS = [
  { label: 'Windows Portable', sublabel: 'Free session scanner', href: DOWNLOAD_LINKS.WIN_PORTABLE, icon: WindowsIcon, free: true },
  { label: 'macOS DMG', sublabel: 'Free session scanner', href: DOWNLOAD_LINKS.MAC_DMG, icon: MacIcon, free: true },
  { label: 'Windows MSI', sublabel: 'Always-on appliance', href: DOWNLOAD_LINKS.WIN_MSI, icon: MsiIcon, free: false },
  { label: 'Linux / Docker', sublabel: 'Portable or appliance', href: DOWNLOAD_LINKS.LINUX_DOCKER, icon: DockerIcon, free: true },
] as const;

const LicensingTab: React.FC<LicensingTabProps> = ({
  license,
  licenseLoading,
  refreshLicense,
  keyInput,
  setKeyInput,
  activateLicense,
  onNotify,
}) => {
  const theme = useTheme();
  const tier = normalizeLicenseTier(license?.tier);
  const isBusiness = license?.isActive && tier >= 1;

  return (
    <Grid container spacing={3}>
      <Grid item xs={12} md={6}>
        <Paper
          variant="outlined"
          sx={{
            p: 2.5,
            height: '100%',
            borderRadius: 4,
            bgcolor: alpha(theme.palette.info.main, 0.03),
            border: `1px solid ${alpha(theme.palette.info.main, 0.2)}`,
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1} mb={1}>
            <Chip label={TIER_2_COPY.free.price} size="small" color="info" sx={{ fontWeight: 900 }} />
            <Typography variant="subtitle2" fontWeight={900}>
              {TIER_2_COPY.free.title}
            </Typography>
          </Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2, lineHeight: 1.6 }}>
            {TIER_2_COPY.free.body}
          </Typography>
          <Stack spacing={0.75}>
            {TIER_2_COPY.free.highlights.map((item) => (
              <Stack key={item} direction="row" spacing={1} alignItems="flex-start">
                <CheckIcon sx={{ fontSize: 16, color: 'info.main', mt: 0.2 }} />
                <Typography variant="caption" sx={{ fontWeight: 600, lineHeight: 1.5 }}>
                  {item}
                </Typography>
              </Stack>
            ))}
          </Stack>
        </Paper>
      </Grid>

      <Grid item xs={12} md={6}>
        <Paper
          variant="outlined"
          sx={{
            p: 2.5,
            height: '100%',
            borderRadius: 4,
            bgcolor: isBusiness
              ? alpha(theme.palette.success.main, 0.04)
              : alpha(theme.palette.primary.main, 0.03),
            border: isBusiness
              ? `2px solid ${alpha(theme.palette.success.main, 0.35)}`
              : `1px solid ${alpha(theme.palette.primary.main, 0.2)}`,
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1} mb={1}>
            <Chip
              label={TIER_2_COPY.business.price}
              size="small"
              color={isBusiness ? 'success' : 'primary'}
              sx={{ fontWeight: 900 }}
            />
            <Typography variant="subtitle2" fontWeight={900}>
              {TIER_2_COPY.business.title}
            </Typography>
            {isBusiness && (
              <Chip label="ACTIVE" size="small" color="success" variant="outlined" sx={{ ml: 'auto', fontWeight: 800, fontSize: '0.6rem' }} />
            )}
          </Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2, lineHeight: 1.6 }}>
            {TIER_2_COPY.business.body}
          </Typography>
          <Stack spacing={0.75} sx={{ mb: isBusiness ? 0 : 2 }}>
            {TIER_2_COPY.business.highlights.map((item) => (
              <Stack key={item} direction="row" spacing={1} alignItems="flex-start">
                <CheckIcon sx={{ fontSize: 16, color: isBusiness ? 'success.main' : 'primary.main', mt: 0.2 }} />
                <Typography variant="caption" sx={{ fontWeight: 600, lineHeight: 1.5 }}>
                  {item}
                </Typography>
              </Stack>
            ))}
          </Stack>
          {!isBusiness && (
            <Button
              fullWidth
              variant="contained"
              color="primary"
              href={LICENSING_LINKS.BUSINESS_CHECKOUT}
              target="_blank"
              rel="noopener noreferrer"
              component="a"
              endIcon={<LaunchIcon />}
              sx={{ fontWeight: 800, borderRadius: 3 }}
            >
              Get Business ({TIER_2_COPY.business.price})
            </Button>
          )}
        </Paper>
      </Grid>

      <Grid item xs={12} md={6}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            height: '100%',
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: 'blur(20px)',
            border: license?.isActive
              ? `2px solid ${alpha(theme.palette.success.main, 0.4)}`
              : `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            position: 'relative',
            overflow: 'hidden',
            boxShadow: `0 8px 16px ${alpha('#000', 0.1)}`,
          }}
        >
          {license?.isActive && (
            <VerifiedIcon
              sx={{
                position: 'absolute',
                bottom: -20,
                right: -20,
                fontSize: 140,
                opacity: 0.03,
                transform: 'rotate(-15deg)',
                color: 'success.main',
              }}
            />
          )}

          <Stack direction="row" justifyContent="space-between" alignItems="flex-start" mb={4}>
            <Stack direction="row" alignItems="center" spacing={1.5}>
              <Box sx={{ p: 1, borderRadius: 2, bgcolor: alpha(theme.palette.primary.main, 0.1) }}>
                <PremiumIcon color="primary" sx={{ fontSize: 24 }} />
              </Box>
              <Box>
                <Typography variant="subtitle1" sx={{ fontWeight: 600, letterSpacing: -0.5, lineHeight: 1 }}>
                  LICENSE STATUS
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  HWID, tier, and discovery count
                </Typography>
              </Box>
            </Stack>
            <Chip
              label={licenseLoading ? 'VALIDATING...' : licenseTierLabel(license?.tier, !!license?.isActive)}
              color={license?.isActive ? 'success' : 'default'}
              sx={{ fontWeight: 900, borderRadius: 1.5, fontSize: '0.65rem', height: 24 }}
            />
          </Stack>

          <Box
            sx={{
              p: 2.5,
              borderRadius: 3,
              bgcolor: alpha(theme.palette.background.default, 0.5),
              border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
              mb: 3,
            }}
          >
            <Typography variant="caption" fontWeight={900} color="primary" sx={{ display: 'block', mb: 1, letterSpacing: 0.5, opacity: 0.7 }}>
              HARDWARE IDENTIFIER (HWID)
            </Typography>
            <Stack direction="row" spacing={1} alignItems="center">
              <Typography
                sx={{
                  fontFamily: 'monospace',
                  fontWeight: 800,
                  fontSize: '0.75rem',
                  flexGrow: 1,
                  letterSpacing: 0.5,
                  opacity: 0.9,
                  wordBreak: 'break-all',
                }}
              >
                {licenseLoading ? 'REFRESHING...' : license?.hardwareId || 'GENERATING...'}
              </Typography>
              <Stack direction="row">
                <MuiIconButton
                  size="small"
                  onClick={() => {
                    if (license?.hardwareId) navigator.clipboard.writeText(license.hardwareId);
                  }}
                >
                  <CopyIcon sx={{ fontSize: '14px' }} />
                </MuiIconButton>
                <MuiIconButton size="small" onClick={() => refreshLicense()} color="primary">
                  <ResetIcon fontSize="small" />
                </MuiIconButton>
              </Stack>
            </Stack>
          </Box>

          <Stack direction="row" spacing={2}>
            <Box
              sx={{
                flex: 1,
                p: 2,
                borderRadius: 3,
                bgcolor: alpha(theme.palette.primary.main, 0.05),
                border: `1px solid ${alpha(theme.palette.primary.main, 0.1)}`,
                textAlign: 'center',
              }}
            >
              <Typography variant="caption" fontWeight={900} sx={{ opacity: 0.5, display: 'block', mb: 0.5, fontSize: '0.6rem' }}>
                DISCOVERED
              </Typography>
              <Typography variant="h4" fontWeight={900} sx={{ letterSpacing: -1, color: 'primary.main' }}>
                {license?.currentCount ?? 0}
              </Typography>
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5, fontSize: '0.65rem' }}>
                Unlimited on all tiers
              </Typography>
            </Box>
            <Box
              sx={{
                flex: 1,
                p: 2,
                borderRadius: 3,
                bgcolor: alpha(theme.palette.secondary.main, 0.05),
                border: `1px solid ${alpha(theme.palette.secondary.main, 0.1)}`,
                textAlign: 'center',
              }}
            >
              <Typography variant="caption" fontWeight={900} sx={{ opacity: 0.5, display: 'block', mb: 0.5, fontSize: '0.6rem' }}>
                CURRENT TIER
              </Typography>
              <Typography variant="h5" fontWeight={900} sx={{ letterSpacing: -0.5, color: 'secondary.main' }}>
                {licenseTierLabel(license?.tier, !!license?.isActive)}
              </Typography>
            </Box>
          </Stack>
        </Paper>
      </Grid>

      <Grid item xs={12} md={6}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            height: '100%',
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.8),
            backdropFilter: 'blur(20px)',
            border: `1px solid ${alpha(theme.palette.divider, 0.1)}`,
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'center',
          }}
        >
          <Box sx={{ textAlign: 'center', mb: 4 }}>
            <Typography variant="h6" fontWeight={900} sx={{ letterSpacing: -0.5 }}>
              ACTIVATE BUSINESS
            </Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
              Paste the license key from your order confirmation email.
            </Typography>
          </Box>

          <Stack spacing={2} sx={{ maxWidth: 400, mx: 'auto', width: '100%' }}>
            <TextField
              fullWidth
              placeholder="XXXXX-XXXXX-XXXXX-XXXXX"
              value={keyInput}
              onChange={(e) => setKeyInput(e.target.value)}
              sx={{
                '& .MuiOutlinedInput-root': {
                  fontFamily: 'monospace',
                  fontWeight: 800,
                  borderRadius: 3,
                  bgcolor: alpha(theme.palette.background.default, 0.4),
                  textAlign: 'center',
                },
              }}
            />
            <Button
              fullWidth
              variant="contained"
              color="primary"
              size="large"
              disabled={licenseLoading || !keyInput}
              onClick={async () => {
                const result = await activateLicense(keyInput);
                if (result.success) setKeyInput('');
                else onNotify?.(result.message || 'Activation failed');
              }}
              sx={{
                fontWeight: 900,
                py: 2,
                borderRadius: 3,
                boxShadow: `0 8px 24px ${alpha(theme.palette.primary.main, 0.3)}`,
              }}
            >
              {licenseLoading ? <CircularProgress size={24} color="inherit" /> : 'APPLY LICENSE KEY'}
            </Button>

            <Typography variant="caption" color="text.secondary" sx={{ textAlign: 'center', px: 2 }}>
              Activation requires an outbound connection to the StacksAtlas licensing server.{' '}
              <Link href={LICENSING_LINKS.DOCS_PRICING} target="_blank" rel="noopener noreferrer">
                View pricing
              </Link>
              {' · '}
              <Link href={LICENSING_LINKS.DOCS_INSTALL} target="_blank" rel="noopener noreferrer">
                Install guide
              </Link>
            </Typography>
          </Stack>
        </Paper>
      </Grid>

      <Grid item xs={12}>
        <Paper
          variant="outlined"
          sx={{
            p: 3,
            borderRadius: 4,
            bgcolor: alpha(theme.palette.background.paper, 0.6),
            border: `1px solid ${alpha(theme.palette.divider, 0.12)}`,
          }}
        >
          <Stack direction="row" alignItems="center" spacing={1.5} mb={0.5}>
            <DownloadIcon color="primary" />
            <Typography variant="subtitle1" fontWeight={900}>
              Downloads
            </Typography>
          </Stack>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 2.5, maxWidth: 720, lineHeight: 1.6 }}>
            Signed builds from{' '}
            <Link href={LICENSING_LINKS.SITE_DOWNLOAD} target="_blank" rel="noopener noreferrer">
              releases.stacksatlas.com
            </Link>
            . Portable builds need no key; MSI and Docker can run as an always-on appliance after Business activation.
          </Typography>

          <Grid container spacing={1.5}>
            {DOWNLOADS.map(({ label, sublabel, href, icon: Icon, free }) => (
              <Grid item xs={12} sm={6} md={3} key={label}>
                <Button
                  fullWidth
                  variant="outlined"
                  href={href}
                  target="_blank"
                  rel="noopener noreferrer"
                  component="a"
                  startIcon={<Icon />}
                  endIcon={<LaunchIcon sx={{ fontSize: 14, opacity: 0.6 }} />}
                  sx={{
                    justifyContent: 'flex-start',
                    textAlign: 'left',
                    py: 1.5,
                    px: 2,
                    borderRadius: 3,
                    fontWeight: 700,
                    flexDirection: 'column',
                    alignItems: 'flex-start',
                    gap: 0.25,
                    '& .MuiButton-endIcon': { position: 'absolute', right: 12, top: '50%', transform: 'translateY(-50%)' },
                    position: 'relative',
                    pr: 5,
                  }}
                >
                  <Typography variant="body2" fontWeight={800} sx={{ lineHeight: 1.2 }}>
                    {label}
                  </Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ fontWeight: 600 }}>
                    {sublabel}
                    {free ? ' · No key' : ' · License required'}
                  </Typography>
                </Button>
              </Grid>
            ))}
          </Grid>
        </Paper>
      </Grid>
    </Grid>
  );
};

export default LicensingTab;
